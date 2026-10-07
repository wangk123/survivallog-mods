$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite

$tpl = [System.IO.File]::ReadAllText((Join-Path $projRoot 'tools\install_notes.tpl.txt'))
if ($tpl -notmatch '\{V\}') { throw '模板里没有 {V} 占位符，检查 tools\install_notes.tpl.txt' }
$out = $tpl.Replace('{V}', $ver)

$dst = Join-Path $projRoot 'release\【必读】安装说明.txt'
# UTF-8 带 BOM：记事本/不同编码环境打开中文不乱码
[System.IO.File]::WriteAllText($dst, $out, (New-Object System.Text.UTF8Encoding($true)))
Write-Host ('OK: ' + $dst + '  （版本引用已统一为 v' + $ver + '）')
