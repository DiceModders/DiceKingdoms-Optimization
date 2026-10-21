using BepInEx.Configuration;
using DiceModders.Shared;

namespace DiceModders.Optimization;

/// <summary>
/// Combat visuals that cost frames but do not change the simulation:
///  * "No combat particles": UnitVisual.SpawnParticles (parry / death / conversion effects) is skipped. In a big battle one of these pooled
///    effects is started for almost every unit hit.
///  * "Unit animation updates every N frames": UnitVisual.Update runs once per rendered frame for every unit and recomputes its position,
///    spring physics and four Animator parameters. With 100+ units that is the biggest per-frame cost of a battle. Updating each unit only every
///    2nd..4th frame (staggered, so not all units skip the same frame) cuts that cost to a half .. quarter. A unit's position is always
///    taken from the simulation, so a skipped frame only makes its movement a little less smooth - nothing the other players depend on.
///
/// The simulation itself (CombatGrid.SimulationStep) is deterministic lock-step logic that every machine must run identically, so it is never touched.
/// </summary>
internal static class CombatEffects
{
    internal static Toggle NoParticles;
    internal static ConfigEntry<int> UnitUpdateEvery;

    internal static long ParticleCalls, ParticlesSkipped, UnitUpdatesSeen, UnitUpdatesSkipped;

    internal static void Load(ConfigFile config, Hotkeys keys, Patcher patcher)
    {
        NoParticles = new Toggle("No combat particles",
            config.Bind("Combat", "NoParticles", false, "Skip the parry / death / conversion particle effects of units.").Value, ModPlugin.L);
        UnitUpdateEvery = config.Bind("Combat", "UnitUpdateEveryNFrames", 1,
            new ConfigDescription("Update each unit's visuals only every N-th rendered frame (1 = every frame, the game's behaviour).",
                new AcceptableValueRange<int>(1, 4)));

        keys.Bind(config, "NoParticles", HotkeyKey.F8, "Toggle: skip unit combat particles", NoParticles.Flip, "No combat particles", () => NoParticles.On);
        keys.Bind(config, "UnitUpdateMore", HotkeyKey.PageUp, "Update unit visuals less often (every 1..4 frames)", () => StepUnitUpdate(+1), "Unit updates: less often");
        keys.Bind(config, "UnitUpdateLess", HotkeyKey.PageDown, "Update unit visuals more often", () => StepUnitUpdate(-1), "Unit updates: more often");

        patcher.Patch("UnitVisual", "SpawnParticles", typeof(CombatEffects), prefix: nameof(SpawnParticlesPrefix));
        patcher.Patch("UnitVisual", "Update", typeof(CombatEffects), prefix: nameof(UnitUpdatePrefix));
    }

    internal static void StepUnitUpdate(int delta)
    {
        UnitUpdateEvery.Value = System.Math.Clamp(UnitUpdateEvery.Value + delta, 1, 4);
        ModPlugin.L.LogMessage("Unit visual updates: " + UnitUpdateText());
    }

    /// <summary>Short on purpose: it sits between the stepper's - and + buttons.</summary>
    internal static string UnitUpdateText() => UnitUpdateEvery.Value.ToString();

    // UnitVisual.SpawnParticles(ParticleSystem prefab)
    internal static bool SpawnParticlesPrefix()
    {
        ParticleCalls++;
        if (!NoParticles.On) return true;
        ParticlesSkipped++;
        return false;
    }

    // UnitVisual.Update()
    internal static bool UnitUpdatePrefix(object __instance)
    {
        int n = UnitUpdateEvery.Value;
        if (n <= 1) return true;
        UnitUpdatesSeen++;
        // stagger: each unit has its own phase, derived from its (stable) native address
        int phase = (int)((long)Mem.PtrOf(__instance) >> 5);
        if ((UnityEngine.Time.frameCount + phase) % n == 0) return true;
        UnitUpdatesSkipped++;
        return false;
    }
}
