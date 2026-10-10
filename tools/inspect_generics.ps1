$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
foreach ($t in $asm.MainModule.GetTypes()) {
    if ($t.Name -eq 'ReactiveProperty`1' -or $t.Name -eq 'ObservableList`1') {
        Write-Host ("##### {0}" -f $t.FullName)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}
$asm.Dispose()
