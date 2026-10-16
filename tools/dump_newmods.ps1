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
    Write-Host "`n==================== $([System.IO.Path]::GetFileName($t)) ===================="
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($t, $rp)
    foreach ($ty in $asm.MainModule.GetTypes()) {
        Write-Host ("TYPE {0}" -f $ty.FullName)
        foreach ($m in $ty.Methods) {
            if ($m.Name -match '^get_|^set_') { continue }
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
            Write-Host ("  M {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.Name)
        }
        foreach ($f in $ty.Fields) {
            if ($f.Name -match '^<') { continue }
            Write-Host ("  F {0} : {1}" -f $f.Name, $f.FieldType.Name)
        }
    }
    Write-Host "---- STRINGS ----"
    foreach ($s in $asm.MainModule.Strings) { Write-Host ("  S {0}" -f $s) }
    $asm.Dispose()
}
