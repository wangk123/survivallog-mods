$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$vj = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.suite
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'
if (-not (Test-Path $iscc)) { throw ('ISCC 不存在: ' + $iscc) }

Write-Host ('编译安装器 v' + $ver + ' ...')
# per-mod 版本 define（双轨：mod 包目录名 = mod 自身版本；Base 用 SuiteVer）
$defines = @('/DSuiteVer=' + $ver)
foreach ($p in $vj.mods.PSObject.Properties) { $defines += ('/DVer' + $p.Name + '=' + $p.Value) }
& $iscc @defines (Join-Path $projRoot 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) { throw ('ISCC 编译失败，退出码 ' + $LASTEXITCODE) }

$exe = Join-Path $projRoot ('release\生存日志MOD安装器_v' + $ver + '.exe')
if (-not (Test-Path $exe)) { throw ('安装器编译产物缺失: ' + $exe) }
Write-Host ('OK: ' + $exe + '  ' + [math]::Round((Get-Item $exe).Length / 1MB, 2) + ' MB')
