using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using DiceModders.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace DiceModders.Optimization;

/// <summary>
/// Cheaper rendering for weak computers. Everything is reversible and re-applied to objects that appear later (new scene, new match).
///  * Shadows off:        every Light's shadow mode is set to None (the original mode is remembered per light).
///  * Post-processing off: every HDRP Volume (bloom, colour grading, ambient occlusion ...) is disabled.
///  * Texture quality:    QualitySettings.masterTextureLimit (0 full, 1 half, 2 quarter, 3 eighth resolution) - less video memory and bandwidth.
/// The game's own Video settings screen keeps working; where it sets the same property last, the later change wins (the options here are
/// re-applied every few seconds while they are on).
/// </summary>
internal static class GraphicsLite
{
    internal static Toggle ShadowsOff, PostFxOff;
    internal static ConfigEntry<int> TextureStep;

    private static readonly Dictionary<int, LightShadows> LightOriginal = new();
    private static readonly Dictionary<int, bool> VolumeOriginal = new();
    private static int _savedTextureLimit = -1;
    private static long _nextApply;

    internal static int LightsChanged, VolumesChanged;

    internal static void Load(ConfigFile config, Hotkeys keys)
    {
        var L = ModPlugin.L;
        ShadowsOff = new Toggle("Shadows off", config.Bind("Graphics", "ShadowsOff", false, "Switch off the shadows of all lights.").Value, L);
        PostFxOff = new Toggle("Post-processing off", config.Bind("Graphics", "PostProcessingOff", false, "Disable all post-processing volumes.").Value, L);
        TextureStep = config.Bind("Graphics", "TextureStep", 0,
            new ConfigDescription("0 = full textures, 1 = half, 2 = quarter, 3 = eighth resolution.", new AcceptableValueRange<int>(0, 3)));

        keys.Bind(config, "ShadowsOff", HotkeyKey.J, "Toggle: shadows off", ShadowsOff.Flip, "Shadows off", () => ShadowsOff.On);
        keys.Bind(config, "PostFxOff", HotkeyKey.K, "Toggle: post-processing off", PostFxOff.Flip, "Post-processing off", () => PostFxOff.On);
    }

    internal static string TextureText() => TextureStep.Value switch
    {
        0 => "full",
        1 => "half resolution",
        2 => "quarter resolution",
        _ => "eighth resolution",
    };

    internal static bool AnyOn => ShadowsOff.On || PostFxOff.On || TextureStep.Value > 0;

    /// <summary>Everything on at the strongest setting that still looks like the game.</summary>
    internal static void LowEndPreset()
    {
        if (!ShadowsOff.On) ShadowsOff.Flip();
        if (!PostFxOff.On) PostFxOff.Flip();
        TextureStep.Value = 1;
        CombatEffects.UnitUpdateEvery.Value = 2;
        if (!CombatEffects.NoParticles.On) CombatEffects.NoParticles.Flip();
    }

    internal static void RestoreDefaults()
    {
        if (ShadowsOff.On) ShadowsOff.Flip();
        if (PostFxOff.On) PostFxOff.Flip();
        TextureStep.Value = 0;
        CombatEffects.UnitUpdateEvery.Value = 1;
        if (CombatEffects.NoParticles.On) CombatEffects.NoParticles.Flip();
        Apply(true);
    }

    /// <summary>Called every frame by the mod's ticker; does real work at most every 3 seconds (or at once when <paramref name="force"/>).</summary>
    internal static void Apply(bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now < _nextApply) return;
        _nextApply = now + 3000;

        try
        {
            ApplyQuality();
            ApplyLights();
            ApplyVolumes();
        }
        catch (Exception e)
        {
            ModPlugin.L.LogWarning("[graphics] " + e.GetType().Name + ": " + e.Message);
            _nextApply = now + 30000;      // do not spam
        }
    }

    private static void ApplyQuality()
    {
        int wanted = TextureStep.Value;
        if (wanted > 0)
        {
            if (_savedTextureLimit < 0) _savedTextureLimit = QualitySettings.masterTextureLimit;
            if (QualitySettings.masterTextureLimit != wanted) QualitySettings.masterTextureLimit = wanted;
        }
        else if (_savedTextureLimit >= 0)
        {
            QualitySettings.masterTextureLimit = _savedTextureLimit;
            _savedTextureLimit = -1;
        }
    }

    private static void ApplyLights()
    {
        if (!ShadowsOff.On && LightOriginal.Count == 0) return;
        var lights = UnityEngine.Object.FindObjectsOfType<Light>();
        if (lights == null) return;

        foreach (var light in lights)
        {
            if (light == null) continue;
            int id = light.GetInstanceID();
            if (ShadowsOff.On)
            {
                if (light.shadows == LightShadows.None) continue;
                LightOriginal[id] = light.shadows;
                light.shadows = LightShadows.None;
                LightsChanged++;
            }
            else if (LightOriginal.TryGetValue(id, out var original))
            {
                light.shadows = original;
            }
        }
        if (!ShadowsOff.On) LightOriginal.Clear();
    }

    private static void ApplyVolumes()
    {
        if (!PostFxOff.On && VolumeOriginal.Count == 0) return;
        var volumes = UnityEngine.Object.FindObjectsOfType<Volume>();
        if (volumes == null) return;

        foreach (var volume in volumes)
        {
            if (volume == null) continue;
            int id = volume.GetInstanceID();
            if (PostFxOff.On)
            {
                if (!volume.enabled) continue;
                VolumeOriginal[id] = true;
                volume.enabled = false;
                VolumesChanged++;
            }
            else if (VolumeOriginal.TryGetValue(id, out bool wasEnabled))
            {
                volume.enabled = wasEnabled;
            }
        }
        if (!PostFxOff.On) VolumeOriginal.Clear();
    }
}
