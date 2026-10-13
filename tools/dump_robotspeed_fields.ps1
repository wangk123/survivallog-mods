$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
# NOTE: interop dlls are regenerated per game launch; user is playing NOW so
# the current interop dir may be from the old binary. Prefer it anyway (field names stable),
# fall back note printed at end.
$interop = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($interop, $rp)
$types = $asm.MainModule.GetTypes()

function Show-Type($fullName, $what) {
    $t = $types | Where-Object { $_.FullName -eq $fullName }
    if (-not $t) { Write-Host "### $fullName NOT FOUND"; return }
    Write-Host "### $fullName ($what):"
    foreach ($f in $t.Fields) {
        if ($f.IsStatic) { Write-Host ("   [static] {0} : {1}" -f $f.Name, $f.FieldType.Name) }
    }
    foreach ($p in $t.Properties) { Write-Host ("   prop {0} : {1}" -f $p.Name, $p.PropertyType.Name) }
}

Show-Type 'GameCore.HotUpdate.Config_GlobalSetting' 'fields/props'
Show-Type 'GameCore.HotUpdate.Config_RobotAttribute' 'fields/props'
Show-Type 'GameCore.HotUpdate.Config_Bag' 'fields/props'

$cm = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host '### ConfigManager Robot/GlobalSetting/Action/Bag members:'
foreach ($m in $cm.Methods) {
    if ($m.Name -match 'RobotAttribute|GlobalSetting') { Write-Host ("   M {0} -> {1}" -f $m.Name, $m.ReturnType.Name) }
}
foreach ($f in $cm.Fields) {
    if ($f.Name -match 'RobotAttribute|GlobalSetting|_Config_Action_Dict|_Config_Bag_Dict') { Write-Host ("   F {0} : {1}" -f $f.Name, $f.FieldType.FullName) }
}
foreach ($p in $cm.Properties) {
    if ($p.Name -match 'RobotAttribute|GlobalSetting|Config_Action|Config_Bag') { Write-Host ("   P {0} : {1}" -f $p.Name, $p.PropertyType.FullName) }
}

Show-Type 'GameCore.HotUpdate.Battle.Logic.RobotComponent' 'CargoExpandBagIds static + module props'
$asm.Dispose()
Write-Host ('interop dll timestamp: ' + (Get-Item $interop).LastWriteTime)
