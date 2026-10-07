$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:\Users\wangk\.zcode\workspace\default\mod_stack\tools\Cecil\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll')
foreach ($tn in @('GameCore.HotUpdate.ReduxUI.Ac_ToolTable_Open','GameCore.HotUpdate.ReduxUI.Ac_ToolTable_RecipeClick')) {
    $t = $asm.MainModule.GetType($tn)
    if (-not $t) { "no: $tn"; continue }
    "== $tn"
    foreach ($m in $t.Methods) {
        if ($m.Name -match 'SendAction|get_JsonData') {
            $static = (([int]$m.Attributes) -band 16) -ne 0
            "  $($m.Name) static=$static"
        }
    }
}
