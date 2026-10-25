# Switch the live game between the old BepInEx base and the be.788 test base.
#
# Usage (from repo root, in PowerShell):
#   powershell -ExecutionPolicy Bypass -File tools\switch_bepinex_base.ps1 -Action switch  -FreshInterop
#   powershell -ExecutionPolicy Bypass -File tools\switch_bepinex_base.ps1 -Action switch
#   powershell -ExecutionPolicy Bypass -File tools\switch_bepinex_base.ps1 -Action restore
#   Append -Launch to start the game via Steam after switching.
#
# Notes:
#   - The first "switch" makes a one-time pristine backup of the live base
#     (winhttp.dll / doorstop_config.ini / dotnet / BepInEx\core) and never
#     overwrites it, so "restore" always returns the exact pre-test state.
#   - -FreshInterop wipes BepInEx\interop so be.788 regenerates it from scratch
#     (slower first launch, cleanest reproduction of a fresh Nexus-pack install).
#     Without it, BepInEx decides whether the existing interop is reusable.
#   - Mod plugins, configs, cfg files and watch files under BepInEx are untouched.

param(
    [string]$GameDir = "E:\SteamLibrary\steamapps\common\Survival Log",
    [string]$BaseDir = "E:\Game\Mod\mod_stack\release\SurvivalLog.Base_v1.3.8_be788test",
    [Parameter(Mandatory = $true)][ValidateSet("switch", "restore")][string]$Action,
    [switch]$FreshInterop,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$BackupDir = Join-Path $GameDir "_base_backup_be788test"

function Assert-GameClosed {
    $proc = Get-Process -Name "Survival Log" -ErrorAction SilentlyContinue
    if ($proc) { throw "Game is running (Survival Log.exe). Close it first." }
}

function Copy-Over([string]$from, [string]$toDir) {
    Copy-Item $from $toDir -Recurse -Force
}

function Show-ActiveVersion {
    $dll = Join-Path $GameDir "BepInEx\core\BepInEx.Core.dll"
    if (Test-Path $dll) {
        Write-Host ("[state] active BepInEx core: " + (Get-Item $dll).VersionInfo.ProductVersion)
    }
}

switch ($Action) {

    "switch" {
        if (-not (Test-Path (Join-Path $BaseDir "winhttp.dll"))) { throw "Base dir not found: $BaseDir" }
        Assert-GameClosed

        if (-not (Test-Path (Join-Path $BackupDir "winhttp.dll"))) {
            New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
            Copy-Item (Join-Path $GameDir "winhttp.dll") $BackupDir -Force
            Copy-Item (Join-Path $GameDir "doorstop_config.ini") $BackupDir -Force
            Copy-Over (Join-Path $GameDir "dotnet") $BackupDir
            Copy-Over (Join-Path $GameDir "BepInEx\core") (Join-Path $BackupDir "core")
            Write-Host "[backup] pristine old base saved to: $BackupDir"
        }
        else {
            Write-Host "[backup] already exists, kept untouched: $BackupDir"
        }

        Remove-Item (Join-Path $GameDir "dotnet") -Recurse -Force
        Copy-Over (Join-Path $BaseDir "dotnet") $GameDir

        Remove-Item (Join-Path $GameDir "BepInEx\core") -Recurse -Force
        Copy-Over (Join-Path $BaseDir "BepInEx\core") (Join-Path $GameDir "BepInEx")

        Copy-Item (Join-Path $BaseDir "winhttp.dll") $GameDir -Force
        Copy-Item (Join-Path $BaseDir "doorstop_config.ini") $GameDir -Force

        if ($FreshInterop) {
            if (Test-Path (Join-Path $GameDir "BepInEx\interop")) {
                Remove-Item (Join-Path $GameDir "BepInEx\interop") -Recurse -Force
            }
            Write-Host "[interop] wiped - first launch regenerates from scratch (slower, cleanest test)."
        }
        else {
            Write-Host "[interop] kept - BepInEx decides whether to reuse or regenerate."
        }
        Write-Host "[done] be.788 base is now active."
    }

    "restore" {
        if (-not (Test-Path (Join-Path $BackupDir "winhttp.dll"))) { throw "No backup found: $BackupDir" }
        Assert-GameClosed

        Remove-Item (Join-Path $GameDir "dotnet") -Recurse -Force
        Copy-Over (Join-Path $BackupDir "dotnet") $GameDir

        Remove-Item (Join-Path $GameDir "BepInEx\core") -Recurse -Force
        Copy-Over (Join-Path $BackupDir "core") (Join-Path $GameDir "BepInEx")

        Copy-Item (Join-Path $BackupDir "winhttp.dll") $GameDir -Force
        Copy-Item (Join-Path $BackupDir "doorstop_config.ini") $GameDir -Force

        # interop on disk was generated for be.788 - the old loader must not trust it
        if (Test-Path (Join-Path $GameDir "BepInEx\interop")) {
            Remove-Item (Join-Path $GameDir "BepInEx\interop") -Recurse -Force
        }
        Write-Host "[done] old base restored; interop wiped so the old loader regenerates it cleanly."
    }
}

Show-ActiveVersion
if ($Launch) { Start-Process "steam://rungameid/4164790" }
