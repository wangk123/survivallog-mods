$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)

$ci = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Config_Item' }
Write-Host '##### Config_Item properties (interop view):'
foreach ($p in $ci.Properties) {
    Write-Host ("  {0} : {1}" -f $p.Name, $p.PropertyType.Name)
}
Write-Host ''
Write-Host '##### Config_Item methods:'
foreach ($m in $ci.Methods) {
    if ($m.Name -match '^\.') { continue }
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
    Write-Host ("  {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
}

# 楼梯/楼层/寻路相关类型侦察
Write-Host ''
Write-Host '##### Types matching stair/floor/nav/build/interact:'
foreach ($t in $asm.MainModule.GetTypes()) {
    if ($t.FullName -match '(?i)stair|floor|storey|navmesh|navigat|pathfind|go.?up|go.?down|basement|changebuilding|switchfloor') {
        Write-Host ("  {0}" -f $t.FullName)
    }
}
$asm.Dispose()
