$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

$t = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Config_ProductionList' }
if (-not $t) {
    Write-Host 'Config_ProductionList not found; searching similar:'
    foreach ($x in $types) { if ($x.FullName -match 'Config_Production') { Write-Host ('  ' + $x.FullName) } }
} else {
    Write-Host '########## Config_ProductionList properties:'
    foreach ($p in $t.Properties) { Write-Host ("   {0} : {1}" -f $p.Name, $p.PropertyType.Name) }
}
# ConfigManager accessor
$cm = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host ''
Write-Host '########## ConfigManager Production-related methods:'
foreach ($m in $cm.Methods) {
    if ($m.Name -match 'Production') { Write-Host ("   {0} -> {1}" -f $m.Name, $m.ReturnType.Name) }
}
$asm.Dispose()
