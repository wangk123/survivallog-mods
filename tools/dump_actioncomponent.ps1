$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
Write-Host "--- ActionComponent-ish types ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -match 'ActionComponent|PlayerComponent|^GameCore.HotUpdate.Player$|UnitManager|PlayerManager|ActorManager') {
        Write-Host ("  {0} (methods={1})" -f $ty.FullName, $ty.Methods.Count)
    }
}
Write-Host ""
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -match 'ActionComponent' } | Select-Object -First 3
foreach ($ty in $t) {
    Write-Host "--- $($ty.FullName) properties ---"
    foreach ($p in $ty.Properties) { Write-Host ("  {0} : {1}" -f $p.Name, $p.PropertyType.Name) }
}
$asm.Dispose()
