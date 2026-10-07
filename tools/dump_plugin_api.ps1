$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

function ShowMethod($m) {
    $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
    $g = if ($m.HasGenericParameters) { '<' + (($m.GenericParameters | ForEach-Object { $_.Name }) -join ',') + '>' } else { '' }
    Write-Host ("  {0}{1}({2}) -> {3} [static={4}]" -f $m.Name, $g, $ps, $m.ReturnType.Name, $m.IsStatic)
}

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Unity.IL2CPP.dll', $rp)
$bp = $asm.MainModule.GetType('BepInEx.Unity.IL2CPP.BasePlugin')
Write-Host "### BasePlugin"
foreach ($m in $bp.Methods) {
    if ($m.Name -in @('AddComponent','get_Log','get_Config','Load','Unload')) { ShowMethod $m }
}
foreach ($p in $bp.Properties) { Write-Host ("  PROP {0} : {1}" -f $p.Name, $p.PropertyType.Name) }
$asm.Dispose()

$asm2 = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\UnityEngine.CoreModule.dll', $rp)
$go = $asm2.MainModule.GetTypes() | Where-Object { $_.Name -eq 'GameObject' }
Write-Host "### GameObject (interop)"
foreach ($m in $go.Methods) { if ($m.Name -match 'AddComponent|\.ctor') { ShowMethod $m } }
$asm2.Dispose()
