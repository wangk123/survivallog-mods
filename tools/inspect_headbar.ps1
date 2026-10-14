# Dump head-bar class tree & talk channels (ASCII only, temp script)
$ErrorActionPreference = 'Stop'
Add-Type -Path "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll"
$interop = "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop"
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $interop "HotUpdate.dll"))

# 1) class tree under BaseHeadBar / UnitHeadBar
$types = $asm.MainModule.Types
Write-Host "=== subclasses of BaseHeadBar / UnitHeadBar / any *HeadBar* ==="
foreach ($t in $types) {
  if ($t.FullName -match 'HeadBar') {
    Write-Host ("{0}  (base: {1})" -f $t.FullName, $t.BaseType)
    foreach ($n in $t.NestedTypes) { Write-Host ("  +nested {0}" -f $n.FullName) }
  }
}
# also scan nested types of Battle.Show namespace container? types are flat namespaces here; fine.

function DumpType($fullName, $nameFilter) {
  foreach ($t in $types) {
    if ($t.FullName -eq $fullName) {
      Write-Host ("=== {0} (base: {1}) ===" -f $t.FullName, $t.BaseType)
      foreach ($f in $t.Fields) { Write-Host ("  F {0} : {1}" -f $f.Name, $f.FieldType) }
      foreach ($m in $t.Methods) {
        if ($nameFilter -and ($m.Name -notmatch $nameFilter)) { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.ToString() + $(if ($_.IsOut) {'=out'} elseif ($_.ParameterType.IsByReference) {'=ref'} else {''}) }) -join ', '
        Write-Host ("  M {0} {1}({2}) static={3}" -f $m.ReturnType, $m.Name, $ps, $m.IsStatic)
      }
    }
  }
}
DumpType 'GameCore.HotUpdate.Battle.Show.UnitHeadBar' $null
DumpType 'GameCore.HotUpdate.Battle.Show.BaseHeadBar' $null
DumpType 'GameCore.HotUpdate.Battle.Logic.CharacterTalkManager' $null
DumpType 'GameCore.HotUpdate.Battle.Logic.BubbleMessageComponent' 'Talk|Message|Trigger|Presence'
DumpType 'GameCore.HotUpdate.Battle.Show.AgentModel' 'HeadTalk|Talk|Notify|HeadBar'
