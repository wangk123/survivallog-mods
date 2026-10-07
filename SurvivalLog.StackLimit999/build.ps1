# 构建 SurvivalLog.StackLimit999 并部署到游戏 BepInEx\plugins
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 [-NoDeploy]
$ErrorActionPreference = 'Stop'

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SdkDir     = Join-Path (Split-Path -Parent $ProjectDir) 'sdk\dotnet'
$GamePlugins = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins'
$Dll        = Join-Path $ProjectDir 'bin\Release\SurvivalLog.StackLimit999.dll'

$dotnetExe = Join-Path $SdkDir 'dotnet.exe'
if (-not (Test-Path $dotnetExe)) { throw "dotnet not found: $dotnetExe (先解压 SDK)" }

$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

& $dotnetExe build (Join-Path $ProjectDir 'SurvivalLog.StackLimit999.csproj') -c Release --nologo -v m
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

Write-Host "BUILD OK -> $Dll"

if (Test-Path "$Dll.meta") { Remove-Item "$Dll.meta" -Force }

if (-not ($args -contains '-NoDeploy')) {
    if (-not (Test-Path $GamePlugins)) { throw "plugins dir missing: $GamePlugins" }
    Copy-Item $Dll (Join-Path $GamePlugins 'SurvivalLog.StackLimit999.dll') -Force
    Write-Host "DEPLOYED -> $GamePlugins\SurvivalLog.StackLimit999.dll"
}
