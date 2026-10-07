# ASCII-only. Find furniture list API for locating the workbench.
$ErrorActionPreference = 'Stop'
Add-Type -Path "C:\Users\wangk\.zcode\workspace\default\mod_stack\release\SurvivalLog.StackLimit999_v2.7.9\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll")
$mod = $asm.MainModule

"===== BattleLogicWorld fields/props ====="
$w = $mod.GetType('GameCore.HotUpdate.Battle.Logic.BattleLogicWorld')
foreach ($f in $w.Fields) { "  field: $($f.FieldType.Name) $($f.Name)" }

"`n===== Furniture public members (first 60) ====="
$ft = $mod.GetType('GameCore.HotUpdate.Battle.Logic.Furniture')
$n = 0
foreach ($m in $ft.Methods) {
    if ($m.Name -match '^get_|^set_') {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '
        "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
        if (++$n -ge 60) { break }
    }
}

"`n===== FurnitureManager-like types ====="
foreach ($t in $mod.Types) {
    if ($t.Name -match 'FurnitureManager|FurnitureMgr') { "TYPE: $($t.FullName)" }
}
