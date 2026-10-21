using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DiceModders.Shared;
using DiceModders.UiKit;
using UnityEngine;

namespace DiceModders.Optimization;

// Inside namespace DiceModders.* the bare name "UiKit" would be the NAMESPACE DiceModders.UiKit; this alias makes it the static class again.
using UiKit = DiceModders.UiKit.UiKit;

/// <summary>
/// DiceKingdoms Optimization: options for weaker computers and big battles (50-100+ units attacking). All switches are in
/// Settings > Video > Optimization. Nothing here changes the simulation, so players with and without this mod can play together.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.UiKit")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class ModPlugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.optimization";
    public const string Name = "DiceKingdoms Optimization";
    public const string Version = "1.0.0";

    internal static ManualLogSource L;
    internal static Hotkeys Keys;

    public override void Load()
    {
        L = Log;
        Keys = new Hotkeys(Config, L, Name);

        var patcher = new Patcher(Guid, L);
        CombatEffects.Load(Config, Keys, patcher);
        GraphicsLite.Load(Config, Keys);
        patcher.Summary();

        UiKit.Settings.Add(SettingsTab.Video, "Optimization", s =>
        {
            s.Info(() => "Now: " + FrameStats.Text());
            s.Button("Presets", "Low-end", () => { GraphicsLite.LowEndPreset(); GraphicsLite.Apply(true); });
            s.Button("", "Restore", GraphicsLite.RestoreDefaults);

            s.Header("Battles");
            s.Toggle("No combat particles", () => CombatEffects.NoParticles.On, v => Set(CombatEffects.NoParticles, v));
            s.Stepper("Unit update every Nth frame", CombatEffects.UnitUpdateText, d => CombatEffects.StepUnitUpdate(d));
            s.Info("1 = normal. A higher number makes units update their pictures less often (the fight itself is not changed).");
            s.Info(() => $"Skipped: {CombatEffects.ParticlesSkipped} effects, {CombatEffects.UnitUpdatesSkipped} updates");

            s.Header("Graphics");
            s.Toggle("Shadows off", () => GraphicsLite.ShadowsOff.On, v => { Set(GraphicsLite.ShadowsOff, v); GraphicsLite.Apply(true); });
            s.Toggle("Post-processing off", () => GraphicsLite.PostFxOff.On, v => { Set(GraphicsLite.PostFxOff, v); GraphicsLite.Apply(true); });
            s.Stepper("Texture quality", GraphicsLite.TextureText, d =>
            {
                GraphicsLite.TextureStep.Value = Math.Clamp(GraphicsLite.TextureStep.Value + d, 0, 3);
                GraphicsLite.Apply(true);
            });
            s.Info("These options only change how the game looks on your computer, so they are safe with players who do not have this mod.");
        });

        Keys.Bind(Config, "LowEndPreset", HotkeyKey.End, "Apply the low-end preset", () => { GraphicsLite.LowEndPreset(); GraphicsLite.Apply(true); }, "Low-end preset");
        Keys.Bind(Config, "RestoreDefaults", HotkeyKey.Delete, "Switch every option off", GraphicsLite.RestoreDefaults, "Restore defaults");
        Keys.Bind(Config, "ShowStats", HotkeyKey.Insert, "Print the current frame rate and the skip counters to the log",
            () => L.LogMessage($"{FrameStats.Text()}; particles skipped {CombatEffects.ParticlesSkipped}/{CombatEffects.ParticleCalls}, unit updates skipped {CombatEffects.UnitUpdatesSkipped}/{CombatEffects.UnitUpdatesSeen}"),
            "Print frame rate and counters");

        Keys.LogBindings();
        AddComponent<OptimizationTicker>();
    }

    private static void Set(Toggle t, bool wanted)
    {
        if (t.On != wanted) t.Flip();
    }
}

public sealed class OptimizationTicker : MonoBehaviour
{
    public OptimizationTicker(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ModPlugin.Keys?.Poll();
        FrameStats.Tick();
        GraphicsLite.Apply(false);
    }
}
