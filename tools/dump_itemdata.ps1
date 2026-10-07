$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

$id = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemData' }
Write-Host '===== ItemData fields (NativeFieldInfoPtr = raw offset available) ====='
foreach ($f in $id.Fields) {
    if ($f.Name -match '^NativeFieldInfoPtr_(.+)$') {
        Write-Host ("  {0}  : {1}" -f $Matches[1], $f.FieldType.Name)
    }
}
Write-Host ''
Write-Host '===== ItemData properties/methods ====='
foreach ($m in $id.Methods) {
    if ($m.Name -match '^get_|^set_') {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
