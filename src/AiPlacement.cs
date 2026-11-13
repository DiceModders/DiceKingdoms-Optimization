using System;
using DiceModders.ModCore.Tools;

namespace DiceModders.Optimization;

using BuildData = DiceKingdoms.Game.BuildData;
using BuildingType = DiceKingdoms.Game.BuildingType;

/// <summary>
/// Where the computer players look for building spots.
///
/// <c>AIPlay.WhereToPlaceBuilding</c> walks a spiral over EVERY tile of the island (<c>SpiralGrid.Enumerate(centre, grid size)</c>) and, for each free
/// tile and each of the 4 rotations, checks the disease risk, prices the spot (<c>TotalCostToPlaceBuildingAt</c>) and counts the neighbouring buildings
/// with LINQ (<c>ProximityAt</c>). It keeps the spot with the most neighbours, ties going to the cheaper one. On a normal 49 x 49 island that is ~2,400
/// tiles; on a big grid it is many times more for every building every AI places, and the game stops responding during the AI's build turn.
///
/// Only tiles touching a building can have neighbours (<c>IslandGrid.NeighborTiles</c>), so for a building that counts them no spot further than a
/// few tiles from the town can win. On grids bigger than the game's, this limits the spiral to the square around the island centre that holds every
/// building plus <see cref="Margin"/> tiles (never smaller than a normal island) for the whole build turn: <c>AIPlay.BuildAndClean</c> searches
/// directly once (the wonder's reserved spot) and then once per building through <c>PlaceBuildingSomewhere</c>. Buildings that need water (docks,
/// <c>localPositionsInWater</c>) do not count for the town square (the start phase puts a dock on the coast, which would stretch the square over the
/// whole island) and get their own square that reaches the water tile nearest to the island centre, plus the margin (a full-island search
/// for a dock froze the game for many seconds as well). The AI runs on the host only and sends its moves like a player, so the other players are
/// not affected.
///
/// Only methods returning void or bool are patched. <c>WhereToPlaceBuilding</c> itself returns a large struct, and a patch on it broke the returned
/// building: the AI kept retrying a placement that could never work (<c>PlaceBuildingSomewhere</c> reports success even when the placement fails).
/// </summary>
internal static class AiPlacement
{
    private const int Margin = 8;          // tiles around the outermost building (a building is at most 3 x 3, neighbours are 1 tile away)
    private const int NormalSide = 49;     // the game's island grid: never search a smaller square than that

    private static PatchSwitch _turn, _somewhere, _spiral;
    private static int _town;              // search square of the build turn in progress (0 = the game's)
    private static int _coast;             // search square for a building that needs water, this build turn
    private static int _limit;             // what SpiralGrid.Enumerate uses right now
    // per island grid: distance (in rings around the centre) to the nearest water tile. The coast hardly changes during a match.
    private static readonly System.Collections.Generic.Dictionary<IntPtr, int> CoastRing = new();

    internal static void Install(Patcher patcher)
    {
        _turn = patcher.Switch("AIPlay", "BuildAndClean", typeof(AiPlacement), prefix: nameof(TurnPrefix), postfix: nameof(TurnPostfix));
        _somewhere = patcher.Switch("AIPlay", "PlaceBuildingSomewhere", typeof(AiPlacement), prefix: nameof(SomewherePrefix), postfix: nameof(SomewherePostfix));
        _spiral = patcher.Switch("SpiralGrid", "Enumerate", typeof(AiPlacement), prefix: nameof(EnumeratePrefix));
        Options.AiSearchNearTown.SettingChanged += (_, _) => Refresh();
        DiceModders.ModCore.ModCore.Match.Ended += CoastRing.Clear;
        DiceModders.ModCore.ModCore.Match.Reset += CoastRing.Clear;
#if DEVLOG
        // measuring only (Debug builds): how often a nature tile is cleared during a build turn
        _clean = patcher.Switch("AIPlay", "CleanNature", typeof(AiPlacement), postfix: nameof(CleanPostfix));
#endif
    }

    internal static void Refresh()
    {
        bool on = Plugin.On && Options.AiSearchNearTown.Value;
        _turn.On = on;
        _somewhere.On = on;
        _spiral.On = on;
#if DEVLOG
        if (_clean != null) _clean.On = Plugin.On;
#endif
        _town = _limit = 0;
    }

    // AIPlay.BuildAndClean(BuildData data, Player.AIState state, List<BuildingType> buildingToPlace): one AI's build turn
    // The game builds the turn in two BuildAndClean calls (before and after the market). A wonder player searches its wonder spot at the start
    // of EVERY call (WhereToPlaceBuilding over the whole search square, ~150 ms on a 201 grid) and only then checks for a hammer; with no hammer
    // left the call ends without doing anything else (the rubber-band values it computes are only used by the loop). So without a hammer the
    // call is skipped: same result, minus the wasted wonder search (measured: ~155 ms per wonder player per turn, most second calls).
    internal static bool TurnPrefix(BuildData data)
    {
        if (!HasHammer(data))
        {
#if DEVLOG
            Plugin.L.LogInfo("[ai-stats] build call skipped: no hammer left");
#endif
            return false;
        }
#if DEVLOG
        StatsBegin(data);
#endif
        _town = _coast = _limit = 0;
        try
        {
            var grid = data?.Island?.grid;
            if (grid == null) return true;
            int side = grid.size.x;
            if (side <= NormalSide) return true;
            int c = (side - 1) / 2, far = 0;
            var e = grid.Buildings.GetEnumerator();
            while (e.MoveNext())
            {
                var b = e.Current;
                if (NeedsWater(b.type)) continue;   // the start dock sits on the coast: it would stretch the town square over the whole island
                var p = b.transform.position;
                far = Math.Max(far, Math.Max(Math.Abs(p.x - c), Math.Abs(p.y - c)));
            }
            int town = Math.Max(NormalSide, 2 * (far + Margin) + 1);
            int coast = Math.Max(town, 2 * (NearestWater(grid, c) + Margin) + 1);
            _town = town < side ? town : 0;
            _coast = coast < side ? coast : 0;
            _limit = _town;
        }
        catch (Exception ex) { _town = _coast = _limit = 0; Plugin.L.LogWarning("[ai] search area: " + ex.Message); }
        return true;
    }

    /// <summary>At least one hammer in the island's storage (the condition of the game's build loop). True when it cannot be read: then the game decides.</summary>
    private static bool HasHammer(BuildData data)
    {
        try { return data?.Island?.statistics == null || data.Island.statistics.Storage.stored.hammer >= 1; }
        catch (Exception) { return true; }
    }

    internal static void TurnPostfix()
    {
#if DEVLOG
        StatsEnd();
#endif
        _town = _coast = _limit = 0;
    }

    /// <summary>Rings around the island centre up to the first water tile (cached per island for the match).</summary>
    private static int NearestWater(DiceKingdoms.Game.IslandGrid grid, int c)
    {
        if (CoastRing.TryGetValue(grid.Pointer, out int ring)) return ring;
        ring = c;
        for (int d = 1; d <= c && ring == c; d++)
            for (int i = -d; i <= d && ring == c; i++)
                if (IsWater(grid, c + i, c - d) || IsWater(grid, c + i, c + d) || IsWater(grid, c - d, c + i) || IsWater(grid, c + d, c + i)) ring = d;
        CoastRing[grid.Pointer] = ring;
        return ring;
    }

    private static bool IsWater(DiceKingdoms.Game.IslandGrid grid, int x, int y) =>
        grid.Ground(new Unity.Mathematics.int2(x, y)) == DiceKingdoms.Game.GroundTile.WATER;

    // AIPlay.PlaceBuildingSomewhere(BuildData data, BuildingType buildingType, ...): a building that needs water gets the square that reaches the
    // nearest coast (the game's spiral starts at the centre too, so the coast nearest to it is inside)
    internal static void SomewherePrefix(BuildingType buildingType)
    {
#if DEVLOG
        _searchStart = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        try { _limit = NeedsWater(buildingType) ? _coast : _town; }
        catch (Exception) { _limit = 0; }
    }

    // per BuildingType: does it stand partly in water (docks)? Asked once per type.
    private static readonly System.Collections.Generic.Dictionary<IntPtr, bool> WaterTypes = new();

    private static bool NeedsWater(BuildingType type)
    {
        if (type == null) return false;
        if (WaterTypes.TryGetValue(type.Pointer, out bool water)) return water;
        var tiles = type.localPositionsInWater;
        water = tiles != null && tiles.Length > 0;
        WaterTypes[type.Pointer] = water;
        return water;
    }

    internal static void SomewherePostfix(bool __result)
    {
#if DEVLOG
        _searches++; if (!__result) _failed++;
        long now = System.Diagnostics.Stopwatch.GetTimestamp(); long dt = now - _searchStart; if (dt > _slowest) _slowest = dt;
#endif
        _limit = _town;
    }

    // SpiralGrid.Enumerate(int2 center, int size)
    internal static void EnumeratePrefix(ref int size)
    {
        if (_limit > 0 && size > _limit) size = _limit;
    }

#if DEVLOG
    // ---- measuring (Debug builds only): one log line per BuildAndClean call ----
    private static PatchSwitch _clean;
    private static long _turnStart, _searchStart, _slowest;
    private static int _searches, _failed, _cleaned;
    private static string _who;

    private static void StatsBegin(BuildData data)
    {
        _turnStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _searches = _failed = _cleaned = 0; _slowest = 0;
        try { _who = data?.Island?.Owner?.name ?? "?"; } catch { _who = "?"; }
    }

    private static void StatsEnd()
    {
        if (_who == null) return;   // the call was skipped (no hammer): already logged
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _turnStart) * f;
        Plugin.L.LogInfo($"[ai-stats] {_who}: build {ms:0} ms, square {(_town > 0 ? _town : 0)}, placement searches {_searches} ({_failed} failed), nature cleared {_cleaned}, slowest search {_slowest * f:0} ms");
        _who = null;
    }

    // AIPlay.CleanNature(...) returns true when it cleared a tile
    internal static void CleanPostfix(bool __result) { if (__result) _cleaned++; }
#endif
}