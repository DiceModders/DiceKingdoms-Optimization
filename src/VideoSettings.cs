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
        Tx.Block(ui, () => new SettingsBlockSpec
        {
            Id = Plugin.Guid + ".settings",
            Page = SettingsPage.Video,
            Title = Tx.T("block.title"),
            Order = 10,
            Build = block =>
            {
                Toggle(block, Tx.T("noParticles"), Options.NoParticles);
                var every = Options.UnitUpdateEvery;
                var row = block.AddSlider(Tx.T("unitEvery"), 1, 4, every.Value, v => every.Value = (int)v, "0", true);
                if (row != null) every.SettingChanged += (_, _) => row.SetValue(every.Value);
                Toggle(block, Tx.T("warmUp"), Options.WarmUpFiles);
                Toggle(block, Tx.T("fastPlayerList"), Options.FastPlayerList);
                Toggle(block, Tx.T("aiNearTown"), Options.AiSearchNearTown);
                Toggle(block, Tx.T("aiFast"), Options.AiFastSearch);
                block.AddInfo(Tx.T("info"));
            },
        });
    }

    private static void Toggle(SettingsBlockHandle block, string label, ConfigEntry<bool> entry)
    {
        var row = block.AddToggle(label, entry.Value, v => entry.Value = v);
        if (row != null) entry.SettingChanged += (_, _) => row.SetValue(entry.Value);
    }
}
