# DiceKingdoms Optimization (`DiceModders.Optimization.dll`)

Options for weaker computers and for big battles (50-100+ units attacking). A mod for **Dice Kingdoms** (BepInEx 6 IL2CPP). It is one self-contained project: it shares no code with other mods.

Nothing here changes the simulation, so you can play with players who do not have this mod.

## What it does

All switches are in **Settings > Video > Optimization**:

| Option | Effect |
|---|---|
| **No combat particles** | skips spawning the parry / die / convert particle effects |
| **Unit update every Nth frame** (1-4) | unit visuals are updated less often (staggered per unit); 1 = the game's normal speed |
| **Shadows off** | every light's shadows are switched off (the original mode is remembered) |
| **Post-processing off** | every HDRP volume (bloom, colour grading, ambient occlusion ...) is disabled |
| **Texture quality** | full / half / quarter / eighth resolution textures |
| **Presets** | *Low-end* switches everything on at a sensible strength, *Restore* switches everything off |

The game's own Video settings (quality level, V-Sync) keep working; where both change the same property, the later change wins (the options here are re-applied every few seconds while they are on).

## Why a slow player slows everybody

Combat is deterministic lockstep: every computer runs one simulation step per frame, so the whole lobby waits for the slowest player. The simulation itself cannot be made faster or skipped by a mod without desyncing, so this mod only reduces the *visual* work around it, which is what makes a weak computer drop frames.

## Hotkeys (hold Right Ctrl; configurable in the .cfg)

| Key | Action |
|---|---|
| F8 | no combat particles |
| PageUp / PageDown | unit update interval +1 / -1 |
| J / K | shadows off / post-processing off |
| End | low-end preset |
| Delete | restore defaults |
| Insert | print the frame rate and the skip counters to the log |
| Home | print the hotkeys to the log |

To see where a battle spends its frames, use the Extractor's frame-time probe (Right Ctrl + P).

## Code map

`ModPlugin.cs` (plugin + the Settings rows, via UiKit), `CombatEffects.cs` (particles, unit updates), `GraphicsLite.cs` (shadows, post-processing, textures), `FrameStats.cs`, `Common/` (private copy of the small helper classes).

## Requirements

* Dice Kingdoms with **BepInEx 6 IL2CPP (x64)** - see the wiki's [Mods Installation](https://dicemodders.github.io/DiceKingdoms-Modding-Wiki/guides/mod-installation/) guide. Start the game once so BepInEx creates its folders.
* **[UiKit](https://github.com/DiceModders/UIKit_Another_Version)** (`DiceModders.UiKit.dll`) in `BepInEx\plugins`. This mod does **not** contain a copy of it.

## Install (players)

Copy `DiceModders.Optimization.dll` into `BepInEx\plugins` (next to `DiceModders.UiKit.dll`).

## Build (developers)

```powershell
.\scripts\link-interop.ps1 "D:\SteamLibrary\steamapps\common\Dice-Kingdoms"
.\scripts\link-uikit.ps1
.\scripts\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Dice-Kingdoms"
.\scripts\build-dev.ps1     # Debug build: console and log ON
```

## License

MIT - see [LICENSE](LICENSE).
