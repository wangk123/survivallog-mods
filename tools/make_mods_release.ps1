﻿$ErrorActionPreference = 'Stop'
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
# unity-libs 预置（2026-10-17）：BepInEx 首启生成 interop 需要未裁剪 Unity 基础库，官方默认
# 从 unity.bepinex.dev 下载 {VERSION}.zip——境外源境内常失败 → mod 静默不生效（玩家实测反馈）。
# 预置整目录（zip+已解压 dll）命中 BepInEx.cfg UnityBaseLibrariesSource 的"同名 zip 本地
# 优先"逻辑，首启全程离线。zip 名 6000.2.7 = 游戏 Unity 版本，游戏大版本升级后需重新取新版
New-Item (Join-Path $base 'BepInEx\unity-libs') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $game 'BepInEx\unity-libs\*') (Join-Path $base 'BepInEx\unity-libs') -Recurse -Force
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
    @{ Name='FridgeChill';    Readme='readme_chill.md'    },
    @{ Name='CabinetEverywhere'; Readme='readme_cabaccess.md' },
    @{ Name='CabinetBurden';    Readme='readme_cabburden.md' },
    @{ Name='RobotSpeed';       Readme='readme_robotspeed.md' }
)
foreach ($m in $mods) {
    $dll = Join-Path $projRoot ('SurvivalLog.' + $m.Name + '\bin\Release\SurvivalLog.' + $m.Name + '.dll')
    if (-not (Test-Path $dll)) { throw ('build missing: ' + $dll) }
    # 新鲜度守卫（2026-10-17 事故）：本脚本不编译，只收 bin\Release 现货——若 dll 比源码旧，
    # 说明打包的是陈货（当日实测：合并后的筛选代码从未进 Release，重装后筛选"消失"）。
    # 拒绝打包，先 `sdk\dotnet\dotnet.exe build -c Release`（publish.ps1 会先构建，正常发版不受影响）。
    $srcDir = Join-Path $projRoot ('SurvivalLog.' + $m.Name)
    $newestSrc = Get-ChildItem $srcDir -Recurse -Include *.cs,*.js,*.csproj | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and (Get-Item $dll).LastWriteTime -lt $newestSrc.LastWriteTime) {
        throw ('stale build: ' + $dll + ' 比源码旧（最新源文件 ' + $newestSrc.Name + ' ' + $newestSrc.LastWriteTime.ToString('yyyy-MM-dd HH:mm') + '）。请先: sdk\dotnet\dotnet.exe build ' + $srcDir + ' -c Release')
    }

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
        # Fridge*/Vehicle* 倍数键一并归一（v1.3.6：此前仅裸 Width/HeightMult 有归一，
        # Fridge 键漏网是隐患；Vehicle 键随载具组新增，默认同为 1.5）
        $txt = $txt -replace '(?m)^(FridgeWidthMult\s*=\s).*$',  '${1}1.5'
        $txt = $txt -replace '(?m)^(FridgeHeightMult\s*=\s).*$', '${1}1.5'
        $txt = $txt -replace '(?m)^(VehicleWidthMult\s*=\s).*$',  '${1}1.5'
        $txt = $txt -replace '(?m)^(VehicleHeightMult\s*=\s).*$', '${1}1.5'
        # BurdenMult 同名不同义：背包=2（倍增玩家背包），柜子负重=1000（解除家具限制）——按 mod 区分
        if ($m.Name -eq 'BackpackExpand') { $txt = $txt -replace '(?m)^(BurdenMult\s*=\s).*$', '${1}2' }
        if ($m.Name -eq 'CabinetBurden') {
            $txt = $txt -replace '(?m)^(BurdenMult\s*=\s).*$',   '${1}1000'
            $txt = $txt -replace '(?m)^(IncludeBagIds\s*=\s).*$', '${1}'
            # ExcludeBagIds 本身就是发布默认值（3,4），保留
        }
        # RobotSpeed：中文键归一为发布默认（除系数开关外全部翻倍；
        # 插槽/模块相关键已随功能移除（作者定版 2026-10-13），老 cfg 残留键由 BepInEx 忽略）
        if ($m.Name -eq 'RobotSpeed') {
            $txt = $txt -replace '(?m)^(动作速度对齐手动\s*=\s).*$', '${1}true'
            $txt = $txt -replace '(?m)^(移速倍率\s*=\s).*$', '${1}2'
            $txt = $txt -replace '(?m)^(充电速率倍率\s*=\s).*$', '${1}2'
            $txt = $txt -replace '(?m)^(续航倍率\s*=\s).*$', '${1}2'
            $txt = $txt -replace '(?m)^(耗电降低倍率\s*=\s).*$', '${1}2'
            $txt = $txt -replace '(?m)^(动作时长倍率\s*=\s).*$', '${1}2'
            $txt = $txt -replace '(?m)^(货舱格数倍率\s*=\s).*$', '${1}2'
        }
        # CabinetEverywhere（2026-10-17 合并筛选/场景视图后）：Filter/Scenario/Debug 键归一为
        # 发布默认，防本机实验值带进发布包（如 [Filter] Enable=false、DumpFurniture=true）。
        # Enable 裸键仅 [Filter] 分区存在；View_* 逐键归一（默认值按界面各异，见插件 Bind）；
        # Debug 的 Verbose/ListOnReady 由下方通用归一覆盖。
        if ($m.Name -eq 'CabinetEverywhere') {
            $txt = $txt -replace '(?m)^(Enable\s*=\s).*$', '${1}true'
            $txt = $txt -replace '(?m)^(View_Cooking\s*=\s).*$',         '${1}Food'
            $txt = $txt -replace '(?m)^(View_RatCage\s*=\s).*$',         '${1}Food'
            $txt = $txt -replace '(?m)^(View_BrewPanel\s*=\s).*$',       '${1}Food'
            $txt = $txt -replace '(?m)^(View_DyePanel\s*=\s).*$',        '${1}Food'
            $txt = $txt -replace '(?m)^(View_Compressor\s*=\s).*$',      '${1}Food'
            $txt = $txt -replace '(?m)^(View_ToolTable\s*=\s).*$',       '${1}Material'
            $txt = $txt -replace '(?m)^(View_Shredder\s*=\s).*$',        '${1}Material'
            $txt = $txt -replace '(?m)^(View_GreenhouseBuild\s*=\s).*$', '${1}Material'
            $txt = $txt -replace '(?m)^(View_Trade\s*=\s).*$',           '${1}Browser'
            $txt = $txt -replace '(?m)^(View_DroneHub\s*=\s).*$',        '${1}Browser'
            $txt = $txt -replace '(?m)^(DumpFurniture\s*=\s).*$',        '${1}false'
            $txt = $txt -replace '(?m)^(PerfLog\s*=\s).*$',            '${1}false'
            $txt = $txt -replace '(?m)^(PruneToolTableTabs\s*=\s).*$',   '${1}true'
            $txt = $txt -replace '(?m)^(CoalesceBagRefresh\s*=\s).*$',  '${1}true'
        }
        $txt = $txt -replace '(?m)^(KeepMult\s*=\s).*$',   '${1}2'
        $txt = $txt -replace '(?m)^(Verbose\s*=\s).*$',    '${1}false'
        $txt = $txt -replace '(?m)^(DiagOnOpen\s*=\s).*$', '${1}false'
        $txt = $txt -replace '(?m)^(Extra\w*Ids\s*=\s).*$', '${1}'
        $txt = $txt -replace '(?m)^(ExcludeIds\s*=\s).*$', '${1}'
        $txt = $txt -replace '(?m)^(BagIds\s*=\s).*$',     '${1}'
        # CabinetEverywhere：IncludeFurnitureIds 归一为空；ExcludeBagIds/ExcludeFurnitureIds
        # 本身就是发布默认值（v1.2 起名单默认非空），保留不清空
        $txt = $txt -replace '(?m)^(IncludeFurnitureIds\s*=\s).*$', '${1}'
        $txt = $txt -replace '(?m)^(ListOnReady\s*=\s).*$', '${1}true'
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
