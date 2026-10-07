# ============ 一键发版流水线 ============
# 用法：tools\publish.ps1 [-Yes] [-SkipTest] [-SkipGithub]
#   -Yes        跳过交互确认
#   -SkipTest   跳过安装器回归测试（不建议）
#   -SkipGithub 跳过 git 提交/tag/GitHub Release
# 版本号只来自根目录 version.json（作者定义）；CHANGELOG.md 必须已有该版说明。
param(
    [switch]$Yes,
    [switch]$SkipTest,
    [switch]$SkipGithub
)
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
$tools    = Join-Path $projRoot 'tools'
$relRoot  = Join-Path $projRoot 'release'
$ghExe    = Join-Path $tools 'gh\bin\gh.exe'
$suiteMods = @('BackpackExpand','CabinetExpand','FridgeExpand','FridgeChill','QuickAction')

# 按原文件是否带 BOM 决定写回编码（不制造无谓 diff）
function Set-SrcText($path, $text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($path, $text, $enc)
}

# ---------- 1. 版本与更新说明一致性 ----------
$ver = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
if (-not $ver) { throw 'version.json 缺少 suite 字段' }
Write-Host ('=== 发版 v' + $ver + ' ===')
$logPath = Join-Path $projRoot 'CHANGELOG.md'
$logTxt  = [System.IO.File]::ReadAllText($logPath)
if ($logTxt -notmatch (('##\s*v' + [regex]::Escape($ver)) + '\b')) {
    throw ('CHANGELOG.md 没有 v' + $ver + ' 的小节：先补该版更新说明再发版。')
}
if (-not $Yes) {
    $a = Read-Host ('确认发版 v' + $ver + '？（重建 zip/安装器，覆盖 v' + $ver + ' 同名产物）[Y/N]')
    if ($a -notmatch '^[Yy]') { throw '已取消' }
}

# ---------- 2. 插件版本常量同步（仅套件 5 MOD；StackLimit999/ActionProbe 不动）----------
foreach ($m in $suiteMods) {
    $dir = Join-Path $projRoot ('SurvivalLog.' + $m)
    $csproj = Join-Path $dir ('SurvivalLog.' + $m + '.csproj')
    $t = [System.IO.File]::ReadAllText($csproj)
    $t2 = [regex]::Replace($t, '(<Version>)[\d.]+(</Version>)', ('${1}' + $ver + '${2}'))
    if ($t2 -ne $t) { Set-SrcText $csproj $t2; Write-Host ('同步 ' + $m + '.csproj 版本 → ' + $ver) }
    $plug = Get-ChildItem $dir -Filter '*Plugin.cs' | Select-Object -First 1
    if ($plug) {
        $c = [System.IO.File]::ReadAllText($plug.FullName)
        $c2 = [regex]::Replace($c, '(public const string Version\s*=\s*")[\d.]+(")', ('${1}' + $ver + '${2}'))
        if ($c2 -ne $c) { Set-SrcText $plug.FullName $c2; Write-Host ('同步 ' + $plug.Name + ' → ' + $ver) }
    }
}

# ---------- 3. 构建（用仓库自带便携 SDK，与历史构建一致）----------
$dotnetExe = Join-Path $projRoot 'sdk\dotnet\dotnet.exe'
if (-not (Test-Path $dotnetExe)) { $dotnetExe = 'dotnet' } # 兜底系统 PATH
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
foreach ($m in $suiteMods) {
    Write-Host ('--- 构建 SurvivalLog.' + $m + ' ---')
    Push-Location (Join-Path $projRoot ('SurvivalLog.' + $m))
    try { & $dotnetExe build -c Release --nologo | Out-Host }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw ('构建失败: SurvivalLog.' + $m) }
}

# ---------- 4. 打 zip 分包 ----------
& (Join-Path $tools 'make_mods_release.ps1')
& (Join-Path $tools 'make_quickaction_release.ps1')

# ---------- 5. 编译安装器（依赖第 4 步的 release 目录）----------
& (Join-Path $tools 'build_installer.ps1')

# ---------- 6. 生成安装说明（版本引用统一）----------
& (Join-Path $tools 'make_install_notes.ps1')

# ---------- 7. 安装器回归测试（失败即中止，禁止发版）----------
if (-not $SkipTest) {
    & (Join-Path $tools 'test_installer.ps1')
} else {
    Write-Host '!! 已跳过安装器回归测试（-SkipTest）' -ForegroundColor Yellow
}

# ---------- 8. 组装网盘上传目录 ----------
$cfgPath = Join-Path $tools 'publish.config.json'
$syncDir = ''
if (Test-Path $cfgPath) {
    $j = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($j.baiduSyncDir) { $syncDir = $j.baiduSyncDir }
}
$syncHint = ''
if (-not $syncDir) {
    foreach ($c in @((Join-Path $env:USERPROFILE 'BaiduNetdiskSync'), (Join-Path $env:USERPROFILE 'Documents\BaiduNetdiskSync'))) {
        if (Test-Path $c) { $syncDir = Join-Path $c '生存日志MOD'; $syncHint = '（自动探测到同步空间；建议把路径回填 tools\publish.config.json）'; break }
    }
}
$stage = if ($syncDir) { $syncDir } else { Join-Path $relRoot 'baidu_upload' }
New-Item $stage -ItemType Directory -Force | Out-Null
$artifacts = @(
    (Join-Path $relRoot ('生存日志MOD安装器_v' + $ver + '.exe')),
    (Join-Path $relRoot '【必读】安装说明.txt')
)
foreach ($m in @('Base') + $suiteMods) {
    $artifacts += (Join-Path $relRoot ('SurvivalLog.' + $m + '_v' + $ver + '.zip'))
}
foreach ($f in $artifacts) {
    if (-not (Test-Path $f)) { throw ('产物缺失: ' + $f) }
    Copy-Item $f $stage -Force
}
Write-Host ('网盘上传目录: ' + $stage + $syncHint)

# GitHub Release 资产用 ASCII 别名：实测 GitHub 资产改名接口拒绝中文名
# （返回 200 但忽略），中文原名文件走网盘渠道
$ghAssets = @()
$asciiExe = Join-Path $env:TEMP ('SurvivalLogMOD-Installer_v' + $ver + '.exe')
Copy-Item (Join-Path $relRoot ('生存日志MOD安装器_v' + $ver + '.exe')) $asciiExe -Force
$ghAssets += $asciiExe
$asciiNotes = Join-Path $env:TEMP 'install-notes.txt'
Copy-Item (Join-Path $relRoot '【必读】安装说明.txt') $asciiNotes -Force
$ghAssets += $asciiNotes
foreach ($m in @('Base') + $suiteMods) {
    $ghAssets += (Join-Path $relRoot ('SurvivalLog.' + $m + '_v' + $ver + '.zip'))
}

# ---------- 9. git 提交 / tag / GitHub Release ----------
if (-not $SkipGithub -and (Test-Path (Join-Path $projRoot '.git'))) {
    git -C $projRoot add -A
    if (git -C $projRoot status --porcelain) {
        git -C $projRoot commit -m ('release: v' + $ver) | Out-Host
    } else { Write-Host 'git 无变更可提交' }

    $tag = 'v' + $ver
    if (git -C $projRoot tag -l $tag) {
        Write-Host ('tag ' + $tag + ' 已存在，跳过')
    } else {
        # 必须用附注标签：--follow-tags 只推附注标签，轻量标签推不上去
        git -C $projRoot tag -a $tag -m ('release ' + $tag)
    }

    # 注意：PS5.1 下 EAP=Stop 时给原生命令加重定向（2>&1/2>$null）可能触发
    # NativeCommandError 终止，所以这里全部裸调用、只看退出码
    $remote = git -C $projRoot remote | Where-Object { $_ -eq 'origin' }
    if ($remote) {
        git -C $projRoot push origin --follow-tags HEAD
        if ($LASTEXITCODE -ne 0) { Write-Host '!! git push 失败（看上面输出；tag 已在本地）' -ForegroundColor Yellow }
    } else {
        Write-Host '无 origin 远程，跳过 push'
    }

    if ((Test-Path $ghExe) -and $remote) {
        & $ghExe auth status
        if ($LASTEXITCODE -eq 0) {
            $notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n##\s*v|\z)')).Value.Trim()
            $notesFile = Join-Path $env:TEMP ('release_notes_v' + $ver + '.md')
            [System.IO.File]::WriteAllText($notesFile, $notes, (New-Object System.Text.UTF8Encoding($false)))
            & $ghExe release create $tag --title ('v' + $ver) --notes-file $notesFile @ghAssets
            if ($LASTEXITCODE -ne 0) { Write-Host '!! GitHub Release 创建失败（tag 已打好，可手动重试 gh release create）' -ForegroundColor Yellow }
            else { Write-Host ('GitHub Release ' + $tag + ' 已创建并附 ' + $ghAssets.Count + ' 个产物') }
        } else {
            Write-Host 'gh 未登录，跳过 GitHub Release（tools\gh\bin\gh.exe auth login）'
        }
    } elseif (-not (Test-Path $ghExe)) {
        Write-Host '未找到 tools\gh\bin\gh.exe，跳过 GitHub Release'
    }
}

# ---------- 10. 旧版本产物提醒 ----------
Get-ChildItem $relRoot -Directory -Filter 'SurvivalLog.*_v*' |
    Where-Object { $_.Name -notmatch ('_v' + [regex]::Escape($ver) + '$') } |
    ForEach-Object { Write-Host ('提醒: 旧版本产物目录仍在 → ' + $_.Name + '（确认无用可删）') }

Write-Host ''
Write-Host ('=== v' + $ver + ' 发版完成 ===')
