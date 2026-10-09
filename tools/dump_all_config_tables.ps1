$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$t = $asm.MainModule.GetTypes() | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ConfigManager' }
Write-Host "--- ConfigManager fields (config dicts) ---"
$dictNames = @()
foreach ($f in $t.Fields) {
    if ($f.Name -notmatch '^Native') {
        Write-Host ("  {0} : {1}" -f $f.Name, $f.FieldType.Name)
        if ($f.FieldType.Name -like '*Dictionary*') { $dictNames += $f.Name }
    }
}
Write-Host ""
Write-Host "--- Types with Action-related int fields (Config_* tables) ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -notlike 'GameCore.HotUpdate.Config_*') { continue }
    $actionFields = @()
    foreach ($f in $ty.Fields) {
        if ($f.Name -match 'Action') { $actionFields += ("{0}:{1}" -f $f.Name, $f.FieldType.Name) }
    }
    if ($actionFields.Count -gt 0) {
        Write-Host ("  {0} -> {1}" -f $ty.Name, ($actionFields -join ', '))
    }
}
$asm.Dispose()
