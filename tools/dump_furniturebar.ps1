$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
Write-Host "--- Types matching FurnitureBar / Bar ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -match 'FurnitureBar|Bar_') { Write-Host ("  {0}" -f $ty.FullName) }
}
Write-Host "--- Config_Action During-related search: who reads FurnitureBar ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -notlike 'GameCore.HotUpdate.*') { continue }
    foreach ($p in $ty.Properties) {
        if ($p.Name -match 'FurnitureBar') { Write-Host ("  {0}.{1} : {2}" -f $ty.FullName, $p.Name, $p.PropertyType.Name) }
    }
}
Write-Host "--- E_Action enum-like constants in Config_* tables with 'Bar' fields ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -notlike 'GameCore.HotUpdate.Config_*') { continue }
    foreach ($p in $ty.Properties) {
        if ($p.Name -match 'Bar') { Write-Host ("  {0}.{1} : {2}" -f $ty.Name, $p.Name, $p.PropertyType.Name) }
    }
}
$asm.Dispose()
