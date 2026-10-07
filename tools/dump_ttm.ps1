$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$cands = $asm.MainModule.Types | Where-Object { $_.Name -match 'ToolTableManager' }
foreach ($t in $cands) {
    "== $($t.FullName)"
    foreach ($m in $t.Methods) {
        $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
        "  $($m.Name)($ps) -> $($m.ReturnType.Name)"
    }
}
