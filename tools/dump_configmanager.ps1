$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host ("BaseType: {0}" -f $t.BaseType.FullName)
Write-Host ("Interfaces: " + (($t.Interfaces | ForEach-Object { $_.InterfaceType.Name }) -join ', '))
Write-Host "--- Fields (non-native) ---"
foreach ($f in $t.Fields) { if ($f.Name -notmatch '^Native') { Write-Host ("  {0} : {1} (static={2})" -f $f.Name, $f.FieldType.Name, $f.IsStatic) } }
Write-Host "--- Methods ---"
foreach ($m in $t.Methods) {
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
    Write-Host ("  {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
}
$asm.Dispose()
