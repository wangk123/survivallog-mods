$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

function DumpApi($path, $patterns) {
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $rp)
    Write-Host ("===== {0} (version {1}) =====" -f [IO.Path]::GetFileName($path), $asm.Name.Version)
    foreach ($t in $asm.MainModule.GetTypes()) {
        foreach ($p in $patterns) {
            if ($t.FullName -like $p) {
                $meths = ($t.Methods | Where-Object { -not $_.IsConstructor } | Select-Object -First 12 | ForEach-Object { $_.Name }) -join ', '
                Write-Host ("  {0}  [{1}]" -f $t.FullName, $meths)
            }
        }
    }
    $asm.Dispose()
}

$core = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core'
DumpApi (Join-Path $core 'BepInEx.Unity.IL2CPP.dll') @('BepInEx.Unity.IL2CPP.*')
DumpApi (Join-Path $core 'Il2CppInterop.Runtime.dll') @('Il2CppInterop.Runtime.Injection.*')
DumpApi (Join-Path $core '0Harmony.dll') @('HarmonyLib.Harmony')
DumpApi (Join-Path $core 'BepInEx.Core.dll') @('BepInEx.*PluginAttribute','BepInEx.Configuration.ConfigFile','BepInEx.Configuration.ConfigEntry*1')
