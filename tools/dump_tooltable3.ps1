# ASCII-only. Dump Reducer_Web_ToolTable method list + key method IL + event name strings.
$ErrorActionPreference = 'Stop'
Add-Type -Path "C:\Users\wangk\.zcode\workspace\default\mod_stack\release\SurvivalLog.StackLimit999_v2.7.9\BepInEx\core\Mono.Cecil.dll"

$dll = "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
$mod = $asm.MainModule

"===== 1) Reducer_Web_ToolTable methods ====="
$tt = $mod.GetType('GameCore.HotUpdate.ReduxUI.Reducer_Web_ToolTable')
if (-not $tt) { "TYPE NOT FOUND"; exit }
foreach ($m in $tt.Methods) {
    $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
    "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
}

"`n===== 2) WebUI_ToolTable event strings (ldstr) ====="
foreach ($tname in @('GameCore.HotUpdate.ReduxUI.WebUI_ToolTable_Event','GameCore.HotUpdate.ReduxUI.WebUI_ToolTable')) {
    $t = $mod.GetType($tname)
    if (-not $t) { "no type: $tname"; continue }
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Name -eq 'ldstr' -and $ins.Operand) {
                "$tname :: $($m.Name) -> ""$($ins.Operand)"""
            }
        }
    }
}

"`n===== 3) CountOwnedInPool / CheckMaterialSufficiency / SelectItemsPartial param names ====="
foreach ($m in $tt.Methods) {
    if ($m.Name -match 'CountOwnedInPool|CheckMaterialSufficiency|SelectItemsPartial|ApplyFillRecipePartial|RA_RecipeClick|SyncWorkbenchTo|RA_Make') {
        $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }) -join ', '
        "----- $($m.Name)($ps) -> $($m.ReturnType.FullName)"
    }
}
