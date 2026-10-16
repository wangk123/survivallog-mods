$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$hits = New-Object System.Collections.Generic.List[string]
foreach ($ty in $asm.MainModule.GetTypes()) {
    foreach ($m in $ty.Methods) {
        if (-not $m.HasBody) { continue }
        $found = $false
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Code -eq 'Ldstr') {
                $s = [string]$ins.Operand
                if ($s -eq 'MaterialRack') { $found = $true }
            }
        }
        if ($found) {
            $hits.Add(("METHOD {0}.{1}" -f $ty.FullName, $m.Name))
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.OpCode.Code -eq 'Ldstr') {
                    $s = [string]$ins.Operand
                    if ($s.Length -gt 0) { $hits.Add(("   S {0}" -f $s)) }
                }
            }
        }
    }
}
$hits | ForEach-Object { Write-Output $_ }
$asm.Dispose()
