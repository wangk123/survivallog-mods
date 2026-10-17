using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetExpand;

/// <summary>
/// 柜子扩容（独立 mod；2026-10-13 起吸收原"冰箱扩容 FridgeExpand"，包名/组件名不变）。
/// 家里全部"柜子类"容器 + 冰箱族格子宽高各 ×倍数（默认 1.5），两组倍数独立可调。
///
/// 名单机制（2026-10-13 统一化，用户拍板"只扩家里的柜子，忽略其他"）：
/// 自动发现（扫 Config_Furniture 全部 BagId>0）+ 黑名单排除（机器槽/载具/场景搜刮等）。
/// 分组：包的全部使用家具都是冷链电器（IsElectrical&&Type==1&&ColdRate>0，与原生
/// CollectFridges 同判定）→ 冰箱组；其余 → 柜子组。此前 8 包白名单导致置物架家族只有
/// 中型（110001，与工具柜共包）被扩，180 格反超大型原生 120 格——"大柜子反而格子少"
/// 用户反馈的根因；统一 ×1.5（AwayFromZero 取整对每维严格单调）恢复家族内排序。
///
/// 材料架（家具 205，包 110009）实证（webui-rackrows.js 官方注释）：分行视图底层仍是
/// 包的宽×高格子——高=行数（能放几种材料），宽=每行件数上限（行长÷材料宽），
/// 行数据由 C# 从格子实时构造，扩容有效且 UI 动态适配。
///
/// 合并后的单写者原则：全游戏只有本 mod 写 Config_Bag.Size（BackpackExpand 只管玩家
/// 背包链 1-11，名单与家具发现天然不相交）——彻底消除旧"双 mod 共管 ×2.25 双乘"风险。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetexpand";
    public const string Name = "CabinetExpand";
    // 跟随套件 version.json，发版时同步（版本号由作者定义）
    public const string Version = "1.3.5";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> WidthMult;
    internal static ConfigEntry<float> HeightMult;
    internal static ConfigEntry<float> FridgeWidthMult;
    internal static ConfigEntry<float> FridgeHeightMult;
    internal static ConfigEntry<int> MaxCols;
    internal static ConfigEntry<string> ExtraIds;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> DiagOnOpen;

    internal static volatile bool Rerun = true;

    /// <summary>
    /// 冰箱组静态种子（原 FridgeExpand 默认名单成员，3002 除外——箱子族有大量非冷链
    /// 共用家具，归柜子组）。1004 无法靠"全冷链"自动判定（医用冷藏柜 66114 用它但
    /// cold=0），故保留静态；未来新冰箱由"全冷链电器包"动态判定兜底。
    /// </summary>
    internal static readonly int[] DefaultFridgeIds = { 1004, 115000, 115001, 115005 };

    /// <summary>
    /// 黑名单（从自动发现结果中剔除，柜子组/冰箱组都适用）。依据 2026-10-13 全量 dump
    /// （880 条带包家具 → 58 包）：柜子家族 19 包 + 冰箱族自动入选，其余 34 包按下述
    /// 理由排除；游戏更新新增柜子/冰箱自动跟上。冰箱五包（1004/3002/115000/115001/115005）
    /// 已并入冰箱组不再列此处（原 FridgeExpand 名单成员 3002 因有大量非冷链共用家具归柜子组）。
    /// </summary>
    internal static readonly int[] DefaultExcludeIds =
    {
        // 厨房电器与机器功能槽（格子数=玩法语义，不是储物）
        5001, 5002, 5003, 5004, 5005, 5006, 5011, // 燃料炉/电烤箱/微波炉/燃气灶/榨汁机/咖啡机/电火锅
        5007, 5008, 5009, 5014,                   // 老鼠笼小/中/大/超大
        5015, 5017, 5020, 115004,                 // 粉碎机/口粮压制机/暖房工程台/酿酒桶
        115002, 1050, 1062,                       // 堆肥箱/吊篮(种植)/通信设备
        // 非储物语义
        10001, 10004,                             // 床/垃圾桶
        // 边界容器（用户 2026-10-13 拍板排除；需要可用 ExtraIds 加回）
        1010, 2003,        // 纸箱+工具桌/自制工具台(313)共包；纸箱+载具+床头柜9109共包
        1060, 1014,        // 门口暂存格(门家具)/窗台收纳(窗家具)
        1008, 1009,        // 无人机货舱/无人机中控台
        115003, 3003,      // 货车车厢/手推车·小拖车
        // 纯场景搜刮容器（没有任何玩家可放置家具使用，扩了只影响外出搜刮）
        110007, 3001, 2001, 2002, 2004, 10003,   // 医院货架系/家具城柜台·围栏/非卖品冷柜/一箱啤酒/工具箱·收银台/工具箱9115
    };

    public override void Load()
    {
        Log = base.Log;
        WidthMult = Config.Bind("Expand", "WidthMult", 1.5f, "柜子宽度倍数（四舍五入取整）。");
        HeightMult = Config.Bind("Expand", "HeightMult", 1.5f, "柜子高度倍数（四舍五入取整）。");
        FridgeWidthMult = Config.Bind("Expand", "FridgeWidthMult", 1.5f,
            "冰箱宽度倍数（四舍五入取整）。1 = 还原原生。原冰箱扩容 mod 已并入本 mod。");
        FridgeHeightMult = Config.Bind("Expand", "FridgeHeightMult", 1.5f,
            "冰箱高度倍数（四舍五入取整）。1 = 还原原生。");
        MaxCols = Config.Bind("Expand", "MaxCols", 23,
            "扩容后的宽度上限（倍数取整后再封顶；0 = 不封顶）。默认 23 = 与超大型置物架同宽，" +
            "所有柜子页签宽度统一，各界面只需一档缩放、切柜子不跳尺寸。材料架受影响：30→23 列（540→414 格）。");
        ExtraIds = Config.Bind("Expand", "ExtraIds", "",
            "追加的 BagId（逗号分隔，优先级最高）。全冷链电器的包（冰箱族）会自动归入冰箱倍数组，" +
            "其余归柜子倍数组。注意 bag 10002=金属柜，家具\"小型置物架\"用的是包 110002（id 撞号勿加错）。");
        ExcludeIds = Config.Bind("Expand", "ExcludeIds", "",
            "排除的 BagId（逗号分隔，柜子/冰箱两组都排除）。默认黑名单已排除：厨房电器/机器槽(老鼠笼·粉碎机等)/" +
            "床·垃圾桶/纸箱共包(1010/2003)/门窗口暂存/无人机/载具/场景搜刮货架；这里追加你想关掉的包。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮输出明细。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "首次生效时在日志列出全部受管包与尺寸前后（核对名单用）。");
        DiagOnOpen = Config.Bind("Debug", "DiagOnOpen", false,
            "读档时在日志列出全部容器的 BagConfigId（确认名单用，平时关闭防刷屏）。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        WidthMult.SettingChanged += reload;
        HeightMult.SettingChanged += reload;
        FridgeWidthMult.SettingChanged += reload;
        FridgeHeightMult.SettingChanged += reload;
        MaxCols.SettingChanged += reload;
        ExtraIds.SettingChanged += reload;
        ExcludeIds.SettingChanged += reload;

        try
        {
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(BagInitDiag));
            Log.LogInfo("[CabinetExpand] 诊断补丁已挂（读档时列出全部容器的 BagConfigId）");
        }
        catch (Exception e)
        {
            Log.LogWarning("[CabinetExpand] 诊断补丁挂载失败（不影响改写功能）: " + e.Message);
        }

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CabinetTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<CabinetTicker>();
        var go = new GameObject("CabinetExpand.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CabinetTicker>();

        // 视口适配守卫（2026-10-16）：大柜子页签把界面右栏挤出屏幕的修复，
        // 12 个官方适配页让官方自测真实宽度 + 3 个无适配页补官方同款缩放；详见 ViewportFit.cs
        ViewportFit.Install(Config);
        ViewportFit.Attach();
        Log.LogInfo($"[CabinetExpand] loaded：柜子宽×{WidthMult.Value} 高×{HeightMult.Value}；" +
                    $"冰箱宽×{FridgeWidthMult.Value} 高×{FridgeHeightMult.Value}（已并入原冰箱扩容）；" +
                    $"视口守卫 {(ViewportFit.Enabled.Value ? "开" : "关")}");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    /// <summary>
    /// 名单发现：扫 Config_Furniture 全部 BagId&gt;0（不限 FurnitureType——Type=0 里有大量
    /// 原生家柜：床头柜/衣柜/材料架等，实证 FurnitureType 不可靠）。分组：
    /// 全冷链电器使用 → 冰箱组；其余 → 柜子组。剔除 DefaultExcludeIds 与 cfg ExcludeIds
    /// （两组都剔），最后 ExtraIds 优先级最高按冷链属性加回对应组。
    /// 返回 null 表示配置表未就绪。
    /// </summary>
    internal static (HashSet<int> cabinets, HashSet<int> fridges) DiscoverTargets(List<string> fridgeNotes)
    {
        var cm = BagTable.Cm();
        var dict = BagTable.FurnDict(cm);
        if (dict == null || dict.Count == 0) return (null, null);

        var cabinets = new HashSet<int>();
        var fridges = new HashSet<int>();
        var coldOnly = new Dictionary<int, bool>(); // 包 → 至今只见冷链电器在用（缺省=true）
        foreach (var pair in dict)
        {
            var f = pair.Value;
            if (f == null) continue;

            int bagId = 0; bool isElec = false; int elecType = 0; float cold = 0f;
            try { bagId = f.BagId; isElec = f.IsElectrical; elecType = f.ElectricalType; cold = f.ColdRate; }
            catch { continue; }
            if (bagId <= 0) continue;

            cabinets.Add(bagId);
            bool coldChain = isElec && elecType == 1 && cold > 0f; // 与原生 CollectFridges 同判定
            if (!coldOnly.TryGetValue(bagId, out var prev)) prev = true; // 首见默认"仍可能全冷链"
            coldOnly[bagId] = prev && coldChain;
        }

        foreach (var kv in coldOnly)
        {
            if (!kv.Value) continue;
            cabinets.Remove(kv.Key);
            fridges.Add(kv.Key);
        }
        foreach (var id in DefaultFridgeIds)
        {
            if (cabinets.Remove(id)) fridges.Add(id);
        }

        foreach (var id in DefaultExcludeIds) { cabinets.Remove(id); fridges.Remove(id); }
        var exclude = BagTable.ParseIds(ExcludeIds.Value);
        foreach (var id in exclude) { cabinets.Remove(id); fridges.Remove(id); }
        foreach (var id in BagTable.ParseIds(ExtraIds.Value))
        {
            if ((coldOnly.TryGetValue(id, out var allCold) && allCold) ||
                Array.IndexOf(DefaultFridgeIds, id) >= 0) fridges.Add(id);
            else cabinets.Add(id);
        }

        // 名单自证（黑名单过滤后的最终状态，避免把被拦下的包误读为已受管）
        foreach (var id in fridges)
        {
            if (coldOnly.TryGetValue(id, out var allCold2) && allCold2)
                fridgeNotes?.Add($"{id}({BagName(id)})全冷链");
            else
                fridgeNotes?.Add($"{id}({BagName(id)})静态种子");
        }
        return (cabinets, fridges);
    }

    private static string BagName(int bagId)
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

    // ── 宽度封顶（2026-10-16）：本地原尺寸缓存 + 分流 + 封顶改写，不动 ModShared ──

    /// <summary>受管包扩后的最大宽/高（ViewportFit 守卫『打开即最大尺寸』的初始锁基准）。</summary>
    internal static int ManagedMaxW;
    internal static int ManagedMaxH;

    internal static void UpdateManagedMax(HashSet<int> cabinets, HashSet<int> fridges)
    {
        try
        {
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict == null) return;
            int mw = 0, mh = 0;
            foreach (var pair in dict)
            {
                if (!cabinets.Contains(pair.Key) && !fridges.Contains(pair.Key)) continue;
                var bag = pair.Value;
                if (bag == null) continue;
                try
                {
                    var s = bag.Size;
                    if (s != null && s.Count >= 2)
                    {
                        if (s[0] > mw) mw = s[0];
                        if (s[1] > mh) mh = s[1];
                    }
                }
                catch { }
            }
            if (mw > 0) ManagedMaxW = mw;
            if (mh > 0) ManagedMaxH = mh;
        }
        catch { }
    }

    /// <summary>本会话首见即原生（我们的改写是唯一写者）；跨周期幂等。</summary>
    private static readonly Dictionary<int, int> NativeW = new();
    private static readonly Dictionary<int, int> NativeH = new();

    private static int ReadNative(string what, int id, int fallback)
    {
        if (what == "w") { if (NativeW.TryGetValue(id, out var w)) return w; NativeW[id] = fallback; return fallback; }
        if (NativeH.TryGetValue(id, out var h)) return h;
        NativeH[id] = fallback;
        return fallback;
    }

    /// <summary>
    /// 按投影宽度分流：round(nativeW × mult) &gt; cap 的包进 capSet（cap&lt;=0 时全进 normal）。
    /// nativeW 优先取本地缓存，未缓存读当前值（本会话首次见到的当前值即原生值）。
    /// </summary>
    internal static (HashSet<int> normal, HashSet<int> capped) PartitionByCap(HashSet<int> ids, float mult, int cap)
    {
        var normal = new HashSet<int>();
        var capped = new HashSet<int>();
        if (cap <= 0) { normal.UnionWith(ids); return (normal, capped); }
        var cm = BagTable.Cm();
        var dict = cm == null ? null : BagTable.BagDict(cm);
        foreach (var id in ids)
        {
            int native = 0;
            if (dict != null && dict.TryGetValue(id, out var bag) && bag != null)
            {
                try { var s = bag.Size; if (s != null && s.Count >= 2) native = ReadNative("w", id, s[0]); } catch { }
            }
            if (native <= 0) { normal.Add(id); continue; } // 读不到就交回常规路径
            var proj = (int)Math.Round(native * mult, MidpointRounding.AwayFromZero);
            if (proj > cap) capped.Add(id);
            else normal.Add(id);
        }
        return (normal, capped);
    }

    /// <summary>封顶改写：want = 倍数取整后宽度 min(want, cap)；与共享 RewriteSize 同风格日志。</summary>
    internal static List<string> CappedRewrite(HashSet<int> ids, float wMult, float hMult, int cap)
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
            int nw = ReadNative("w", pair.Key, w0);
            int nh = ReadNative("h", pair.Key, h0);
            if (nw <= 0 || nh <= 0) continue;
            int wantW = Math.Min((int)Math.Round(nw * wMult, MidpointRounding.AwayFromZero), cap);
            int wantH = (int)Math.Round(nh * hMult, MidpointRounding.AwayFromZero);
            if (w0 != wantW || h0 != wantH)
            {
                size[0] = wantW;
                size[1] = wantH;
                changed++;
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) {w0}x{h0} -> {wantW}x{wantH}（宽度封顶 MaxCols={cap}）");
            }
        }
        if (changed > 0) BagTable.InvalidateBagSizeIndex(cm, lines);
        return lines;
    }
}

/// <summary>
/// 诊断 v3：patch BagComponent.OnInit(int)（实例方法，此通道旧 mod 已验证可靠；
/// v1 的 SendAction（泛型参数）与 v2 的 Trigger_RA_Open 均未触发——工具柜界面
/// 大概率不走 ToolTable，而是走背包双栏视图）。
/// 读档时每个容器实体初始化都会经过这里，一次性列出全部容器及其 BagConfigId。
/// </summary>
internal static class BagInitDiag
{
    private static readonly System.Collections.Generic.HashSet<int> Seen = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.BagComponent), "OnInit")]
    private static void Postfix(GameCore.HotUpdate.Battle.Logic.BagComponent __instance, int bagConfigId)
    {
        if (!CabinetExpandPlugin.DiagOnOpen.Value) return;
        try
        {
            if (!Seen.Add(bagConfigId)) return;
            string name = "?"; int w = 0, h = 0;
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict != null)
            {
                GameCore.HotUpdate.Config_Bag bag = null;
                try { dict.TryGetValue(bagConfigId, out bag); } catch { }
                if (bag != null)
                {
                    name = BagTable.Name(bag);
                    var sz = bag.Size;
                    if (sz != null && sz.Count >= 2) { w = sz[0]; h = sz[1]; }
                }
            }
            CabinetExpandPlugin.Log.LogInfo(
                $"[CabinetExpand][诊断] 容器初始化 bagConfigId={bagConfigId}（{name}，当前 {w}x{h}，初始包={__instance.GetInitialBagConfigId()}）");
        }
        catch (Exception e)
        {
            CabinetExpandPlugin.Log.LogWarning("[CabinetExpand][诊断] OnInit 读取失败: " + e.Message);
        }
    }
}

internal sealed class CabinetTicker : MonoBehaviour
{
    public CabinetTicker(IntPtr ptr) : base(ptr) { }
    public CabinetTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 持续轮询幂等改写（同 BackpackExpand：防读档/切场景配置表重载覆盖）
        _check += Time.deltaTime;
        var due = CabinetExpandPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var fridgeNotes = new List<string>();
        var (cabinets, fridges) = CabinetExpandPlugin.DiscoverTargets(fridgeNotes);
        if (cabinets == null) { CabinetExpandPlugin.Rerun = true; return; }

        var listAll = CabinetExpandPlugin.ListOnReady.Value && !_announced;
        var lines = new List<string>();
        if (fridgeNotes.Count > 0 && listAll)
            lines.Add("冰箱组（全冷链电器包，自动发现，含未来新冰箱）：" + string.Join("、", fridgeNotes));

        // 宽度封顶分流（2026-10-16 用户拍板"材料架与其他界面同宽"）：投影宽度超 MaxCols 的包
        // 不进共享 RewriteSize（否则每周期 23↔30 来回改写抖动刷日志），走本地封顶改写。
        // 默认 23 = 超大型置物架同宽——全部柜子页签宽度统一，界面缩放一档、切柜子不跳尺寸。
        int cap = CabinetExpandPlugin.MaxCols.Value;
        var (cabinetsNormal, cabinetsCap) = CabinetExpandPlugin.PartitionByCap(cabinets, CabinetExpandPlugin.WidthMult.Value, cap);
        var (fridgesNormal, fridgesCap) = CabinetExpandPlugin.PartitionByCap(fridges, CabinetExpandPlugin.FridgeWidthMult.Value, cap);

        var (scanned1, changed1, lines1) = BagTable.RewriteSize(
            cabinetsNormal, CabinetExpandPlugin.WidthMult.Value, CabinetExpandPlugin.HeightMult.Value,
            CabinetExpandPlugin.Verbose.Value || listAll);
        if (scanned1 < 0) { CabinetExpandPlugin.Rerun = true; return; }
        lines.AddRange(lines1);

        var (scanned2, changed2, lines2) = BagTable.RewriteSize(
            fridgesNormal, CabinetExpandPlugin.FridgeWidthMult.Value, CabinetExpandPlugin.FridgeHeightMult.Value,
            CabinetExpandPlugin.Verbose.Value || listAll);
        // scanned2 < 0 只在配置表整体未就绪时发生，此时 scanned1 也会 <0，上面已返回
        lines.AddRange(lines2);

        if (fridgesCap.Count > 0)
            lines.AddRange(CabinetExpandPlugin.CappedRewrite(fridgesCap,
                CabinetExpandPlugin.FridgeWidthMult.Value, CabinetExpandPlugin.FridgeHeightMult.Value, cap));
        if (cabinetsCap.Count > 0)
            lines.AddRange(CabinetExpandPlugin.CappedRewrite(cabinetsCap,
                CabinetExpandPlugin.WidthMult.Value, CabinetExpandPlugin.HeightMult.Value, cap));

        CabinetExpandPlugin.Rerun = false;
        CabinetExpandPlugin.UpdateManagedMax(cabinets, fridges);

        if (lines.Count > 0)
            foreach (var l in lines) CabinetExpandPlugin.Log.LogInfo("[CabinetExpand] " + l);

        if (!_announced && scanned1 >= 0)
        {
            _announced = true;
            CabinetExpandPlugin.Log.LogInfo(
                $"[CabinetExpand] 生效：{cabinetsNormal.Count + cabinetsCap.Count} 个柜子包（宽×{CabinetExpandPlugin.WidthMult.Value} 高×{CabinetExpandPlugin.HeightMult.Value}" +
                (cabinetsCap.Count > 0 ? $"，{cabinetsCap.Count} 包宽度封顶≤{cap}" : "") + "）+ " +
                $"{fridges.Count} 个冰箱包（宽×{CabinetExpandPlugin.FridgeWidthMult.Value} 高×{CabinetExpandPlugin.FridgeHeightMult.Value}" +
                (fridgesCap.Count > 0 ? $"，{fridgesCap.Count} 包宽度封顶≤{cap}" : "") + "）；" +
                "名单=家具表自动发现+黑名单，新增柜子/冰箱自动跟上。");
        }
    }
}
