$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

$id = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemData' }

Write-Host '===== ItemData base chain ====='
$t = $id
while ($null -ne $t.BaseType) {
    $base = $t.BaseType
    Write-Host ("  {0} -> {1}" -f $t.FullName, $base.FullName)
    $resolved = try { $base.Resolve() } catch { $null }
    if ($null -eq $resolved) { break }
    Write-Host '    base fields:'
    foreach ($f in $resolved.Fields) {
        if ($f.Name -match '^NativeFieldInfoPtr_(.+)$') {
            Write-Host ("      {0}  : {1}" -f $Matches[1], $f.FieldType.Name)
        }
    }
    $t = $resolved
}

Write-Host ''
Write-Host '===== ItemManager own fields (look for id counter) ====='
$im = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Battle.Logic.ItemManager' }
foreach ($f in $im.Fields) {
    if ($f.Name -match '^NativeFieldInfoPtr_(.+)$' -and $f.Name -notmatch 'k__BackingField') {
        Write-Host ("  {0}  : {1}" -f $Matches[1], $f.FieldType.Name)
    }
}
Write-Host '--- ItemManager backing fields:'
foreach ($f in $im.Fields) {
    if ($f.Name -match '^NativeFieldInfoPtr_.*k__BackingField$') {
        $n = $f.Name -replace '^NativeFieldInfoPtr_','' -replace '_k__BackingField$',''
        Write-Host ("  {0}  : {1}" -f $n, $f.FieldType.Name)
    }
}
$asm.Dispose()
