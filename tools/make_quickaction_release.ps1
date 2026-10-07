$ErrorActionPreference = 'Stop'
$game     = 'E:\SteamLibrary\steamapps\common\Survival Log'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver      = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
$relRoot  = Join-Path $projRoot 'release'
$dll      = Join-Path $projRoot 'SurvivalLog.QuickAction\bin\Release\SurvivalLog.QuickAction.dll'
if (-not (Test-Path $dll)) { throw ('build missing: ' + $dll) }

$pkg = Join-Path $relRoot ('SurvivalLog.QuickAction_v' + $ver)
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item (Join-Path $pkg 'BepInEx\plugins') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $pkg 'BepInEx\config')  -ItemType Directory -Force | Out-Null
Copy-Item $dll (Join-Path $pkg 'BepInEx\plugins') -Force

# Preset cfg: copy runtime cfg, normalize to release defaults
$cfgName = 'com.local.survivallog.quickaction.cfg'
$srcCfg = Join-Path $game ('BepInEx\config\' + $cfgName)
if (Test-Path $srcCfg) {
    $dstCfg = Join-Path $pkg ('BepInEx\config\' + $cfgName)
    Copy-Item $srcCfg $dstCfg -Force
    $txt = [System.IO.File]::ReadAllText($dstCfg, [System.Text.Encoding]::UTF8)
    $txt = $txt -replace '(?m)^(A\d+\s*=\s).*$', '${1}500'
    $txt = $txt -replace '(?m)^((?:' + [char]0x7FFB + [char]0x9605 + [char]0x7B14 + [char]0x8BB0 + '|' + [char]0x5305 + [char]0x88F9 + [char]0x62C6 + [char]0x5C01 + '|' + [char]0x9677 + [char]0x9631 + '|' + [char]0x8FDB + [char]0x98DF + ')Ms\s*=\s).*$', '${1}500'
    $txt = $txt -replace '(?m)^(Overrides\s*=\s).*$', '${1}'
    $txt = $txt -replace '(?m)^# Default value.*\r?\n', ''
    [System.IO.File]::WriteAllText($dstCfg, $txt, (New-Object System.Text.UTF8Encoding($false)))
} else { Write-Warning 'runtime cfg not found; package ships without preset cfg' }

Copy-Item (Join-Path $projRoot 'tools\readme_quickaction.md') (Join-Path $pkg 'README.md') -Force

$zip = Join-Path $relRoot ('SurvivalLog.QuickAction_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '=== Release contents ==='
Get-ChildItem $pkg -Recurse -File | ForEach-Object { Write-Host ($_.FullName.Replace($pkg,'') + '  ' + [math]::Round($_.Length/1KB,1) + ' KB') }
Write-Host ''
Write-Host ('ZIP: ' + $zip + '  ' + [math]::Round((Get-Item $zip).Length/1MB,2) + ' MB')
