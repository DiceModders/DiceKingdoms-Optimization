# Helpers for build.ps1 (dot-sourced). The same file in every mod: the mod's name comes from the one .csproj in the repo root.
# ASCII only, Windows PowerShell 5.1 compatible.

function Get-GameDir {
    param([string]$GameDir, [string]$Root)
    $gameDirFile = Join-Path $Root ".gamedir"          # git-ignored; remembers your game folder
    if ($GameDir) { Set-Content -Path $gameDirFile -Value $GameDir -Encoding UTF8 }
    elseif (Test-Path $gameDirFile) { $GameDir = (Get-Content $gameDirFile -Raw).Trim() }
    elseif ($env:DICEKINGDOMS_DIR) { $GameDir = $env:DICEKINGDOMS_DIR }
    if (-not $GameDir) { throw "Game folder unknown. Run once:  .\scripts\build.ps1 -GameDir `"<your Dice-Kingdoms folder>`"" }
    if (-not (Test-Path (Join-Path $GameDir "Dice-Kingdoms.exe"))) { throw "Dice-Kingdoms.exe not found in '$GameDir'." }
    if (-not (Test-Path (Join-Path $GameDir "BepInEx\plugins"))) { throw "BepInEx\plugins does not exist in '$GameDir'. Install BepInEx 6 IL2CPP and run the game once first." }
    return $GameDir
}

function Get-Project {
    param([string]$Root)
    $p = Get-ChildItem $Root -Filter "*.csproj" | Select-Object -First 1
    if (-not $p) { throw "No .csproj in '$Root'." }
    return $p
}

function Get-Libraries {
    # the libraries a mod is compiled against: every <Reference Include="DiceModders.X" ...> in the .csproj
    param([string]$Project)
    return [regex]::Matches((Get-Content $Project -Raw), 'Reference Include="(DiceModders\.[A-Za-z]+)"') | ForEach-Object { $_.Groups[1].Value }
}

function Initialize-Links {
    # interop: a junction to <game>\BepInEx\interop. libs: copies of the libraries from <game>\BepInEx\plugins (players install them there too).
    param([string]$Root, [string]$GameDir, [string]$Project)
    $interop = Join-Path $Root "interop"
    if (-not (Test-Path (Join-Path $interop "UnityEngine.CoreModule.dll"))) {
        $src = Join-Path $GameDir "BepInEx\interop"
        if (-not (Test-Path $src)) { throw "'$src' does not exist: start the game once with BepInEx so it generates the interop assemblies." }
        if (Test-Path $interop) { Remove-Item $interop -Force -Recurse }
        New-Item -ItemType Junction -Path $interop -Target $src | Out-Null
        Write-Host "interop -> $src"
    }
    $libs = Join-Path $Root "libs"
    New-Item -ItemType Directory -Force -Path $libs | Out-Null
    foreach ($lib in (Get-Libraries $Project)) {
        $dll = Join-Path $GameDir "BepInEx\plugins\$lib.dll"
        if (-not (Test-Path $dll)) { throw "$lib.dll is not in BepInEx\plugins. Install that library first (see the README)." }
        Copy-Item $dll (Join-Path $libs "$lib.dll") -Force   # always the installed version: the mod is built against what players run
    }
}

function Set-CfgValue {
    # Sets "Key = Value" inside [Section] of a BepInEx .cfg file (adds the key / section when it is missing).
    param([string]$Path, [string]$Section, [string]$Key, [string]$Value)
    if (-not (Test-Path $Path)) { return $false }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Get-Content $Path)) { [void]$lines.Add($l) }
    $inSection = $false; $sectionIndex = -1; $done = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t -match '^\[(.+)\]$') { $inSection = ($Matches[1] -eq $Section); if ($inSection) { $sectionIndex = $i }; continue }
        if ($inSection -and ($t -match ('^' + [regex]::Escape($Key) + '\s*='))) { $lines[$i] = "$Key = $Value"; $done = $true; break }
    }
    if (-not $done) {
        if ($sectionIndex -ge 0) { $lines.Insert($sectionIndex + 1, "$Key = $Value") }
        else { [void]$lines.Add(""); [void]$lines.Add("[$Section]"); [void]$lines.Add("$Key = $Value") }
    }
    Set-Content -Path $Path -Value $lines -Encoding UTF8
    return $true
}

function Set-BepInExLogging {
    # On: BepInEx console window + LogOutput.log (development). Off: neither (playing).
    param([string]$GameDir, [bool]$On)
    $cfg = Join-Path $GameDir "BepInEx\config\BepInEx.cfg"
    if (-not (Test-Path $cfg)) { Write-Warning "BepInEx.cfg not found (run the game once), console / log settings not changed."; return }
    $v = if ($On) { "true" } else { "false" }
    [void](Set-CfgValue $cfg "Logging.Console" "Enabled" $v)
    [void](Set-CfgValue $cfg "Logging.Disk" "Enabled" $v)
    Write-Host ("BepInEx console window and log file: " + $(if ($On) { "ON" } else { "OFF" }) + " (from the next game start).")
}
