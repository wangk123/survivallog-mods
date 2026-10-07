$ErrorActionPreference = 'Continue'
$setup = 'E:\Game\Mod\mod_stack\release\生存日志MOD安装器_v1.3.1.exe'
$test   = 'E:\Game\Mod\mod_stack\release\_setup_test'
$game   = Join-Path $test 'game'

if (Test-Path $test) { Remove-Item $test -Recurse -Force }
New-Item $game -ItemType Directory -Force | Out-Null

$proc = Start-Process -FilePath $setup -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES',('/DIR=' + $game),('/LOG=' + (Join-Path $test 'setup.log')) -PassThru
Write-Host ("启动 PID: " + $proc.Id + "  名称: " + $proc.ProcessName)

$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $alive = Get-Process | Where-Object { $_.ProcessName -like '*生存*' -or $_.ProcessName -like '*setup*' -or $_.ProcessName -like 'tmp*' -or $_.ProcessName -like '~inno*' }
    $names = ($alive | ForEach-Object { $_.ProcessName + '(' + $_.Id + ')' }) -join ', '
    $nFiles = 0
    if (Test-Path $game) { $nFiles = (Get-ChildItem $game -Recurse -File -ErrorAction SilentlyContinue).Count }
    Write-Host ((Get-Date -Format 'HH:mm:ss') + "  进程: [$names]  已落地文件: $nFiles")
    if (-not $alive) { Write-Host '安装进程已全部退出'; break }
}
try { Write-Host ("主进程退出码: " + $proc.ExitCode) } catch { Write-Host '主进程退出码: 未知' }
Write-Host '--- setup.log 头 30 行 ---'
if (Test-Path (Join-Path $test 'setup.log')) { Get-Content (Join-Path $test 'setup.log') -TotalCount 30 } else { Write-Host 'setup.log 不存在（二阶段 setup 未启动）' }
