<#
.SYNOPSIS
  NORMAL BUILD (releases / playing): builds Optimization in Release mode, installs it into the game's BepInEx\plugins folder and
  switches the BepInEx console window and log file OFF. Nothing else is written. For debugging use scripts\build-dev.ps1.

.EXAMPLE
  .\scripts\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Dice-Kingdoms"   # first time: remembers the path
  .\scripts\build.ps1                                                             # afterwards
  .\scripts\build.ps1 -Run                                                        # build, install and start the game

  If PowerShell refuses to run scripts:  powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
#>
param(
    [string]$GameDir,
    [switch]$Run
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "common.ps1")
$GameDir = Get-GameDir $GameDir $root
$plugins = Join-Path $GameDir "BepInEx\plugins"

Initialize-Links $root $GameDir
if (Get-Process -Name "Dice-Kingdoms" -ErrorAction SilentlyContinue) {
    Write-Warning "The game is running: Windows may refuse to overwrite loaded DLLs. Close the game, then run this again."
}

Push-Location $root
try { Build-Mod $root "Release" } finally { Pop-Location }
Install-Mod $root "Release" $plugins
if (-not (Test-Path (Join-Path $plugins "DiceModders.UiKit.dll"))) { Write-Warning "DiceModders.UiKit.dll is not in BepInEx\plugins: this mod needs UiKit to load." }
Set-BepInExLogging $GameDir $false
Write-Host "Done: Optimization installed in $plugins" -ForegroundColor Green
if ($Run) { Start-Process -FilePath (Join-Path $GameDir "Dice-Kingdoms.exe") -WorkingDirectory $GameDir }
