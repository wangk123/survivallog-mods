$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '##### message-routing related types:'
foreach ($t in $types) {
    if ($t.Name -match '(?i)WebUILayer|WebUIScreen|WebUIPage|PageMessage|WebMessage|MessageRouter|WebUIEvent|EventDispatcher|UnityEvent') {
        Write-Host ("  TYPE {0} : base {1}" -f $t.FullName, $t.BaseType.Name)
        foreach ($m in $t.Methods) {
            if ($m.Name -match 'Register|Subscribe|Dispatch|Handle|Route|AddListener|Send|Emit') {
                $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
                Write-Host ("    M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
            }
        }
    }
}

Write-Host '##### LogicAdapter members:'
foreach ($t in $types) {
    if ($t.Name -eq 'LogicAdapter') {
        Write-Host ("  TYPE {0} : base {1} (static={2})" -f $t.FullName, $t.BaseType.Name)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("    M {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
        }
    }
}

Write-Host '##### WebUI_BackpackUI_Event members:'
foreach ($t in $types) {
    if ($t.Name -eq 'WebUI_BackpackUI_Event') {
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^get_') { Write-Host ("    P {0} -> {1}" -f $m.Name, $m.ReturnType.Name) }
        }
    }
}
$asm.Dispose()
