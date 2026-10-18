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
///
/// 第三组"载具"（2026-10-18 用户拍板"只扩载具和家里的购物袋，宽高各×1.5"）：
/// 载具后备箱 = Config_Vehicle(档位).TrunkBagId → Config_Bag（自行车站/轿车/SUV，
/// VehicleShop 换挡，包 ID 运行时枚举自动跟上新车）；家门口塑料袋（购物袋）= 专用包
/// VehicleConfig.DoorBoxBagId=1023（家具 1024 表面默认 2003 是纸箱/睡袋等场景共用包，
/// 门口袋系统用自己的 1023，无家具引用故家具自动发现天然扫不到）。数量上限
/// DoorBoxMaxCount=2 深度固化（存档 schema 就是 DoorBoxItems/DoorBoxItems2 两个字段），
/// 不加袋子数，扩格子即达成"每个袋子装更多"。溢出行为实证：到家卸货 TryMoveBagAll
/// 能装多少装多少，装不下的留在后备箱不丢失。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetexpand";
    public const string Name = "CabinetExpand";
    // 跟随套件 version.json，发版时同步（版本号由作者定义）
    public const string Version = "1.3.7";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> WidthMult;
    internal static ConfigEntry<float> HeightMult;
    internal static ConfigEntry<float> FridgeWidthMult;
    internal static ConfigEntry<float> FridgeHeightMult;
    internal static ConfigEntry<float> VehicleWidthMult;
    internal static ConfigEntry<float> VehicleHeightMult;
    internal static ConfigEntry<int> MaxCols;
    internal static ConfigEntry<string> ExtraIds;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> DiagOnOpen;

    internal static volatile bool Rerun = true;

    /// <summary>家门口塑料袋专用包（VehicleConfig.DoorBoxBagId 常量的本 mod 镜像，2026-10-18 反汇编实证）。</summary>
    internal const int DoorBoxBagId = 1023;

    /// <summary>载具组当前受管包快照（门口袋初始化诊断用：核对实际 bagConfigId 是否在管）。</summary>
    internal static volatile System.Collections.Generic.HashSet<int> ManagedVehicleBags;

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
        VehicleWidthMult = Config.Bind("Expand", "VehicleWidthMult", 1.5f,
            "载具与门口购物袋宽度倍数（四舍五入取整）。名单=Config_Vehicle 全部后备箱包(自动跟上新车)+门口塑料袋专用包 1023。1 = 还原原生。");
        VehicleHeightMult = Config.Bind("Expand", "VehicleHeightMult", 1.5f,
            "载具与门口购物袋高度倍数（四舍五入取整）。1 = 还原原生。");
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
        VehicleWidthMult.SettingChanged += reload;
        VehicleHeightMult.SettingChanged += reload;
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
                    $"载具与购物袋宽×{VehicleWidthMult.Value} 高×{VehicleHeightMult.Value}；" +
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

    /// <summary>
    /// 载具组发现（2026-10-18）：枚举 Config_Vehicle 全部行取 TrunkBagId（档位→包，
    /// 新车自动跟上）+ 门口塑料袋专用包 1023（VehicleConfig.DoorBoxBagId 常量镜像）。
    /// 这两个来源都没有家具引用，与家具自动发现天然不相交；仍防御性从柜子/冰箱组
    /// 摘除撞号包（载具语义优先）并记日志。ExcludeIds 同样对载具组生效。
    /// tableReady=Config_Vehicle 表是否已加载（主菜单阶段为 false——车表随对局世界
    /// 加载，届时由 Ticker 补录名单；1023 种子不受影响，恒生效）。
    /// </summary>
    internal static HashSet<int> DiscoverVehicles(
        HashSet<int> cabinets, HashSet<int> fridges, List<string> notes, out bool tableReady)
    {
        var vehicles = new HashSet<int>();
        var cm = BagTable.Cm();
        tableReady = false;

        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Vehicle> vdict = null;
        try { vdict = cm == null ? null : cm._Config_Vehicle_Dict; } catch { }
        if (vdict != null && vdict.Count > 0)
        {
            tableReady = true;
            foreach (var pair in vdict)
            {
                var v = pair.Value;
                if (v == null) continue;
                int trunkId = 0;
                try { trunkId = v.TrunkBagId; } catch { continue; }
                if (trunkId <= 0) continue;
                if (vehicles.Add(trunkId))
                    notes?.Add($"{pair.Key}({VehName(v)})→包{trunkId}({BagName(trunkId)})");
            }
        }

        if (vehicles.Add(DoorBoxBagId))
            notes?.Add($"门口塑料袋→包{DoorBoxBagId}({BagName(DoorBoxBagId)})");

        foreach (var id in BagTable.ParseIds(ExcludeIds.Value)) vehicles.Remove(id);

        // 撞号防御：包同时被家具发现收录时归载具组（当前名单实证无撞号，纯保险）
        foreach (var id in vehicles)
        {
            if (cabinets.Remove(id) || fridges.Remove(id))
                notes?.Add($"包{id} 同时命中家具发现，已归载具组");
        }
        return vehicles;
    }

    private static string VehName(GameCore.HotUpdate.Config_Vehicle v)
    {
        try { return v.Name_Local ?? v.Name ?? "?"; }
        catch { return "?"; }
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
    // 门口袋/载具实证钉子（always-on，不受 DiagOnOpen 控制，每包只打一次）：
    // 受管载具包的实例初始化必打（=扩容在真实容器上落地）；2003（纸箱共包，门口袋
    // 若被版本改绑的候选）也提示一次——门口袋显示未扩容时据回报定夺（IsDoorBox 标志
    // 在 BagFurniture 非 BagComponent，跨类取不可靠，弃用标志改按包打点）。
    private static readonly System.Collections.Generic.HashSet<int> Watched = new();
    internal static readonly int DoorBoxAltId = 2003; // 家具 1024 表面默认包（场景共包，不扩）

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.BagComponent), "OnInit")]
    private static void Postfix(GameCore.HotUpdate.Battle.Logic.BagComponent __instance, int bagConfigId)
    {
        try
        {
            var managed = CabinetExpandPlugin.ManagedVehicleBags;
            if (managed != null && managed.Contains(bagConfigId) && Watched.Add(bagConfigId))
                CabinetExpandPlugin.Log.LogInfo(
                    $"[CabinetExpand][载具] 包 {bagConfigId}（{BagNameSafe(bagConfigId)}）实例初始化，扩容已生效");
            else if (bagConfigId == DoorBoxAltId && Watched.Add(bagConfigId))
                CabinetExpandPlugin.Log.LogInfo(
                    $"[CabinetExpand][门口袋核对] 检测到共包 {DoorBoxAltId}（{BagNameSafe(DoorBoxAltId)}，纸箱/睡袋等场景共用）实例初始化。" +
                    "门口购物袋若未扩容而其他柜子正常，说明该版本门口袋改绑此包，请回报本行。");
        }
        catch (Exception e)
        {
            CabinetExpandPlugin.Log.LogWarning("[CabinetExpand][载具] 诊断读取失败: " + e.Message);
        }

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

    private static string BagNameSafe(int bagConfigId)
    {
        try
        {
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict != null && dict.TryGetValue(bagConfigId, out var bag) && bag != null)
                return BagTable.Name(bag);
        }
        catch { }
        return "?";
    }
}

internal sealed class CabinetTicker : MonoBehaviour
{
    public CabinetTicker(IntPtr ptr) : base(ptr) { }
    public CabinetTicker() { }

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

        var vehicleNotes = new List<string>();
        bool vehTableReady;
        var vehicles = CabinetExpandPlugin.DiscoverVehicles(cabinets, fridges, vehicleNotes, out vehTableReady)
                       ?? new HashSet<int>();
        CabinetExpandPlugin.ManagedVehicleBags = vehicles;

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

        // 载具组：与柜子/冰箱同分流（宽度封顶保护背包双栏视图，超宽的包本地封顶改写）
        var (vehiclesNormal, vehiclesCap) = CabinetExpandPlugin.PartitionByCap(vehicles, CabinetExpandPlugin.VehicleWidthMult.Value, cap);
        var (scanned3, _, lines3) = BagTable.RewriteSize(
            vehiclesNormal, CabinetExpandPlugin.VehicleWidthMult.Value, CabinetExpandPlugin.VehicleHeightMult.Value,
            CabinetExpandPlugin.Verbose.Value || listAll);
        if (scanned3 < 0) { CabinetExpandPlugin.Rerun = true; return; }
        lines.AddRange(lines3);
        if (vehiclesCap.Count > 0)
            lines.AddRange(CabinetExpandPlugin.CappedRewrite(vehiclesCap,
                CabinetExpandPlugin.VehicleWidthMult.Value, CabinetExpandPlugin.VehicleHeightMult.Value, cap));

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
                (fridgesCap.Count > 0 ? $"，{fridgesCap.Count} 包宽度封顶≤{cap}" : "") + "）+ " +
                $"{vehicles.Count} 个载具包（宽×{CabinetExpandPlugin.VehicleWidthMult.Value} 高×{CabinetExpandPlugin.VehicleHeightMult.Value}" +
                (vehiclesCap.Count > 0 ? $"，{vehiclesCap.Count} 包宽度封顶≤{cap}" : "") + "）；" +
                "名单=家具表自动发现+黑名单，新增柜子/冰箱/车辆自动跟上。");
        }

        // 车表随对局世界加载（主菜单阶段未就绪）：就绪首轮补录完整载具名单（1023 种子
        // 已在生效播报里，此后新增的后备箱包改写时自带 BAG 明细行）
        if (vehTableReady && !_vehListed)
        {
            _vehListed = true;
            CabinetExpandPlugin.Log.LogInfo(
                "[CabinetExpand] 载具组补录（Config_Vehicle 全档后备箱+门口塑料袋 1023，新车自动跟上）：" +
                string.Join("、", vehicleNotes));
        }
    }

    private float _check;
    private bool _announced;
    private bool _vehListed;
}
