using DiceModders.ModCore;

namespace DiceModders.Optimization;

/// <summary>
/// This mod's texts (texts.json built into the DLL, a DiceModders.Optimization.texts.json next to the DLL wins line by line) in the game's language,
/// through ModCore: the exact language, its base, then English. Ask at draw time, so a language change shows on the next redraw.
/// </summary>
internal static class Tx
{
    internal static TextSet Set;
    internal static void Load() => Set = DiceModders.ModCore.ModCore.Texts.Load();
    internal static string T(string key) => Set?.Get(key) ?? key;
    internal static string T(string key, params object[] values) => Set?.Get(key, values) ?? key;

    /// <summary>
    /// A Settings block that is made again, in the new language, when the game's language changes (UiKit rows keep the text they were made with).
    /// <paramref name="spec"/> is called for every build, so every text in it is read again.
    /// </summary>
    internal static DiceModders.UiKit.Native.SettingsBlockHandle Block(DiceModders.UiKit.UiScope ui, System.Func<DiceModders.UiKit.Native.SettingsBlockSpec> spec)
    {
        if (ui == null) return null;
        var handle = ui.Native.Settings.AddBlock(spec());
        DiceModders.ModCore.ModCore.Texts.LanguageChanged += () =>
        {
            handle?.Dispose();
            handle = ui.Native.Settings.AddBlock(spec());
        };
        return handle;
    }
}
