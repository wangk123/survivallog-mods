$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$targets = @(
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\StatsUI.dll',
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\BetterTrade.dll',
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\ProjectCook.dll'
)

foreach ($t in $targets) {
    $name = [System.IO.Path]::GetFileName($t)
    Write-Output "`n==================== $name ===================="
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($t, $rp)
    Write-Output "---- RESOURCES ----"
    foreach ($r in $asm.MainModule.Resources) { Write-Output ("  R {0} ({1})" -f $r.Name, $r.ResourceType) }
    Write-Output "---- LDSTR (unique, len>=6) ----"
    $seen = @{}
    foreach ($ty in $asm.MainModule.GetTypes()) {
        foreach ($m in $ty.Methods) {
            if (-not $m.HasBody) { continue }
            foreach ($ins in $m.Body.Instructions) {
                if ($ins.OpCode.Code -eq 'Ldstr') {
                    $s = [string]$ins.Operand
                    if ($s.Length -ge 6 -and -not $seen.ContainsKey($s)) {
                        $seen[$s] = $true
                        Write-Output ("  [{0}] {1}" -f $ty.Name, ($s -replace "`r?`n", ' \n '))
                    }
                }
            }
        }
    }
    $asm.Dispose()
}
