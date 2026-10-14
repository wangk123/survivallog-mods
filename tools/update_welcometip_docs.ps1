# 更新 8 份 DESIGN.md 与 ModShared/README.md 的"防盗署名气泡"行为句到队列版口径（EnqueueTalk 防顶替）
$ErrorActionPreference = 'Stop'
$root = 'E:\Game\Mod\mod_stack'

$old = '- 行为：进档后主角头顶显示 3 秒原生气泡文字——`AgentModel.Unit_ShowHeadTalk(text, 3)`（主角模型经 `WebGm.GetLeadingRoleModel(out string)` 获取）。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，ImmediateShowTalk/ShowTalk 直调同样无框，非本 mod 缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。'
$new = '- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。'

function Set-SrcText($path, $text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($path, $text, $enc)
}

$mods = @('BackpackExpand','CabinetExpand','FridgeChill','CabinetEverywhere','CabinetBurden','RobotSpeed','QuickAction','CabinetTagFilter')
$n = 0
foreach ($m in $mods) {
    $p = Join-Path $root ("SurvivalLog.$m\DESIGN.md")
    $t = [IO.File]::ReadAllText($p)
    if ($t.Contains($old)) { Set-SrcText $p ($t.Replace($old, $new)); $n++ } else { Write-Host ("MISS: " + $p) }
}
Write-Host ("DESIGN.md updated: " + $n)

# README 的气泡通道 bullet：跨行块用正则锚定替换
$rp = Join-Path $root 'ModShared\README.md'
$r = [IO.File]::ReadAllText($rp)
$rnew = @'
- 气泡通道（实测定稿，六修）：主通道 `CharacterTalkManager.EnqueueTalk`——游戏官方说话队列，
  与原生气泡消息排队轮流显示、互不顶替（直显会被进档瞬间的原生推送覆盖）；管理器不可用时
  兜底 `Unit_ShowHeadTalk(text, 3)` 直显。主角模型 `WebGm.GetLeadingRoleModel(out string)` 一行获取。
  主角头顶条为官方**无框文字**样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；
  During≈真实秒（传 360 会远超 3 秒）。
'@
$r2 = [regex]::Replace($r, '(?s)- 气泡通道（interop 实证 \+ 5 轮实测定稿）：.*?远超 3 秒。', $rnew.Trim())
if ($r2 -ne $r) { Set-SrcText $rp $r2; Write-Host 'README updated' } else { Write-Host 'README MISS' }
