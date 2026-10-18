$ErrorActionPreference = 'Stop'
# Test: does the NEW installer overwrite a pre-existing CORRUPT BepInEx\unity-libs\6000.2.7.zip?
# Reproduces the player path: old-suite install (no unity-libs in package) -> BepInEx downloaded a corrupt zip
# -> old uninstall leaves it -> NEW installer -> must overwrite with the good copy -> NEW uninstall removes it.
# ASCII-only on purpose (PS 5.1 reads BOM-less UTF-8 scripts as ANSI; Chinese literals get mojibake - bitten once).
$proj = 'E:\Game\Mod\mod_stack'
$rel  = Join-Path $proj 'release'
$ver  = (Get-Content (Join-Path $proj 'version.json') -Raw | ConvertFrom-Json).suite
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'
$sbx  = Join-Path $rel '_corrupt_test'
$game = Join-Path $sbx 'game'
if (Test-Path $sbx) { Remove-Item $sbx -Recurse -Force }
New-Item (Join-Path $game 'BepInEx\unity-libs') -ItemType Directory -Force | Out-Null

# plant corrupt leftovers, timestamps = now (NEWER than package files = worst case for overwrite logic)
$badZip = Join-Path $game 'BepInEx\unity-libs\6000.2.7.zip'
$badDll = Join-Path $game 'BepInEx\unity-libs\UnityEngine.CoreModule.dll'
'C0RRUPT-GARBAGE-NOT-A-REAL-ZIP' | Set-Content $badZip
'C0RRUPT-DLL-BYTES'              | Set-Content $badDll
(Get-Item $badZip).LastWriteTime = Get-Date
(Get-Item $badDll).LastWriteTime = Get-Date

# good references from the shipped Base package
$goodZip = Join-Path $rel ("SurvivalLog.Base_v" + $ver + "\BepInEx\unity-libs\6000.2.7.zip")
$goodDll = Join-Path $rel ("SurvivalLog.Base_v" + $ver + "\BepInEx\unity-libs\UnityEngine.CoreModule.dll")
$goodZipHash = (Get-FileHash $goodZip -Algorithm SHA256).Hash
$goodDllHash = (Get-FileHash $goodDll -Algorithm SHA256).Hash
Write-Host ('good zip SHA256 = ' + $goodZipHash)

# build an OLD-style installer (unity-libs entry stripped) to simulate pre-unity-libs versions (<= v1.3.4)
$issPath = Join-Path $proj 'installer\installer.iss'
$iss = [IO.File]::ReadAllText($issPath)
$pattern = '(?m)^Components: base; Source: "\{#RelRoot\}\\SurvivalLog\.Base_v\{#SuiteVer\}\\BepInEx\\unity-libs\\\*"; DestDir: "\{app\}\\BepInEx\\unity-libs"; Flags: ignoreversion recursesubdirs createallsubdirs\r?\n'
$oldIss = $iss -replace $pattern, ''
if ($oldIss -eq $iss) { throw 'failed to strip unity-libs entry from old-sim iss' }
$oldIss = $oldIss -replace '(?m)^OutputBaseFilename=.*$', 'OutputBaseFilename=OLD_SIM_setup'
$oldIssPath = Join-Path $proj 'installer\_old_sim.iss'
[IO.File]::WriteAllText($oldIssPath, $oldIss, (New-Object System.Text.UTF8Encoding($true)))
& $iscc (('/DSuiteVer=' + $ver)) $oldIssPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'old-sim ISCC compile failed' }
$oldSetup = Join-Path $rel 'OLD_SIM_setup.exe'
if (-not (Test-Path $oldSetup)) { throw ('old-sim setup missing: ' + $oldSetup) }
Write-Host 'PHASE 0  old-sim installer compiled (unity-libs entry removed)'

# PHASE 1: old-style install, then uninstall -> corrupt zip must SURVIVE (the reported symptom)
$p = Start-Process -FilePath $oldSetup -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES',("/DIR=`"$game`""),'/COMPONENTS="base"' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw ('old install exit ' + $p.ExitCode) }
$afterOld = (Get-FileHash $badZip -Algorithm SHA256).Hash
Write-Host ('PHASE 1a after OLD install : corrupt zip untouched by old installer = ' + ($afterOld -ne $goodZipHash))
Start-Process -FilePath (Join-Path $game 'unins000.exe') -ArgumentList '/VERYSILENT','/NORESTART' -Wait
Write-Host ('PHASE 1b after OLD uninstall: 6000.2.7.zip REMAINS = ' + (Test-Path $badZip) + '  <- reproduces the reported observation (old unins000.dat never recorded this file)')
Write-Host ('PHASE 1b after OLD uninstall: winhttp.dll removed = ' + (-not (Test-Path (Join-Path $game 'winhttp.dll'))))

# PHASE 2: NEW installer over the leftover corrupt state
$newSetup = (Get-ChildItem $rel -File -Filter ('*_v' + $ver + '.exe') | Where-Object { $_.Name -notmatch '^OLD_SIM' } | Select-Object -First 1).FullName
$p = Start-Process -FilePath $newSetup -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES',("/DIR=`"$game`""),'/COMPONENTS="base"' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw ('new install exit ' + $p.ExitCode) }
$zipNow = (Get-FileHash $badZip -Algorithm SHA256).Hash
$dllNow = (Get-FileHash $badDll -Algorithm SHA256).Hash
Write-Host ('PHASE 2a after NEW install : 6000.2.7.zip OVERWRITTEN with good copy = ' + ($zipNow -eq $goodZipHash))
Write-Host ('PHASE 2a after NEW install : CoreModule.dll OVERWRITTEN = ' + ($dllNow -eq $goodDllHash))
$n = (Get-ChildItem (Join-Path $game 'BepInEx\unity-libs') -File).Count
Write-Host ('PHASE 2a unity-libs file count = ' + $n + ' (expect 74)')

# PHASE 3: NEW uninstall
Start-Process -FilePath (Join-Path $game 'unins000.exe') -ArgumentList '/VERYSILENT','/NORESTART' -Wait
Write-Host ('PHASE 3  after NEW uninstall: 6000.2.7.zip REMOVED = ' + (-not (Test-Path $badZip)))
Write-Host ('PHASE 3  after NEW uninstall: unity-libs dir REMOVED = ' + (-not (Test-Path (Join-Path $game 'BepInEx\unity-libs'))))

$ok = ($zipNow -eq $goodZipHash) -and ($dllNow -eq $goodDllHash) -and (-not (Test-Path $badZip))
Write-Host ('VERDICT: overwrite-on-corrupt ' + $(if ($ok) { 'PASS' } else { 'FAIL' }))
Remove-Item $sbx -Recurse -Force
Remove-Item $oldSetup -Force
Remove-Item $oldIssPath -Force
Write-Host 'cleanup done'
if (-not $ok) { exit 1 }
