$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

# 1) Config_Item real fields (non-Native)
$ci = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Config_Item' }
Write-Host '### Config_Item fields (non-Native):'
foreach ($f in $ci.Fields) { if ($f.Name -notmatch '^Native') { Write-Host ("  {0} : {1} (public={2})" -f $f.Name, $f.FieldType.Name, $f.IsPublic) } }

# 2) Weight-related types/members
Write-Host '### Weight members:'
foreach ($t in $types) {
    foreach ($f in $t.Fields) {
        if ($f.Name -notmatch '^Native' -and $f.Name -match '(?i)weight') { Write-Host ("  F {0} :: {1} : {2}" -f $t.FullName, $f.Name, $f.FieldType.Name) }
    }
    foreach ($m in $t.Methods) {
        if ($m.Name -match '(?i)weight' -and $m.Name -notmatch '^Native') {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
            Write-Host ("  M {0} :: {1}({2}) -> {3}" -f $t.FullName, $m.Name, $ps, $m.ReturnType.Name)
        }
    }
}
$asm.Dispose()
