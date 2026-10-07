$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '===== types matching Backpack/Bag + State/Redux ====='
foreach ($t in $types) {
    if ($t.FullName -match 'Backpack|BagUI' -and $t.FullName -match 'State|Vm|Redux') {
        Write-Host ("  TYPE: " + $t.FullName)
    }
}

$st = $types | Where-Object { $_.FullName -match 'Backpack' -and $_.FullName -match 'State' } | Select-Object -First 3
foreach ($t in $st) {
    Write-Host ''
    Write-Host ("===== " + $t.FullName + " methods =====")
    foreach ($m in $t.Methods) {
        if ($m.Name -match '^\.') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
        Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}

$br = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.BaseReduxState' }
if ($br) {
    Write-Host ''
    Write-Host '===== BaseReduxState methods ====='
    foreach ($m in $br.Methods) {
        if ($m.Name -match '^\.') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
