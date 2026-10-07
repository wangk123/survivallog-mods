$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$interop = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop'
$targets = @('Assembly-CSharp.dll','HotUpdate.dll')
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

function ScanAsm($path) {
    Write-Host "===== $([IO.Path]::GetFileName($path)) ====="
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $rp)
    foreach ($mod in $asm.Modules) {
        $allTypes = $mod.GetTypes()
        Write-Host ("total types: {0}" -f $allTypes.Count)
        foreach ($t in $allTypes) {
            $tname = $t.FullName
            if ($tname -match '(?i)stackable|itemstack|stack') {
                Write-Host ("[TypeName] {0}" -f $tname)
            }
            try {
                foreach ($f in $t.Fields) {
                    $fn = $f.Name
                    if ($fn -match '(?i)stack|MaxCount|MaxNum|Overlap') {
                        Write-Host ("  TYPE: {0} | FIELD: {1} : {2}" -f $tname, $fn, $f.FieldType.Name)
                    }
                }
                foreach ($m in $t.Methods) {
                    $mn = $m.Name
                    if ($mn -match '(?i)GetMaxStack|MaxStack|StackLimit|MaxStackCount|GetStackMax|StackMax|CanStack|IsStackable|MaxOverlap|GetMaxNum|GetMaxCount') {
                        Write-Host ("  TYPE: {0} | METHOD: {1}({2}) -> {3}" -f $tname, $mn, (($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','), $m.ReturnType.Name)
                    }
                }
            } catch {}
        }
    }
    $asm.Dispose()
}

foreach ($t in $targets) {
    $p = Join-Path $interop $t
    if (Test-Path $p) { ScanAsm $p }
}
