# QuickAction cfg 分类键按安装器勾选直写（v1.3.2 起替代 quickaction.boot.ini 引导机制）。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File fix_quickaction_cfg.ps1
#        -CfgPath <cfg全路径> -ItemMs 500 -FurnMs 0 -MaintMs 500 -MiscMs 0 -FunMs 500
# 行为：只在 [分类修改] 段内改写五个分类键的值（勾=500/不勾=0），其余行原样保留
#       （玩家手调的 Overrides、注释、其他段一概不动）；cfg 不存在则按勾选值生成最小 cfg。
# 编码：统一 UTF-8 无 BOM 读写（.NET 直写，规避 PS5.1 Out-File 带 BOM 的坑）。
param(
  [Parameter(Mandatory=$true)][string]$CfgPath,
  [int]$ItemMs = 500,
  [int]$FurnMs = 500,
  [int]$MaintMs = 500,
  [int]$MiscMs = 500,
  [int]$FunMs = 500
)
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$section = '[分类修改]'
$map = [ordered]@{
  '物品使用Ms' = $ItemMs
  '家具功能Ms' = $FurnMs
  '房屋维护Ms' = $MaintMs
  '杂项交互Ms' = $MiscMs
  '娱乐锻炼Ms' = $FunMs
}
$done = @{}
foreach ($k in $map.Keys) { $done[$k] = $false }

if (-not (Test-Path -LiteralPath $CfgPath)) {
  $new = New-Object System.Collections.Generic.List[string]
  $new.Add($section)
  foreach ($k in $map.Keys) { $new.Add("$k = $($map[$k])") }
  $new.Add('')
  $new.Add('[其他]')
  $new.Add('Overrides = ')
  [System.IO.File]::WriteAllLines($CfgPath, $new, $utf8NoBom)
  exit 0
}

$raw = [System.IO.File]::ReadAllLines($CfgPath, [System.Text.Encoding]::UTF8)
$lines = New-Object System.Collections.Generic.List[string]
$inSection = $false
$sawSection = $false
$flushMissing = {
  foreach ($k in $map.Keys) {
    if (-not $done[$k]) { $lines.Add("$k = $($map[$k])"); $done[$k] = $true }
  }
}
foreach ($l in $raw) {
  $t = $l.Trim()
  $isHeader = ($t.Length -ge 2 -and $t.StartsWith('[') -and $t.EndsWith(']'))
  if ($isHeader) {
    if ($inSection) { & $flushMissing; $inSection = $false }
    if ($t -eq $section) { $inSection = $true; $sawSection = $true }
    $lines.Add($l); continue
  }
  if ($inSection) {
    $replaced = $false
    foreach ($k in $map.Keys) {
      if ($done[$k]) { continue }
      if ($t -match ('^\s*' + [regex]::Escape($k) + '\s*=')) {
        $lines.Add("$k = $($map[$k])"); $done[$k] = $true; $replaced = $true; break
      }
    }
    if (-not $replaced) { $lines.Add($l) }
  } else {
    $lines.Add($l)
  }
}
if ($inSection) { & $flushMissing }
if (-not $sawSection) {
  # 整段缺失（玩家手删等极端情况）：文件末尾补段，保证五键按勾选落地
  if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -ne '') { $lines.Add('') }
  $lines.Add($section)
  foreach ($k in $map.Keys) { $lines.Add("$k = $($map[$k])") }
}
[System.IO.File]::WriteAllLines($CfgPath, $lines, $utf8NoBom)
exit 0
