$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

foreach ($tn in @('GameCore.HotUpdate.SaveChildData','GameCore.HotUpdate.AgentSave','GameCore.HotUpdate.GameSaveData')) {
    $t = $mod.GetType($tn)
    if (-not $t) { "no type: $tn"; continue }
    "== $tn ItemSave-related getters:"
    foreach ($m in $t.Methods) {
        if ($m.Name -match '^get_' -and ($m.ReturnType.Name -match 'ItemSave|Item' -or $m.Name -match 'Item')) {
            "  $($m.Name) -> $($m.ReturnType.FullName)"
        }
    }
}
"`n== ItemManager.SaveGame signature check + ItemSave usage in module:"
foreach ($t2 in $mod.Types) {
    foreach ($m in $t2.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($p in $m.Parameters) {
            if ($p.ParameterType.Name -eq 'ItemSave') { "$($t2.FullName) :: $($m.Name) takes ItemSave"; break }
        }
    }
}
