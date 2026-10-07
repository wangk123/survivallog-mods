$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

function ShowType($name) {
    foreach ($t in $types) {
        if ($t.Name -eq $name) {
            Write-Host ("##### {0} : base {1}" -f $t.FullName, $t.BaseType.FullName)
            foreach ($f in $t.Fields) { if ($f.Name -match '^NativeFieldInfoPtr') { Write-Host ("  F {0}" -f $f.Name.Substring(19)) } }
            foreach ($m in $t.Methods) {
                if ($m.Name -match '^get_|^set_') { Write-Host ("  M {0} -> {1}" -f $m.Name, $m.ReturnType.Name) }
            }
        }
    }
}
ShowType 'Ac_BackpackUI_DragItem'
ShowType 'ItemData'
$asm.Dispose()
