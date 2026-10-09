$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ActionComponent' }
Write-Host ("ActionComponent BaseType: {0}" -f $t.BaseType.FullName)
Write-Host ("Is MonoBehaviour-derived: {0}" -f ($t.BaseType.FullName -match 'MonoBehaviour'))
# FindObjectOfType in interop core module
$core = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\UnityEngine.CoreModule.dll', $rp)
$fo = $core.MainModule.GetType('UnityEngine.Object')
foreach ($m in $fo.Methods) {
    if ($m.Name -match 'FindObjectsOfType|FindObjectOfType') { Write-Host ("  Object.{0}()" -f $m.Name) }
}
$core.Dispose(); $asm.Dispose()
