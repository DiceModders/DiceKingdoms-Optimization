# Copies DiceModders.UiKit.dll into <repo>\libs so the mod can be compiled against it.
# UiKit is a separate BepInEx plugin (https://github.com/DiceModders/UIKit_Another_Version): build it once, put it into <game>\BepInEx\plugins,
# then run this script. Players also need that DLL in BepInEx\plugins - the mod does NOT contain a copy of it.
# Usage:  .\scripts\link-uikit.ps1                         (takes <game>\BepInEx\plugins\DiceModders.UiKit.dll)
#         .\scripts\link-uikit.ps1 -Dll "D:\...\DiceModders.UiKit.dll"
param([string]$GameDir, [string]$Dll)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Dll) {
    . (Join-Path $PSScriptRoot "common.ps1")
    $GameDir = Get-GameDir $GameDir $root
    $Dll = Join-Path $GameDir "BepInEx\plugins\DiceModders.UiKit.dll"
}
if (-not (Test-Path $Dll)) { throw "UiKit not found at '$Dll'. Build UiKit and copy DiceModders.UiKit.dll into BepInEx\plugins, or pass -Dll." }
$libs = Join-Path $root "libs"
New-Item -ItemType Directory -Force -Path $libs | Out-Null
Copy-Item $Dll (Join-Path $libs "DiceModders.UiKit.dll") -Force
Write-Host "libs\DiceModders.UiKit.dll <- $Dll"
