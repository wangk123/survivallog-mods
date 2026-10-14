# 更新 7 份 DESIGN.md 与 ModShared/README.md 的"防盗署名气泡"行为句到定稿口径（3 秒无框文字版）
$ErrorActionPreference = 'Stop'
$root = 'E:\Game\Mod\mod_stack'

$old = '- 行为：进档后主角头顶弹 3 秒原生对话气泡——`AgentModel.Unit_ShowHeadTalk(text, 3)`；主角模型取 `WebGm.GetLeadingRoleModel(out string)`（静态），异常时退 `(headBar as UnitHeadBar).ShowTalk(text, 3, true)`。每次进档一次，回主菜单再进再弹。'
$new = '- 行为：进档后主角头顶显示 3 秒原生气泡文字——`AgentModel.Unit_ShowHeadTalk(text, 3)`（主角模型经 `WebGm.GetLeadingRoleModel(out string)` 获取）。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，ImmediateShowTalk/ShowTalk 直调同样无框，非本 mod 缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。'

function Set-SrcText($path, $text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($path, $text, $enc)
}

$mods = @('BackpackExpand','CabinetExpand','FridgeChill','CabinetEverywhere','CabinetBurden','RobotSpeed','QuickAction')
$n = 0
foreach ($m in $mods) {
    $p = Join-Path $root ("SurvivalLog.$m\DESIGN.md")
    $t = [IO.File]::ReadAllText($p)
    if ($t.Contains($old)) { Set-SrcText $p ($t.Replace($old, $new)); $n++ } else { Write-Host ("MISS: " + $p) }
}
Write-Host ("DESIGN.md updated: " + $n)

# README 的气泡通道 bullet（两行折行写法）
$rp = Join-Path $root 'ModShared\README.md'
$r = [IO.File]::ReadAllText($rp)
$rold = @'
- 气泡通道（interop 实证）：`AgentModel.Unit_ShowHeadTalk(text, during)`；主角模型
  `WebGm.GetLeadingRoleModel(out string)`（静态一行）；异常兜底 `(headBar as UnitHeadBar).ShowTalk(text, 3, true)`。
'@
$rnew = @'
- 气泡通道（interop 实证 + 5 轮实测定稿）：`AgentModel.Unit_ShowHeadTalk(text, 3)`，主角模型经
  `WebGm.GetLeadingRoleModel(out string)` 一行获取。定稿结论：主角头顶条为官方**无框文字**样式
  （silentHeadTalk 默认 false；ImmediateShowTalk / headBar.ShowTalk 直调同样无框，非缺陷）；
  During≈真实秒，传 360（游戏秒换算）会远超 3 秒。
'@
if ($r.Contains($rold.Trim())) { Set-SrcText $rp ($r.Replace($rold.Trim(), $rnew.Trim())); Write-Host 'README updated' } else { Write-Host 'README MISS' }
