$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

foreach ($tn in @('GameCore.HotUpdate.GameSaveData','GameCore.HotUpdate.ItemSave','GameCore.HotUpdate.SaveChildData')) {
    $t = $mod.GetType($tn)
    if (-not $t) { "no type: $tn"; continue }
    "== $tn fields:"
    foreach ($f in $t.Fields) {
        if ($f.Name -match '^Native') { continue }
        "  $($f.FieldType.FullName) $($f.Name)"
    }
    "== $tn methods (get_/set_ only, first 25):"
    $n = 0
    foreach ($m in $t.Methods) {
        if ($m.Name -match '^get_' -and $n -lt 25) { "  $($m.Name) -> $($m.ReturnType.Name)"; $n++ }
    }
}
