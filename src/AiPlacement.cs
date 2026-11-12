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
    }

    internal static void Refresh()
    {
        bool on = Plugin.On && Options.AiSearchNearTown.Value;
        _turn.On = on;
        _somewhere.On = on;
        _spiral.On = on;
        _town = _limit = 0;
    }

    // AIPlay.BuildAndClean(BuildData data, Player.AIState state, List<BuildingType> buildingToPlace): one AI's build turn
    internal static void TurnPrefix(BuildData data)
    {
        _town = _coast = _limit = 0;
        try
        {
            var grid = data?.Island?.grid;
            if (grid == null) return;
            int side = grid.size.x;
            if (side <= NormalSide) return;
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
    }

    internal static void TurnPostfix() => _town = _coast = _limit = 0;

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

    internal static void SomewherePostfix() => _limit = _town;

    // SpiralGrid.Enumerate(int2 center, int size)
    internal static void EnumeratePrefix(ref int size)
    {
        if (_limit > 0 && size > _limit) size = _limit;
    }
}
