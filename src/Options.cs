using BepInEx.Configuration;

namespace DiceModders.Optimization;

/// <summary>
/// Every option of the mod, in its config file. The Settings block and in-game config editors both change these; the features listen to them.
/// Shadows, post-processing and texture size are left to the game's own Video quality setting (its Low preset switches them off already).
/// </summary>
internal static class Options
{
    internal static ConfigEntry<bool> NoParticles, WarmUpFiles, AiSearchNearTown, FastPlayerList, AiFastSearch, AiFastSearchVerify;
    internal static ConfigEntry<int> UnitUpdateEvery;

    internal static void Bind(ConfigFile config)
    {
        NoParticles = config.Bind("Battles", "NoParticles", false, "Skip the parry / death / conversion particle effects of units.");
        UnitUpdateEvery = config.Bind("Battles", "UnitUpdateEveryNFrames", 1, new ConfigDescription(
            "Update each unit's animation only every N-th frame (1 = every frame, the game's behaviour). Units still stand where the simulation puts them.",
            new AcceptableValueRange<int>(1, 4)));
        AiSearchNearTown = config.Bind("Computer players", "SearchNearTown", false,
            "On islands bigger than the game's, computer players look for building spots around their town first and only search the whole island when " +
            "nothing fits there. Without it the end of a build phase can freeze the game for a long time on big islands. Host only; normal islands are unchanged.");
        AiFastSearch = config.Bind("Computer players", "FastBuildingSearch", false,
            "Computer players find their building spots with a faster search that follows the game's rules exactly (same spots, same order). " +
            "Removes most of the pause at the end of every build phase with many computer players. Host only.");
        AiFastSearchVerify = config.Bind("Computer players", "VerifyFastBuildingSearch", false,
            "Testing: run the fast search AND the game's search for every building and log any difference (the game's result is used). Slower.");
        FastPlayerList = config.Bind("Interface", "FastPlayerList", false,
            "The player list on the left recomputes each player's attack, defence and population a few times a second instead of in every frame " +
            "(the numbers can be up to a quarter of a second late). Saves several percent of frame time with many players on big islands.");
        WarmUpFiles = config.Bind("Loading", "WarmUpGameFiles", false,
            "At the main menu, read the game's data files once in the background (lowest priority) so the first match loads from memory instead of a hard disk. " +
            "Stops by itself on a fast drive (SSD) or when there is not enough free memory.");
    }
}
