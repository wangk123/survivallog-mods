$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)

$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.WebUILayer' }
Write-Host '--- WebUILayer.IsPageActive overloads:'
foreach ($m in $t.Methods) { if ($m.Name -eq 'IsPageActive') { Write-Host ('  ' + $m.FullName) } }

$t2 = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.ReduxUISystem' }
Write-Host '--- ReduxUISystem webUILayer field:'
foreach ($f in $t2.Fields) { if ($f.Name -match 'webUILayer|_logicAdapter') { Write-Host ('  ' + $f.FieldType.Name + ' ' + $f.Name) } }
