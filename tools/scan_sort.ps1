$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '##### Sort/Tidy/Organize/Arrange/Adjust methods:'
foreach ($t in $types) {
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)sort|tidy|organize|arrange|adjust|neaten') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}

Write-Host '##### BackpackUI reducer actions:'
foreach ($t in $types) {
    if ($t.FullName -match 'BackpackUI') {
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^RA_|^Btn_|^Trigger_') {
                Write-Host ("  {0} :: {1}" -f $t.FullName, $m.Name)
            }
        }
        foreach ($f in $t.Fields) {
            if ($f.Name -match '^NativeFieldInfoPtr_(Btn|_)' -and $f.Name -notmatch 'k__BackingField') { Write-Host ("  F {0} :: {1}" -f $t.FullName, $f.Name) }
        }
    }
}
$asm.Dispose()
