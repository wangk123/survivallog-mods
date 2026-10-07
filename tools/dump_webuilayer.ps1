$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($t in $types) {
    if ($t.Name -in @('WebUILayer','RouteToPageOrQueue')) {
        Write-Host ("##### TYPE {0} : base {1}" -f $t.FullName, $t.BaseType.Name)
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  M {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
        }
    }
}

# also find classes whose name contains BackpackUI screen/controller (non-reducer, non-state)
Write-Host '##### BackpackUI controller-ish types:'
foreach ($t in $types) {
    if ($t.FullName -match 'BackpackUI' -and $t.FullName -notmatch 'Reducer_|State_|Ac_|WebUI_BackpackUI_Event|Msg|Localization') {
        Write-Host ("  TYPE {0} : base {1}" -f $t.FullName, $t.BaseType.Name)
        foreach ($m in $t.Methods) {
            if ($m.Name -match 'Drag|Sort|Handle|Route|Register|Init|Show|Open|Event') {
                $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
                Write-Host ("    M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
            }
        }
    }
}
$asm.Dispose()
