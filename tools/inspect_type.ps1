$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($t in $types) {
    if ($t.Name -ne $args[0]) { continue }
    Write-Host ("##### {0} (base: {1})" -f $t.FullName, $t.BaseType.FullName)
    if ($args[1] -eq 'props') {
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^get_') { Write-Host ("  P {0} : {1} [static={2}]" -f $m.Name.Substring(4), $m.ReturnType.Name, $m.IsStatic) }
        }
    } else {
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  M {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
        }
    }
    break
}
$asm.Dispose()
