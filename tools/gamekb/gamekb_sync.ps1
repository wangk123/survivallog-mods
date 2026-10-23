# gamekb_sync.ps1 - one-command gamekb sync after a game update (see docs/07 section 5)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\gamekb\gamekb_sync.ps1 [-AutoProbe]
#   -AutoProbe : also launch the game via steam:// to run ConfigDumpProbe, wait for _DONE.txt,
#                then stop the game and collect tables. Aborts if the game is already running.
param([switch]$AutoProbe)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))   # repo root
$game = $null

# ---- 1. locate game dir: registry -> libraryfolders.vdf -> appmanifest ----
function Find-GameDir {
    $steamPath = (Get-ItemProperty 'HKCU:\SOFTWARE\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steamPath) { $steamPath = 'D:\APP\Steam' }
    $lf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (-not (Test-Path $lf)) { throw "libraryfolders.vdf not found: $lf" }
    $libs = @((Get-Content $lf | Select-String '"path"\s+"([^"]+)"').Matches | ForEach-Object { $_.Groups[1].Value })
    foreach ($lib in $libs) {
        $mani = Join-Path $lib 'steamapps\appmanifest_4164790.acf'
        if (Test-Path $mani) {
            $dir = Join-Path $lib ('steamapps\common\' + ((Get-Content $mani | Select-String '"installdir"\s+"([^"]+)"').Matches[0].Groups[1].Value))
            if (Test-Path (Join-Path $dir 'GameAssembly.dll')) { return ,$dir }
        }
    }
    throw 'Survival Log install dir not found in any Steam library'
}
$game = Find-GameDir
Write-Host ("game dir: " + $game)

# ---- 2. read buildid + runtime assets version; idempotence gate ----
$mani4164790 = Get-ChildItem (Split-Path -Parent (Split-Path -Parent $game)) -Recurse -Filter 'appmanifest_4164790.acf' -ErrorAction SilentlyContinue | Select-Object -First 1
$buildId = [int]((Get-Content $mani4164790.FullName | Select-String '"buildid"\s+"(\d+)"').Matches[0].Groups[1].Value)
$raVer = (Get-Content (Join-Path $game 'SurvivalLog_Data\StreamingAssets\PackageManifest\MainPackage\RuntimeAssets_MainPackage.version') -ErrorAction SilentlyContinue)
if (-not $raVer) { $raVer = 'unknown' }
$kbRoot = Join-Path $repo 'gamekb'
$existing = Get-ChildItem $kbRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match ('_' + $buildId + '$') }
if ($existing) { Write-Host ("no update: buildid {0} already captured as {1}" -f $buildId, $existing[0].Name); exit 0 }

# ---- 3. pre-flight: dumper exe present + no unclaimed outputs (BEFORE creating dirs) ----
$dumperDir = Join-Path $repo 'tools\il2cppdumper'
$dumperExe = Join-Path $dumperDir 'Il2CppDumper.exe'
if (-not (Test-Path $dumperExe)) {
    Write-Host ('ERROR: Il2CppDumper.exe missing under ' + $dumperDir + ' (gitignored; fetch Perfare/Il2CppDumper v6.7.46 net6-win and put exe+config.json+runtimeconfig there)')
    exit 1
}
$leftover = Get-ChildItem $dumperDir -File | Where-Object { $_.Name -in 'dump.cs','script.json','stringliteral.json','il2cpp.h' }
if ($leftover) { Write-Host ('ERROR: unclaimed dumper outputs in exe dir: ' + ($leftover.Name -join ',')); exit 1 }

# ---- 4. create version dir, copy binaries, meta ----
$stamp = Get-Date -Format 'yyyy-MM-dd'
$verDir = Join-Path $kbRoot ("{0}_{1}" -f $stamp, $buildId)
New-Item -ItemType Directory -Force -Path (Join-Path $verDir 'il2cpp'), (Join-Path $verDir 'binaries') | Out-Null
Copy-Item (Join-Path $game 'GameAssembly.dll') (Join-Path $verDir 'binaries\') -Force
Copy-Item (Join-Path $game 'SurvivalLog_Data\il2cpp_data\Metadata\global-metadata.dat') (Join-Path $verDir 'binaries\') -Force
$sha1 = (Get-FileHash (Join-Path $verDir 'binaries\GameAssembly.dll') -Algorithm SHA256).Hash
$sha2 = (Get-FileHash (Join-Path $verDir 'binaries\global-metadata.dat') -Algorithm SHA256).Hash
$meta = [ordered]@{
    buildid = $buildId; runtime_assets_version = $raVer
    unity_version = ''; captured_at = (Get-Date).ToString('o')
    sha256 = [ordered]@{ 'GameAssembly.dll' = $sha1; 'global-metadata.dat' = $sha2 }
    tools = [ordered]@{ il2cppdumper = '6.7.46'; dumpdiff = '1.0' }
    partial = $false; notes = ''
}
($meta | ConvertTo-Json -Depth 4) | Out-File (Join-Path $verDir 'meta.json') -Encoding ascii
Write-Host ("capturing " + $verDir)

# ---- 5. run Il2CppDumper (outputs land in exe dir - move, never copy) ----
$proc = Start-Process -FilePath $dumperExe -ArgumentList ('"' + (Join-Path $game 'GameAssembly.dll') + '" "' + (Join-Path $game 'SurvivalLog_Data\il2cpp_data\Metadata\global-metadata.dat') + '" "' + $dumperDir + '"') -Wait -PassThru -WindowStyle Hidden
Write-Host ("dumper exit code: " + $proc.ExitCode)
foreach ($f in 'dump.cs','script.json','stringliteral.json') {
    $src = Join-Path $dumperDir $f
    if (Test-Path $src) { Move-Item $src (Join-Path $verDir ('il2cpp\' + $f)) -Force }
}
Remove-Item (Join-Path $dumperDir 'il2cpp.h') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $dumperDir 'DummyDll') -Recurse -ErrorAction SilentlyContinue
if (-not (Test-Path (Join-Path $verDir 'il2cpp\dump.cs'))) { Write-Host 'ERROR: dumper produced no dump.cs'; exit 1 }

# ---- 6. webui mirror ----
robocopy (Join-Path $game 'SurvivalLog_Data\StreamingAssets\WebUI') (Join-Path $verDir 'webui') /MIR /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Host ('ERROR: robocopy failed ' + $LASTEXITCODE); exit 1 }

# ---- 7. diff vs previous version ----
$prev = Get-ChildItem $kbRoot -Directory | Where-Object { $_.Name -ne (Split-Path -Leaf $verDir) } | Sort-Object Name -Descending | Select-Object -First 1
$node = Get-Command node -ErrorAction SilentlyContinue
if ($prev -and $node) {
    $diffMd = Join-Path $verDir ('diff_from_' + ($prev.Name -replace '_\d+$','') + '.md')
    & node (Join-Path $repo 'tools\gamekb\dumpdiff.js') (Join-Path $prev.FullName 'il2cpp\dump.cs') (Join-Path $verDir 'il2cpp\dump.cs') '--moddeps' (Join-Path $repo 'tools\gamekb\moddeps.json') | Out-File $diffMd -Encoding utf8
    & node (Join-Path $repo 'tools\gamekb\modcompat_check.js') $verDir | Tee-Object -FilePath $diffMd -Append
    Write-Host ("diff written: " + $diffMd)
}

# ---- 8. tables: manual or -AutoProbe ----
$doneMarker = Join-Path $game ('BepInEx\gamekb_tables\' + $raVer + '\_DONE.txt')
if ($AutoProbe) {
    $running = Get-Process SurvivalLog -ErrorAction SilentlyContinue
    if ($running) { Write-Host 'ABORT -AutoProbe: game is running (author may be playing). Rerun when idle.'; exit 1 }
    if (-not (Test-Path (Join-Path $game 'BepInEx\plugins\SurvivalLog.ConfigDumpProbe.dll'))) {
        Write-Host 'ERROR: ConfigDumpProbe.dll not deployed to BepInEx\plugins (build SurvivalLog.ConfigDumpProbe and copy first)'; exit 1
    }
    Start-Process 'steam://rungameid/4164790'
    $deadline = (Get-Date).AddMinutes(5)
    while (-not (Test-Path $doneMarker) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
    if (-not (Test-Path $doneMarker)) { Write-Host 'ERROR: _DONE.txt not appeared in 5min (check probe deploy / game start)'; exit 1 }
    Get-Process SurvivalLog -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
    $still = Get-Process SurvivalLog -ErrorAction SilentlyContinue
    if ($still) { Write-Host 'WARN: SurvivalLog still alive after stop; tables not collected automatically'; exit 1 }
} 
if (Test-Path $doneMarker) {
    $tablesSrc = Join-Path $game ('BepInEx\gamekb_tables\' + $raVer)
    $tablesDst = Join-Path $verDir 'tables'
    if (Test-Path $tablesDst) { Remove-Item $tablesDst -Recurse -Force }
    Move-Item $tablesSrc $tablesDst
    Write-Host ("tables collected: " + (Get-Content $doneMarker -TotalCount 1))
} else {
    Write-Host ('TABLES PENDING: launch the game once (probe writes BepInEx\gamekb_tables\' + $raVer + '), then rerun this script to collect.')
}

Write-Host ('DONE buildid=' + $buildId + ' raVer=' + $raVer)
Write-Host 'manual step: fill meta.json notes + review diff_from_*.md'
exit 0
