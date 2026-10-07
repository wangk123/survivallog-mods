$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

$cands = $mod.Types | Where-Object { $_.Name -match 'HotKey|Hotkey' }
foreach ($t in $cands) {
    "== $($t.FullName)"
    foreach ($f in $t.Fields) {
        if ($f.Name -match 'Ptr_') { continue }
        "  field: $($f.FieldType.Name) $($f.Name)"
    }
    foreach ($m in $t.Methods) {
        $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
        "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
    }
}
