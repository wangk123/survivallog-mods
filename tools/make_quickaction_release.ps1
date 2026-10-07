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

# Preset cfg: 直接生成规范预置（分类默认值 = 安装器默认勾选：吃/交互/维护开，家具/娱乐关）
# 与 mod 内 ReadBoot() 缺省值保持一致
$cfgName = 'com.local.survivallog.quickaction.cfg'
$dstCfg = Join-Path $pkg ('BepInEx\config\' + $cfgName)
$lines = @(
    '[分类修改]',
    '物品使用Ms = 500',
    '家具功能Ms = 0',
    '房屋维护Ms = 500',
    '杂项交互Ms = 500',
    '娱乐锻炼Ms = 0',
    '',
    '[其他]',
    'Overrides = '
)
[System.IO.File]::WriteAllLines($dstCfg, $lines, (New-Object System.Text.UTF8Encoding($false)))

Copy-Item (Join-Path $projRoot 'tools\readme_quickaction.md') (Join-Path $pkg 'README.md') -Force

$zip = Join-Path $relRoot ('SurvivalLog.QuickAction_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '=== Release contents ==='
Get-ChildItem $pkg -Recurse -File | ForEach-Object { Write-Host ($_.FullName.Replace($pkg,'') + '  ' + [math]::Round($_.Length/1KB,1) + ' KB') }
Write-Host ''
Write-Host ('ZIP: ' + $zip + '  ' + [math]::Round((Get-Item $zip).Length/1MB,2) + ' MB')
