$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($t in $types) {
    if ($t.Name -eq 'LogicAdapter') {
        Write-Host ("##### TYPE {0} : base {1}" -f $t.FullName, $t.BaseType.Name)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  M {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
        }
    }
    if ($t.Name -eq 'WebUI_BackpackUI_Event') {
        Write-Host ("##### TYPE {0}" -f $t.FullName)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^get_') { Write-Host ("  P {0}" -f $m.Name) }
        }
    }
}
$asm.Dispose()
