# ASCII-only. Check static-ness of tooltable methods + action/data field shapes.
$ErrorActionPreference = 'Stop'
Add-Type -Path "C:\Users\wangk\.zcode\workspace\default\mod_stack\release\SurvivalLog.StackLimit999_v2.7.9\BepInEx\core\Mono.Cecil.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll")
$mod = $asm.MainModule

"===== static check: Reducer_Web_ToolTable ====="
$tt = $mod.GetType('GameCore.HotUpdate.ReduxUI.Reducer_Web_ToolTable')
foreach ($m in $tt.Methods) {
    if ($m.Name -match 'ParseMaterialKey|CountMaterials|CountOwnedInPool|GetAllLinkedOwnerIds|SelectItemsPartial|CheckMaterialSufficiency') {
        $static = ($m.Attributes -band [Mono.Cecil.MethodAttributes]::Static) -ne 0
        "$($m.Name) static=$static params=$($m.Parameters.Count)"
    }
}

"`n===== Ac_ToolTable_RecipeClick fields ====="
foreach ($tn in @('GameCore.HotUpdate.ReduxUI.Ac_ToolTable_RecipeClick','GameCore.HotUpdate.ReduxUI.Ac_ToolTable_Make','GameCore.HotUpdate.ReduxUI.Ac_ToolTable_ItemMove','GameCore.HotUpdate.ReduxUI.Ac_ToolTable_SwitchTab')) {
    $t = $mod.GetType($tn)
    if (-not $t) { "no type: $tn"; continue }
    $fields = ($t.Fields | ForEach-Object { "$($_.FieldType.Name) $($_.Name)" }) -join '; '
    "$tn : $fields"
}

"`n===== PartialPick type fields ====="
foreach ($tn in @('GameCore.HotUpdate.ReduxUI.PartialPick','GameCore.HotUpdate.ReduxUI.Data_Web_ToolTable_Item','GameCore.HotUpdate.ReduxUI.Data_Web_ToolTable')) {
    $t = $mod.GetType($tn)
    if (-not $t) { "no type: $tn"; continue }
    $fields = ($t.Fields | ForEach-Object { "$($_.FieldType.Name) $($_.Name)" }) -join '; '
    "$tn : $fields"
}

"`n===== WebUI_ToolTable_Event public fields (event name constants) ====="
$ev = $mod.GetType('GameCore.HotUpdate.ReduxUI.WebUI_ToolTable_Event')
if ($ev) {
    foreach ($f in $ev.Fields) { "$($f.FieldType.Name) $($f.Name)" }
}

"`n===== State_Web_ToolTable fields ====="
$st = $mod.GetType('GameCore.HotUpdate.ReduxUI.State_Web_ToolTable')
if ($st) {
    foreach ($f in $st.Fields) { "$($f.FieldType.Name) $($f.Name)" }
}
