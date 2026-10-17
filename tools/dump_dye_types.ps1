# tools/dump_dye_types.ps1 —— dump Dye 相关 interop 类型与字段（取证用，跑完即用）
# 背景：HotUpdate.dll 是 Il2Cpp interop 代理程序集（方法体为 native 桩，字段以
#       NativeFieldInfoPtr_* 静态字段 + 同名实例属性暴露），因此字段取证看 Properties
#       （带真实托管类型），NativeFieldInfoPtr_* 只作存在性对照。
# 加载方式照抄 tools/dump_tooltable.ps1 头部（Add-Type Mono.Cecil.dll + ReadAssembly HotUpdate.dll）。
# 运行：powershell -NoProfile -ExecutionPolicy Bypass -File tools/dump_dye_types.ps1
# 产物（任务 2026-10-17 储藏柜合并/场景视图 Task 1）：
#   ① WebUI_Dye / State_Web_Dye / Data_Web_Dye_BagTab 等 Dye 类型全字段清单
#   ③a Config_FurnitureTag 全字段（重点 Color 字段类型）
#   另附 WebUI_* 类型清单（页面 id 交叉验证用：html createWebUICore 的 pageId 必须等于 C# UIKey，
#      否则 Root.html notifyPageReady 找不到 state.pages[pageId]，页面永不 ready）

$ErrorActionPreference = 'Stop'
# 重定向时 PowerShell 5.1 默认用控制台代码页（GBK），中文注释会乱码，强制 UTF-8 输出
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch {}
$cecil = 'E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Mono.Cecil.dll'
Add-Type -Path $cecil
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.ReadSymbols = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll', $rp)
$types = $asm.MainModule.GetTypes()

function Dump-Type([Mono.Cecil.TypeDefinition]$t) {
    if (-not $t) { Write-Host '  MISSING'; return }
    Write-Host ("########## " + $t.FullName)
    Write-Host "  -- properties (interop 托管视图，字段名+真实类型):"
    foreach ($p in $t.Properties) {
        if ($p.Name -match '^NativeFieldInfoPtr') { continue }
        Write-Host ("     {0} : {1}" -f $p.Name, $p.PropertyType.FullName)
    }
    Write-Host "  -- NativeFieldInfoPtr_* 原生字段指针（存在性对照，类型恒为 IntPtr）:"
    $natives = @($t.Fields | Where-Object { $_.Name -like 'NativeFieldInfoPtr*' })
    if ($natives.Count -eq 0) { Write-Host '     (无)' }
    foreach ($f in $natives) {
        Write-Host ("     {0}" -f $f.Name.Replace('NativeFieldInfoPtr_', ''))
    }
    Write-Host ''
}

# ---------- 产物①：Dye 相关类型清单 ----------
Write-Host '##### [1] Types matching Dye (排除 MethodInfoStore/Formatter/__c 噪音):'
foreach ($t in $types) {
    if ($t.FullName -match '(?i)Dye' -and $t.FullName -notmatch 'MethodInfoStore|Formatter|__c$|_d__') {
        Write-Host ("  " + $t.FullName)
    }
}
Write-Host ''

# ---------- 产物①：核心类型全字段 dump ----------
$coreTypes = @(
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye',
    'GameCore.HotUpdate.ReduxUI.State_Web_Dye',
    'GameCore.HotUpdate.ReduxUI.Data_Web_Dye_BagTab',
    'GameCore.HotUpdate.ReduxUI.Data_Web_Dye_Item',
    'GameCore.HotUpdate.ReduxUI.Data_Web_Dye_Pigment',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_StateMsg',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_BagMsg',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_BagTab',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_Item',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_Pigment',
    'GameCore.HotUpdate.ReduxUI.WebUI_Dye_LocalizationMsg',
    'GameCore.HotUpdate.ReduxUI.DyePaletteEntry',
    'GameCore.HotUpdate.ReduxUI.DyePigmentTooltip'
)
Write-Host '##### [2] Dye 核心类型全字段:'
foreach ($tn in $coreTypes) {
    Dump-Type ($types | Where-Object { $_.FullName -eq $tn })
}

# WebUI_Dye 方法清单（UpdateBagTabs 等绑定入口，Task 8 PollDye 参考）
$wd = $types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.ReduxUI.WebUI_Dye' }
if ($wd) {
    Write-Host "##### [2b] WebUI_Dye methods:"
    foreach ($m in $wd.Methods) {
        if ($m.Name -match '^\.') { continue }
        $ps = ($m.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ', '
        Write-Host ("   {0}({1}) -> {2}" -f $m.Name, $ps, $m.ReturnType.FullName)
    }
    Write-Host ''
}

# ---------- 产物③a：Config_FurnitureTag（重点 Color 字段类型）----------
Write-Host '##### [3] Config_FurnitureTag 全字段（重点：Color 字段类型）:'
Dump-Type ($types | Where-Object { $_.FullName -eq 'GameCore.HotUpdate.Config_FurnitureTag' })

# ---------- 页面 id 交叉验证材料：WebUI_* 类型清单 ----------
Write-Host '##### [4] ReduxUI.WebUI_* 类型清单（页面 id 用 html createWebUICore 侧取证，此清单对照类名）:'
foreach ($t in $types) {
    if ($t.FullName -match '^GameCore\.HotUpdate\.ReduxUI\.WebUI_' -and $t.FullName -notmatch '/|\+') {
        Write-Host ("  " + $t.FullName.Replace('GameCore.HotUpdate.ReduxUI.', ''))
    }
}
$asm.Dispose()
