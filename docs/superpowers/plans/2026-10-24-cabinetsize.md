# 柜子容量 CabinetSize 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新建数值型柜子容量 mod（四档阶梯 + 12 包显式 cfg + 全局 20x12 上限 + 弃 ViewportFit 用官方自适应），取代并退役 CabinetExpand（源码留档不覆盖）。

**Architecture:** 三文件单 mod（入口+Ticker / `[Bag.*]` cfg 文件自解析 / 发现与改写引擎），复用 ModShared `BagTable` 只读工具（**不修改 ModShared**，绝对值改写在 mod 内本地实现，先例=CabinetExpand.CappedRewrite）。值解析优先级：cfg 显式（0=恢复原生）＞ 原生格数档位（≤60小/61~99中/100~149大/≥150特大）＞ 不缩水守卫 ＞ 全局夹顶。

**Tech Stack:** BepInEx Unity IL2CPP / Harmony postfix / net6.0 / ModShared 共享源码编译。

**Spec:** `SurvivalLog.CabinetSize/DESIGN.md`（定稿 commit ff39bac；本计划与它冲突时以 DESIGN 为准并回报）

**测试基建说明（对 TDD 的偏差，勿"补"单测）:** 本仓 IL2CPP 游戏 mod 无单测基建（游戏类型无法脱离运行时实例化），验证分层=构建退出码门禁（任务内）→ 实机自证 trace 文件（Task 5）→ 作者实机 UI 矩阵（Task 6，DESIGN §7）。纯逻辑（解析/分档/夹顶）通过 ListOnReady 日志全量核对。

## Global Constraints（每任务隐含遵守）

- **版本号只由作者定**：csproj `<Version>` 写 `1.3.8`（当前 `version.json` 的 suite 值）；发版前若 suite 变化须同步，不得自造版本号。
- **禁止 `git add -A` / `git add .`**：只 `git add` 各任务"文件"清单里点名的文件；工作树有作者未提交改动，绝不整文件 stage 他人脏文件。
- **构建判退出码**：`"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.CabinetSize -c Release > build_cabinetsize.log 2>&1; echo $?`——**禁 `| tail` 管道**（退出码被吃，实坑）。
- **游戏单实例红线**：`SurvivalLog.exe` 在跑且内存活跃时**绝不等待、绝不杀进程**，任务整体中止择时重试；杀后 `sleep 3` 回读 tasklist 确认死透。
- **游戏启动**：`powershell Start-Process 'steam://rungameid/4164790'`（直接 start exe 会被 Steamworks 转拉导致插件哑火）；表就绪约在主菜单后 15-25 秒。
- **日志强杀不落盘**：实机验证一律看自证文件 `BepInEx\CabinetSize_trace.txt`（本 mod 写），别指望 LogOutput.log。
- **旧代码不覆盖**：`SurvivalLog.CabinetExpand/` 目录**一个字节都不动**（退役=退出构建发布链）。
- **ModShared 不改**：`ModShared/BagTableRw.cs` 只读复用；改它触发 4 份扩容文档联动，本计划零改动。
- **ps1 一律 ASCII-only**（改动 publish/make_mods_release 等脚本时新增行不写中文）；**installer.iss 保持 BOM**（UTF-8 BOM 编辑，中文仅限既有中文字段同款用法）。
- **UI 验证铁律**：先实机再报"修好"；同题两轮不过换道（Task 6 红灯处理已内置降级路径）。

---

### Task 1: 项目骨架 + 双开防御

**Files:**
- Create: `SurvivalLog.CabinetSize/SurvivalLog.CabinetSize.csproj`
- Create: `SurvivalLog.CabinetSize/CabinetSizePlugin.cs`（骨架，后续任务填充）

**Interfaces:**
- Produces: `CabinetSizePlugin.Guid = "com.local.survivallog.cabinetsize"`、`Name = "CabinetSize"`、`Version = "1.3.8"`、`internal static ManualLogSource Log`、`internal static volatile bool Rerun = true`（Task 2/3/4 依赖这些名字）。

- [ ] **Step 1: 写 csproj**（拷 CabinetExpand 模板，去掉 Vuplex.WebView 引用——本 mod 无 JS 注入；版本=1.3.8）

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>SurvivalLog.CabinetSize</AssemblyName>
    <RootNamespace>SurvivalLog.CabinetSize</RootNamespace>
    <Version>1.3.8</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <DebugType>embedded</DebugType>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CA1416</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../ModShared/BagTableRw.cs" />
    <Compile Include="../ModShared/WelcomeTip.cs" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="0Harmony">
      <HintPath>E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/core/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Core">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.IL2CPP">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\BepInEx.Unity.IL2CPP.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\core\Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Il2Cppmscorlib.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppSystem.Core">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Il2CppSystem.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="HotUpdate">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\HotUpdate.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 写插件骨架**（含双开防御——检测到前任 CabinetExpand 的 GUID 即自禁，这是单写者原则的硬闸）

```csharp
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// 柜子容量（数值型；2026-10-24 定稿取代 CabinetExpand，设计文档见本目录 DESIGN.md）。
/// 家里的柜子按 小/中/大/特大 四档调到固定宽高；12 常用包 cfg 显式条目；其余按档自动生效；
/// 全局上限默认 20x12（官方自适应设计预算内，弃用一切自研视口适配）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetSizePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetsize";
    public const string Name = "CabinetSize";
    public const string Version = "1.3.8"; // 跟随套件 version.json（版本号由作者定义）

    internal static ManualLogSource Log;
    internal static volatile bool Rerun = true;

    /// <summary>家门口塑料袋专用包（VehicleConfig.DoorBoxBagId 常量镜像，与 SUV 后备箱共用）。</summary>
    internal const int DoorBoxBagId = 1023;

    public override void Load()
    {
        Log = base.Log;

        // 双开防御：前任 CabinetExpand 在场则自禁（两 mod 同写 Config_Bag.Size 必互相污染）
        // 注：本机 BepInEx IL2CPP-BE 构型无静态 Chainloader.PluginInfos（Task 1 实施实证，
        // 先例 ModCenterPlugin.cs:186-188），用 IL2CPPChainloader.Instance.Plugins 判定
        try
        {
            if (BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance?.Plugins?.ContainsKey("com.local.survivallog.cabinetexpand") == true)
            {
                Log.LogWarning("[CabinetSize] 检测到旧 mod CabinetExpand 仍在运行：两 mod 同写 Config_Bag.Size 会互相污染，"
                               + "本 mod 已自禁。请删除 BepInEx\\plugins\\SurvivalLog.CabinetExpand.dll 与其 cfg 后重启游戏。");
                return;
            }
        }
        catch (Exception e) { Log.LogWarning("[CabinetSize] 双开检测异常（继续加载）: " + e.Message); }

        // Task 2：cfg 绑定 + BagSections 初始化
        // Task 3/4：改写引擎 + Ticker

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<SizeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<SizeTicker>();
        var go = new GameObject("CabinetSize.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SizeTicker>();

        SurvivalLog.ModShared.WelcomeTip.Install(Log);
        Log.LogInfo("[CabinetSize] loaded（数值型四档；上限/名单生效后见 ListOnReady 日志与 trace 文件）");
    }

    internal sealed class SizeTicker : MonoBehaviour
    {
        public SizeTicker(IntPtr ptr) : base(ptr) { }
        public SizeTicker() { }
        public void Update() { /* Task 3 填充 */ }
    }
}
```

- [ ] **Step 3: 构建验证**（退出码必须 0）

Run: `cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.CabinetSize -c Release > build_cabinetsize.log 2>&1; echo $?`
Expected: `0`，产物 `SurvivalLog.CabinetSize/bin/Release/SurvivalLog.CabinetSize.dll`

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.CabinetSize/SurvivalLog.CabinetSize.csproj SurvivalLog.CabinetSize/CabinetSizePlugin.cs
git commit -m "feat(cabinetsize): 项目骨架+双开防御（退役 mod 在场自禁）"
```

---

### Task 2: 配置层——BepInEx 绑定键 + `[Bag.*]` 文件自解析 + 预设迁移

**Files:**
- Create: `SurvivalLog.CabinetSize/BagSections.cs`
- Modify: `SurvivalLog.CabinetSize/CabinetSizePlugin.cs`（Load 内插入 cfg 绑定块，替换 `// Task 2` 注释行）

**Interfaces:**
- Produces（Task 3/4 依赖）:
  - `CabinetSizePlugin` 新增静态字段：`MaxWidth/MaxHeight/Preset/ExcludeIds`（`ConfigEntry<int/int/int/string>`）、`SmallW/SmallH/MediumW/MediumH/LargeW/LargeH/XLargeW/XLargeH`（`ConfigEntry<int>`）、`ListOnReady/DiagOnOpen`（`ConfigEntry<bool>`）。
  - `BagSections.Entries`：`Dictionary<int,(int w,int h)>`（玩家/默认 `[Bag.<id>]` 显式条目，W/H 任一 ≤0 = 恢复原生，解析时不收录该条目）。
  - `BagSections.Init(string cfgPath)`、`BagSections.Poll()` → `bool`（文件 mtime 变且条目集变化返回 true）。

- [ ] **Step 1: 写 BagSections.cs**（cfg 文件 `[Bag.*]` 节的解析/首跑写入/监听/预设迁移；BepInEx 对未绑定节按 orphan 原样保留，两套读写互不干扰）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// [Bag.&lt;id&gt;] 节自解析：12 常用包默认条目首跑写入 + 玩家自加节即时识别（mtime 监听，每轮 Ticker Poll）。
/// 键名用 ASCII（Bag.110001）——与 BepInEx 绑定键（Global/Tier/Debug）分治：本类只认自己的节，
/// BepInEx 保存 cfg 时对未知节按 orphan 原样回写（注释行可能被丢弃，柜名以 README/ListOnReady 日志为准）。
/// </summary>
internal static class BagSections
{
    internal static readonly Dictionary<int, (int w, int h)> Entries = new();

    /// <summary>预设 v1 的 12 包默认值（DESIGN §5.1 阶梯口径 A）。调值=新增 PresetHistory 版本，勿改历史行。</summary>
    internal static readonly (int id, int w, int h)[] PresetV1 =
    {
        (110002, 10, 12), // 小型置物架   原生 6x8   小档
        (110001, 14, 12), // 中型置物架   原生 8x10  中档
        (110000, 18, 12), // 大型置物架   原生 12x10 大档
        (110008, 20, 12), // 超大型置物架 原生 15x12 特大档
        (110009, 20, 12), // 材料架       原生 20x12 特大档（顶格保持）
        (1004,   10, 12), // 双门冰箱     原生 8x7   小档
        (115000, 10, 12), // 双开门冰箱   原生 8x7   小档
        (115001, 14, 12), // 冰柜         原生 8x10  中档
        (115005, 18, 12), // 大容量冰柜   原生 12x10 大档
        (1021,   10, 12), // 自行车车筐   原生 5x4（反推）小档
        (1022,   14, 12), // 轿车后备箱   原生 10x8（反推）中档
        (1023,   18, 12), // SUV后备箱+门口塑料袋 原生 12x10 大档
    };

    /// <summary>当前预设版本。调默认值时 bump 并在 PresetHistory 追加新版本行。</summary>
    internal const int CurrentPreset = 1;

    /// <summary>预设史（版本→12 包默认值）。迁移语义：文件条目等于旧版本默认 → 自动改写为新版本默认；玩家改过的（不等于任何旧默认）原样保留。</summary>
    internal static readonly Dictionary<int, (int id, int w, int h)[]> PresetHistory = new()
    {
        { 1, PresetV1 },
    };

    private static string _path;
    private static DateTime _lastMtime = DateTime.MinValue;
    private static int _lastHash;

    private static readonly Regex SectionRe = new(@"^\s*\[\s*Bag\.(\d+)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WidthRe = new(@"^\s*Width\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightRe = new(@"^\s*Height\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Init(string cfgPath)
    {
        _path = cfgPath;
        EnsureDefaultSections();
        MigratePreset();
        Reload();
    }

    /// <summary>首跑写入：文件不存在或没有任何 [Bag. 节时，追加 12 包默认节（一次性；之后 BepInEx 重写文件也会按 orphan 保留这些节）。</summary>
    private static void EnsureDefaultSections()
    {
        try
        {
            var text = File.Exists(_path) ? File.ReadAllText(_path) : "";
            if (text.Contains("[Bag.")) return;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(";; ===== 柜子容量：常用 12 包显式宽高（Width/Height 任一改 0 = 该包恢复官方原值；给其他柜子加条目见 README）=====");
            foreach (var (id, w, h) in PresetV1)
                sb.AppendLine().AppendLine("[Bag." + id + "]").AppendLine("Width = " + w).AppendLine("Height = " + h);
            File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 默认节写入失败（不影响其余功能）: " + e.Message); }
    }

    /// <summary>预设迁移：Preset 键 &lt; 当前版本时，把等于旧默认的文件条目改写为新默认（cfg 首跑固化坑的对策）。当前只有 v1，升级到 v2+ 时自然生效。</summary>
    private static void MigratePreset()
    {
        try
        {
            int from = CabinetSizePlugin.Preset.Value;
            if (from >= CurrentPreset || !File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            for (int ver = from + 1; ver <= CurrentPreset; ver++)
            {
                if (!PresetHistory.TryGetValue(ver, out var table) || !PresetHistory.TryGetValue(ver - 1, out var prev)) continue;
                foreach (var (id, nw, nh) in table)
                {
                    var (pw, ph) = Array.Find(prev, t => t.id == id);
                    foreach (var nl in new[] { "\r\n", "\n" }) // 容忍 BepInEx 重写时可能的换行风格差异
                    {
                        var oldBlock = SectionText(id, pw, ph, nl);
                        if (text.Contains(oldBlock))
                        {
                            text = text.Replace(oldBlock, SectionText(id, nw, nh, nl));
                            break;
                        }
                    }
                }
            }
            File.WriteAllText(_path, text, new UTF8Encoding(true));
            CabinetSizePlugin.Preset.Value = CurrentPreset;
            CabinetSizePlugin.Log.LogInfo($"[CabinetSize] 预设已迁移到 v{CurrentPreset}（未手动改过的条目已跟新默认）");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 预设迁移失败（可手动改 cfg）: " + e.Message); }
    }

    private static string SectionText(int id, int w, int h, string nl) =>
        "[Bag." + id + "]" + nl + "Width = " + w + nl + "Height = " + h;

    private static void Reload()
    {
        Entries.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            int cur = -1, w = 0, h = 0; bool hasW = false, hasH = false;
            foreach (var raw in File.ReadAllLines(_path))
            {
                var line = raw.TrimEnd();
                var m = SectionRe.Match(line);
                if (m.Success) { Flush(); cur = int.Parse(m.Groups[1].Value); w = h = 0; hasW = hasH = false; continue; }
                if (cur <= 0) continue;
                var mw = WidthRe.Match(line); if (mw.Success) { w = int.Parse(mw.Groups[1].Value); hasW = true; continue; }
                var mh = HeightRe.Match(line); if (mh.Success) { h = int.Parse(mh.Groups[1].Value); hasH = true; }
            }
            Flush();
            void Flush()
            {
                if (cur > 0 && hasW && hasH && w > 0 && h > 0) Entries[cur] = (w, h); // 任一 ≤0/缺失 = 恢复原生（不收录）
            }
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] cfg 解析失败: " + e.Message); }
        _lastMtime = File.Exists(_path) ? File.GetLastWriteTime(_path) : DateTime.MinValue;
        _lastHash = Hash();
    }

    private static int Hash()
    {
        int hash = Entries.Count;
        foreach (var kv in Entries) hash = hash * 31 ^ (kv.Key * 397 + kv.Value.w * 29 + kv.Value.h);
        return hash;
    }

    /// <summary>每轮 Ticker 调用：文件 mtime 变化则重解析；条目集变化返回 true（触发立即改写）。</summary>
    internal static bool Poll()
    {
        try
        {
            if (!File.Exists(_path) || File.GetLastWriteTime(_path) == _lastMtime) return false;
            Reload();
            if (Hash() != _lastHash) { _lastHash = Hash(); return true; }
            return false;
        }
        catch { return false; }
    }
}
```

- [ ] **Step 2: 插件 Load 内加 cfg 绑定块**（替换 `// Task 2：cfg 绑定 + BagSections 初始化` 注释行；所有 SettingChanged → Rerun）

在 `CabinetSizePlugin` 类顶部（`Rerun` 字段之后）加字段与绑定方法：

```csharp
    internal static BepInEx.Configuration.ConfigEntry<int> MaxWidth, MaxHeight, Preset;
    internal static BepInEx.Configuration.ConfigEntry<string> ExcludeIds;
    internal static BepInEx.Configuration.ConfigEntry<int> SmallW, SmallH, MediumW, MediumH, LargeW, LargeH, XLargeW, XLargeH;
    internal static BepInEx.Configuration.ConfigEntry<bool> ListOnReady, DiagOnOpen;

    private void BindConfig()
    {
        const string g = "柜子宽度上限：档位与自定义条目超出即夹到此值（0=不封顶，超宽可能挤出屏幕自担风险）。默认 20=官方原生最大宽度（材料架列数），官方自适应设计预算内。";
        MaxWidth = Config.Bind("Global", "MaxWidth", 20, g);
        MaxHeight = Config.Bind("Global", "MaxHeight", 12,
            "柜子高度上限（0=不封顶）。默认 12=原生全表最大行数。");
        Preset = Config.Bind("Global", "Preset", 1, "默认值预设版本（勿手改）：mod 升级后未手动调过的条目自动跟新默认值。");
        ExcludeIds = Config.Bind("Global", "ExcludeIds", "", "追加排除的 BagId（逗号分隔）：在默认黑名单之外额外恢复官方原值。");
        SmallW = Config.Bind("Tier.Small", "Width", 10, "小档宽度（原生格数≤60 的柜子自动落此档）。");
        SmallH = Config.Bind("Tier.Small", "Height", 12, "小档高度。");
        MediumW = Config.Bind("Tier.Medium", "Width", 14, "中档宽度（原生格数 61~99）。");
        MediumH = Config.Bind("Tier.Medium", "Height", 12, "中档高度。");
        LargeW = Config.Bind("Tier.Large", "Width", 18, "大档宽度（原生格数 100~149）。");
        LargeH = Config.Bind("Tier.Large", "Height", 12, "大档高度。");
        XLargeW = Config.Bind("Tier.XLarge", "Width", 20, "特大档宽度（原生格数≥150）。");
        XLargeH = Config.Bind("Tier.XLarge", "Height", 12, "特大档高度。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true, "首次生效时在日志/trace 列出全部受管包与尺寸前后（核对名单用）。");
        DiagOnOpen = Config.Bind("Debug", "DiagOnOpen", false, "读档时在日志列出全部容器的 BagConfigId（排查某柜不生效，平时关防刷屏）。");

        System.EventHandler reload = (s, e) => { Rerun = true; };
        foreach (var entry in new BepInEx.Configuration.ConfigEntryBase[] {
                     MaxWidth, MaxHeight, Preset, ExcludeIds, SmallW, SmallH, MediumW, MediumH,
                     LargeW, LargeH, XLargeW, XLargeH, ListOnReady, DiagOnOpen })
            entry.SettingChanged += reload;
    }
```

Load 中替换注释为：

```csharp
        BindConfig();
        BagSections.Init(Config.ConfigPath);
```

（`Config.ConfigPath` 是 BepInEx `ConfigFile` 的完整路径属性，指向 `BepInEx/config/com.local.survivallog.cabinetsize.cfg`。）

- [ ] **Step 3: 构建验证**

Run: `cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.CabinetSize -c Release > build_cabinetsize.log 2>&1; echo $?`
Expected: `0`

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.CabinetSize/BagSections.cs SurvivalLog.CabinetSize/CabinetSizePlugin.cs
git commit -m "feat(cabinetsize): 配置层（BepInEx绑定键+Bag节自解析+预设迁移）"
```

---

### Task 3: 发现与改写引擎 + Ticker 轮询 + trace 自证

**Files:**
- Create: `SurvivalLog.CabinetSize/SizeEngine.cs`
- Modify: `SurvivalLog.CabinetSize/CabinetSizePlugin.cs`（仅填充 `SizeTicker.Update()` 方法体——替换 `public void Update() { /* Task 3 填充 */ }` 为下方代码块，字段一并加入类内；`// Task 3/4：改写引擎 + Ticker` 注释行留给 Task 4）

**Interfaces:**
- Consumes: `BagSections.Entries`、Task 2 全部 `ConfigEntry` 字段、`ModShared.BagTable`（`Cm()/BagDict()/FurnDict()/ParseIds()/Name()/InvalidateBagSizeIndex()`）。
- Produces: `SizeEngine.DiscoverManaged(List<string> notes, out bool vehReady)` → `HashSet<int>`（null=配置表未就绪）、`SizeEngine.RewriteManaged(HashSet<int> ids, bool verbose)` → `List<string>`、`SizeEngine.WriteTrace(string)`。

- [ ] **Step 1: 写 SizeEngine.cs**（受管集=家具发现−34黑名单−cfg排除+载具+1023；值解析=显式＞档位＞不缩水＞夹顶；幂等绝对值改写=CabinetExpand.CappedRewrite 同款）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace SurvivalLog.CabinetSize;

/// <summary>受管集发现 + 值解析 + 幂等改写 + trace 自证。机制与实证依据见 DESIGN.md §2/§5。</summary>
internal static class SizeEngine
{
    /// <summary>黑名单 34 包（沿用 CabinetExpand 2026-10-13 口径，理由留档其 DESIGN §5；机器槽/非储物/边界容器/纯场景搜刮）。</summary>
    internal static readonly int[] DefaultExcludeIds =
    {
        5001, 5002, 5003, 5004, 5005, 5006, 5011,       // 厨房电器（格子=烹饪槽位语义）
        5007, 5008, 5009, 5014, 5015, 5017, 5020,       // 老鼠笼/粉碎机/口粮压制机/暖房工程台/酿酒桶
        115004, 115002, 1050, 1062,                     // 堆肥箱/吊篮(种植)/通信设备
        10001, 10004,                                   // 床/垃圾桶
        1010, 2003, 1060, 1014, 1008, 1009, 115003, 3003, // 纸箱共包/门窗口暂存/无人机/货车/手推车
        110007, 3001, 2001, 2002, 2004, 10003,          // 纯场景搜刮容器
    };

    /// <summary>受管集：家具自动发现（BagId&gt;0）+ 载具（Config_Vehicle 全档 TrunkBagId + 门口袋 1023）− 黑名单 − cfg ExcludeIds。null=配置表未就绪。</summary>
    internal static HashSet<int> DiscoverManaged(List<string> notes, out bool vehReady)
    {
        vehReady = false;
        var cm = BagTable.Cm();
        var furn = BagTable.FurnDict(cm);
        if (furn == null || furn.Count == 0) return null;

        var set = new HashSet<int>();
        foreach (var pair in furn)
        {
            var f = pair.Value;
            if (f == null) continue;
            int bagId = 0;
            try { bagId = f.BagId; } catch { continue; }
            if (bagId > 0) set.Add(bagId);
        }

        try
        {
            var vdict = cm._Config_Vehicle_Dict;
            if (vdict != null && vdict.Count > 0)
            {
                vehReady = true;
                foreach (var pair in vdict)
                {
                    var v = pair.Value;
                    if (v == null) continue;
                    int trunkId = 0;
                    try { trunkId = v.TrunkBagId; } catch { continue; }
                    if (trunkId > 0 && set.Add(trunkId))
                        notes?.Add($"{pair.Key}({VehName(v)})→包{trunkId}({BagName(trunkId)})");
                }
            }
        }
        catch { }
        set.Add(CabinetSizePlugin.DoorBoxBagId);

        foreach (var id in DefaultExcludeIds) set.Remove(id);
        foreach (var id in BagTable.ParseIds(CabinetSizePlugin.ExcludeIds.Value)) set.Remove(id);
        return set;
    }

    private static string VehName(GameCore.HotUpdate.Config_Vehicle v)
    {
        try { return v.Name_Local ?? v.Name ?? "?"; } catch { return "?"; }
    }

    internal static string BagName(int bagId)
    {
        try
        {
            var cm = BagTable.Cm();
            var dict = BagTable.BagDict(cm);
            if (dict != null && dict.TryGetValue(bagId, out var bag) && bag != null)
                return BagTable.Name(bag);
        }
        catch { }
        return "?";
    }

    // ── 值解析：cfg 显式 ＞ 原生格数档位 ＞ 不缩水守卫 ＞ 全局夹顶 ──

    internal static string TierOf(int area) =>
        area <= 60 ? "小" : area <= 99 ? "中" : area <= 149 ? "大" : "特大";

    internal static (int w, int h, string src) Resolve(int id, int nativeW, int nativeH)
    {
        int w, h; string src;
        if (BagSections.Entries.TryGetValue(id, out var e)) { w = e.w; h = e.h; src = "cfg显式"; }
        else
        {
            var tier = TierOf(nativeW * nativeH);
            src = tier + "档";
            (w, h) = tier switch
            {
                "小" => (CabinetSizePlugin.SmallW.Value, CabinetSizePlugin.SmallH.Value),
                "中" => (CabinetSizePlugin.MediumW.Value, CabinetSizePlugin.MediumH.Value),
                "大" => (CabinetSizePlugin.LargeW.Value, CabinetSizePlugin.LargeH.Value),
                _ => (CabinetSizePlugin.XLargeW.Value, CabinetSizePlugin.XLargeH.Value),
            };
        }
        w = Math.Max(w, nativeW); h = Math.Max(h, nativeH); // 不缩水：未来新包原生超档位时保护
        if (CabinetSizePlugin.MaxWidth.Value > 0) w = Math.Min(w, CabinetSizePlugin.MaxWidth.Value);
        if (CabinetSizePlugin.MaxHeight.Value > 0) h = Math.Min(h, CabinetSizePlugin.MaxHeight.Value);
        return (w, h, src);
    }

    // ── 幂等绝对值改写（会话首见即原生；单写者=本 mod）──

    private static readonly Dictionary<int, int> NativeW = new();
    private static readonly Dictionary<int, int> NativeH = new();

    /// <summary>逐包改写到目标值。verbose=true 时未变更包也输出 原生→目标（ListOnReady 用）。返回日志行。</summary>
    internal static List<string> RewriteManaged(HashSet<int> ids, bool verbose)
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = cm == null ? null : BagTable.BagDict(cm);
        if (dict == null) return lines;
        int changed = 0;
        foreach (var pair in dict)
        {
            if (!ids.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            Il2CppSystem.Collections.Generic.List<int> size = null;
            try { size = bag.Size; } catch { }
            if (size == null || size.Count < 2) continue;
            int w0 = size[0], h0 = size[1];
            if (w0 <= 0 || h0 <= 0) continue;
            if (!NativeW.TryGetValue(pair.Key, out var nw)) { nw = w0; NativeW[pair.Key] = w0; }
            if (!NativeH.TryGetValue(pair.Key, out var nh)) { nh = h0; NativeH[pair.Key] = h0; }

            var (tw, th, src) = Resolve(pair.Key, nw, nh);
            if (w0 != tw || h0 != th)
            {
                size[0] = tw;
                size[1] = th;
                changed++;
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) {w0}x{h0} -> {tw}x{th}（{src}）");
            }
            else if (verbose)
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) 已是 {w0}x{h0}（原生 {nw}x{nh}，{src}）");
        }
        if (changed > 0) BagTable.InvalidateBagSizeIndex(cm, lines);
        return lines;
    }

    /// <summary>自证 trace 文件（日志强杀不落盘，实机验证一律看它）。路径 BepInEx\CabinetSize_trace.txt。</summary>
    internal static void WriteTrace(string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "CabinetSize_trace.txt"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + content,
                new UTF8Encoding(false));
        }
        catch { }
    }
}
```

- [ ] **Step 2: 填充 Ticker.Update**（替换 `CabinetSizePlugin.cs` 里 `public void Update() { /* Task 3 填充 */ }`；轮询节奏与旧 mod 相同 5 秒）

```csharp
        public void Update()
        {
            _check += Time.deltaTime;
            var due = CabinetSizePlugin.Rerun || _check >= 5f;
            if (!due) return;
            _check = 0f;

            if (BagSections.Poll()) CabinetSizePlugin.Rerun = true; // 玩家改 cfg 文件：变更立即落地

            var vehNotes = new List<string>();
            bool vehReady;
            var ids = SizeEngine.DiscoverManaged(vehNotes, out vehReady);
            if (ids == null) { CabinetSizePlugin.Rerun = true; return; }

            var listAll = CabinetSizePlugin.ListOnReady.Value && !_announced;
            var lines = SizeEngine.RewriteManaged(ids, listAll);
            CabinetSizePlugin.Rerun = false;

            if (lines.Count > 0)
                foreach (var l in lines) CabinetSizePlugin.Log.LogInfo("[CabinetSize] " + l);

            if (!_announced)
            {
                _announced = true;
                var summary = $"生效：{ids.Count} 个受管包（cfg 显式 {BagSections.Entries.Count} 条；"
                            + $"档位 小{CabinetSizePlugin.SmallW.Value}x{CabinetSizePlugin.SmallH.Value}"
                            + $"/中{CabinetSizePlugin.MediumW.Value}x{CabinetSizePlugin.MediumH.Value}"
                            + $"/大{CabinetSizePlugin.LargeW.Value}x{CabinetSizePlugin.LargeH.Value}"
                            + $"/特大{CabinetSizePlugin.XLargeW.Value}x{CabinetSizePlugin.XLargeH.Value}；"
                            + $"上限 {CabinetSizePlugin.MaxWidth.Value}x{CabinetSizePlugin.MaxHeight.Value}）；"
                            + "名单=家具发现+黑名单+载具，新增柜子自动按档跟上。";
                CabinetSizePlugin.Log.LogInfo("[CabinetSize] " + summary);
                if (listAll) SizeEngine.WriteTrace(summary + "\r\n\r\n" + string.Join("\r\n", lines));
            }

            // 车表随对局世界加载（主菜单未就绪）：就绪首轮补录后备箱名单（1023 种子恒生效）
            if (vehReady && !_vehListed)
            {
                _vehListed = true;
                CabinetSizePlugin.Log.LogInfo("[CabinetSize] 载具组补录（Config_Vehicle 全档后备箱+门口塑料袋 1023）："
                    + string.Join("、", vehNotes));
            }
        }

        private float _check;
        private bool _announced;
        private bool _vehListed;
```

（`using System.Collections.Generic;` 插件文件 Task 1 已引入。）

- [ ] **Step 3: 构建验证**

Run: `cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.CabinetSize -c Release > build_cabinetsize.log 2>&1; echo $?`
Expected: `0`

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.CabinetSize/SizeEngine.cs SurvivalLog.CabinetSize/CabinetSizePlugin.cs
git commit -m "feat(cabinetsize): 发现与改写引擎+Ticker轮询+trace自证"
```

---

### Task 4: 读档诊断补丁（OnInit postfix + 门口袋打点）

**Files:**
- Modify: `SurvivalLog.CabinetSize/SizeEngine.cs`（文件末尾追加诊断类）
- Modify: `SurvivalLog.CabinetSize/CabinetSizePlugin.cs`（Load 内挂补丁，替换 `// Task 3/4：改写引擎 + Ticker` 注释行）

**Interfaces:**
- Consumes: `CabinetSizePlugin.DiagOnOpen`、`SizeEngine.BagName(int)`。
- Produces: 无（纯日志诊断）。

- [ ] **Step 1: SizeEngine.cs 末尾追加诊断类**（通道=实例方法 `BagComponent.OnInit(int)`，三代通道实证里唯一可靠的；门口袋/后备箱三包 always-on 打点=载具落地自证）

```csharp
/// <summary>读档诊断：DiagOnOpen=true 时列出全部容器 BagConfigId；载具三包实例初始化 always-on 打点（每包每次会话一行）。</summary>
internal static class BagInitDiag
{
    private static readonly System.Collections.Generic.HashSet<int> Seen = new();
    private static readonly System.Collections.Generic.HashSet<int> Watched = new();
    private static readonly System.Collections.Generic.HashSet<int> VehicleWatch = new() { 1021, 1022, 1023 };

    [HarmonyLib.HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.BagComponent), "OnInit")]
    [HarmonyLib.HarmonyPostfix]
    private static void Postfix(GameCore.HotUpdate.Battle.Logic.BagComponent __instance, int bagConfigId)
    {
        try
        {
            if (VehicleWatch.Contains(bagConfigId) && Watched.Add(bagConfigId))
                CabinetSizePlugin.Log.LogInfo(
                    $"[CabinetSize][载具] 包 {bagConfigId}（{SizeEngine.BagName(bagConfigId)}）实例初始化，扩容已生效");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize][载具] 诊断读取失败: " + e.Message); }

        if (CabinetSizePlugin.DiagOnOpen == null || !CabinetSizePlugin.DiagOnOpen.Value) return;
        try
        {
            if (!Seen.Add(bagConfigId)) return;
            int w = 0, h = 0;
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict != null && dict.TryGetValue(bagConfigId, out var bag) && bag != null)
            {
                var sz = bag.Size;
                if (sz != null && sz.Count >= 2) { w = sz[0]; h = sz[1]; }
            }
            CabinetSizePlugin.Log.LogInfo(
                $"[CabinetSize][诊断] 容器初始化 bagConfigId={bagConfigId}（{SizeEngine.BagName(bagConfigId)}，当前 {w}x{h}，初始包={__instance.GetInitialBagConfigId()}）");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize][诊断] OnInit 读取失败: " + e.Message); }
    }
}
```

- [ ] **Step 2: Load 内挂补丁**（替换 `// Task 3/4：改写引擎 + Ticker` 注释行）

```csharp
        try
        {
            var harmony = new HarmonyLib.Harmony(Guid);
            harmony.PatchAll(typeof(BagInitDiag));
            Log.LogInfo("[CabinetSize] 诊断补丁已挂（读档时列出全部容器的 BagConfigId）");
        }
        catch (Exception e)
        {
            Log.LogWarning("[CabinetSize] 诊断补丁挂载失败（不影响改写功能）: " + e.Message);
        }
```

- [ ] **Step 3: 构建验证**

Run: `cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.CabinetSize -c Release > build_cabinetsize.log 2>&1; echo $?`
Expected: `0`

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.CabinetSize/SizeEngine.cs SurvivalLog.CabinetSize/CabinetSizePlugin.cs
git commit -m "feat(cabinetsize): OnInit读档诊断+载具三包落地自证打点"
```

---

### Task 5: 部署 + 实机首轮验证（名单核对）

**Files:**
- 无新源码；产物部署与验证。

**Interfaces:**
- Consumes: Task 3/4 全部；`CabinetSize_trace.txt` 自证文件。

- [ ] **Step 1: 前置检查（红线）**——`tasklist` 原始输出确认 `SurvivalLog.exe` **不在运行**；在跑则**本任务整体中止**，告知作者择时重试（绝不杀进程）。

Run: `tasklist | findstr /i SurvivalLog`（看原始输出判断；GBK 控制台 grep 不可靠）

- [ ] **Step 2: 部署**（新 dll 进、旧 dll/cfg 出——旧 dll 是双写冲突源，必须同时撤）

```bash
GAME="E:/SteamLibrary/steamapps/common/Survival Log"
cp "E:/Game/Mod/mod_stack/SurvivalLog.CabinetSize/bin/Release/SurvivalLog.CabinetSize.dll" "$GAME/BepInEx/plugins/"
rm -f "$GAME/BepInEx/plugins/SurvivalLog.CabinetExpand.dll"        # 退役（源码在仓库，可随时重build回滚）
rm -f "$GAME/BepInEx/config/com.local.survivallog.cabinetexpand.cfg"  # 惰性残留清理
rm -f "$GAME/BepInEx/CabinetSize_trace.txt"                        # 清旧自证
```

（删除前按仓库规矩核对目标存在性与路径无误；这三项均为本套件自有产物，非用户数据。）

- [ ] **Step 3: 启动游戏采自证**

Run: `powershell Start-Process 'steam://rungameid/4164790'`，然后轮询等待 `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/CabinetSize_trace.txt` 出现（表就绪后 ≤5 秒一轮 Ticker + 首轮 ListOnReady 即写入；主菜单后约 15-25 秒，超时 90 秒报失败）。

- [ ] **Step 4: trace 名单核对**（对照 DESIGN §5，逐行核）：
  - 受管包总数 = **27**（家具发现 24 + 载具 1021/1022/1023；1023 无家具引用）；
  - 12 显式包行含 `cfg显式` 且目标值= §5.1 表（110002→10x12 … 1023→18x12）；
  - 15 档位包行：12 个 `小档`→10x12、3 个 `中档`→14x12（1001/1006/3002/10002/10005/10006/10007/10008/10009/110003/110005/110006 小；1002/1003/110004 中）；
  - **1021/1022 原生值核对**：trace 的 `原生 5x4` / `原生 10x8`（此前为反推值，此步实证；不符则更新 DESIGN §5.1 与 BagSections.cs 注释并单独 commit）；
  - 无黑名单包（5001-5020/10001/1010/2003 等）出现在行内。
  任一不符 → 修 DiscoverManaged/名单后回到 Step 2 重验；**同题两轮不过换道**（回报作者定夺）。

- [ ] **Step 5: 双开防御一次性实锤**（可选但推荐）：把仓库里重 build 的 CabinetExpand.dll 临时拷回 plugins → 再启动一次 → 确认日志出现自禁警告且 trace **不更新**（旧 trace 内容保留）→ 撤掉 CabinetExpand.dll。作者在玩则跳过此步（留待 Task 6 顺带）。

- [ ] **Step 6: Commit**（仅当 Step 4 触发了 1021/1022 修正）

```bash
git add SurvivalLog.CabinetSize/BagSections.cs SurvivalLog.CabinetSize/DESIGN.md
git commit -m "fix(cabinetsize): 1021/1022原生值实机核对修正"
```

---

### Task 6: 热生效验证 + UI 矩阵（作者参与）

**Files:**
- 无新源码（红灯时的降级=改 cfg 数值，不改代码；`[Tier.Large] Width 18→16` 这类）。

**Interfaces:**
- Consumes: cfg 文件热改 + Task 5 部署好的游戏。

- [ ] **Step 1: 热生效三连**（游戏运行中直接记事本改 cfg，每个改动等 ≤5 秒开柜核对，无需重启）：
  1. `[Tier.Small] Width = 10` 改 `12` → 任一小档柜（如床头柜）变 12 列；改回 `10` 复原；
  2. `[Bag.110001] Width = 14` 改 `20` → 中型置物架变 20 列；改回复原；
  3. 文件末尾新增节 `[Bag.1001]` + `Width = 14` / `Height = 12` → 柜子1（储物柜）从 10x12（小档）变 14x12（显式）；再在节内把 Width 改 `0` → 恢复原生 8x6。
  全过 → 热生效双通道（BepInEx 键 + 文件节）实证。

- [ ] **Step 2: UI 矩阵（DESIGN §7 的 1-3 项；作者肉眼判定，或经作者明确许可后代跑）**：
  1. 四档宽度 × 界面：把 110000（大档 18 宽）与 110008（特大 20 宽）分别对接后打开 背包双栏 / 烹饪 / 制造台 / 交易 / 无人机中控台 —— 右栏不出屏、无变形；
  2. 材料架（20x12）对接页：制造台/粉碎机/交易/无人机左栏分行视图正常；
  3. 官方零适配三页（酿酒桶/染色台/老鼠笼）对接 14/18 宽柜 —— 允许"先小后大"的官方锁节奏，判定标准=不溢出屏幕；
  4. 门口塑料袋：装袋/到家卸货（TryMoveBagAll 装不下留后备箱）正常。
  **红灯处理（内置降级路径）**：若 18/20 宽在多列页溢出 → cfg 把 `[Tier.Large] Width=16`、`[Tier.XLarge] Width=16` 重测；仍红 → 收 14，并把结论记入 DESIGN §5/§9（数值以作者实测为准，这是"作者手测定终值"环节的数据来源）。

- [ ] **Step 3: 存量档兼容**：开一局有超界物品的旧档（或往小柜塞满后调小档位值），确认物品被 SetBagPosSafe 矫正挪回、无丢失（旧 mod 已验证机制，复测一次）。

- [ ] **Step 4: 定稿数值与提交**：作者拍板终值后——若与 PresetV1 不同：`BagSections.cs` 内**新增** `PresetV2` 行 + `CurrentPreset = 2` + `PresetHistory.Add(2, PresetV2)`（勿改 v1 历史行），本地 cfg 删除重生成核对；数值同步 DESIGN §5.1；commit：

```bash
git add SurvivalLog.CabinetSize/BagSections.cs SurvivalLog.CabinetSize/DESIGN.md
git commit -m "feat(cabinetsize): 作者实测定稿PresetV2数值"
```

（若终值=PresetV1 原值，本步只更新 DESIGN §10 拍板记录并 commit 文档。）

---

### Task 7: 构建发版链接线 + 退役旧 mod

**Files:**
- Modify: `tools/publish.ps1:17`
- Modify: `tools/make_mods_release.ps1:43`
- Modify: `installer/installer.iss`（[InstallDelete] 区 72 行附近 + [Files] 116-118 行 + [Components] 的 mod_cabinet 描述）
- Modify: `tools/test_installer.ps1`（预放物与断言）
- Modify: `tools/gamekb/moddeps.json`（删 CabinetExpand 键、增 CabinetSize 键）

**Interfaces:**
- Consumes: Task 5/6 验证通过的 mod。
- Produces: `publish.ps1` 可一键打包新 mod；安装器升级清理旧 mod；modcompat 核对新条目。

- [ ] **Step 1: publish.ps1**——17 行 `$suiteMods` 里 `'CabinetExpand'` → `'CabinetSize'`（ASCII 行，保持单引号逗号格式不变）。

- [ ] **Step 2: make_mods_release.ps1**——43 行 `@{ Name='CabinetExpand';  Readme='readme_cabinet.md'  },` → `@{ Name='CabinetSize';     Readme='readme_cabinet.md'  },`（readme 文件名不变，内容 Task 8 重写）。

- [ ] **Step 3: installer.iss**（保持 BOM）：
  1. [InstallDelete] 区（72 行旧 dll 删除行保留）追加两行：
     ```
     Type: files; Name: "{app}\BepInEx\config\com.local.survivallog.cabinetexpand.cfg"
     Type: files; Name: "{app}\MOD说明\柜子扩容.md"
     ```
  2. [Files] 116-118 三行（dll/cfg/README）替换为两行（**不再随包发 cfg**——mod 首跑自生成 12 节，发 cfg 会与预设迁移打架）：
     ```
     Components: mod_cabinet; Source: "{#RelRoot}\SurvivalLog.CabinetSize_v{#SuiteVer}\BepInEx\plugins\SurvivalLog.CabinetSize.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion
     Components: mod_cabinet; Source: "{#RelRoot}\SurvivalLog.CabinetSize_v{#SuiteVer}\README.md"; DestDir: "{app}\MOD说明"; DestName: "柜子容量.md"; Flags: ignoreversion
     ```
     同时删除原 117 行（cabinetexpand.cfg onlyifdoesntexist 那行）。
  3. `grep -n mod_cabinet installer/installer.iss` 找 [Components] 描述行，显示名 `柜子扩容` → `柜子容量`（组件键名 `mod_cabinet` 不变，升级路径稳定）。

- [ ] **Step 4: test_installer.ps1**（保持 BOM；该脚本组件集=`base,mod_backpack,mod_quick`，柜子组件未勾选）：
  - 预放区（45 行旁）追加预放旧 cfg 与说明文件，验证 InstallDelete：
     ```powershell
     Set-Content (Join-Path $setupDir 'BepInEx\config\com.local.survivallog.cabinetexpand.cfg') 'OLD-CABINET-CFG'
     Set-Content (Join-Path $setupDir 'MOD说明\柜子扩容.md') 'OLD-DOC' 
     ```
     （`MOD说明` 目录若不存在先 `New-Item -ItemType Directory -Force`。）
  - 断言区追加：
     ```powershell
     Check 'BepInEx\plugins\SurvivalLog.CabinetSize.dll' $false
     Check 'BepInEx\config\com.local.survivallog.cabinetexpand.cfg' $false
     Check 'MOD说明\柜子扩容.md' $false
     ```

- [ ] **Step 5: moddeps.json**——顶层键 `CabinetExpand` **整键删除**（退役 mod 不再核对；源码留档无需兼容核对），同位置新增：
  ```json
  "CabinetSize": {
    "harmony": [
      { "type": "GameCore.HotUpdate.Battle.Logic.BagComponent", "method": "OnInit" }
    ],
    "configFields": [
      { "accessor": "_Config_Bag_Dict", "rowType": "Config_Bag", "fields": ["Size"] },
      { "accessor": "_Config_Furniture_Dict", "rowType": "Config_Furniture", "fields": ["BagId"] },
      { "accessor": "_Config_Vehicle_Dict", "rowType": "Config_Vehicle", "fields": ["TrunkBagId"] }
    ],
    "nativeSigs": []
  },
  ```

- [ ] **Step 6: 回归验证**：
  1. `node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732` → 新键全绿 exit 0；
  2. `powershell -NoProfile -ExecutionPolicy Bypass -File tools/test_installer.ps1` → 全 OK 无 MISSING/UNEXPECTED（需要先 `make_mods_release.ps1` 产出安装器或走既有产物路径，按脚本头部注释的既有用法执行）。

- [ ] **Step 7: Commit**

```bash
git add tools/publish.ps1 tools/make_mods_release.ps1 installer/installer.iss tools/test_installer.ps1 tools/gamekb/moddeps.json
git commit -m "feat(cabinetsize): 套件发版链接线+安装器退役清理（CabinetExpand退出发布链）"
```

---

### Task 8: 名单注册与文档收官

**Files:**
- Modify: `SurvivalLog.ModCenter/Meta.cs`（cabinetexpand 条目）
- Modify: `tools/_mc_fixture.js`
- Modify: `tools/update_welcometip_docs.ps1:15`
- Modify: `tools/readme_cabinet.md`（整文件重写）
- Modify: `docs/00-目录说明.md`、`README.md`（mod 清单条目）
- Modify: `SurvivalLog.CabinetSize/DESIGN.md`（§3 文件表、§4 键名、§8 moddeps 措辞、§10 拍板记录）

**Interfaces:**
- Consumes: Task 1-7 全部定稿事实。

- [ ] **Step 1: ModCenter 注册**——`grep -n cabinetexpand SurvivalLog.ModCenter/Meta.cs tools/_mc_fixture.js` 定位条目，GUID `com.local.survivallog.cabinetexpand` → `com.local.survivallog.cabinetsize`、显示名 `柜子扩容/CabinetExpand` → `柜子容量/CabinetSize`（键名/结构照旧；ModCenter 本身待复评，不扩功能）。跑 ModCenter 既有夹具（若 `_mc_fixture.js` 是 node 可直接跑）确认绿。

- [ ] **Step 2: update_welcometip_docs.ps1:15**——`$mods` 数组 `'CabinetExpand'` → `'CabinetSize'`。

- [ ] **Step 3: 重写 tools/readme_cabinet.md**（玩家说明书，中文，结构照旧文件风格；必含：四档表与阈值、12 常用包默认值表（含原生对照）、"给其他柜子加条目"三行示例（`[Bag.1001]` / `Width = 14` / `Height = 12`，`0`=恢复原生）、全局上限说明（20x12=官方预算，调大自担）、改档位 vs 改单包的区别、"游戏更新新增柜子自动按档跟上、日志/trace 看名单"、卸载=删 dll 即还原）。

- [ ] **Step 4: docs/00 与根 README**——mod 清单里 柜子扩容 条目改为 柜子容量（机制一句话：数值四档+12包显式+上限20x12+官方自适应；详细指向 `SurvivalLog.CabinetSize/DESIGN.md`）。CabinetExpand 在清单中移除（源码目录保留不再列出，历史见 CHANGELOG）。

- [ ] **Step 5: DESIGN.md 收官同步**（四处）：
  - §3 文件表改三文件实况（`CabinetSizePlugin.cs` 入口+cfg+Ticker / `BagSections.cs` cfg 文件自解析+预设 / `SizeEngine.cs` 发现+解析+改写+诊断），并注记"单文件 450 行预估改为三文件=审阅粒度，职责不变"；
  - §4 键名落 ASCII 实况：`[Global] MaxWidth/MaxHeight/Preset/ExcludeIds`、`[Tier.Small|Medium|Large|XLarge] Width/Height`、`[Debug] ListOnReady/DiagOnOpen`、`[Bag.<id>]`，中文仅描述与注释（解析器/ModCenter 桥友好）；
  - §8 的 moddeps 措辞改实况："删除 CabinetExpand 键、新增 CabinetSize 键"；
  - §10 追加实施记录行（本计划执行日期、Task 6 定稿数值、Task 5 的 1021/1022 核对结论）。

- [ ] **Step 6: Commit**

```bash
git add SurvivalLog.ModCenter/Meta.cs tools/_mc_fixture.js tools/update_welcometip_docs.ps1 tools/readme_cabinet.md docs/00-目录说明.md README.md SurvivalLog.CabinetSize/DESIGN.md
git commit -m "docs(cabinetsize): 名单注册+玩家说明书+DESIGN实施收官（CabinetExpand退役出清单）"
```

---

## 自检记录（writing-plans Self-Review）

1. **Spec 覆盖**：DESIGN §1 数值模型→T2/T3；§2 机制（发现/解析/幂等/热生效/双开/诊断）→T2/T3/T4；§3 代码结构→T1-T4（三文件 vs §3 单文件：T8 Step5 同步文档）；§4 配置→T2；§5 名单数值→T2(PresetV1)/T3(黑名单)/T5(核对)；§6 交互→T1(双开)/T8(注册)；§7 验证矩阵→T5/T6；§8 接线退役→T7/T8；§9/§10 已知限制与拍板→T6 Step4/T8 Step5。**无缺口**。
2. **占位符扫描**：MigratePreset 含完整通用逻辑（v1 时自然 no-op，非"以后再写"）；无 TBD/TODO/"适当处理"。
3. **类型一致性**：`BagSections.Entries`/`Init`/`Poll`、`SizeEngine.DiscoverManaged/RewriteManaged/WriteTrace/BagName/TierOf/Resolve`、`CabinetSizePlugin.Rerun/DoorBoxBagId/DiagOnOpen` 等跨任务引用名已逐一核对一致；`ConfigEntry` 全限定名在 T2 字段声明处使用（避免 T1 骨架未引 using 编译错——T2 字段用 `BepInEx.Configuration.ConfigEntry<...>` 全名，绑定方法体内同理）。
