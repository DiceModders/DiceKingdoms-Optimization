using BepInEx.Configuration;
using DiceModders.UiKit;
using DiceModders.UiKit.Native;

namespace DiceModders.Optimization;

/// <summary>
/// The "Optimization" block in Settings > Video. The rows change the config entries; when an entry changes anywhere else (an in-game config editor)
/// the row follows through the entry's own event. "VerifyFastBuildingSearch" is a testing switch and stays in the config file only.
/// </summary>
internal static class VideoSettings
{
    internal static void Install(UiScope ui)
    {
        ui?.Native.Settings.AddBlock(new SettingsBlockSpec
        {
            Id = Plugin.Guid + ".settings",
            Page = SettingsPage.Video,
            Title = "Optimization",
            Order = 10,
            Build = block =>
            {
                Toggle(block, "No combat particles", Options.NoParticles);
                var every = Options.UnitUpdateEvery;
                var row = block.AddSlider("Unit animation every Nth frame", 1, 4, every.Value, v => every.Value = (int)v, "0", true);
                if (row != null) every.SettingChanged += (_, _) => row.SetValue(every.Value);
                Toggle(block, "Warm up game files (hard disks)", Options.WarmUpFiles);
                Toggle(block, "Player list: numbers a few times a second", Options.FastPlayerList);
                Toggle(block, "Computer players: search near their town", Options.AiSearchNearTown);
                Toggle(block, "Computer players: fast building search", Options.AiFastSearch);
                block.AddInfo("Only this computer changes: safe with players who do not have the mod. Computer players only run on the host.");
            },
        });
    }

    private static void Toggle(SettingsBlockHandle block, string label, ConfigEntry<bool> entry)
    {
        var row = block.AddToggle(label, entry.Value, v => entry.Value = v);
        if (row != null) entry.SettingChanged += (_, _) => row.SetValue(entry.Value);
    }
}
