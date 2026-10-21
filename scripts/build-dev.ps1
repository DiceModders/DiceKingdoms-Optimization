<#
.SYNOPSIS
  DEVELOPMENT BUILD (debugging, logging): builds Optimization in Debug mode (defines DEVLOG: extra diagnostics), installs it and switches the
  BepInEx console window and log file ON (BepInEx\LogOutput.log). Go back to the normal state with scripts\build.ps1.

.EXAMPLE
  .\scripts\build-dev.ps1
  .\scripts\build-dev.ps1 -Run
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
try { Build-Mod $root "Debug" } finally { Pop-Location }
Install-Mod $root "Debug" $plugins
if (-not (Test-Path (Join-Path $plugins "DiceModders.UiKit.dll"))) { Write-Warning "DiceModders.UiKit.dll is not in BepInEx\plugins: this mod needs UiKit to load." }
Set-BepInExLogging $GameDir $true
Write-Host "Done (DEBUG build + logging). Normal state again: .\scripts\build.ps1" -ForegroundColor Green
if ($Run) { Start-Process -FilePath (Join-Path $GameDir "Dice-Kingdoms.exe") -WorkingDirectory $GameDir }
