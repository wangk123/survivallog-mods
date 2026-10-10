$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Il2CppInterop.Runtime.dll', $rp)
foreach ($t in $asm.MainModule.GetTypes()) {
    if ($t.Name -eq 'Il2CppType') {
        foreach ($m in $t.Methods) {
            $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ','
            Write-Host ("  M {0}({1}) -> {2} [static={3}]" -f $m.Name, $ps, $m.ReturnType.FullName, $m.IsStatic)
        }
    }
}
$asm.Dispose()
