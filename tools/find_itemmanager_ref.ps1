$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
foreach ($t in $asm.MainModule.GetTypes()) {
    foreach ($f in $t.Fields) {
        if ($f.FieldType.Name -eq 'ItemManager' -or ($f.FieldType.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager')) {
            Write-Host ("FIELD {0} :: {1} (static={2})" -f $t.FullName, $f.Name, $f.IsStatic)
        }
    }
    foreach ($m in $t.Methods) {
        if ($m.ReturnType.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager') {
            Write-Host ("METHOD {0}::{1}() -> ItemManager (static={2})" -f $t.FullName, $m.Name, $m.IsStatic)
        }
    }
}
$asm.Dispose()
