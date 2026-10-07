$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
$mod = $asm.MainModule

"===== Ac_* action types with Continue/Start/Enter/Load in name ====="
foreach ($ty in $mod.Types) {
    if ($ty.Name -match '^Ac_' -and $ty.Name -match 'Continue|Enter|Start|Load|Menu|Main|Lobby|Hall') {
        $sa = ($ty.Methods | Where-Object { $_.Name -eq 'SendAction' }) | Select-Object -First 1
        if ($sa) {
            $ps = ($sa.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '
            "  $($ty.FullName) :: SendAction($ps)"
        }
    }
}

"`n===== methods named ContinueGame/EnterGame/StartGame/LoadLatest/ContinueLast ====="
foreach ($ty in $mod.Types) {
    foreach ($m in $ty.Methods) {
        if ($m.Name -match 'ContinueGame|EnterGame|StartGame|LoadLatest|ContinueLast|EnterBattle|ContinueLastSave') {
            $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
            "  $($ty.FullName) :: $($m.Name)($ps) -> $($m.ReturnType.Name)"
        }
    }
}
