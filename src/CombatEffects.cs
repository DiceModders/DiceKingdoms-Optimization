using DiceModders.ModCore.Tools;

namespace DiceModders.Optimization;

/// <summary>
/// Combat visuals that cost frames but do not change the simulation:
///  * No combat particles: <c>UnitVisual.SpawnParticles</c> (parry / death / conversion effects) is skipped. In a big battle one of these pooled effects
///    is started for almost every hit.
///  * Unit animation every N frames: <c>UnitVisual.Update</c> runs every frame for every unit (position, spring physics, four Animator parameters).
///    With 100+ units that is the biggest per-frame cost of a battle. Updating each unit only every 2nd..4th frame (staggered, so the units do not all
///    skip the same frame) cuts it to a half .. a quarter. A unit's position always comes from the simulation, so skipping only makes movement a
///    little less smooth on this computer.
/// Both patches are only in the game while their option is on (a patch on a per-unit, per-frame method costs time even when it does nothing).
/// The simulation itself (<c>CombatGrid.SimulationStep</c>) is deterministic lock-step logic every machine must run identically; it is never touched.
/// </summary>
internal static class CombatEffects
{
    private static PatchSwitch _particles, _update;

    internal static void Install(Patcher patcher)
    {
        _particles = patcher.Switch("UnitVisual", "SpawnParticles", typeof(CombatEffects), prefix: nameof(SpawnParticlesPrefix));
        _update = patcher.Switch("UnitVisual", "Update", typeof(CombatEffects), prefix: nameof(UnitUpdatePrefix));
        Options.NoParticles.SettingChanged += (_, _) => Refresh();
        Options.UnitUpdateEvery.SettingChanged += (_, _) => Refresh();
    }

    /// <summary>Called by the option events and the mod switch.</summary>
    internal static void Refresh()
    {
        _particles.On = Plugin.On && Options.NoParticles.Value;
        _update.On = Plugin.On && Options.UnitUpdateEvery.Value > 1;
    }

    // UnitVisual.SpawnParticles(ParticleSystem prefab): only patched while "no particles" is on
    internal static bool SpawnParticlesPrefix() => false;

    // UnitVisual.Update(): only patched while the unit animation runs every 2nd..4th frame
    internal static bool UnitUpdatePrefix(object __instance)
    {
        int n = Options.UnitUpdateEvery.Value;
        int phase = (int)((long)Mem.PtrOf(__instance) >> 5);   // each unit its own phase, from its (stable) native address
        return (UnityEngine.Time.frameCount + phase) % n == 0;
    }
}
