$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host "--- ConfigManager all native-backed field names ---"
foreach ($f in $t.Fields) {
    if ($f.Name -match '^NativeFieldInfoPtr__(.+)_k__BackingField$') {
        Write-Host ("  {0} : {1}" -f $Matches[1], $f.FieldType.Name)
    }
}
Write-Host "--- Config_Furniture fields ---"
$tf = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Config_Furniture' }
foreach ($f in $tf.Fields) {
    if ($f.Name -match '^NativeFieldInfoPtr__(.+)_k__BackingField$') {
        Write-Host ("  {0} : {1}" -f $Matches[1], $f.FieldType.Name)
    }
}
$asm.Dispose()
