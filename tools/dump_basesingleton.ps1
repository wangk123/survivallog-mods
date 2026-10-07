$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -match 'BaseSingleton' }
foreach ($x in $t) {
    Write-Host ("##### {0} : {1}" -f $x.FullName, $x.BaseType.FullName)
    foreach ($m in $x.Methods) {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.Name, $m.IsStatic)
    }
    foreach ($f in $x.Fields) { if ($f.Name -notmatch '^Native') { Write-Host ("  FIELD {0} : {1}" -f $f.Name, $f.FieldType.Name) } }
}
# also GetAll_Config_Item exact signature
$cm = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
foreach ($m in $cm.Methods) {
    if ($m.Name -match 'Config_Item_All|GetAll_Config_Item|_Config_Item_Dict') {
        $rt = $m.ReturnType
        $detail = $rt.FullName
        if ($rt -is [Mono.Cecil.GenericInstanceType]) {
            $detail = $rt.Namespace + '.' + $rt.Name + '<' + (($rt.GenericArguments | ForEach-Object { $_.FullName }) -join ',') + '>'
        }
        Write-Host ("CM METHOD {0} -> {1}" -f $m.Name, $detail)
    }
}
$asm.Dispose()
