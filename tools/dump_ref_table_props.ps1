$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
foreach ($tn in @('Config_Furniture','Config_Trap','Config_CookingLv','Config_PlantLv','Config_Instance','Config_Event','Config_FurnitureFunc')) {
    $t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq "GameCore.HotUpdate.$tn" }
    if (-not $t) { Write-Host "MISSING $tn"; continue }
    Write-Host "--- $tn properties ---"
    foreach ($p in $t.Properties) {
        Write-Host ("  {0} : {1}" -f $p.Name, $p.PropertyType.FullName)
    }
}
Write-Host "--- ConfigManager _Config_*_Dict properties ---"
$cm = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
foreach ($p in $cm.Properties) {
    if ($p.Name -match 'Dict') { Write-Host ("  {0} : {1}" -f $p.Name, ($p.PropertyType.FullName -replace '.*Dictionary_2_', '')) }
}
$asm.Dispose()
