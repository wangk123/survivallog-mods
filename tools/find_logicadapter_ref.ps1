$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()
$LA = 'GameCore.HotUpdate.ReduxUI.LogicAdapter'

Write-Host '===== static fields of type LogicAdapter ====='
foreach ($t in $types) {
    foreach ($f in $t.Fields) {
        if ($f.FieldType.FullName -eq $LA -and $f.IsStatic) {
            Write-Host ("  {0}.{1}" -f $t.FullName, $f.Name)
        }
    }
}

Write-Host '===== instance fields of type LogicAdapter (owners) ====='
foreach ($t in $types) {
    foreach ($f in $t.Fields) {
        if ($f.FieldType.FullName -eq $LA -and -not $f.IsStatic) {
            Write-Host ("  {0} :: {1}" -f $t.FullName, $f.Name)
        }
    }
}

Write-Host '===== static methods returning/taking LogicAdapter ====='
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if ($m.ReturnType.FullName -eq $LA -or ($m.Parameters | Where-Object { $_.ParameterType.FullName -eq $LA })) {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0}::{1}({2}) [{3}]" -f $t.FullName, $m.Name, $ps, $(if ($m.IsStatic) {'static'} else {'instance'}))
        }
    }
}

Write-Host '===== BaseSingleton users: is there BaseSingleton_1[LogicAdapter]? ====='
foreach ($t in $types) {
    if ($t.FullName -match 'BaseSingleton.*LogicAdapter') { Write-Host ("  " + $t.FullName) }
}
$asm.Dispose()
