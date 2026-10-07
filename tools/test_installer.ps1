$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
$setup = Join-Path $projRoot ('release\生存日志MOD安装器_v' + $ver + '.exe')
$test   = Join-Path $projRoot 'release\_setup_test'
$setupDir = Join-Path $test 'game'
$script:fails = @()

if (-not (Test-Path $setup)) { throw ('安装器不存在: ' + $setup) }
if (Test-Path $test) { Remove-Item $test -Recurse -Force }
New-Item $setupDir -ItemType Directory -Force | Out-Null

# --- 测试 1：静默安装，只选 背包扩容 + 动作提速（base 固定必装）---
# 预放一个"玩家旧配置"，验证 onlyifdoesntexist 不覆盖
New-Item (Join-Path $setupDir 'BepInEx\config') -ItemType Directory -Force | Out-Null
Set-Content (Join-Path $setupDir 'BepInEx\config\com.local.survivallog.backpackexpand.cfg') 'WidthMult = 9  # 玩家自己调过的'

$p = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES',("/DIR=`"$setupDir`""),'/COMPONENTS="mod_backpack,mod_quick"' -Wait -PassThru
Write-Host ("安装退出码: " + $p.ExitCode)
if ($p.ExitCode -ne 0) { $script:fails += ('安装器退出码 ' + $p.ExitCode) }

Write-Host '--- 落地文件检查 ---'
function Check($rel, $must) {
    $ok = Test-Path (Join-Path $setupDir $rel)
    $mark = 'OK  '
    if ($must -and -not $ok) { $mark = 'MISSING!  '; $script:fails += ('缺文件: ' + $rel) }
    if (-not $must -and $ok) { $mark = 'UNEXPECTED!  '; $script:fails += ('多文件: ' + $rel) }
    Write-Host ($mark + $rel)
}
Check 'winhttp.dll' $true
Check 'doorstop_config.ini' $true
Check '.doorstop_version' $true
Check 'dotnet\coreclr.dll' $true
Check 'BepInEx\core\BepInEx.Core.dll' $true
Check 'BepInEx\config\BepInEx.cfg' $true
Check 'BepInEx\plugins\SurvivalLog.BackpackExpand.dll' $true
Check 'BepInEx\plugins\SurvivalLog.QuickAction.dll' $true
Check 'BepInEx\config\com.local.survivallog.quickaction.cfg' $true
Check 'MOD说明\背包扩容.md' $true
Check 'MOD说明\动作提速.md' $true
Check 'BepInEx\plugins\SurvivalLog.CabinetExpand.dll' $false
Check 'BepInEx\plugins\SurvivalLog.FridgeExpand.dll' $false
Check 'BepInEx\plugins\SurvivalLog.FridgeChill.dll' $false
Check 'unins000.exe' $true

Write-Host '--- 旧配置保留检查（应仍为 WidthMult = 9）---'
$kept = Get-Content (Join-Path $setupDir 'BepInEx\config\com.local.survivallog.backpackexpand.cfg') -TotalCount 1
Write-Host $kept
if ($kept -notmatch 'WidthMult\s*=\s*9') { $script:fails += '玩家旧配置被覆盖（onlyifdoesntexist 失效）' }

$dotnetN = (Get-ChildItem (Join-Path $setupDir 'dotnet') -File).Count
$coreN   = (Get-ChildItem (Join-Path $setupDir 'BepInEx\core') -File -Recurse).Count
Write-Host ("dotnet 文件数: " + $dotnetN + "  (上次基准 187)   core 文件数: " + $coreN)
if ($dotnetN -lt 150) { $script:fails += ('dotnet 文件数异常: ' + $dotnetN) }
if ($coreN -lt 10)   { $script:fails += ('BepInEx core 文件数异常: ' + $coreN) }

# --- 测试 2：静默卸载，验证清理 + 旧配置保留 ---
Start-Process -FilePath (Join-Path $setupDir 'unins000.exe') -ArgumentList '/VERYSILENT','/NORESTART' -Wait
Write-Host '--- 卸载后检查 ---'
function CheckGone($rel) {
    $gone = -not (Test-Path (Join-Path $setupDir $rel))
    Write-Host ($rel + ' 已移除: ' + $gone)
    if (-not $gone) { $script:fails += ('卸载残留: ' + $rel) }
}
CheckGone 'winhttp.dll'
CheckGone 'dotnet'
CheckGone 'BepInEx\plugins\SurvivalLog.QuickAction.dll'
$cfgKept = Test-Path (Join-Path $setupDir 'BepInEx\config\com.local.survivallog.backpackexpand.cfg')
Write-Host ("玩家旧配置保留: " + $cfgKept)
if (-not $cfgKept) { $script:fails += '卸载时把玩家旧配置删了' }
Write-Host ("BepInEx 目录残留(空壳属正常): " + (Test-Path (Join-Path $setupDir 'BepInEx')))

Remove-Item $test -Recurse -Force
Write-Host '临时目录已清理'

if ($script:fails.Count -gt 0) {
    Write-Host ''
    Write-Host '=== 回归失败，禁止发版 ==='
    $script:fails | ForEach-Object { Write-Host ('  - ' + $_) }
    throw ('安装器回归测试失败 ' + $script:fails.Count + ' 项')
}
Write-Host '=== 安装器回归测试通过 ==='
