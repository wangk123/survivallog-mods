$ErrorActionPreference = 'Stop'
$game     = 'E:\SteamLibrary\steamapps\common\Survival Log'
$projRoot = 'E:\Game\Mod\mod_stack'
$relRoot  = Join-Path $projRoot 'release'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver      = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite

# ---------- BASE package ----------
$base = Join-Path $relRoot ('SurvivalLog.Base_v' + $ver)

New-Item (Join-Path $base 'BepInEx\core') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $base 'BepInEx\plugins') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $game 'BepInEx\core\*') (Join-Path $base 'BepInEx\core') -Force
Copy-Item (Join-Path $game 'dotnet') $base -Recurse -Force
Copy-Item (Join-Path $game 'winhttp.dll') $base -Force
Copy-Item (Join-Path $game 'doorstop_config.ini') $base -Force
Copy-Item (Join-Path $game '.doorstop_version') $base -Force
# 预置 BepInEx.cfg（安装器里 onlyifdoesntexist，不会覆盖玩家已有配置）：
# [Logging.Disk] Enabled 强制 true——保证玩家机器生成 LogOutput.log，安装说明的排障 FAQ 依赖它
New-Item (Join-Path $base 'BepInEx\config') -ItemType Directory -Force | Out-Null
$cfgDst = Join-Path $base 'BepInEx\config\BepInEx.cfg'
Copy-Item (Join-Path $game 'BepInEx\config\BepInEx.cfg') $cfgDst -Force
$txt = [System.IO.File]::ReadAllText($cfgDst)
$m = [regex]::Match($txt, '(?sm)(\[Logging\.Disk\].*?^Enabled\s*=\s*)false')
if ($m.Success) {
    $txt = $txt.Substring(0, $m.Index) + $m.Groups[1].Value + 'true' + $txt.Substring($m.Index + $m.Length)
    [System.IO.File]::WriteAllText($cfgDst, $txt, (New-Object System.Text.UTF8Encoding($false)))
}
Copy-Item (Join-Path $projRoot 'tools\readme_base.md') (Join-Path $base 'README.md') -Force
$baseZip = Join-Path $relRoot ('SurvivalLog.Base_v' + $ver + '.zip')
if (Test-Path $baseZip) { Remove-Item $baseZip -Force }
Compress-Archive -Path (Join-Path $base '*') -DestinationPath $baseZip -CompressionLevel Optimal

# ---------- One package per mod ----------
$mods = @(
    @{ Name='BackpackExpand'; Readme='readme_backpack.md' },
    @{ Name='CabinetExpand';  Readme='readme_cabinet.md'  },
    @{ Name='FridgeExpand';   Readme='readme_fridge.md'   },
    @{ Name='FridgeChill';    Readme='readme_chill.md'    }
)
foreach ($m in $mods) {
    $dll = Join-Path $projRoot ('SurvivalLog.' + $m.Name + '\bin\Release\SurvivalLog.' + $m.Name + '.dll')
    if (-not (Test-Path $dll)) { throw ('build missing: ' + $dll) }

    $pkg = Join-Path $relRoot ('SurvivalLog.' + $m.Name + '_v' + $ver)

    New-Item (Join-Path $pkg 'BepInEx\plugins') -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $pkg 'BepInEx\config') -ItemType Directory -Force | Out-Null
    Copy-Item $dll (Join-Path $pkg 'BepInEx\plugins') -Force

    # 预置配置：从本机运行时 cfg 复制，并把关键值归一为发布默认（不带走本机实验值）
    $cfgName = 'com.local.survivallog.' + $m.Name.ToLower() + '.cfg'
    $srcCfg = Join-Path $game ('BepInEx\config\' + $cfgName)
    if (Test-Path $srcCfg) {
        $dstCfg = Join-Path $pkg ('BepInEx\config\' + $cfgName)
        Copy-Item $srcCfg $dstCfg -Force
        $txt = [System.IO.File]::ReadAllText($dstCfg, [System.Text.Encoding]::UTF8)
        $txt = $txt -replace '(?m)^(WidthMult\s*=\s).*$',  '${1}1.5'
        $txt = $txt -replace '(?m)^(HeightMult\s*=\s).*$', '${1}1.5'
        $txt = $txt -replace '(?m)^(BurdenMult\s*=\s).*$', '${1}2'
        $txt = $txt -replace '(?m)^(KeepMult\s*=\s).*$',   '${1}2'
        $txt = $txt -replace '(?m)^(Verbose\s*=\s).*$',    '${1}false'
        $txt = $txt -replace '(?m)^(DiagOnOpen\s*=\s).*$', '${1}false'
        $txt = $txt -replace '(?m)^(Extra\w*Ids\s*=\s).*$', '${1}'
        $txt = $txt -replace '(?m)^(ExcludeIds\s*=\s).*$', '${1}'
        $txt = $txt -replace '(?m)^(BagIds\s*=\s).*$',     '${1}'
        [System.IO.File]::WriteAllText($dstCfg, $txt, (New-Object System.Text.UTF8Encoding($false)))
    }

    Copy-Item (Join-Path $projRoot ('tools\' + $m.Readme)) (Join-Path $pkg 'README.md') -Force

    $zip = Join-Path $relRoot ('SurvivalLog.' + $m.Name + '_v' + $ver + '.zip')
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal
}

Write-Host '=== Release contents ==='
Get-ChildItem $relRoot -Directory -Filter 'SurvivalLog.*_v*' | ForEach-Object {
    $z = [math]::Round((Get-Item ($_.FullName + '.zip')).Length/1MB, 2)
    Write-Host ($_.Name + '  -> zip ' + $z + ' MB')
}
