$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '===== 1) ItemManager methods ====='
$im = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager' }
foreach ($m in $im.Methods) {
    if ($m.Name -match '^\.') { continue }
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ', '
    Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.FullName)
}

Write-Host ''
Write-Host '===== 2) methods containing ldstr DRAG_ITEM / SPLIT / CLICK_ITEM / MOVE_ITEM ====='
$hits = @()
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Name -eq 'ldstr' -and $ins.Operand -match '^(DRAG_ITEM|SPLIT|CLICK_ITEM|MOVE_ITEM|SORT_BACKPACK|DROP_ITEM|USE_ITEM|PICK)' ) {
                Write-Host ("  HIT [{0}] {1} :: {2}({3}) ldstr={4}" -f $ins.Operand, $t.FullName, $m.Name, (($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','), $ins.Operand)
                $hits += $m
                break
            }
        }
    }
}

$asm.Dispose()
