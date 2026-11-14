# DiceKingdoms Optimization (`DiceModders.Optimization.dll`)

Options for big battles and slow hard disks (50-100+ units attacking). A mod for **Dice Kingdoms** (BepInEx 6 IL2CPP).

Nothing here changes the simulation, so you can play with players who do not have this mod.

## What it does

All options are in **Settings > Video > Optimization**:

| Option | Effect |
|---|---|
| **No combat particles** | skips spawning the parry / death / conversion particle effects |
| **Unit animation every Nth frame** (1-4, a slider) | unit visuals are updated less often (spread evenly over the units); 1 = the game's normal speed |
| **Warm up game files** | at the main menu the game's data files are read once in the background, at the lowest priority, so the first match loads from memory instead of a hard disk. It stops by itself on a fast drive (SSD) or when there is not enough free memory |

In the mod's config file (`[Computer players]`):

| Option | Effect |
|---|---|
| **SearchNearTown** (on by default) | a computer player looks for a building spot only around its town (the farthest building plus a margin), and for Docks and Fishing Ships around the nearest coast, instead of trying every tile of the island with every rotation. On a normal island that changes nothing; on a big island (Lobby Rules) it removes the freeze of several seconds on every computer turn. The town still grows outwards as it fills up. The AI runs on the host only and sends its moves like a player, so the other players do not need this mod |
| **FastBuildingSearch** (on by default) | computer players find their building spots with a search that reads the island directly instead of going through the game's slower search. It follows the game's rules exactly: the same spot, the same rotation and the same random numbers. In the first turns, while the disease rule applies, the game's own search is used |
| **VerifyFastBuildingSearch** (off by default) | testing only: runs both searches for every building and logs any difference; the game's result is used. Slower |

In `[Interface]`:

| Option | Effect |
|---|---|
| **FastPlayerList** (on by default) | the player list on the left recomputes each player's attack, defence and population a few times a second and at every phase change, instead of in every frame. Everything else in the list still updates every frame |

Shadows, post-processing and texture size are not here: the game's own Video quality setting already switches them (its Low preset turns shadows and post-processing off).

The whole mod can be switched off in the mod list; everything then goes back to the game's behaviour.

## Why a slow player slows everybody

Combat is deterministic lockstep: every computer runs one simulation step per frame, so the whole lobby waits for the slowest player. The simulation itself cannot be made faster or skipped by a mod without desyncing, so this mod reduces the *visual* work around it, which is what makes a weak computer drop frames.

To see where a battle spends its frames, use the frame-time probe of the [Extractor](https://github.com/DiceModders/DiceKingdoms-Extractor) developer tool.

## Code map

| File | Job |
|---|---|
| `Plugin.cs` | the plugin: binds the options, starts the features, adds the Settings block |
| `Options.cs` | every option (config entries) |
| `VideoSettings.cs` | the block in Settings > Video |
| `CombatEffects.cs` | particles and unit animation updates (patches switched on only while an option needs them) |
| `AiPlacement.cs` | where computer players search for building spots |
| `AiSearch.cs` | the fast building search (replaces the game's search function natively, same results) |
| `SummaryCache.cs` | the player list's attack, defence and population numbers, reused for a short time |
| `DiskWarmup.cs` | reading the game files in the background |

## Requirements

* Dice Kingdoms with **BepInEx 6 IL2CPP (x64)**: see the wiki's [Mods Installation](https://dicemodders.github.io/DiceKingdoms-Modding-Wiki/guides/mod-installation/) guide. Start the game once so BepInEx creates its folders.
* The DiceModders libraries in `BepInEx\plugins`: [ModCore](https://github.com/DiceModders/DiceKingdoms-ModCore) (`DiceModders.ModCore.dll`), [UiKit](https://github.com/DiceModders/UiKit) (`DiceModders.UiKit.dll`) and [ModBridge](https://github.com/DiceModders/DiceKingdoms-ModBridge) (`DiceModders.ModBridge.dll`, shows ModCore's mod list and lobby rules on the game's screens). This mod does not contain copies of them.

## Install (players)

Copy `DiceModders.Optimization.dll` into `BepInEx\plugins`, next to the libraries.

## Build (developers)

Needs the .NET SDK (6 or newer) and PowerShell. Game files are never committed: the script links `BepInEx\interop` from your game folder and copies the libraries from `BepInEx\plugins` into `libs`.

```powershell
.\scripts\build.ps1 -GameDir "<your Dice-Kingdoms folder>"   # first time: remembers the folder
.\scripts\build.ps1                                           # build + install (Release, console and log OFF)
.\scripts\build.ps1 -Dev -Run                                 # Debug build, console and log ON, then start the game
```

Close the game before building: it locks the DLLs. If PowerShell refuses to run scripts: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

## License

MIT, see [LICENSE](LICENSE).
