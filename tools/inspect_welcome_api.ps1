# Inspect exact interop signatures needed by WelcomeTip (ASCII only, temp script).
$ErrorActionPreference = 'Stop'
Add-Type -Path "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll"
$interop = "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop"
$mods = @("HotUpdate.dll", "Assembly-CSharp.dll") | ForEach-Object { Join-Path $interop $_ }
$targets = @{
  'GameCore.HotUpdate.WebGm' = @('GetLeadingRoleModel')
  'GameCore.HotUpdate.Battle.Show.AgentModel' = @('Unit_ShowHeadTalk','Unit_StopHeadTalk','headBar','Robot_ShowRobotTalk')
  'GameCore.HotUpdate.Battle.Show.UnitHeadBar' = @('ShowTalk','StopTalk','MaxDuring')
  'GameCore.HotUpdate.Battle.Show.BattleShowWorld' = @('AgentModelManager')
  'GameCore.HotUpdate.Battle.Show.AgentModelManager' = @('GetAgentModel')
  'GameCore.HotUpdate.Battle.Logic.BattleLogicWorld' = @('AgentManager')
  'GameCore.HotUpdate.Battle.Logic.AgentManager' = @('GetLeadingRole')
  'GameCore.HotUpdate.Battle.Logic.LeadingRole' = @('ShowInstanceId')
}
foreach ($m in $mods) {
  if (-not (Test-Path $m)) { continue }
  $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($m)
  foreach ($t in $asm.MainModule.Types) {
    if ($targets.ContainsKey($t.FullName)) {
      Write-Host ("=== {0} (asm: {1}) ===" -f $t.FullName, (Split-Path $m -Leaf))
      foreach ($mem in $t.Methods) {
        foreach ($k in $targets[$t.FullName]) {
          if ($mem.Name -eq $k -or $mem.Name -eq ('get_' + $k) -or $mem.Name -eq ('set_' + $k)) {
            $ps = ($mem.Parameters | ForEach-Object {
              $tag = ''
              if ($_.ParameterType.IsByReference) { $tag = if ($_.IsOut) { '=out' } else { '=ref' } }
              $_.ParameterType.ToString() + $tag
            }) -join ', '
            Write-Host ("  M {0} {1}({2}) static={3}" -f $mem.ReturnType, $mem.Name, $ps, $mem.IsStatic)
          }
        }
      }
      foreach ($f in $t.Fields) {
        foreach ($k in $targets[$t.FullName]) {
          if ($f.Name -eq $k -or $f.Name -eq ('_' + $k)) {
            Write-Host ("  F {0} : {1} static={2}" -f $f.Name, $f.FieldType, $f.IsStatic)
          }
        }
      }
    }
  }
}
