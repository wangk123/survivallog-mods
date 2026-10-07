$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

function Dump-IL($t, $m) {
    Write-Host ("----- IL: {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, (($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','), $m.ReturnType.Name)
    foreach ($ins in $m.Body.Instructions) {
        $op = $ins.Operand
        $tn = if ($null -ne $op) { $op.GetType().Name } else { '' }
        if ($tn -eq 'MethodReference' -or $tn -eq 'MethodDefinition') { $op = "-> " + $op.DeclaringType.Name + "." + $op.Name }
        elseif ($tn -eq 'FieldReference' -or $tn -eq 'FieldDefinition') { $op = "fld " + $op.DeclaringType.Name + "." + $op.Name }
        elseif ($tn -eq 'Instruction') { $op = "IL_" + $op.Offset.ToString('X4') }
        elseif ($tn -eq 'Instruction[]') { $op = "[" + (($op | ForEach-Object { 'IL_' + $_.Offset.ToString('X4') }) -join ', ') + "]" }
        Write-Host ("  IL_{0:X4}: {1} {2}" -f $ins.Offset, $ins.OpCode.Name, $op)
    }
}

$im = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager' }

foreach ($name in @('SplitItem','TryMergeIntoOwner','TakeOneFromStack')) {
    foreach ($m in $im.Methods) {
        if ($m.Name -eq $name) { Dump-IL $im $m }
    }
}

$ev = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.WebUI_BackpackUI_Event' }
Write-Host ''
Write-Host '===== WebUI_BackpackUI_Event methods ====='
foreach ($m in $ev.Methods) {
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
    Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
}

$asm.Dispose()
