$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

$targets = @(
    'GameCore.HotUpdate.Battle.Logic.E_Action_Default_MovingFloor',
    'GameCore.HotUpdate.Battle.Logic.FloorRouteHelper',
    'GameCore.HotUpdate.Battle.Logic.AE_FloorMove',
    'GameCore.HotUpdate.Battle.Logic.AE_FloorTeleport',
    'GameCore.HotUpdate.Battle.Logic.FloorMenuBridge'
)
foreach ($tn in $targets) {
    $t = $types | Where-Object { $_.FullName -eq $tn }
    if (-not $t) { Write-Host "MISSING: $tn"; continue }
    Write-Host ""
    Write-Host "########## $tn"
    Write-Host '-- fields:'
    foreach ($f in $t.Fields) {
        if ($f.Name -like 'NativeFieldInfoPtr*') {
            Write-Host ("   {0} : {1}" -f $f.Name, $f.FieldType.Name)
        }
    }
    Write-Host '-- methods:'
    foreach ($m in $t.Methods) {
        if ($m.Name -like '.*') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ', '
        Write-Host ("   {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
    }
}
$asm.Dispose()
