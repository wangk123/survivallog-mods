# ASCII-only. Dump IL of ItemManager.OnMoveItem / TryMergeIntoOwner / OnQuickMoveItem
# and find all call sites of OnMoveItem to determine argument order.
$ErrorActionPreference = 'Stop'
Add-Type -Path "C:\Users\wangk\.zcode\workspace\default\mod_stack\release\SurvivalLog.StackLimit999_v2.7.9\BepInEx\core\Mono.Cecil.dll"

$dll = "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
$mod = $asm.MainModule

function Dump-IL($typeName, $methodName) {
    $t = $mod.GetType($typeName)
    if (-not $t) { "TYPE NOT FOUND: $typeName"; return }
    foreach ($m in $t.Methods) {
        if ($m.Name -ne $methodName) { continue }
        if (-not $m.HasBody) { continue }
        "===== $typeName :: $methodName ($($m.Parameters.Count) params) ====="
        foreach ($p in $m.Parameters) { "  param $($p.Index): $($p.ParameterType.FullName) $($p.Name)" }
        foreach ($ins in $m.Body.Instructions) {
            $op = $ins.OpCode.Name
            $operand = ''
            if ($ins.Operand -ne $null) { $operand = $ins.Operand.ToString() }
            "  IL_$($ins.Offset.ToString('X4')): $op $operand"
        }
        ""
    }
}

Dump-IL 'GameCore.HotUpdate.Battle.Logic.ItemManager' 'OnMoveItem'
Dump-IL 'GameCore.HotUpdate.Battle.Logic.ItemManager' 'TryMergeIntoOwner'

"===== call sites of OnMoveItem (whole module) ====="
foreach ($t in $mod.Types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        $hits = @()
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Name -eq 'call' -or $ins.OpCode.Name -eq 'callvirt') {
                if ($ins.Operand -and $ins.Operand.ToString() -match 'ItemManager::OnMoveItem') {
                    $hits += "IL_$($ins.Offset.ToString('X4'))"
                }
            }
        }
        if ($hits.Count -gt 0) {
            "$($t.FullName) :: $($m.Name)  -> $($hits -join ',')"
        }
    }
}
