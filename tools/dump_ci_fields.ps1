$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$ci = $asm.MainModule.GetType('GameCore.HotUpdate.Config_Item')
Write-Host '### ALL Config_Item fields:'
foreach ($f in $ci.Fields) { Write-Host ("  {0} : {1}" -f $f.Name, $f.FieldType.Name) }
$asm.Dispose()
