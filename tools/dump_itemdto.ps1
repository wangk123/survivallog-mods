$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

foreach ($t in $types) {
    if ($t.Name -eq 'ItemDataDto') {
        Write-Host ("===== " + $t.FullName + " =====  base=" + $t.BaseType.FullName)
        foreach ($f in $t.Fields) {
            if ($f.Name -match '^NativeFieldInfoPtr_(.+)$') {
                Write-Host ("  FIELD {0} : {1}" -f $Matches[1], $f.FieldType.Name)
            }
        }
        foreach ($m in $t.Methods) {
            if ($m.Name -match '^\.') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}

# WebUI_Backpack_ItemsAMsg - the items message for side A
$msg = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.WebUI_Backpack_ItemsAMsg' }
if ($msg) {
    Write-Host ''
    Write-Host ("===== " + $msg.FullName + " =====")
    foreach ($m in $msg.Methods) {
        if ($m.Name -match '^\.') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
