<#
.SYNOPSIS
  Builds this mod, installs it into the game's BepInEx\plugins folder and sets BepInEx logging.
  Normal build (Release): BepInEx logging left as it is.  -Dev (Debug, defines DEVLOG): console window and LogOutput.log ON.  -Quiet: both OFF.

.EXAMPLE
  .\scripts\build.ps1 -GameDir "<your Dice-Kingdoms folder>"   # first time: remembers the folder
  .\scripts\build.ps1                                           # afterwards
  .\scripts\build.ps1 -Dev -Run                                 # debug build with logging, then start the game

  If PowerShell refuses to run scripts:  powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
#>
param([string]$GameDir, [switch]$Dev, [switch]$Run, [switch]$Quiet)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "common.ps1")
$GameDir = Get-GameDir $GameDir $root
$plugins = Join-Path $GameDir "BepInEx\plugins"
$project = Get-Project $root
$config = if ($Dev) { "Debug" } else { "Release" }

if (Get-Process -Name "Dice-Kingdoms" -ErrorAction SilentlyContinue) { throw "Close the game first: it locks the DLLs." }
Initialize-Links $root $GameDir $project.FullName

dotnet build $project.FullName -c $config --nologo -v q "-p:NoWarn=NU1603"
if ($LASTEXITCODE -ne 0) { throw "Build failed (see errors above)." }

$dll = Join-Path $root ("bin\$config\net6.0\" + $project.BaseName + ".dll")
Copy-Item $dll $plugins -Force
Write-Host ("installed " + $project.BaseName + ".dll") -ForegroundColor Green
# a normal install leaves logging alone (it used to switch the log file off on every install, hiding the next run's log)
if ($Dev) { Set-BepInExLogging $GameDir $true } elseif ($Quiet) { Set-BepInExLogging $GameDir $false }
if ($Run) { Start-Process "steam://rungameid/1501690" }   # through Steam, so Steam does not inherit BepInEx's start-up variables
