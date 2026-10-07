$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($tname in @('GameCore.HotUpdate.ReduxUI.Ac_BackpackUI_RefreshBag','GameCore.HotUpdate.ReduxUI.Ac_BackpackUI_Sort','GameCore.HotUpdate.ReduxUI.Ac_BackpackUI_DragItem','GameCore.HotUpdate.ReduxUI.Ac_BackpackUI_ClickItem')) {
    $t = $types | Where-Object { $_.FullName -eq $tname }
    if ($t) {
        Write-Host ("===== " + $t.FullName + " =====")
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
        }
        Write-Host ''
    }
}

$la = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.LogicAdapter' }
if ($la) {
    Write-Host '===== LogicAdapter methods (Backpack/Bag/Refresh related) ====='
    foreach ($m in $la.Methods) {
        if ($m.Name -match '(?i)backpack|bag|refresh|sort|drag|item') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}
$asm.Dispose()
