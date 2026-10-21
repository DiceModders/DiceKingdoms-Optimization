# Shared helpers for build.ps1 / build-dev.ps1 / link-*.ps1. Dot-sourced; ASCII only, Windows PowerShell 5.1 compatible.

function Get-GameDir {
    param([string]$GameDir, [string]$Root)
    $gameDirFile = Join-Path $Root ".gamedir"          # git-ignored; remembers your game folder
    if ($GameDir) { Set-Content -Path $gameDirFile -Value $GameDir -Encoding UTF8 }
    elseif (Test-Path $gameDirFile) { $GameDir = (Get-Content $gameDirFile -Raw).Trim() }
    elseif ($env:DICEKINGDOMS_DIR) { $GameDir = $env:DICEKINGDOMS_DIR }
    if (-not $GameDir) { throw "Game folder unknown. Run once:  .\scripts\build.ps1 -GameDir `"D:\...\Dice-Kingdoms`"" }
    $exe = Join-Path $GameDir "Dice-Kingdoms.exe"
    $plugins = Join-Path $GameDir "BepInEx\plugins"
    if (-not (Test-Path $exe)) { throw "Dice-Kingdoms.exe not found in '$GameDir'." }
    if (-not (Test-Path $plugins)) { throw "'$plugins' does not exist. Install BepInEx 6 IL2CPP and run the game once first." }
    return $GameDir
}

function Set-CfgValue {
    # Sets "Key = Value" inside [Section] of a BepInEx .cfg file (adds the key / section when it is missing).
    param([string]$Path, [string]$Section, [string]$Key, [string]$Value)
    if (-not (Test-Path $Path)) { return $false }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Get-Content $Path)) { [void]$lines.Add($l) }
    $inSection = $false
    $sectionIndex = -1
    $done = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t -match '^\[(.+)\]$') {
            $inSection = ($Matches[1] -eq $Section)
            if ($inSection) { $sectionIndex = $i }
            continue
        }
        if ($inSection -and ($t -match ('^' + [regex]::Escape($Key) + '\s*='))) {
            $lines[$i] = "$Key = $Value"
            $done = $true
            break
        }
    }
    if (-not $done) {
        if ($sectionIndex -ge 0) { $lines.Insert($sectionIndex + 1, "$Key = $Value") }
        else { [void]$lines.Add(""); [void]$lines.Add("[$Section]"); [void]$lines.Add("$Key = $Value") }
    }
    Set-Content -Path $Path -Value $lines -Encoding UTF8
    return $true
}

function Set-BepInExLogging {
    # $On = $true : BepInEx console window + LogOutput.log (development).  $On = $false : no console, no log file (normal use).
    param([string]$GameDir, [bool]$On)
    $cfg = Join-Path $GameDir "BepInEx\config\BepInEx.cfg"
    if (-not (Test-Path $cfg)) {
        Write-Warning "BepInEx.cfg not found (run the game once so BepInEx creates it), console / log settings were not changed."
        return
    }
    $v = "false"
    if ($On) { $v = "true" }
    [void](Set-CfgValue $cfg "Logging.Console" "Enabled" $v)
    [void](Set-CfgValue $cfg "Logging.Disk" "Enabled" $v)
    if ($On) { Write-Host "BepInEx console window and log file: ON (from the next game start)." -ForegroundColor Yellow }
    else { Write-Host "BepInEx console window and log file: OFF (from the next game start)." -ForegroundColor Green }
}

function Initialize-Links {
    # makes sure the interop junction (and, when the mod uses it, libs\DiceModders.UiKit.dll) exist
    param([string]$Root, [string]$GameDir)
    if (-not (Test-Path (Join-Path $Root "interop\UnityEngine.CoreModule.dll"))) { & (Join-Path $PSScriptRoot "link-interop.ps1") $GameDir }
    if (-not (Test-Path (Join-Path $Root "libs\DiceModders.UiKit.dll"))) { & (Join-Path $PSScriptRoot "link-uikit.ps1") -GameDir $GameDir }
}

function Build-Mod {
    param([string]$Root, [string]$Configuration)
    dotnet build (Join-Path $Root "DiceModders.Optimization.csproj") -c $Configuration --nologo -v q "-p:NoWarn=NU1603"
    if ($LASTEXITCODE -ne 0) { throw "Build failed (see errors above)." }
}

function Install-Mod {
    param([string]$Root, [string]$Configuration, [string]$Plugins)
    $dllName = "DiceModders.Optimization.dll"
    $dll = Get-ChildItem $Root -Recurse -Filter $dllName |
        Where-Object { $_.FullName -match ("\\bin\\" + $Configuration + "\\") } | Select-Object -First 1
    if (-not $dll) { throw "$dllName not found after the build." }
    Copy-Item $dll.FullName $Plugins -Force
    Write-Host "  installed $dllName"
}
