using System;
using System.Collections.Generic;
using DiceModders.ModCore.Tools;

namespace DiceModders.Optimization;

using Island = DiceKingdoms.Game.Island;
using GameGrid = DiceKingdoms.Game.IslandGrid;

/// <summary>
/// The player list on the left (one <c>PlayerSummary</c> row per player) recomputes three numbers in EVERY frame: attack power, defence power and
/// population (<c>PowerConstants.AttackPower</c>, <c>DefensePower</c>, <c>IslandGrid.Population</c>). Each one walks all buildings of the island, and
/// the storage is decrypted twice: with 12 players on big islands that was ~5 % of all frame time. While a row updates, these three answers are reused
/// for a short time (<see cref="MaxAge"/>) instead of being computed again; the rows are spread so they do not all recompute in the same frame, and
/// everything is recomputed when a phase begins or ends. Only the player list is affected: the same methods called by the game's logic (the computer
/// players' attack and defence scores) always compute. Display only, so it cannot desync a match.
/// </summary>
internal static class SummaryCache
{
    private const double MaxAge = 0.25;   // seconds a shown number may be old

    private static PatchSwitch _row, _attack, _defense, _population;
    private static bool _inRow;
    private static readonly Dictionary<IntPtr, (double at, float value)> Attack = new(), Defense = new();
    private static readonly Dictionary<IntPtr, (double at, int value)> Population = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    internal static void Install(Patcher patcher)
    {
        _row = patcher.Switch("PlayerSummary", "Update", typeof(SummaryCache), prefix: nameof(RowPrefix), postfix: nameof(RowPostfix));
        _attack = patcher.Switch("PowerConstants", "AttackPower", typeof(SummaryCache), prefix: nameof(AttackPrefix), postfix: nameof(AttackPostfix));
        _defense = patcher.Switch("PowerConstants", "DefensePower", typeof(SummaryCache), prefix: nameof(DefensePrefix), postfix: nameof(DefensePostfix));
        _population = patcher.Switch("IslandGrid", "get_Population", typeof(SummaryCache), prefix: nameof(PopulationPrefix), postfix: nameof(PopulationPostfix));
        DiceModders.ModCore.ModCore.Match.PhaseEntered += _ => Clear();
        DiceModders.ModCore.ModCore.Match.PhaseSynced += _ => Clear();
        DiceModders.ModCore.ModCore.Match.Ended += Clear;
        DiceModders.ModCore.ModCore.Match.Reset += Clear;
    }

    internal static void Refresh()
    {
        bool on = Plugin.On && Options.FastPlayerList.Value;
        _row.On = _attack.On = _defense.On = _population.On = on;
        Clear();
    }

    private static void Clear() { Attack.Clear(); Defense.Clear(); Population.Clear(); }

    private static double Now => Clock.Elapsed.TotalSeconds;

    /// <summary>A stored value is fresh for <see cref="MaxAge"/> seconds, with a per-island offset so the rows recompute in different frames.</summary>
    private static bool Fresh(double at, IntPtr key) => Now - at < MaxAge * (0.75 + 0.5 * ((key.ToInt64() >> 4) & 7) / 7.0);

    internal static void RowPrefix() => _inRow = true;
    internal static void RowPostfix() => _inRow = false;

    // PowerConstants.AttackPower(Island) / DefensePower(Island): float
    internal static bool AttackPrefix(Island island, ref float __result) => Use(Attack, island, ref __result);
    internal static void AttackPostfix(Island island, float __result) => Store(Attack, island, __result);
    internal static bool DefensePrefix(Island island, ref float __result) => Use(Defense, island, ref __result);
    internal static void DefensePostfix(Island island, float __result) => Store(Defense, island, __result);

    // IslandGrid.Population: int
    internal static bool PopulationPrefix(GameGrid __instance, ref int __result)
    {
        if (!_inRow || __instance == null) return true;
        if (Population.TryGetValue(__instance.Pointer, out var e) && Fresh(e.at, __instance.Pointer)) { __result = e.value; return false; }
        return true;
    }

    internal static void PopulationPostfix(GameGrid __instance, int __result)
    {
        if (_inRow && __instance != null) Population[__instance.Pointer] = (Now, __result);
    }

    private static bool Use(Dictionary<IntPtr, (double at, float value)> map, Island island, ref float result)
    {
        if (!_inRow || island == null) return true;   // the game's own logic always computes
        if (map.TryGetValue(island.Pointer, out var e) && Fresh(e.at, island.Pointer)) { result = e.value; return false; }
        return true;
    }

    private static void Store(Dictionary<IntPtr, (double at, float value)> map, Island island, float result)
    {
        if (_inRow && island != null) map[island.Pointer] = (Now, result);
    }
}
