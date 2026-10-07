$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'
if (-not (Test-Path $iscc)) { throw ('ISCC 不存在: ' + $iscc) }

Write-Host ('编译安装器 v' + $ver + ' ...')
& $iscc (('/DSuiteVer=' + $ver)) (Join-Path $projRoot 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) { throw ('ISCC 编译失败，退出码 ' + $LASTEXITCODE) }

$exe = Join-Path $projRoot ('release\生存日志MOD安装器_v' + $ver + '.exe')
if (-not (Test-Path $exe)) { throw ('安装器编译产物缺失: ' + $exe) }
Write-Host ('OK: ' + $exe + '  ' + [math]::Round((Get-Item $exe).Length / 1MB, 2) + ' MB')
