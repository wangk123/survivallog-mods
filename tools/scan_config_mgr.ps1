$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)

foreach ($t in $asm.MainModule.GetTypes()) {
    $hasItemRef = $false
    $memberHits = New-Object System.Collections.Generic.List[string]
    foreach ($f in $t.Fields) {
        if ($f.FieldType.FullName -match 'Config_Item') {
            $memberHits.Add(("  [field] {0} : {1}" -f $f.Name, $f.FieldType.FullName)); $hasItemRef = $true
        }
    }
    foreach ($m in $t.Methods) {
        if ($m.ReturnType.FullName -match 'Config_Item' -or ($m.Parameters | Where-Object { $_.ParameterType.FullName -match 'Config_Item' })) {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            $memberHits.Add(("  [method] {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)); $hasItemRef = $true
        }
    }
    if ($hasItemRef) {
        Write-Host ("##### {0}" -f $t.FullName)
        $memberHits | Select-Object -First 40 | ForEach-Object { Write-Host $_ }
    }
}
$asm.Dispose()
