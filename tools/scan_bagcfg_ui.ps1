$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

# 1) Config_Bag full members
foreach ($t in $types) {
    if ($t.Name -eq 'Config_Bag') {
        Write-Host ("##### {0} : base {1}" -f $t.FullName, $t.BaseType.FullName)
        foreach ($f in $t.Fields) { if ($f.Name -match '^NativeFieldInfoPtr') { Write-Host ("  F {0}" -f $f.Name.Substring(19)) } }
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^get_|^set_') {
                Write-Host ("  M {0} -> {1}" -f $m.Name, $m.ReturnType.Name)
            }
        }
    }
}

# 2) ReduxUI methods with merge/split/drag/stack/combine
Write-Host '##### ReduxUI merge/split/drag/stack methods:'
foreach ($t in $types) {
    if ($t.FullName -notmatch 'ReduxUI|WebUI') { continue }
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)merge|split|drag|stack|combine|superpos') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}

# 3) ItemManager / BagManager-ish merge-split methods already known; search ALL for Merge/Split names
Write-Host '##### all Merge/Split methods:'
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)^Merge|^Split|MergeItem|SplitItem|CombineItem') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}
$asm.Dispose()
