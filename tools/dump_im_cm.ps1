$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)

# ItemManager all methods
$im = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager' }
Write-Host '##### ItemManager methods:'
foreach ($m in $im.Methods) {
    if ($m.Name -match '^\.') { continue }
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
    Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
}

# ConfigManager Bag dict accessors
$cm = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host '##### ConfigManager Bag members:'
foreach ($m in $cm.Methods) {
    if ($m.Name -match 'Config_Bag') {
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
        Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
