$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)

$vm = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.WebUIVm' }
Write-Host '===== WebUIVm methods ====='
foreach ($m in $vm.Methods) {
    if ($m.Name -match '^\.') { continue }
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName + ' ' + $_.Name }) -join ', '
    Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
}
$asm.Dispose()
