$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
Write-Host "--- Logic types with ActionComponent property ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    foreach ($p in $ty.Properties) {
        if ($p.PropertyType.Name -eq 'ActionComponent') {
            Write-Host ("  {0}.{1} : {2}" -f $ty.FullName, $p.Name, $p.PropertyType.Name)
        }
    }
}
Write-Host "--- Types with 'CurPlayer'/'CurrentPlayer'/'MainPlayer'/'Player' static or singleton-ish ---"
foreach ($ty in $asm.MainModule.GetTypes()) {
    if ($ty.FullName -notlike 'GameCore.HotUpdate*') { continue }
    foreach ($m in $ty.Methods) {
        if ($m.IsStatic -and $m.Name -match '^(get_)?(Cur|Current|Main|Local)?Player\w*$' -and $m.ReturnType.FullName -notmatch 'Int|Single|Boolean|String|Void') {
            Write-Host ("  {0}.{1}() -> {2}" -f $ty.FullName, $m.Name, $m.ReturnType.FullName)
        }
    }
}
$asm.Dispose()
