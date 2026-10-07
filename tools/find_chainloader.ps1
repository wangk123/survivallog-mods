$ErrorActionPreference = 'Stop'
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Unity.IL2CPP.dll', $rp)
foreach ($t in $asm.MainModule.GetTypes()) {
  if ($t.Name -like '*Chainloader*' -or $t.Name -like '*Plugin*') {
    Write-Host ('TYPE ' + $t.FullName)
    foreach ($m in $t.Methods | Where-Object { -not $_.IsConstructor } | Select-Object -First 10) {
      Write-Host ('   ' + $m.Name)
    }
    foreach ($p in $t.Properties | Select-Object -First 10) {
      Write-Host ('   P ' + $p.Name + ' : ' + $p.PropertyType.FullName)
    }
  }
}
$asm.Dispose()
