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
/// 家里的柜子按 小/中/大/特大 四档调到固定宽高；13 常用包（置物架5+冰箱4+载具3+工具柜）为
/// BepInEx 绑定显式条目（v4 起，ModCenter 反射桥直达）；其余按档自动生效；全局上限默认 20x16。
/// General.Enabled 总开关（ModCenter 启停位）：false=热关还原官方原值并停写，下次启动不加载。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetSizePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetsize";
    public const string Name = "CabinetSize";
    public const string Version = "1.3.8"; // 跟随套件 version.json（版本号由作者定义）

    internal static ManualLogSource Log;
    internal static volatile bool Rerun = true;

    /// <summary>ConfigFile 静态句柄（BagSections.EnsureDefaultSections 缺节兜底 Save 用）。</summary>
    internal static BepInEx.Configuration.ConfigFile Cfg;

    /// <summary>General.Enabled 总开关（ModCenter 启停位，2026-10-25 补齐——此前未 Bind 面板启停被拒）：
    /// false=热关还原官方原值并停写（Ticker 首行 gate + _applied 单次还原）；下次启动不加载。</summary>
    internal static BepInEx.Configuration.ConfigEntry<bool> Enabled;

    internal static BepInEx.Configuration.ConfigEntry<int> MaxWidth, MaxHeight, Preset;
    internal static BepInEx.Configuration.ConfigEntry<string> ExcludeIds;
    internal static BepInEx.Configuration.ConfigEntry<string> BagOverrides;
    internal static BepInEx.Configuration.ConfigEntry<int> SmallW, SmallH, MediumW, MediumH, LargeW, LargeH, XLargeW, XLargeH;
    internal static BepInEx.Configuration.ConfigEntry<bool> ListOnReady, DiagOnOpen;

    /// <summary>13 常用包显式宽高绑定键（v4 起为 BepInEx 正式条目，ModCenter 反射桥直达；
    /// 值解析权威=本表（SizeEngine.Resolve 首选），0=恢复该包官方原值）。</summary>
    internal static readonly Dictionary<int, (BepInEx.Configuration.ConfigEntry<int> W, BepInEx.Configuration.ConfigEntry<int> H)> BagBindings = new();

    private void BindConfig()
    {
        Cfg = Config;
        // General.Enabled 放最前 Bind（保证 cfg 键已生成后才可能早退；与套件其余 mod T10 模式一致）
        Enabled = Config.Bind("General", "Enabled", true, "总开关：false=还原官方原值并停写；下次启动不加载。");
        const string g = "柜子宽度上限：档位与自定义条目超出即夹到此值（0=不封顶，超宽可能挤出屏幕自担风险）。默认 20=官方原生最大宽度（材料架列数），官方自适应设计预算内。";
        MaxWidth = Config.Bind("Global", "MaxWidth", 20, g);
        MaxHeight = Config.Bind("Global", "MaxHeight", 16,
            "柜子高度上限（0=不封顶）。默认 16=当前特大档高度（作者 2026-10-25 实测定稿）。");
        Preset = Config.Bind("Global", "Preset", BagSections.CurrentPreset, "默认值预设版本（勿手改）：mod 升级后未手动调过的条目自动跟新默认值。");
        ExcludeIds = Config.Bind("Global", "ExcludeIds", "", "追加排除的 BagId（逗号分隔）：在默认黑名单之外额外恢复官方原值。");
        BagOverrides = Config.Bind("Global", "BagOverrides", "",
            "个性化覆盖清单（id:WxH 逗号分隔，如 3002:20x12）：任意柜子单独定宽高，优先级最高（高于 13 包显式与档位）；" +
            "对黑名单柜（纸箱/机器槽等）同样生效。改条目即时生效；删行=从清单移除该柜（回落到下一优先级）。");
        SmallW = Config.Bind("Tier.Small", "Width", 14, "小档宽度（原生格数≤60 的柜子自动落此档）。");
        SmallH = Config.Bind("Tier.Small", "Height", 10, "小档高度。");
        MediumW = Config.Bind("Tier.Medium", "Width", 16, "中档宽度（原生格数 61~99）。");
        MediumH = Config.Bind("Tier.Medium", "Height", 12, "中档高度。");
        LargeW = Config.Bind("Tier.Large", "Width", 18, "大档宽度（原生格数 100~149）。");
        LargeH = Config.Bind("Tier.Large", "Height", 14, "大档高度。");
        XLargeW = Config.Bind("Tier.XLarge", "Width", 20, "特大档宽度（原生格数≥150）。");
        XLargeH = Config.Bind("Tier.XLarge", "Height", 16, "特大档高度。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true, "首次生效时在日志/trace 列出全部受管包与尺寸前后（核对名单用）。");
        DiagOnOpen = Config.Bind("Debug", "DiagOnOpen", false, "读档时在日志列出全部容器的 BagConfigId（排查某柜不生效，平时关防刷屏）。");

        // 13 常用包显式宽高（v4 绑定制）：Bind 描述=预设 note（BepInEx 写成节内 ## 注释）；
        // 旧版孤儿节文件在 Bind 阶段自动被读成现值（含玩家改过的值），迁移见 BagSections.MigratePreset
        foreach (var (id, w, h, note) in BagSections.PresetV4)
        {
            var desc = note.TrimStart(' ', ';');
            var we = Config.Bind("Bag." + id, "Width", w, desc + " 宽度（0 = 恢复该柜官方原值）。");
            var he = Config.Bind("Bag." + id, "Height", h, desc + " 高度（0 = 恢复该柜官方原值）。");
            BagBindings[id] = (we, he);
        }

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; BagSections.NoteBindChange(); }; // 显式形参类型：本机 Roslyn 对推断形参的 lambda 转换会触发 CS0656（NullableAttribute 缺失），套件内三个 mod 同款写法；NoteBindChange=ApplyHotKeys 陈旧文件闸（T17 跨 mod 竞态，见 BagSections.ApplyHotKeys 注释）
        Enabled.SettingChanged += reload; // SettingChanged 在 ConfigEntry<T> 上（ConfigEntryBase 无事件，本机 BepInEx.Core 6.0.0.0 实证），故逐条订阅
        MaxWidth.SettingChanged += reload;
        MaxHeight.SettingChanged += reload;
        Preset.SettingChanged += reload;
        ExcludeIds.SettingChanged += reload;
        BagOverrides.SettingChanged += reload;
        SmallW.SettingChanged += reload;
        SmallH.SettingChanged += reload;
        MediumW.SettingChanged += reload;
        MediumH.SettingChanged += reload;
        LargeW.SettingChanged += reload;
        LargeH.SettingChanged += reload;
        XLargeW.SettingChanged += reload;
        XLargeH.SettingChanged += reload;
        ListOnReady.SettingChanged += reload;
        DiagOnOpen.SettingChanged += reload;
        foreach (var kv in BagBindings)
        {
            kv.Value.W.SettingChanged += reload;
            kv.Value.H.SettingChanged += reload;
        }
    }

    /// <summary>家门口塑料袋专用包（VehicleConfig.DoorBoxBagId 常量镜像，与 SUV 后备箱共用）。</summary>
    internal const int DoorBoxBagId = 1023;

    public override void Load()
    {
        Log = base.Log;

        // 双开防御：前任 CabinetExpand 在场则自禁（两 mod 同写 Config_Bag.Size 必互相污染）
        try
        {
            if (IL2CPPChainloader.Instance?.Plugins?.ContainsKey("com.local.survivallog.cabinetexpand") == true)
            {
                Log.LogWarning("[CabinetSize] 检测到旧 mod CabinetExpand 仍在运行：两 mod 同写 Config_Bag.Size 会互相污染，"
                               + "本 mod 已自禁。请删除 BepInEx\\plugins\\SurvivalLog.CabinetExpand.dll 与其 cfg 后重启游戏。");
                return;
            }
        }
        catch (Exception e) { Log.LogWarning("[CabinetSize] 双开检测异常（继续加载）: " + e.Message); }

        BindConfig();

        // General.Enabled=false：不挂诊断补丁、不启 Ticker（表值本会话从未改过；下次启动不加载）
        if (!Enabled.Value)
        {
            Log.LogInfo("[CabinetSize] General.Enabled=false：本会话不加载（下次启动同样跳过，重新启用后恢复）");
            return;
        }

        BagSections.Init(Config.ConfigFilePath); // 本机 BepInEx.Core 6.0.0.0 的属性名是 ConfigFilePath（brief 误写 ConfigPath，已实证反射）
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
        public void Update()
        {
            // General.Enabled 总开关：false=立即把本会话已改的包尺寸还原官方原值并停写。Load 阶段 false
            // 时 Ticker 不会创建，本分支覆盖运行中被关闭（ModCenter 写 cfg 热生效）；重开后 ≤5 秒恢复落笔。
            if (CabinetSizePlugin.Enabled != null && !CabinetSizePlugin.Enabled.Value)
            {
                if (_applied)
                {
                    var rl = new List<string>();
                    if (SizeEngine.RestoreAll(rl, out var changed)) // 表未就绪返回 false，下一帧重试
                    {
                        _applied = false;
                        CabinetSizePlugin.Log.LogInfo("[CabinetSize] General.Enabled=false：已还原官方原值并停写"
                            + (changed > 0 ? $"（回写 {changed} 处）" : "（表已在原值）"));
                        for (int i = 0; i < rl.Count && i < 30; i++)
                            CabinetSizePlugin.Log.LogInfo("[CabinetSize] " + rl[i]);
                    }
                }
                return;
            }

            _check += Time.deltaTime;
            var due = CabinetSizePlugin.Rerun || _check >= 5f;
            if (!due) return;
            _check = 0f;

            if (BagSections.Poll()) CabinetSizePlugin.Rerun = true; // 玩家改 cfg 文件：变更立即落地

            var vehNotes = new List<string>();
            bool vehReady;
            SizeEngine.RefreshOverrides(); // 个性化清单（BagOverrides）先于受管集发现刷新（发现会注入其 id）
            var ids = SizeEngine.DiscoverManaged(vehNotes, out vehReady);
            if (ids == null) { CabinetSizePlugin.Rerun = true; return; }

            var listAll = CabinetSizePlugin.ListOnReady.Value && !_announced;
            var lines = SizeEngine.RewriteManaged(ids, listAll);
            CabinetSizePlugin.Rerun = false;
            _applied = true; // 已按当前配置落笔（General.Enabled 关闭时据此触发一次性还原）

            if (lines.Count > 0)
                foreach (var l in lines) CabinetSizePlugin.Log.LogInfo("[CabinetSize] " + l);

            if (!_announced)
            {
                _announced = true;
                int playerOnly = 0; // 玩家自加节（非 13 绑定包）
                foreach (var id in BagSections.Entries.Keys)
                    if (!CabinetSizePlugin.BagBindings.ContainsKey(id)) playerOnly++;
                var summary = $"生效：{ids.Count} 个受管包（显式 {CabinetSizePlugin.BagBindings.Count} 包绑定 + 自加 {playerOnly} 包"
                    + (SizeEngine.Overrides.Count > 0 ? $" + 个性化 {SizeEngine.Overrides.Count} 条" : "") + "；"
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
                if (CabinetSizePlugin.ListOnReady.Value)
                {
                    var full = SizeEngine.RewriteManaged(ids, true); // 全部已在目标值 → 纯"已是"快照，含载具三包，零写入副作用
                    SizeEngine.WriteTrace("载具组补录：" + string.Join("、", vehNotes) + "\r\n\r\n" + string.Join("\r\n", full));
                }
            }
        }

        private float _check;
        private bool _announced;
        private bool _vehListed;
        // General.Enabled 门：本轮会话是否已按配置落笔过（false 化时据此触发一次性还原）
        private bool _applied;
    }
}
