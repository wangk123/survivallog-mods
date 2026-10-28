$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:\Game\Mod\mod_stack\tools\Cecil\Mono.Cecil.dll'
$core = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core'
$targets = @(
  (Join-Path $core 'BepInEx.Preloader.Core.dll'),
  (Join-Path $core 'BepInEx.Core.dll'),
  (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'),
  (Join-Path $core 'BepInEx.Unity.Common.dll')
)
foreach ($dll in $targets) {
  "DLL: [$dll] exists=" + (Test-Path -LiteralPath $dll)
}
$out = New-Object System.Collections.Generic.List[string]
foreach ($dll in $targets) {
  try { $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll) }
  catch {
    $out.Add("READ FAIL: $dll")
    $out.Add($_.Exception.ToString())
    continue
  }
  $mod = $asm.MainModule
  $out.Add("")
  $out.Add("########## " + [System.IO.Path]::GetFileName($dll))
  foreach ($ty in $mod.Types) {
    if ($ty.FullName -match 'Patcher|Chainloader|PluginsLoader|Paths$') {
      $out.Add("TYPE: " + $ty.FullName + "  (IsInterface=" + $ty.IsInterface + ")")
      foreach ($i in $ty.Interfaces) { $out.Add("    impl: " + $i.InterfaceType.FullName) }
      foreach ($m in $ty.Methods) {
        if ($m.IsPublic -or $m.IsFamily -or $ty.IsInterface) {
          $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName + ' ' + $_.Name }) -join ', '
          $out.Add("    METHOD: " + $m.ReturnType.Name + ' ' + $m.Name + '(' + $ps + ')')
        }
      }
      foreach ($p in $ty.Properties) {
        if ($p.Name -match 'Patch|Plugin') { $out.Add("    PROP: " + $p.PropertyType.Name + ' ' + $p.Name) }
      }
    }
  }
}
$out | Out-File -FilePath 'E:\Game\Mod\mod_stack\tools\_pp_patcher_api.out.txt' -Encoding UTF8
Write-Host ('LINES: ' + $out.Count)
