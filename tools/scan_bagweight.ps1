$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

function ShowType($name) {
    foreach ($t in $types) {
        if ($t.FullName -eq $name -or $t.Name -eq $name) {
            Write-Host ("##### {0} : base {1}" -f $t.FullName, $t.BaseType.FullName)
            foreach ($f in $t.Fields) { if ($f.Name -notmatch '^NativeFieldInfoPtr') { Write-Host ("  F {0} : {1}" -f $f.Name, $f.FieldType.Name) } }
            foreach ($m in $t.Methods) {
                $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
                Write-Host ("  M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
            }
        }
    }
}
ShowType 'AE_TestBagWeight'
ShowType 'BagWeight'

Write-Host '##### methods mentioning BagWeight:'
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)bagweight') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
    foreach ($f in $t.Fields) {
        if ($f.Name -match '(?i)bagweight' -and $f.Name -notmatch '^NativeMethodInfoPtr') {
            Write-Host ("  FIELD {0} :: {1} : {2}" -f $t.FullName, $f.Name, $f.FieldType.Name)
        }
    }
}
$asm.Dispose()
