$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false

$outDir = 'E:\Game\Mod\mod_stack\tools\newmods_res'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$targets = @(
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\StatsUI.dll',
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\BetterTrade.dll',
    'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\ProjectCook.dll'
)

foreach ($t in $targets) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($t)
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($t, $rp)
    foreach ($r in $asm.MainModule.Resources) {
        if ($r.ResourceType -eq 'Embedded') {
            $dest = Join-Path $outDir ($name + '.' + $r.Name)
            [System.IO.File]::WriteAllBytes($dest, $r.GetResourceData())
            Write-Output ("{0} -> {1} ({2} bytes)" -f $r.Name, $dest, (Get-Item $dest).Length)
        }
    }
    $asm.Dispose()
}
