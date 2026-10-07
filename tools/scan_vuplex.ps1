$ErrorActionPreference = 'Continue'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
foreach ($dll in @('HotUpdate.dll','Assembly-CSharp.dll')) {
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\$dll", $rp)
    Write-Host "##### $dll"
    foreach ($t in $asm.MainModule.GetTypes()) {
        foreach ($m in $t.Methods) {
            $hit = $false
            try {
                foreach ($p in $m.Parameters) {
                    if ($p.ParameterType.Scope -ne $null -and $p.ParameterType.Scope.ToString() -match 'Vuplex') { $hit = $true; break }
                }
                if ($m.ReturnType.Scope -ne $null -and $m.ReturnType.Scope.ToString() -match 'Vuplex') { $hit = $true }
            } catch {}
            if ($hit) {
                $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ','
                Write-Host ("  {0} :: {1}({2})" -f $t.FullName, $m.Name, $ps)
            }
        }
        foreach ($e in $t.Events) {
            Write-Host ("  EVENT {0} :: {1}" -f $t.FullName, $e.Name)
        }
    }
    $asm.Dispose()
}
# Vuplex.WebView.dll: list WebView event members
$asm2 = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Vuplex.WebView.dll', $rp)
Write-Host '##### Vuplex.WebView.dll key types'
foreach ($t in $asm2.MainModule.GetTypes()) {
    if ($t.Name -match 'WebView') {
        $evts = ($t.Events | ForEach-Object { $_.Name }) -join ','
        if ($evts) { Write-Host ("  {0} : events [{1}]" -f $t.Name, $evts) }
    }
}
$asm2.Dispose()
