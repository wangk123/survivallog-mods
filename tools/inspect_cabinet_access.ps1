$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

$want = @('ToolCabinetConfig','ReduxUISystem','ReduxStoreLayer','StateTree','BaseReduxState',
          'Data_Web_Cooking_BagTab','Data_Web_RatCage_BagTab','Data_Web_Brew_BagTab',
          'State_Web_Cooking','State_Web_RatCage','State_Web_Brew',
          'AgentManager','Furniture','BagComponent')
foreach ($t in $types) {
    if ($want -notcontains $t.Name) { continue }
    Write-Host ("##### {0} (base: {1})" -f $t.FullName, $t.BaseType.Name)
    foreach ($f in $t.Fields) {
        if ($f.Name -match '^<') { continue }
        Write-Host ("  F {0} : {1} [static={2}]" -f $f.Name, $f.FieldType.FullName, $f.IsStatic)
    }
    foreach ($m in $t.Methods) {
        if ($m.Name -match '^\.|^get_|^set_') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
