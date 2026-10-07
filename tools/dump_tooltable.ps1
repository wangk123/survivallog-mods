$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

Write-Host '##### ToolTable related types:'
foreach ($t in $types) {
    if ($t.FullName -match '(?i)tooltable|recipe|workbench' -and $t.FullName -notmatch 'Formatter|MethodInfo|__c$') {
        Write-Host ("  " + $t.FullName)
    }
}

foreach ($tn in @('GameCore.HotUpdate.ToolTableManager')) {
    $t = $types | Where-Object { $_.FullName -eq $tn }
    if (-not $t) { Write-Host "MISSING $tn"; continue }
    Write-Host ""
    Write-Host "########## $tn methods:"
    foreach ($m in $t.Methods) {
        if ($m.Name -like '.*') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("   {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
    Write-Host "-- fields:"
    foreach ($f in $t.Fields) {
        if ($f.Name -like 'NativeFieldInfoPtr*') {
            Write-Host ("   {0} : {1}" -f $f.Name.Replace('NativeFieldInfoPtr_',''), $f.FieldType.Name)
        }
    }
}

# Config_ToolTable fields
Write-Host ""
$ct = $types | Where-Object { $_.FullName -match '^GameCore\.HotUpdate\.Config_' -and $_.FullName -match 'Tool|Recipe|Synth' }
foreach ($t in $ct) {
    if ($t.FullName -match 'Formatter|MethodInfo') { continue }
    Write-Host ("########## " + $t.FullName)
    foreach ($p in $t.Properties) {
        Write-Host ("   {0} : {1}" -f $p.Name, $p.PropertyType.Name)
    }
}
$asm.Dispose()
