$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

"===== types with ExecuteJs / OnJsResult ====="
foreach ($ty in $mod.Types) {
    foreach ($m in $ty.Methods) {
        if ($m.Name -match 'ExecuteJs|OnJsResult') {
            $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
            "  $($ty.FullName) :: $($m.Name)($ps)"
        }
    }
}

"`n===== WebUILayer fields/methods (first 40) ====="
$t = $mod.GetType('GameCore.HotUpdate.ReduxUI.WebUILayer')
if ($t) {
    foreach ($f in $t.Fields) { "  field: $($f.FieldType.Name) $($f.Name)" }
    foreach ($m in $t.Methods) {
        if ($m.Name -match '^get_|Instance|Vm') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '
            "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
        }
    }
} else { 'no WebUILayer type' }

"`n===== LogicAdapter vm-related ====="
$t2 = $mod.GetType('GameCore.HotUpdate.ReduxUI.LogicAdapter')
foreach ($f in $t2.Fields) { if ($f.Name -match 'vm|Vm|web|Web') { "  $($f.FieldType.Name) $($f.Name)" } }
