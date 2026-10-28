$ErrorActionPreference = 'Stop'
Add-Type -Path 'E:\Game\Mod\mod_stack\tools\Cecil\Mono.Cecil.dll'
$core = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core'
$out = New-Object System.Collections.Generic.List[string]

# 1) who references AssemblyPatcher / PatcherPluginPath (call sites)
$targets = @(
  (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'),
  (Join-Path $core 'BepInEx.Preloader.Core.dll')
)
foreach ($dll in $targets) {
  $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
  $out.Add("")
  $out.Add("##### refs in " + [System.IO.Path]::GetFileName($dll))
  foreach ($ty in $asm.MainModule.Types) {
    foreach ($m in $ty.Methods) {
      if ($m.Body -eq $null) { continue }
      foreach ($ins in $m.Body.Instructions) {
        $op = ''
        if ($ins.Operand -ne $null) { $op = $ins.Operand.ToString() }
        if ($op -match 'AssemblyPatcher|PatcherPluginPath') {
          $out.Add("  " + $ty.FullName + " :: " + $m.Name + "  ->  " + $op)
          break
        }
      }
    }
  }
}

# 2) all top-level types in Unity.IL2CPP.dll (entrypoint picture)
$asm2 = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $core 'BepInEx.Unity.IL2CPP.dll'))
$out.Add("")
$out.Add("##### all top-level types in BepInEx.Unity.IL2CPP.dll")
foreach ($ty in $asm2.MainModule.Types) { $out.Add("  " + $ty.FullName) }

# 3) BasePatcher full member list incl. non-public (find Patch hook convention)
$asm3 = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $core 'BepInEx.Preloader.Core.dll'))
foreach ($ty in $asm3.MainModule.Types) {
  if ($ty.FullName -eq 'BepInEx.Preloader.Core.Patching.BasePatcher') {
    $out.Add("")
    $out.Add("##### BasePatcher ALL members")
    foreach ($m in $ty.Methods) {
      $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName + ' ' + $_.Name }) -join ', '
      $out.Add("  METHOD: " + ($m.IsVirtual.ToString()) + " " + $m.ReturnType.Name + ' ' + $m.Name + '(' + $ps + ')')
    }
  }
}

$out | Out-File -FilePath 'E:\Game\Mod\mod_stack\tools\_pp_patcher_callsite.out.txt' -Encoding UTF8
Write-Host ('LINES: ' + $out.Count)
