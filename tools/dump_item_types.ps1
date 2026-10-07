$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$interop = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop'
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $interop 'HotUpdate.dll'), $rp)
$mod = $asm.MainModule
$allTypes = $mod.GetTypes()

# 1) find types named like ItemData / Config_Item / ItemManager
$interesting = $allTypes | Where-Object { $_.Name -match '^(Config_Item|ItemData|ItemManager)$' -or $_.FullName -match '^GameCore\.HotUpdate\.(Battle\.Logic\.)?(ItemData|ItemManager|Config_Item)$' }
foreach ($t in $interesting) {
    Write-Host ("`n########## {0}" -f $t.FullName)
    foreach ($f in $t.Fields) {
        if ($f.Name -match '^Native') { continue }
        Write-Host ("  FIELD {0} : {1}" -f $f.Name, $f.FieldType.Name)
    }
    foreach ($m in $t.Methods) {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
        Write-Host ("  METHOD {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}

# 2) any other types with property/field named StackLimit or MaxStack
Write-Host "`n########## All StackLimit mentions"
foreach ($t in $allTypes) {
    foreach ($f in $t.Fields) {
        if ($f.Name -match '(?i)stacklimit') { Write-Host ("{0} :: {1}" -f $t.FullName, $f.Name) }
    }
}
$asm.Dispose()
