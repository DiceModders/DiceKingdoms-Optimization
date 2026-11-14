using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DiceModders.ModCore;
using DiceModders.ModCore.Tools;

namespace DiceModders.Optimization;

using Core = DiceModders.ModCore.ModCore;   // inside DiceModders.* the bare name would be the namespace

/// <summary>
/// DiceKingdoms Optimization: options for big battles and hard disks. All switches are in Settings > Video > Optimization
/// (and in the config file / an in-game config editor). Nothing here changes the simulation, so players with and without this mod can play together.
/// Nothing runs every frame except the game's own code.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.ModCore")]
[BepInDependency("DiceModders.UiKit")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.optimization";
    public const string Name = "DiceKingdoms Optimization";
    public const string Version = "2.0.0";

    internal static ManualLogSource L;
    /// <summary>The mod's own on/off switch ([General] Enabled); while off every option behaves like the game.</summary>
    internal static bool On;

    public override void Load()
    {
        L = Log;
        Options.Bind(Config);

        var patcher = new Patcher(Guid, Log);
        CombatEffects.Install(patcher);
        AiPlacement.Install(patcher);
        SummaryCache.Install(patcher);
        patcher.Summary();
        // switched from Settings > Video or a config editor: applied at once
        Options.FastPlayerList.SettingChanged += (_, _) => SummaryCache.Refresh();
        Options.AiFastSearch.SettingChanged += (_, _) => AiSearch.Refresh();
        Options.AiFastSearchVerify.SettingChanged += (_, _) => AiSearch.Refresh();

        DiskWarmup.Install();
        Core.Mods.Switch(Config, description: "Turns every option of this mod on or off at once.")
            .WhenOn(() => { On = true; Refresh(); }, () => { On = false; Refresh(); });

        VideoSettings.Install(DiceModders.UiKit.UiKit.CreateScope(Guid));
    }

    /// <summary>Puts every switchable patch in line with the options and the mod switch.</summary>
    private static void Refresh()
    {
        CombatEffects.Refresh();
        AiPlacement.Refresh();
        SummaryCache.Refresh();
        AiSearch.Refresh();
    }
}

