$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '##### methods matching stack/merge/pile/split:'
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)stack|merge|pile|splititem|getmaxcount|maxnum') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}

Write-Host ''
Write-Host '##### Config_* fields matching stack/num/count/max:'
foreach ($t in $types) {
    if ($t.FullName -match '^GameCore\.HotUpdate\.Config_' -and $t.FullName -notmatch 'Formatter|MethodInfo') {
        foreach ($f in $t.Fields) {
            if ($f.Name -match '(?i)stack|maxnum|maxcount|pilenum|num$') {
                Write-Host ("  {0} :: {1} : {2}" -f $t.FullName.Replace('GameCore.HotUpdate.',''), $f.Name.Replace('NativeFieldInfoPtr_',''), $f.FieldType.Name)
            }
        }
    }
}
$asm.Dispose()
