$ErrorActionPreference = 'Stop'
$ver    = '2.9.29'
$game   = 'E:\SteamLibrary\steamapps\common\Survival Log'
$proj   = 'E:\Game\Mod\mod_stack\SurvivalLog.StackLimit999'
$relRoot= 'E:\Game\Mod\mod_stack\release'
$rel    = Join-Path $relRoot ('SurvivalLog.StackLimit999_v' + $ver)

if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item $rel -ItemType Directory | Out-Null
New-Item (Join-Path $rel 'BepInEx\core') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $rel 'BepInEx\plugins') -ItemType Directory -Force | Out-Null

# Doorstop loader (game root)
Copy-Item (Join-Path $game 'winhttp.dll') $rel -Force
Copy-Item (Join-Path $game 'doorstop_config.ini') $rel -Force

# BepInEx core framework
Copy-Item (Join-Path $game 'BepInEx\core\*') (Join-Path $rel 'BepInEx\core') -Force

# Plugin (build first if stale)
$dll = Join-Path $proj 'bin\Release\SurvivalLog.StackLimit999.dll'
Copy-Item $dll (Join-Path $rel 'BepInEx\plugins') -Force

# Docs
Copy-Item (Join-Path $proj 'README.md') (Join-Path $rel 'README.md') -Force

# Zip
$zip = Join-Path $relRoot ('SurvivalLog.StackLimit999_v' + $ver + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $rel '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '=== Release contents ==='
Get-ChildItem $rel -Recurse -File | ForEach-Object { Write-Host ($_.FullName.Replace($rel,'') + '  ' + [math]::Round($_.Length/1KB,1) + ' KB') }
Write-Host ''
Write-Host ('ZIP: ' + $zip + '  ' + [math]::Round((Get-Item $zip).Length/1MB,2) + ' MB')
