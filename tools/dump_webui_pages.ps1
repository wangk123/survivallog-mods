$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$seen = @{}
foreach ($ty in $asm.MainModule.GetTypes()) {
    foreach ($m in $ty.Methods) {
        if (-not $m.HasBody) { continue }
        foreach ($ins in $m.Body.Instructions) {
            if ($ins.OpCode.Code -eq 'Ldstr') {
                $s = [string]$ins.Operand
                if ($s -match 'UI/|\.html|^[A-Z][A-Za-z0-9]+$' -and $s -match 'Bag|Cabinet|Rack|Container|Storage|Shelf') {
                    if (-not $seen.ContainsKey($s)) { $seen[$s] = $ty.FullName }
                }
            }
        }
    }
}
foreach ($k in $seen.Keys) { Write-Output ("{0}  <=  {1}" -f $k, $seen[$k]) }
$asm.Dispose()
