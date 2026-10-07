using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace SurvivalLog.StackLimit999;

/// <summary>
/// 堆叠上限 + 负重上限。
/// 核心手段是按 il2cpp 字段偏移直接改写配置数据（Config_Item.StackLimit / Config_Bag.Burden）。
/// v2.7.1 起不再挂 get/set_StackLimit 属性补丁：游戏启动时会用非内联 getter 全表扫描，
/// 补丁返回 999 会污染派生数据并破坏上下楼寻路（E1 实验实锤：字段零写入也能复现卡楼梯）。
///
/// 合并/拆分全部走游戏自身逻辑：
///   1) 点「一键整理」→ SORT_BACKPACK 登记主线程合并；SORT_BAG（容器整理）**拦截原版**
///      改由本插件安全合并+重排（原生管线碰到克隆/置零物品会闪退，2026-09-11 实测）；
///   2) 拖拽相同物品到同格 → DRAG_ITEM 登记（原版移动照常）→ 主线程把两堆合一；
///   3) 拆分：选中物品按拆分键 → 原生 API（SplitItem / TryPreSplitForCooking），
///      v2.8.0 起绝不字段克隆——克隆物品会让原生 RemoveItem/整理/配方校验闪退或失效。
///   所有合并/拆分完成后通过 LogicAdapter 增量同步（AddOrNotifyItem/DelItem）。
/// HandleCallback 回调内一律不改数据（会把游戏冻死，v2.0 实测教训）；无后台物品扫描。
/// </summary>
internal static class StackPatches
{
    internal static ManualLogSource Log;

    // itemId -> 原版上限（第一次见到时记录，用于 OnlyStackable 模式与“只升不降”）
    private static readonly ConcurrentDictionary<int, int> OriginalLimits = new();

    // 背包里实际出现过的物品配置ID（v2.6 起堆叠改写只针对它们）。
    // v2.5 实测教训：游戏 9/9 更新后，全表 2877 条 Config_Item 一律改写会破坏寻路
    // （上下楼卡在楼梯口原地跑步）——表里混有楼梯/门等特殊条目，改不得。
    private static readonly HashSet<int> PresentConfigIds = new();
    private static readonly object PresentLock = new();

    private static HashSet<int> _excludedItems = new();
    private static HashSet<int> _excludedCategories = new();
    private static readonly object ListLock = new();

    // v2.9.19：多次使用（MaxUseTimes>1）的物品的配置ID。
    // 实证（反汇编 TryPreSplitForCooking @ 0x2A7B9A0）：原生烹饪预拆分读
    // Config_Item.StackLimit（偏移 0x5C），要求 ≤1 才会 SplitItem(源,1) 拆出 1 份进烹饪区；
    // >1 直接返回源自身 → 整实例（全部使用次数）进烹饪区被整份消耗。
    // 因此"多次使用"类物品的 StackLimit 保持官方值 1，不参与改写/合并，烹饪走 100% 官方逻辑。
    // 单次使用（MaxUseTimes<=1）的物品不受影响："1 份=1 次使用"，可堆叠路径按数量消耗即按使用消耗。
    private static readonly HashSet<int> UseItemConfigIds = new();

    // il2cpp 字段直读直写
    private static IntPtr _stackLimitFieldInfo = IntPtr.Zero;
    private static int _stackLimitFieldOffset = -1;
    private static IntPtr _bagBurdenFieldInfo = IntPtr.Zero;
    private static int _bagBurdenFieldOffset = -1;
    private static bool _sampleLogged;
    private static bool _bagSampleLogged;

    [DllImport("GameAssembly", EntryPoint = "il2cpp_field_get_offset")]
    private static extern uint il2cpp_field_get_offset(IntPtr field);

    internal static void Init(ManualLogSource log)
    {
        Log = log;
        RebuildBlacklists();
        StackConfig.SettingsChanged += RequestImmediateRewrite;
    }

    internal static void RebuildBlacklists()
    {
        var items = ParseIds(StackConfig.ExcludedItemIds.Value);
        var cats = ParseIds(StackConfig.ExcludedCategories.Value);
        lock (ListLock)
        {
            _excludedItems = items;
            _excludedCategories = cats;
        }
    }

    private static HashSet<int> ParseIds(string text)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text))
            return set;
        foreach (var part in text.Split(','))
        {
            if (int.TryParse(part.Trim(), out int v))
                set.Add(v);
        }
        return set;
    }

    private static bool HasBlacklist()
    {
        lock (ListLock)
        {
            return _excludedItems.Count > 0 || _excludedCategories.Count > 0;
        }
    }

    private static bool IsExcluded(Config_Item item)
    {
        lock (ListLock)
        {
            return _excludedItems.Contains(item.ID) || _excludedCategories.Contains(item.Category);
        }
    }

    private static void RecordOriginal(int itemId, int value)
    {
        OriginalLimits.TryAdd(itemId, value);
    }

    internal static int ComputeTarget(Config_Item item, int natural)
    {
        if (natural <= 0)
            return natural; // 0/负数按“不可堆叠/特殊”处理，保持原样
        if (HasBlacklist() && IsExcluded(item))
            return natural;
        if (StackConfig.Mode.Value == StackConfig.StackMode.OnlyStackable && natural <= 1)
            return natural;
        return Math.Max(StackConfig.StackLimit.Value, natural); // 只升不降
    }

    // ---------------- Harmony: 属性读写拦截 ----------------
    // v2.7.1 删除 get/set_StackLimit 补丁（E1 实验 2026-09-10 实锤）：
    // 游戏启动时（存档加载前）会通过非内联 getter 全表扫描 Config_Item（日志实测 t=7.1s
    // 逐个调用 cfg 1,2,3...8,2000,...），getter 补丁返回 999 会污染这份派生数据，
    // 直接导致上下楼寻路损坏（卡楼梯口原地跑步）。字段直写零写入的情况下也能复现，
    // 因此"即时层"补丁必须整体移除，只保留对现存物品配置的字段直写（权威层）。

    // ---------------- il2cpp 字段直读直写 ----------------

    internal static bool EnsureStackLimitFieldAccess()
    {
        if (_stackLimitFieldOffset >= 0)
            return true;
        try
        {
            var fi = typeof(Config_Item).GetField("NativeFieldInfoPtr__StackLimit_k__BackingField",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fi == null)
            {
                Log.LogWarning("[StackLimit999] interop 字段 NativeFieldInfoPtr__StackLimit_k__BackingField 不存在");
                return false;
            }
            _stackLimitFieldInfo = (IntPtr)fi.GetValue(null);
            if (_stackLimitFieldInfo == IntPtr.Zero)
                return false;
            _stackLimitFieldOffset = (int)il2cpp_field_get_offset(_stackLimitFieldInfo);
            Log.LogInfo($"[StackLimit999] 已获得 Config_Item.StackLimit 直读直写能力（偏移 {_stackLimitFieldOffset}）");
            return _stackLimitFieldOffset > 0;
        }
        catch (Exception e)
        {
            Log.LogWarning("[StackLimit999] 获取 StackLimit 字段偏移失败: " + e.Message);
            return false;
        }
    }

    private static bool EnsureBagBurdenFieldAccess()
    {
        if (_bagBurdenFieldOffset >= 0)
            return true;
        try
        {
            var fi = typeof(Config_Bag).GetField("NativeFieldInfoPtr__Burden_k__BackingField",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fi == null)
            {
                Log.LogWarning("[StackLimit999] interop 字段 NativeFieldInfoPtr__Burden_k__BackingField 不存在");
                return false;
            }
            _bagBurdenFieldInfo = (IntPtr)fi.GetValue(null);
            if (_bagBurdenFieldInfo == IntPtr.Zero)
                return false;
            _bagBurdenFieldOffset = (int)il2cpp_field_get_offset(_bagBurdenFieldInfo);
            Log.LogInfo($"[StackLimit999] 已获得 Config_Bag.Burden 直读直写能力（偏移 {_bagBurdenFieldOffset}）");
            return _bagBurdenFieldOffset > 0;
        }
        catch (Exception e)
        {
            Log.LogWarning("[StackLimit999] 获取 Burden 字段偏移失败: " + e.Message);
            return false;
        }
    }

    // ---------------- 数据改写（权威层，仅改配置表数据，不扫物品） ----------------

    internal static volatile bool RequestRewrite;

    private static void RequestImmediateRewrite() => RequestRewrite = true;

    internal static int RewriteAllData(out int total)
    {
        total = -1;
        if (!BaseSingleton<ConfigManager>.IsInstanceCreated)
            return 0;

        var cm = BaseSingleton<ConfigManager>.Instance;
        if (cm == null)
            return 0;

        Il2CppSystem.Collections.Generic.Dictionary<int, Config_Item> dict;
        try
        {
            dict = cm.Get_Config_Item_All(); // ConfigManager 未就绪时会抛 ArgumentNullException，静默等下一轮
        }
        catch
        {
            return 0;
        }
        if (dict == null || dict.Count == 0)
            return 0;

        if (!EnsureStackLimitFieldAccess())
        {
            total = -2;
            return 0;
        }

        // v2.6：只改写背包里实际出现过的物品的配置，其余 ~2800 条（含楼梯/门等特殊条目）一律不碰
        CollectPresentConfigIds();

        int[] present;
        int[] useIds;
        lock (PresentLock)
        {
            if (PresentConfigIds.Count == 0)
            {
                total = 0; // 世界还没加载，没有任何背包物品
                return 0;
            }
            present = new int[PresentConfigIds.Count];
            PresentConfigIds.CopyTo(present);
            useIds = new int[UseItemConfigIds.Count];
            UseItemConfigIds.CopyTo(useIds);
        }
        var useIdSet = new HashSet<int>(useIds);

        int changed = 0;
        int scanned = 0;
        foreach (var cfgId in present)
        {
            // v2.9.19：有使用次数的物品不改写（保持官方 StackLimit=1，烹饪预拆分依赖它）
            if (useIdSet.Contains(cfgId))
                continue;
            Config_Item item;
            try
            {
                if (!dict.TryGetValue(cfgId, out item) || item == null)
                    continue;
            }
            catch
            {
                continue;
            }
            scanned++;
            IntPtr fieldAddr = new IntPtr(item.Pointer.ToInt64() + _stackLimitFieldOffset);
            int raw = Marshal.ReadInt32(fieldAddr);

            RecordOriginal(item.ID, raw);
            int target = ComputeTarget(item, raw);
            if (target != raw)
            {
                Marshal.WriteInt32(fieldAddr, target);
                changed++;
            }
        }

        total = scanned;
        return changed;
    }

    // ---------------- 单配置 StackLimit 直读直写（拆分原语 v2 用） ----------------

    internal static Config_Item GetConfigItem(int cfgId)
    {
        if (!BaseSingleton<ConfigManager>.IsInstanceCreated)
            return null;
        var cm = BaseSingleton<ConfigManager>.Instance;
        if (cm == null)
            return null;
        try
        {
            var dict = cm.Get_Config_Item_All();
            if (dict != null && dict.TryGetValue(cfgId, out var item))
                return item;
        }
        catch { }
        return null;
    }

    internal static int ReadStackLimitRaw(Config_Item item)
        => Marshal.ReadInt32(new IntPtr(item.Pointer.ToInt64() + _stackLimitFieldOffset));

    internal static void WriteStackLimitRaw(Config_Item item, int v)
        => Marshal.WriteInt32(new IntPtr(item.Pointer.ToInt64() + _stackLimitFieldOffset), v);

    /// <summary>收集所有容器（玩家背包/柜子/邻居家）里实际存在的物品配置ID。</summary>
    private static void CollectPresentConfigIds()
    {
        try
        {
            if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated)
                return;
            var world = BaseSingleton<BattleLogicWorld>.Instance;
            var im = world == null ? null : world._ItemManager;
            var oc = im?.OwnerCache;
            if (oc == null)
                return;

            lock (PresentLock)
            {
                foreach (var kv in oc)
                {
                    var list = im.GetItemDataList(kv.Key);
                    if (list == null)
                        continue;
                    foreach (var it in list)
                    {
                        if (it == null || it.InstanceId <= 0)
                            continue;
                        int cfgId = it.ItemConfigId;
                        if (cfgId > 0)
                        {
                            PresentConfigIds.Add(cfgId);
                            // v2.9.20：只记录"多次使用"的物品（上限 MaxUseTimes>1，如野兔 3 次）。
                            // 单次使用的物品（上限=1，如梳打饼干）不受影响："1 份=1 次使用"，
                            // 可堆叠路径按数量消耗即按使用消耗，烹饪行为正确。
                            // 判定看上限 MaxUseTimes（总次数），不看剩余 UseTimes——
                            // 多次使用物品哪怕只剩 1 次也仍是多次使用类。
                            if (it.MaxUseTimes > 1)
                                UseItemConfigIds.Add(cfgId);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (StackConfig.DebugLogging.Value)
                Log.LogWarning("[StackLimit999] 收集背包物品失败: " + e.Message);
        }
    }

    // 每个背包配置的原始 Burden（倍数改写的幂等锚点：周期改写不能反复放大）
    private static readonly ConcurrentDictionary<int, int> OriginalBurden = new();

    internal static int RewriteBagBurden(out int total)
    {
        total = -1;
        // 倍数方案（v2.7.7）：原版 Burden × BurdenMultiplier。
        // 寻路 bug 已实锤与数值无关（元凶是 v2.5 时代挂着的 get_StackLimit 属性补丁，
        // v2.7.1 删除后彻底修复），大数值解禁。
        int mult = StackConfig.BurdenMultiplier.Value;
        if (mult <= 1)
        {
            total = 0;
            return 0;
        }
        if (!BaseSingleton<ConfigManager>.IsInstanceCreated)
            return 0;

        var cm = BaseSingleton<ConfigManager>.Instance;
        if (cm == null)
            return 0;

        var dict = cm._Config_Bag_Dict;
        if (dict == null || dict.Count == 0)
            return 0;

        if (!EnsureBagBurdenFieldAccess())
            return 0;

        int changed = 0;
        int scanned = 0;
        foreach (var pair in dict)
        {
            var bag = pair.Value;
            if (bag == null)
                continue;
            scanned++;
            IntPtr fieldAddr = new IntPtr(bag.Pointer.ToInt64() + _bagBurdenFieldOffset);
            int raw = Marshal.ReadInt32(fieldAddr);

            // 第一次见到该配置时记录原始值；之后无论改写多少轮都按 原始值×倍数 计算
            OriginalBurden.TryAdd(pair.Key, raw);
            if (!OriginalBurden.TryGetValue(pair.Key, out int original))
                continue;

            if (!_bagSampleLogged)
            {
                _bagSampleLogged = true;
                Log.LogInfo($"[StackLimit999] 抽样: 背包配置 {pair.Key} 原始 Burden={original} → {original * mult}");
            }

            // 只放大 Burden>0 的配置；Burden<=0 的特殊配置保持原样
            // （v1.4.0 把全部 73 个配置都改掉会导致背包 UI 打不开，实测教训）
            if (original > 0)
            {
                int target = original * mult;
                if (raw != target)
                {
                    Marshal.WriteInt32(fieldAddr, target);
                    changed++;
                }
            }
        }

        total = scanned;
        return changed;
    }
}

/// <summary>
/// 物品状态兼容性判断（v2.9.17 语义）：
/// 1) 配置/污染/预设/燃烧值相同；
/// 2) 有使用次数的物品（书/耐久食物）：只允许"未使用"（满次数）之间合并（用户确认保留）；
/// 3) 个体属性（InstanceVD）：内容完全一致才合并——两瓶一样的烈性酒精可以并，
///    完美料理和普通料理不会并；
/// 4) 保质期：保鲜物品（TimeLeft>0）的 buff 到期时间（InstanceEffectEnd）随新鲜度变化，
///    同种食物"保质期不一样"不再阻断合并——合并后以新鲜（TimeLeft 大）的那堆为准；
///    非保鲜物品（TimeLeft<=0）的 buff 数据仍要求完全一致。
/// 拆分另有 CanSplit：带个体数据的不拆（克隆共享数组指针，使用时会互相污染）。
/// </summary>
internal static class MergeRules
{
    private static bool HasData(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> arr)
        => arr != null && arr.Length > 0;

    /// <summary>基本活性过滤：死条目（InstanceId/cfg/count 非正）不参与任何操作。</summary>
    internal static bool IsMergeEligible(ItemData it)
    {
        if (it == null)
            return false;
        if (it.InstanceId <= 0 || it.ItemConfigId <= 0 || it.ItemCount <= 0)
            return false;
        return true;
    }

    /// <summary>拆分资格：带个体属性/buff 数据的不拆——字段克隆会共享同一份数组指针。</summary>
    internal static bool CanSplit(ItemData it)
        => IsMergeEligible(it) && !HasData(it.InstanceVD) && !HasData(it.InstanceEffectEnd);

    /// <summary>两个 float 数组内容是否完全一致（双方都空视为一致）。</summary>
    internal static bool SameArray(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> a,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> b)
    {
        bool aEmpty = !HasData(a), bEmpty = !HasData(b);
        if (aEmpty || bEmpty)
            return aEmpty && bEmpty; // 一空一非空 → 不同
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    /// <summary>属性指纹（VD+EffectEnd 内容），整理合并按它分组：属性相同的才会归到一组。
    /// v2.9.17：保鲜物品（TimeLeft>0）的 EffectEnd 就是到期时间、随新鲜度变化，
    /// 不参与分组（否则"保质期不一样"的同种食物永远分不到同组）；VD 仍参与
    /// （完美料理 vs 普通料理分开）。非保鲜物品维持 VD+EffectEnd 全量指纹。</summary>
    internal static string AttrFingerprint(ItemData it)
        => it.TimeLeft > 0
            ? "P|" + Fp(it.InstanceVD)
            : Fp(it.InstanceVD) + "|" + Fp(it.InstanceEffectEnd);

    private static string Fp(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> arr)
    {
        if (!HasData(arr))
            return "";
        var sb = new System.Text.StringBuilder(arr.Length * 12);
        foreach (var v in arr)
            sb.Append(v.ToString("R")).Append(',');
        return sb.ToString();
    }

    /// <summary>返回不可合并的原因；null = 可以合并。日志与排查用。</summary>
    internal static string MergeBlockReason(ItemData a, ItemData b)
    {
        if (a == null || b == null)
            return "空物品";
        if (a.ItemConfigId != b.ItemConfigId)
            return "配置不同";
        if (a.Polluted != b.Polluted)
            return "污染状态不同";
        // v2.9.20：多次使用的物品（上限 MaxUseTimes>1，如野兔 3 次）不参与合并——官方烹饪
        // 预拆分/消耗按"1 实例 = 1 份"处理（TryPreSplitForCooking 要求其配置 StackLimit=1），
        // 合并出的多份实例会破坏这条官方路径（整份被一次性消耗的实证）。
        // 单次使用（上限=1）的物品照常合并堆叠；判定看上限，不看剩余次数。
        if (a.MaxUseTimes > 1 || b.MaxUseTimes > 1)
            return "多次使用物品不合并（烹饪按官方单份逻辑处理）";
        // v2.7.9：不再比较 IsMapPreset（物品来源标记：地图预置/商店/玩家获得）。
        // 同种物品跨来源合并是用户的明确诉求（烈性酒/罐头被它挡住，实测日志实锤）；
        // 合并后保留目标堆的标记，语义无害。
        if (a.InstanceBurnValue != b.InstanceBurnValue)
            return "燃烧值不同";
        // v2.9.17 修正：使用次数非满不可合并的规则保留（用户确认正确，之前误删）。
        if (a.MaxUseTimes > 0 && (a.UseTimes != a.MaxUseTimes || b.UseTimes != b.MaxUseTimes))
            return $"使用次数非满(u{a.UseTimes}/{a.MaxUseTimes} vs u{b.UseTimes}/{b.MaxUseTimes})";
        if (!SameArray(a.InstanceVD, b.InstanceVD))
            return "个体属性不同";
        if (!SameArray(a.InstanceEffectEnd, b.InstanceEffectEnd))
        {
            // v2.9.17 review 确认与旧规则不冲突，理由：
            // 1) v2.7.8 引入 EffectEnd 内容比较是为了两个目的——修复 v2.7.6"非空一律拒绝"
            //    对鲱鱼罐头/烈性酒精的误伤（内容相同应可并）、以及把完美料理和普通料理分开。
            // 2) EffectEnd 只存"到期时间点"（float），编码不了增益强度；完美/普通的品质
            //    差异在 InstanceVD（上一条判断，保持不变），所以本条放宽不影响它们。
            // 3) 鲱鱼罐头/烈性酒精没有保质期（TimeLeft<=0），仍走"内容完全一致"的老规则。
            // 本条只对保鲜物品（TimeLeft>0）放宽：到期时间随新鲜度变化，"保质期不一样"
            // 不再阻断合并，合并后以新鲜（TimeLeft 大）的那堆为准（MergePair 拷贝到期数组）。
            // 边界：一空一非空也允许并（罕见；结果堆保留自己的数组，长度不等时不拷贝）。
            if (a.TimeLeft <= 0 || b.TimeLeft <= 0)
                return "buff数据不同";
        }
        if (!IsMergeEligible(a) || !IsMergeEligible(b))
            return "死条目";
        return null;
    }

    internal static bool CanMerge(ItemData a, ItemData b) => MergeBlockReason(a, b) == null;
}

/// <summary>
/// 负重上限（v2.7.7 倍数方案）：Config_Bag.Burden 字段直写 = 原始值 × BurdenMultiplier（默认100）。
/// 不再挂 BagComponent.GetMaxBurden 等属性/方法补丁：
/// 1) 这些方法读的就是 Config_Bag.Burden，字段直写后自动生效（含角色加成计算）；
/// 2) 倍数方案下 postfix 与字段直写会把彼此的结果再乘一次，反复放大；
/// 3) 属性钩子的教训见 v2.7.1（get_StackLimit 补丁曾破坏寻路）——能不挂就不挂。
/// </summary>

/// <summary>
/// 背包事件挂载点（链路探针实测验证的真实分发路径）：
///   拖拽结束 → WebUIVm.HandleCallback("DRAG_ITEM", json)
///   点「一键整理」 → WebUIVm.HandleCallback("SORT_BACKPACK", json)
///   点击选中物品 → WebUIVm.HandleCallback("CLICK_ITEM", json)
///
/// v2.1 实测教训：在 HandleCallback 回调里直接改物品数据（SetItemCount/RemoveItem）
/// 会把游戏冻死（重入其 UI 同步链路）。因此这里只登记请求，立即放行原版逻辑；
/// 主线程 ticker 下一帧再执行合并，并用游戏自己的 LogicAdapter.OnRefreshAllBagViews()
/// 刷新界面（与“从柜子取东西/捡东西”同一条原生刷新路径）。
/// </summary>
internal static class BagEventPatches
{
    internal static ItemManager IM
    {
        get
        {
            if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated)
                return null;
            var world = BaseSingleton<BattleLogicWorld>.Instance;
            return world == null ? null : world._ItemManager;
        }
    }

    private static long ExtractLong(string json, string field)
    {
        if (string.IsNullOrEmpty(json))
            return 0;
        var m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*(-?\\d+)");
        return m.Success ? long.Parse(m.Groups[1].Value) : 0;
    }

    // ---------------- 选中物品追踪（拆分用） ----------------

    private static long _lastClickedItemId;
    private static string _lastClickJson = "(无)";

    // v2.9.21：无人机交易界面（TradeUI）的 CLICK_ITEM 用 "itemKey":"123"（带引号的字符串），
    // 其余界面用 "itemId":123。正则同时兼容两种字段名 + 可选引号，否则交易界面里
    // 解析不到物品 ID，Shift+左键拆分失效、放行原版"Shift+点击 快速转移"。
    private static readonly Regex ItemIdFieldRegex = new(
        "\"(?:itemId|instanceId|instId|srcItemId|itemID|itemKey)\"\\s*:\\s*\"?(-?\\d+)\"?",
        RegexOptions.Compiled);

    private static readonly string[] LoggedEventHints =
    {
        "ITEM", "CLICK", "SELECT", "HOVER", "DRAG", "TRANSFER", "USE", "DROP", "SPLIT", "SORT"
    };

    /// <summary>调试：dump 某容器全部活条目（cfg x count @pos #instanceId）。</summary>
    internal static void DumpOwnerItems(ItemManager im, long ownerId, string reason)
    {
        if (!StackConfig.DebugLogging.Value)
            return;
        var list = im.GetItemDataList(ownerId);
        if (list == null)
            return;
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var it in list)
        {
            if (it == null || it.InstanceId <= 0 || it.ItemCount <= 0)
                continue;
            var p = it.BagPos;
            sb.Append($"cfg{it.ItemConfigId}x{it.ItemCount}[u{it.UseTimes}/{it.MaxUseTimes}]@({p.x},{p.y})#{it.InstanceId % 1000} ");
            if (++n >= 60)
                break;
        }
        StackPatches.Log.LogInfo($"[StackLimit999][容器{ownerId}] ({reason}) {n}项: {sb}");
    }

    private static bool IsWatchedEvent(string ev)
    {
        foreach (var hint in LoggedEventHints)
        {
            if (ev.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // ---------------- 待处理合并（HandleCallback 登记，主线程延迟执行） ----------------

    // 一键整理后延迟合并。v2.0 实测：HandleCallback 回调内改数据会冻死（JS 桥重入），
    // 所以合并必须等回调返回后在主线程 ticker 执行；原版整理的数据层操作在回调栈内
    // 同步完成，下一帧即可安全合并。v2.7.6 从 3000ms 收紧到 300ms（无感知等待），
    // UI 若被整理动画的最终状态推送覆盖，由 +3s/+6.5s 两轮全量补推兜底。
    private const int SortMergeDelayMs = 300;
    private static long _pendingSortMergeDueMs;
    private static bool _pendingSortTidy; // SORT_BAG（容器整理）被拦截时，位置重排也由我们做
    private static (long srcOwnerId, long dstOwnerId, long itemId, long targetId, int cellX, int cellY) _pendingDragMerge;

    // ---------------- 手动拆分（Shift+左键点击物品）----------------
    // v2.9.6：Shift+左键 = 从被点击的堆拆出 SplitCount 件（拦截原版 Shift 快速转移；
    // 右键转移保留原版）。背包、柜子、工作台制作页均可。拆分动作登记后在 ticker
    // 主线程执行（HandleCallback 回调内改数据会冻死，实测教训）。

    private static long _pendingSplitItemId;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsShiftHeld() => (GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT（左右 Shift 均有效）

    // 整理合并后的界面补推：数据层合并成功后 JS 界面可能仍显示旧状态
    // （实测 2026-09-10：13 组全部合并成功但界面不动，随便一拖才显示），
    // 在 +0s/+3s/+6.5s 各全量推送一次受影响容器。推送是幂等的 upsert/del，重复无害。
    private static readonly List<(long dueMs, long ownerId)> _pendingResyncs = new();

    // 已合并/待合并物品的防幽灵记录：JS 会为同一拖拽重发事件，
    // 对已删除物品再次执行合并会凭空刷数量（实测教训）。
    private static readonly ConcurrentDictionary<long, DateTime>
        RecentlyQueued = new ConcurrentDictionary<long, DateTime>();

    private static bool WasRecentlyQueued(long itemId)
    {
        if (RecentlyQueued.TryGetValue(itemId, out var t))
        {
            if ((DateTime.UtcNow - t).TotalSeconds < 10)
                return true;
            RecentlyQueued.TryRemove(itemId, out _);
        }
        return false;
    }

    // 返回 true = 原版 HandleCallback 照常执行
    [HarmonyPatch(typeof(GameCore.HotUpdate.ReduxUI.WebUIVm), "HandleCallback")]
    [HarmonyPrefix]
    public static bool HandleCallbackPrefix(GameCore.HotUpdate.ReduxUI.WebUIVm __instance, string __0, string __1)
    {
        double nowMs = System.Environment.TickCount64;
        string ev = __0 ?? "";

        if (StackConfig.DebugLogging.Value && IsWatchedEvent(ev))
        {
            string json = __1 ?? "";
            if (json.Length > 360)
                json = json.Substring(0, 360) + "…";
            StackPatches.Log.LogInfo($"[StackLimit999][事件] {ev} json={json}");
        }

        // 新物品进背包 → 立刻补写它的堆叠上限（不等 15 秒扫描，v2.6 窄化改写后的补漏机制）
        if (ev.IndexOf("PICKUP", StringComparison.OrdinalIgnoreCase) >= 0)
            StackPatches.RequestRewrite = true;


        if (ev == "SORT_BACKPACK")
        {
            // 游戏一次点击会连续触发多次事件，做 1 秒防抖
            if ((nowMs - _lastSortMs) < 1000)
                return true;
            _lastSortMs = nowMs;

            if (StackConfig.MergeOnSort.Value)
            {
                if (_pendingSortMergeDueMs == 0)
                {
                    _pendingSortMergeDueMs = System.Environment.TickCount64 + SortMergeDelayMs;
                    StackPatches.Log.LogInfo($"[StackLimit999][事件] SORT_BACKPACK 登记合并（延迟 {SortMergeDelayMs}ms 避开整理流程）");
                }
            }
            return true;
        }

        if (ev == "SORT_BAG")
        {
            // v2.9.28：无人机交易界面 = 官方行为区。该界面对外部数据操作完全不友好
            // （实测：位置变化不感知 → 自研重排会造成物品重叠渲染），整理放行原版。
            // 原版排序对该界面是官方实现，天然正确。
            if (IsTradeUiOpen())
            {
                if (StackConfig.DebugLogging.Value)
                    StackPatches.Log.LogInfo("[StackLimit999][事件] SORT_BAG 来自交易界面 → 放行原版排序");
                return true;
            }
            // v2.8.0【关键】拦截原生的容器整理（SORT_BAG）：原生管线会尝试合并同类堆并走
            // RemoveItem/移动销毁管线，碰到拆分克隆物品或置零残骸会直接闪退（2026-09-11
            // 一键整理闪退的实锤现场）。背包整理 SORT_BACKPACK 不受影响（原生安全），
            // 容器整理改由本插件 300ms 后做安全合并 + 重排（SetItemCount 置零 + DelItem，
            // 绝不触碰原生销毁管线）。
            if ((nowMs - _lastSortMs) < 1000)
                return false;
            _lastSortMs = nowMs;

            if (_pendingSortMergeDueMs == 0)
            {
                _pendingSortMergeDueMs = System.Environment.TickCount64 + SortMergeDelayMs;
                _pendingSortTidy = true; // 原版被拦截，位置重排也由我们做
                StackPatches.Log.LogInfo($"[StackLimit999][事件] SORT_BAG 已拦截，登记安全合并+重排（延迟 {SortMergeDelayMs}ms）");
            }
            return false; // 不放行原版
        }

        // CLICK_ITEM（背包界面）、ITEM_CLICK（工作台制作页左栏物品，带 rightClick 字段）、
        // ACTION_ITEM_CLICK（保险起见一并覆盖）都携带 itemId。
        // Shift+左键 = 拆分该堆（拦下原版事件，替代原版 Shift 快速转移；
        // 右键转移、Alt 详情、Ctrl 拾取均不受影响）。不可拆的单件物品放行原版行为。
        if (ev == "CLICK_ITEM" || ev == "ITEM_CLICK" || ev == "ACTION_ITEM_CLICK")
        {
            long id = 0;
            var json = __1 ?? "";
            var m = ItemIdFieldRegex.Match(json);
            if (m.Success)
                id = long.Parse(m.Groups[1].Value);
            if (id > 0)
            {
                // 右键不拆分（json 字段 + 物理按键双保险；右键=原版快速转移）
                bool rightClick = json.Contains("\"rightClick\":true");
                bool rmbHeld = (GetAsyncKeyState(0x02) & 0x8000) != 0; // VK_RBUTTON
                // v2.9.28：无人机交易界面不拆分——该界面列表不收新条目（拆出的堆不可见），
                // 部分交易用官方数量选择器即可。整界面 = 官方行为区。
                bool tradeUiOpen = IsTradeUiOpen();
                if (StackConfig.SplitOnKey.Value
                    && _pendingSplitItemId == 0
                    && !rightClick
                    && !rmbHeld
                    && IsShiftHeld()
                    && !tradeUiOpen)
                {
                    // 纯读取判断可拆性（回调内禁止写数据，实测会冻死）
                    var im = IM;
                    var probe = im != null ? im.GetItemData(id) : null;
                    if (probe != null && probe.ItemCount > 1 && MergeRules.CanSplit(probe))
                    {
                        _pendingSplitItemId = id; // ticker 主线程执行拆分
                        if (StackConfig.DebugLogging.Value)
                            StackPatches.Log.LogInfo($"[StackLimit999][拆分] Shift+左键 登记：物品{id} x{probe.ItemCount}");
                        return false; // 拦下原版（Shift 快速转移被拆分替代）
                    }
                    // 单件/不可拆：放行原版 Shift 转移
                }

                // 烹饪区不做任何拦截（v2.9.19）：带使用次数的物品保持官方 StackLimit=1、
                // 不合并，烹饪预拆分/消耗/返还 100% 走官方逻辑（见 UseItemConfigIds 说明）。

                _lastClickedItemId = id;
                _lastClickJson = json.Length > 200 ? json.Substring(0, 200) + "…" : json;
                if (StackConfig.DebugLogging.Value)
                    StackPatches.Log.LogInfo($"[StackLimit999][选中] itemId={id}（{ev}）");
            }
            return true;
        }

        if (ev == "DRAG_ITEM" && StackConfig.DragMerge.Value)
        {
            if ((nowMs - _lastDragMs) < 250)
                return true;
            _lastDragMs = nowMs;

            try
            {
                string json = __1 ?? "";
                long itemId = ExtractLong(json, "itemId");
                long srcOwner = ExtractLong(json, "srcOwnerId");
                long dstOwner = ExtractLong(json, "dstOwnerId");
                int x = (int)ExtractLong(json, "x");
                int y = (int)ExtractLong(json, "y");

                if (itemId != 0 && dstOwner != 0
                    && _pendingDragMerge.itemId == 0
                    && !WasRecentlyQueued(itemId))
                {
                    if (srcOwner != 0)
                    {
                        // v2.9.17：跨容器拖拽合并——从背包拖到柜子里同种物品上（反之亦然）。
                        // 原版对"不同保质期"的同种物品会拒绝并堆，这里由插件以"保质期大的为准"合并。
                        var target = FindMergeTargetAtCell(dstOwner, itemId, new Vector2Int(x, y), srcOwner);
                        if (target != null)
                        {
                            // 原版移动照常执行（保持拖拽状态机完整），下一帧把两堆合一。
                            // v2.7.4：登记时锁定目标堆 ID；v2.7.5：记录拖放目标格（同容器归位用）。
                            _pendingDragMerge = (srcOwner, dstOwner, itemId, target.InstanceId, x, y);
                            RecentlyQueued[itemId] = DateTime.UtcNow;
                            if (StackConfig.DebugLogging.Value)
                                StackPatches.Log.LogInfo(
                                    $"[StackLimit999][拖拽合并] 登记待合并：物品{itemId}（容器{srcOwner}→{dstOwner}）→ 目标格({x},{y})物品{target.InstanceId}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning("[StackLimit999][拖拽合并] 登记异常: " + e);
            }
        }

        return true;
    }

    // ---- 事件防抖 ----
    private static double _lastSortMs = -10000;
    private static double _lastDragMs = -10000;

    /// <summary>该容器是否为工作台抽屉等合成袋：合成袋按"一实例=一材料单位"参与配方匹配，
    /// 合并堆会破坏工作台选材（2026-09-11 实测"材料组合无效"），一律排除在合并/重排之外。</summary>
    private static bool IsProtectedOwner(long ownerId)
    {
        try { return ItemManager.IsSyntheticBagOwner(ownerId); }
        catch { return false; }
    }

    /// <summary>v2.9.28：无人机交易界面是否打开（State_Web_TradeUI.OwnerId != 0）。
    /// 该界面对外部数据操作不友好（列表不收新条目、位置变化不感知），是官方行为区：
    /// 整理放行原版、不做拆分。只读查询，无泛型方法调用。</summary>
    private static bool IsTradeUiOpen()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return false;
            var sys = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var stateTree = sys?.reduxStoreLayer?.stateTree;
            if (stateTree == null)
                return false;
            var key = Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Web_TradeUI));
            GameCore.HotUpdate.ReduxUI.BaseReduxState st;
            if (!stateTree.TryGetValue(key, out st) || st == null)
                return false;
            var ts = st.TryCast<GameCore.HotUpdate.ReduxUI.State_Web_TradeUI>();
            return ts != null && ts.OwnerId != 0;
        }
        catch { return false; }
    }

    /// <summary>主线程每帧调用：执行登记的合并请求（整理合并延迟到整理流程结束后）。</summary>
    internal static void ProcessPendingMerges()
    {
        try
        {
            if (_pendingSortMergeDueMs != 0 && System.Environment.TickCount64 >= _pendingSortMergeDueMs)
            {
                _pendingSortMergeDueMs = 0;
                MergeAllStacks();
                if (_pendingSortTidy)
                {
                    _pendingSortTidy = false;
                    TidyAllOwners(); // SORT_BAG 原版被拦截：合并后由我们重排物品位置
                }
            }

            if (_pendingDragMerge.itemId != 0)
            {
                var (srcOwnerId, dstOwnerId, itemId, targetId, cellX, cellY) = _pendingDragMerge;
                _pendingDragMerge = (0, 0, 0, 0, 0, 0);
                MergeDraggedIntoCell(srcOwnerId, dstOwnerId, itemId, targetId, new Vector2Int(cellX, cellY));
            }

            // 整理合并后的界面补推（+3s/+6.5s 两轮，第一轮在合并时同步做过）
            for (int i = _pendingResyncs.Count - 1; i >= 0; i--)
            {
                if (System.Environment.TickCount64 >= _pendingResyncs[i].dueMs)
                {
                    long ownerId = _pendingResyncs[i].ownerId;
                    _pendingResyncs.RemoveAt(i);
                    var im = IM;
                    if (im != null)
                        BagUiSync.FullSyncOwner(im, ownerId, "整理后补推");
                }
            }
        }
        catch (Exception e)
        {
            StackPatches.Log.LogWarning("[StackLimit999][合并] 主线程执行异常: " + e);
        }
    }

    /// <summary>非破坏性检查：目标格上是否有可与拖拽物合并的堆。返回该堆，没有则 null。
    /// v2.9.17：支持跨容器（srcOwnerId != ownerId 时不再要求源堆属于目标容器）。</summary>
    private static ItemData FindMergeTargetAtCell(long ownerId, long draggedItemId, Vector2Int cell, long srcOwnerId = 0)
    {
        if (IsProtectedOwner(ownerId))
            return null; // 合成袋（工作台抽屉）不合并
        var im = IM;
        if (im == null)
            return null;

        var src = im.GetItemData(draggedItemId);
        if (src == null || !MergeRules.IsMergeEligible(src))
            return null;
        // 源堆必须还在登记时所在的容器（原语义：src.OwnerId == ownerId；v2.9.17 跨容器
        // 调用时传入真实的 srcOwnerId，同容器调用时 srcOwnerId == ownerId，两种都覆盖）
        if (srcOwnerId != 0 ? src.OwnerId != srcOwnerId : src.OwnerId != ownerId)
            return null;

        var list = im.GetItemDataList(ownerId);
        if (list == null)
            return null;

        foreach (var cand in list)
        {
            if (cand == null || cand.InstanceId == draggedItemId)
                continue;
            var p = cand.BagPos;
            var s = cand.ItemSize;
            bool contains = cell.x >= p.x && cell.x < p.x + s.x && cell.y >= p.y && cell.y < p.y + s.y;
            if (!contains)
                continue;
            if (!MergeRules.CanMerge(src, cand))
            {
                // v2.7.8：拒绝时输出原因，"为什么不能合并"不再靠猜
                if (StackConfig.DebugLogging.Value)
                    StackPatches.Log.LogInfo(
                        $"[StackLimit999][拖拽合并] 目标格物品{cand.InstanceId}({cand.ItemConfigId}) 不可合并：{MergeRules.MergeBlockReason(src, cand)}");
                return null; // 目标格被不可合并的物品占用 → 交给原版（移动/交换）
            }
            if (cand.ItemCount + src.ItemCount > StackConfig.StackLimit.Value)
                return null;
            return cand;
        }
        return null;
    }

    /// <summary>主线程：把拖拽物并入用户拖到的目标堆（登记时锁定的 targetId），
    /// 并把结果堆放回用户拖放的目标格子。v2.9.17：支持跨容器拖拽合并。
    /// v2.9.17 review 修正合并方向：v2.7.5 实测"原版对占用格是交换 A/B"对跨容器同样成立——
    /// 拖"背包大米"到"柜子大米"上，原版会把被拖堆换进柜子、把柜子那堆换回背包。
    /// 若仍固定"src 并进 target"，结果堆（target）此刻在背包里，等于把柜子的米并回了背包，
    /// 方向反了。因此按原版处理后的实际位置分支：
    ///   同容器                     → 结果堆 = target，归位到拖放格（v2.7.5 原语义）
    ///   跨容器且 src 已被换进目标容器 → 结果堆 = src（正好落在用户拖放的格子上），吸收 target
    ///   跨容器且原版没动 src       → 结果堆 = target（留在目标容器），吸收 src</summary>
    private static void MergeDraggedIntoCell(long srcOwnerId, long ownerId, long draggedItemId, long targetId, Vector2Int dropCell)
    {
        var im = IM;
        if (im == null)
            return;

        var src = im.GetItemData(draggedItemId);
        if (src == null || !MergeRules.IsMergeEligible(src))
        {
            // 原版对同配置且放得下的情形可能已自行并堆，此时无事可做
            if (StackConfig.DebugLogging.Value)
                StackPatches.Log.LogInfo($"[StackLimit999][拖拽合并] 物品{draggedItemId} 已不存在（可能原版已合并），跳过");
            return;
        }

        // v2.7.4：只并入登记时锁定的目标堆（用户拖到的那个格子）。
        // 旧实现按配置搜第一个可合并堆，导致"拖A到B，却并进了C"的视觉错乱。
        var target = targetId > 0 ? im.GetItemData(targetId) : null;
        if (target == null || target.InstanceId == draggedItemId
            || !MergeRules.IsMergeEligible(target) || !MergeRules.CanMerge(src, target))
        {
            if (StackConfig.DebugLogging.Value)
                StackPatches.Log.LogInfo(
                    $"[StackLimit999][拖拽合并] 目标堆{targetId} 已不存在或不可合并，跳过（保留原版移动结果）");
            return;
        }

        bool crossOwner = srcOwnerId != ownerId;
        ItemData keep, absorb;
        if (!crossOwner)
        {
            keep = target; absorb = src; // 原语义：并入登记的目标堆
        }
        else if (src.OwnerId == srcOwnerId)
        {
            keep = target; absorb = src; // 原版没动被拖堆 → 正向并入目标容器
        }
        else
        {
            keep = src; absorb = target; // 原版已把被拖堆换进目标容器 → 反向并堆，结果留在拖放格
        }

        int absorbCount = absorb.ItemCount, keepBefore = keep.ItemCount;
        var srcCell = src.BagPos; // 原版交换后 src 的位置是诊断用的"源格"
        if (MergePair(im, keep, absorb))
        {
            // v2.7.5：仅同容器需要归位（原版交换后结果堆停在 A 的原格，要挪回拖放格）。
            // 跨容器两种分支的结果堆都已落在正确格子（src 在拖放格 / target 在原格），不再挪动。
            if (!crossOwner)
            try
            {
                if (keep.BagPos.x != dropCell.x || keep.BagPos.y != dropCell.y)
                {
                    im.SetBagPosSafe(keep, dropCell, false);
                    StackPatches.Log.LogInfo(
                        $"[StackLimit999][拖拽合并] 结果堆{keep.InstanceId} 归位到拖放格({dropCell.x},{dropCell.y})（原在{keep.BagPos.x},{keep.BagPos.y}/源格{srcCell.x},{srcCell.y}）");
                }
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning("[StackLimit999][拖拽合并] 结果堆归位失败（不影响数据，仅位置）: " + e.Message);
            }

            StackPatches.Log.LogInfo(
                $"[StackLimit999][拖拽合并] 吸收物品{absorb.InstanceId}({absorb.ItemConfigId}x{absorbCount}) → 结果堆{keep.InstanceId}" +
                $"({keepBefore}→{keep.ItemCount}){(crossOwner ? $"（跨容器 {srcOwnerId}→{ownerId}）" : "")}");
            if (!crossOwner)
                DumpOwnerItems(im, ownerId, "拖拽合并后");
            BagUiSync.FullSyncOwner(im, ownerId, "拖拽合并");
            if (crossOwner)
                BagUiSync.FullSyncOwner(im, srcOwnerId, "拖拽合并源容器");
        }
    }

    /// <summary>合并一对堆（结果堆 keep ← 吸收 absorb）。
    /// v2.4 实测教训：游戏 RemoveItem/TryMergeIntoOwner 管线一旦碰到字段克隆出来的物品
    /// 就会崩溃（克隆对象没跑游戏构造函数、没注册事件系统）。
    /// 因此绝不调用 RemoveItem：数据层 SetItemCount(absorb,0) 置零 + UI 层 DelItem 移除格子。
    /// v2.9.17 review：缓存兜底按被并堆自己的 OwnerId 查——跨容器/交换场景下被并堆
    /// 可能已被原版换进另一个容器，调用方传入的容器不一定是它所在。</summary>
    private static bool MergePair(ItemManager im, ItemData keep, ItemData absorb)
    {
        // 保质期取两堆中较长的（v2.7.6：用户要求"以大的为准或平均"——取大，更新鲜的生效）
        if (absorb.TimeLeft > 0 && keep.TimeLeft > 0 && absorb.TimeLeft > keep.TimeLeft)
        {
            keep.TimeLeft = absorb.TimeLeft;
            // v2.9.17：保鲜物品的到期时间（InstanceEffectEnd）随保质期一起走——
            // 以新鲜那堆为准，逐元素拷贝（不能整个引用赋值：被并堆随后会被移除，共享数组指针会悬空）
            try
            {
                var srcEnd = absorb.InstanceEffectEnd;
                var dstEnd = keep.InstanceEffectEnd;
                if (srcEnd != null && dstEnd != null && srcEnd.Length == dstEnd.Length)
                {
                    for (int i = 0; i < srcEnd.Length; i++)
                        dstEnd[i] = srcEnd[i];
                }
            }
            catch { }
        }

        int sum = keep.ItemCount + absorb.ItemCount;
        if (sum > StackConfig.StackLimit.Value)
            return false;

        long absorbOwner = absorb.OwnerId; // 置零前先取（防置零后 OwnerId 变化）
        StackPatches.Log.LogInfo($"[StackLimit999][合并] 步骤1 SetItemCount 物品{keep.InstanceId} {keep.ItemCount}→{sum}");
        im.SetItemCount(keep, sum);
        StackPatches.Log.LogInfo($"[StackLimit999][合并] 步骤2 SetItemCount 物品{absorb.InstanceId} →0（数据层移除）");
        im.SetItemCount(absorb, 0);
        StackPatches.Log.LogInfo("[StackLimit999][合并] 步骤3 数据层真正移除被并堆（防 count=0 残骸写回存档）");
        // v2.9.14：只置零+删UI 会把 x0 残骸留在容器列表里（实测被游戏脏检测器报错、
        // 写回存档持续污染）。v2.8.2 起所有物品均为游戏原生对象（无克隆），可以安全移除。
        try { im.RemoveItem(absorb.InstanceId); } catch { }
        // 缓存级兜底 + 验证：直接查 OwnerCache 内部字典（绕开会触发报错日志的 GetItemDataList）
        try
        {
            if (im.OwnerCache != null && im.OwnerCache.TryGetValue(absorbOwner, out var inner) && inner != null)
            {
                if (inner.ContainsKey(absorb.InstanceId))
                {
                    inner.Remove(absorb.InstanceId);
                    StackPatches.Log.LogInfo($"[StackLimit999][合并] RemoveItem 未清干净，已从容器缓存补删 {absorb.InstanceId}");
                }
            }
        }
        catch { }
        StackPatches.Log.LogInfo("[StackLimit999][合并] 步骤4 UI 同步（AddOrNotifyItem + DelItem）");
        BagUiSync.NotifyUpsert(keep);
        BagUiSync.NotifyRemoved(absorb.InstanceId);
        // 快照型 WebUI（无人机交易等）同步刷新（v2.9.22）
        QueueWebUiBagRefresh(keep.OwnerId);
        StackPatches.Log.LogInfo($"[StackLimit999][合并] 完成（{absorb.InstanceId} 已并入 {keep.InstanceId}）");
        return true;
    }

    /// <summary>合并所有容器内的重复堆（整理按钮触发）。带统计诊断，能区分“没有重复”和“筛选误杀”。</summary>
    internal static void MergeAllStacks()
    {
        var im = IM;
        if (im == null)
        {
            StackPatches.Log.LogWarning("[StackLimit999][整理合并] ItemManager 不可用（world/IM 为空）");
            return;
        }

        // 关键：玩家背包物品在 OwnerCache（按容器分组）里；
        // im.Cache 只含世界散落物（v1.6.3 实测教训：遍历 Cache 会漏掉玩家背包）。
        var oc = im.OwnerCache;
        if (oc == null || oc.Count == 0)
        {
            StackPatches.Log.LogWarning("[StackLimit999][整理合并] OwnerCache 为空");
            return;
        }

        int totalMerged = 0, totalGroups = 0, totalEligible = 0;
        var affectedOwners = new List<long>();
        int skippedProtected = 0;
        foreach (var kv in oc)
        {
            if (IsProtectedOwner(kv.Key))
            {
                skippedProtected++; // 工作台抽屉等合成袋不参与合并（破坏配方选材）
                continue;
            }
            int merged = MergeOwnerStacks(im, kv.Key, out int groups, out int eligible);
            totalMerged += merged;
            totalGroups += groups;
            totalEligible += eligible;
            if (merged > 0)
                affectedOwners.Add(kv.Key);
        }

        StackPatches.Log.LogInfo(
            $"[StackLimit999][整理合并] 完成：容器 {oc.Count} 个（跳过合成袋 {skippedProtected}）/ 合格物品 {totalEligible} / 重复组 {totalGroups} / 合并 {totalMerged} 组");
        if (totalMerged == 0 && totalGroups == 0)
            StackPatches.Log.LogInfo("[StackLimit999][整理合并] 当前没有可合并的重复堆（如需测试：先按拆分键拆出一小堆）");

        // v2.7.4：数据层合并完成后全量推送受影响容器，并预约两轮补推。
        // 实测整理流程自己的 JS 状态推送可能晚于我们的合并，把增量通知覆盖掉
        // （表现为"点了整理没合并，随便一拖才显示"），多轮幂等推送确保显示追上数据。
        if (affectedOwners.Count > 0)
        {
            long now = System.Environment.TickCount64;
            foreach (var ownerId in affectedOwners)
            {
                BagUiSync.FullSyncOwner(im, ownerId, "整理合并");
                _pendingResyncs.Add((now + 3000, ownerId));
                _pendingResyncs.Add((now + 6500, ownerId));
            }
            StackPatches.Log.LogInfo($"[StackLimit999][整理合并] 已全量同步 {affectedOwners.Count} 个容器，预约 +3s/+6.5s 两轮补推");
        }
    }

    private static int MergeOwnerStacks(ItemManager im, long ownerId, out int groups, out int eligible)
    {
        groups = 0;
        eligible = 0;
        var list = im.GetItemDataList(ownerId);
        if (list == null || list.Count < 2)
            return 0;

        var snapshot = new List<ItemData>();
        foreach (var it in list)
        {
            if (it != null && MergeRules.IsMergeEligible(it))
                snapshot.Add(it);
        }
        eligible = snapshot.Count;
        if (snapshot.Count < 2)
            return 0;

        var firstOfGroup = new Dictionary<(int, int, string), ItemData>();
        var toMerge = new List<ItemData>();
        foreach (var it in snapshot)
        {
            // v2.9.20：多次使用的物品（上限 MaxUseTimes>1）不参与整理合并——保持官方单份
            // 状态，烹饪预拆分/消耗走 100% 官方逻辑（见 MergeBlockReason 内说明）。
            // 单次使用（上限=1）的物品照常合并。
            if (it.MaxUseTimes > 1)
                continue;
            // v2.7.8：分组键加属性指纹——个体属性/buff 内容相同的才归同组，
            // 属性不同（完美料理 vs 普通）天然分到不同组，不会被误并；
            // v2.7.9：去掉 IsMapPreset——来源标记不阻断合并（用户诉求）
            var key = (it.ItemConfigId, it.Polluted ? 1 : 0, MergeRules.AttrFingerprint(it));
            if (firstOfGroup.ContainsKey(key))
                toMerge.Add(it);
            else
                firstOfGroup[key] = it;
        }
        groups = toMerge.Count;

        int merged = 0;
        foreach (var it in toMerge)
        {
            var key = (it.ItemConfigId, it.Polluted ? 1 : 0, MergeRules.AttrFingerprint(it));
            if (!firstOfGroup.TryGetValue(key, out var target))
                continue;
            if (target == null || target.InstanceId == it.InstanceId)
                continue;
            if (target.ItemCount + it.ItemCount > StackConfig.StackLimit.Value)
                continue;
            if (MergePair(im, target, it))
            {
                merged++;
                StackPatches.Log.LogInfo(
                    $"[StackLimit999][整理合并] 已并入：物品{it.InstanceId}({it.ItemConfigId}x{it.ItemCount}) → 物品{target.InstanceId}(→{target.ItemCount})");
            }
        }
        return merged;
    }

    /// <summary>容器整理的位置重排（SORT_BAG 原版被拦截后由本插件实现）：
    /// 每个容器内活物品按 配置ID→InstanceId 排序后 first-fit 重新入格。
    /// 只挪 BagPos（SetBagPosSafe），不触碰任何原生销毁管线。</summary>
    internal static void TidyAllOwners()
    {
        var im = IM;
        if (im == null)
            return;
        var oc = im.OwnerCache;
        if (oc == null)
            return;

        int tidied = 0;
        foreach (var kv in oc)
        {
            if (IsProtectedOwner(kv.Key))
                continue; // 合成袋（工作台抽屉）不重排
            try
            {
                if (TidyOwner(im, kv.Key))
                    tidied++;
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning($"[StackLimit999][整理重排] 容器{kv.Key} 异常: " + e.Message);
            }
        }
        StackPatches.Log.LogInfo($"[StackLimit999][整理重排] 完成，处理 {tidied} 个容器");
    }

    private static bool TidyOwner(ItemManager im, long ownerId)
    {
        var list = im.GetItemDataList(ownerId); // 游戏侧会驱逐 count=0 的脏条目
        if (list == null || list.Count < 2)
            return false;
        var gridSize = im.GetOwnerBagSize(ownerId);
        if (gridSize.x <= 0 || gridSize.y <= 0)
            return false;

        var items = new List<ItemData>();
        foreach (var it in list)
            if (it != null && it.InstanceId > 0 && it.ItemCount > 0)
                items.Add(it);
        if (items.Count < 2)
            return false;
        items.Sort((a, b) => a.ItemConfigId != b.ItemConfigId
            ? a.ItemConfigId.CompareTo(b.ItemConfigId)
            : a.InstanceId.CompareTo(b.InstanceId));

        var occupied = new bool[gridSize.x, gridSize.y];
        int moved = 0;
        foreach (var it in items)
        {
            var s = it.ItemSize;
            if (s.x <= 0 || s.y <= 0 || s.x > gridSize.x || s.y > gridSize.y)
                continue;
            bool placed = false;
            for (int y = 0; y <= gridSize.y - s.y && !placed; y++)
            {
                for (int x = 0; x <= gridSize.x - s.x && !placed; x++)
                {
                    bool free = true;
                    for (int dy = 0; dy < s.y && free; dy++)
                        for (int dx = 0; dx < s.x && free; dx++)
                            if (occupied[x + dx, y + dy])
                                free = false;
                    if (!free)
                        continue;
                    if (it.BagPos.x != x || it.BagPos.y != y)
                    {
                        im.SetBagPosSafe(it, new Vector2Int(x, y), false);
                        moved++;
                    }
                    for (int dy = 0; dy < s.y; dy++)
                        for (int dx = 0; dx < s.x; dx++)
                            occupied[x + dx, y + dy] = true;
                    placed = true;
                }
            }
            // 放不下（罕见装箱边界）就保持原位，物品不会丢
        }
        if (moved > 0)
            BagUiSync.FullSyncOwner(im, ownerId, "整理重排");
        return moved > 0 || items.Count > 0;
    }

    // ---------------- WebUI 快照刷新（无人机交易/烹饪等 Redux 快照型界面） ----------------

    // 快照型 WebUI（如 TradeUI）不消费 LogicAdapter 增量通知，而是吃
    // State_Web_ShopUI.BagItemsJson 全量快照；快照只在官方流程派发
    // Ac_ShopUI_RefreshBag 时重建。mod 直写数据后官方不会派发 → 界面里看不到新物品
    // （关界面重开才出现）。这里在 mod 改动后派发同一官方动作刷新。
    private static readonly List<(long dueMs, long ownerId)> _pendingWebUiRefreshes = new();

    /// <summary>登记一次 WebUI 快照刷新（立即 + 300ms 各一次，幂等）。</summary>
    internal static void QueueWebUiBagRefresh(long ownerId)
    {
        long now = System.Environment.TickCount64;
        _pendingWebUiRefreshes.Add((now, ownerId));
        _pendingWebUiRefreshes.Add((now + 300, ownerId));
    }

    /// <summary>ticker 主线程：派发官方刷新动作 + 分层诊断。
    /// v2.9.26：加入官方 LogicAdapter.OnSyncBagItems（整容器列表推送，参数为 mod 已在
    /// 使用的 ItemDataDto）与 Ac_Item_RefreshAllBagViews（官方全背包视图刷新）；
    /// 并把 ItemManager 实际数据与 TradeUI 的 UI 缓存列表（State_Web_TradeUI.ItemDataList）
    /// 对比输出——一次测试即可定位新物品在哪一层丢失（逻辑层 / UI 状态层 / JS 推送层）。</summary>
    internal static void ProcessWebUiRefreshes()
    {
        if (_pendingWebUiRefreshes.Count == 0)
            return;
        long now = System.Environment.TickCount64;
        for (int i = _pendingWebUiRefreshes.Count - 1; i >= 0; i--)
        {
            var (due, ownerId) = _pendingWebUiRefreshes[i];
            if (now < due)
                continue;
            _pendingWebUiRefreshes.RemoveAt(i);
            var dbg = StackConfig.DebugLogging.Value;
            try
            {
                var la = BagUiSync.LA();
                var im = IM;

                // ── 官方整容器同步：把当前容器的完整 ItemDataDto 列表推给 UI 层 ──
                if (la != null && im != null)
                {
                    try
                    {
                        var list = im.GetItemDataList(ownerId);
                        var dtoList = new Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.ItemDataDto>();
                        if (list != null)
                        {
                            foreach (var it in list)
                            {
                                if (it == null || it.InstanceId <= 0 || it.ItemCount <= 0)
                                    continue;
                                dtoList.Add(BagUiSync.MakeDto(it));
                            }
                        }
                        la.OnSyncBagItems(ownerId, ownerId, dtoList, dtoList);
                        if (dbg)
                            StackPatches.Log.LogInfo($"[StackLimit999][WebUI刷新] 已调 OnSyncBagItems（容器{ownerId}，{dtoList.Count} 条）");
                    }
                    catch (Exception e)
                    {
                        if (dbg)
                            StackPatches.Log.LogWarning("[StackLimit999][WebUI刷新] OnSyncBagItems 失败: " + e.Message);
                    }
                }

                // ── 官方全背包视图刷新 ──
                try { GameCore.HotUpdate.ReduxUI.Ac_Item_RefreshAllBagViews.SendAction(); }
                catch (Exception e)
                {
                    if (dbg)
                        StackPatches.Log.LogWarning("[StackLimit999][WebUI刷新] RefreshAllBagViews 失败: " + e.Message);
                }

                // 烹饪界面：官方无参刷新动作
                try { GameCore.HotUpdate.ReduxUI.Ac_Cooking_RefreshBag.SendAction(); }
                catch { }
            }
            catch (Exception e)
            {
                if (dbg)
                    StackPatches.Log.LogWarning("[StackLimit999][WebUI刷新] 派发失败（不影响数据）: " + e.Message);
            }
        }
    }

    // ---------------- 拆分（Alt+左键点击物品） ----------------

    /// <summary>ticker 主线程执行登记的 Alt+左键拆分（HandleCallback 内改数据会冻死）。</summary>
    internal static void ProcessPendingSplit()
    {
        if (_pendingSplitItemId == 0)
            return;
        long id = _pendingSplitItemId;
        _pendingSplitItemId = 0;
        TrySplitItem(id);
    }

    /// <summary>从指定物品堆拆出 SplitCount 件（v2.9.5：由 Alt+左键触发，任何容器的物品均可）。</summary>
    internal static void TrySplitItem(long instanceId)
    {
        var im = IM;
        if (im == null)
        {
            StackPatches.Log.LogInfo("[StackLimit999][拆分] 游戏世界未加载，忽略");
            return;
        }

        var src = im.GetItemData(instanceId);
        if (src == null || !MergeRules.CanSplit(src) || src.ItemConfigId <= 0)
        {
            StackPatches.Log.LogWarning($"[StackLimit999][拆分] 物品 {instanceId} 已不存在或不支持拆分");
            return;
        }
        if (src.ItemCount < 2)
        {
            StackPatches.Log.LogInfo("[StackLimit999][拆分] 该物品数量不足 2，无需拆分");
            return;
        }

        int splitCount = StackConfig.SplitCount.Value;
        if (splitCount <= 0)
            splitCount = src.ItemCount / 2;
        if (splitCount >= src.ItemCount)
            splitCount = src.ItemCount - 1;
        if (splitCount < 1)
        {
            StackPatches.Log.LogWarning("[StackLimit999][拆分] 可拆数量不足");
            return;
        }

        long ownerId = src.OwnerId;
        int beforeSrc = src.ItemCount;

        // ---- 拆分原语 v2（v2.9.9）：单容器、零中转 ----
        // 历史教训：
        //   v2.9.0-2.9.8 中转容器方案两次事故——OwnerCache 无法区分真实容器与地面掉落
        //   虚拟 owner，AddItem 写入后回滚 RemoveItem 会留 count=0 脏数据，游戏脏数据
        //   检测器 LogError 洪水 → 错误反馈弹窗（Player.log 4184 条 dirty 实锤）。已整体移除。
        //   原生 SplitItem / TryPreSplitForCooking 均不可用（恒 null），已删。
        var newIt = SplitInPlace(im, src, splitCount, beforeSrc);

        if (newIt == null)
        {
            StackPatches.Log.LogWarning("[StackLimit999][拆分] 本次拆分取消，物品无损");
            return;
        }

        StackPatches.Log.LogInfo(
            $"[StackLimit999][拆分] 成功：源物品{src.InstanceId} {beforeSrc}→{src.ItemCount}，" +
            $"新物品{newIt.InstanceId}x{newIt.ItemCount}@({newIt.BagPos.x},{newIt.BagPos.y})");
        // v2.9.29：视图刷新收敛为 数据增量 + 一次官方 RefreshAllBagViews。
        // 反汇编实证（全 .text call 扫描）：增量通知（AddOrNotifyItem→RA_AddOrNotify→UpdateBag）
        // 只派发 Ac_BackpackUI_RefreshBag（仅 BackpackUI 页面）；工作台/烹饪页只认
        // Ac_ToolTable_RefreshBag / Ac_Cooking_RefreshBag，二者除各自 reducer 外只由
        // RA_RefreshAllBagViews 统一派发——这一个动作就是"刷新所有已打开界面"的官方总闸，
        // 且内部自判各界面开关，无需 mod 侧判断。
        // 旧方案（FullSyncOwner 全量循环 + 0.3s 补推 + QueueWebUiBagRefresh 三件套×2）
        // 每条 AddOrNotifyItem 都内带一次整视图推送（RA_AddOrNotify→UpdateBag），
        // 一次拆分 ≈ 数十次整包 DOM 重建（JS initGrid 无 diff 全重建），工作台连续拆分
        // 卡顿的元凶，全部移除。
        BagUiSync.NotifyUpsert(src, newIt);
        try { GameCore.HotUpdate.ReduxUI.Ac_Item_RefreshAllBagViews.SendAction(); }
        catch (Exception e)
        {
            StackPatches.Log.LogWarning("[StackLimit999][拆分] 刷新已打开界面失败（不影响数据）: " + e.Message);
        }
    }

    /// <summary>拆分原语 v2.1：把该配置的堆叠上限临时压到 1——所有已有堆（count≥1）
    /// 一律视为"已满"，AddItem 绝不并堆、只会去空格新建 x1（v2.9.9 压到源堆数量会漏掉
    /// 其他更小的同配置堆，拆出的件被并进去，"等于没拆"，实测）。
    /// 多件拆分用原生 IncreaseItemCount 补到新堆上。全程单容器、零跨容器操作。</summary>
    private static ItemData SplitInPlace(ItemManager im, ItemData src, int splitCount, int beforeSrc)
    {
        long target = src.OwnerId;

        // 1) 空格检查（没有空格就提前退出，物品无损）
        Vector2Int freeCell = default;
        bool haveFree = false;
        try { haveFree = im.TryResolveBagPos(target, src.ItemSize, src.BagPos, out freeCell); }
        catch { }
        if (!haveFree)
        {
            var scanned = FindFreeCell(im, target, src.ItemSize, out var diag);
            if (!scanned.HasValue)
            {
                StackPatches.Log.LogWarning("[StackLimit999][拆分] 目标容器无空格: " + diag);
                return null;
            }
        }

        // 2) 上限临时=1 → AddItem 新建 x1（不可能并进任何已有堆）→ 恢复上限
        var cfg = StackPatches.GetConfigItem(src.ItemConfigId);
        if (cfg == null || !StackPatches.EnsureStackLimitFieldAccess())
        {
            StackPatches.Log.LogWarning("[StackLimit999][拆分] 配置表不可用");
            return null;
        }
        int savedLimit = StackPatches.ReadStackLimitRaw(cfg);
        ItemData created = null;
        try
        {
            StackPatches.WriteStackLimitRaw(cfg, 1);
            created = im.AddItem(target, src.ItemConfigId, 1, 0, 0, new Vector2Int(0, 0), false, false);
        }
        catch (Exception e)
        {
            StackPatches.Log.LogWarning("[StackLimit999][拆分] AddItem 异常: " + e.Message);
        }
        finally
        {
            StackPatches.WriteStackLimitRaw(cfg, savedLimit);
        }

        // 3) 严格校验：必须是全新 x1 堆，源堆未动
        bool ok = created != null
                  && created.InstanceId != src.InstanceId
                  && created.OwnerId == target
                  && created.ItemCount == 1
                  && src.ItemCount == beforeSrc;
        if (ok)
        {
            // 多件拆分：新堆直接加到 splitCount（不经过 AddItem，绝不会并错堆）
            if (splitCount > 1)
            {
                try { im.IncreaseItemCount(created, splitCount - 1); } catch { }
            }
            im.SetItemCount(src, beforeSrc - splitCount); // 源堆永不为 0（splitCount < beforeSrc）
            try
            {
                created.TimeLeft = src.TimeLeft;
                created.OriginalTimeLeft = src.OriginalTimeLeft;
                created.StartTime = src.StartTime;
                created.TimeScale = src.TimeScale;
            }
            catch { }
            return created;
        }

        // 4) 失败回滚（物品无损）
        if (created != null && created.InstanceId != src.InstanceId && created.ItemCount == 1)
        {
            try { im.RemoveItem(created.InstanceId); } catch { }
        }
        if (src.ItemCount != beforeSrc)
            im.SetItemCount(src, beforeSrc);
        StackPatches.Log.LogWarning(
            $"[StackLimit999][拆分] AddItem 结果不符(created={(created == null ? "null" : created.InstanceId + "x" + created.ItemCount)}," +
            $" src={beforeSrc}→{src.ItemCount})，已回滚（物品无损）");
        return null;
    }

    private static Vector2Int? FindFreeCell(ItemManager im, long ownerId, Vector2Int size, out string diag)
    {
        diag = null;
        var gridSize = im.GetOwnerBagSize(ownerId);
        if (gridSize.x <= 0 || gridSize.y <= 0)
        {
            diag = $"GetOwnerBagSize({ownerId})=({gridSize.x},{gridSize.y})";
            return null;
        }

        var list = im.GetItemDataList(ownerId);
        var occupied = new bool[gridSize.x, gridSize.y];
        if (list != null)
        {
            foreach (var it in list)
            {
                if (it == null || it.InstanceId <= 0 || it.ItemCount <= 0)
                    continue;
                var p = it.BagPos;
                var s = it.ItemSize;
                if (p.x < 0 || p.y < 0)
                    continue;
                for (int dy = 0; dy < s.y; dy++)
                {
                    int gy = p.y + dy;
                    if (gy < 0 || gy >= gridSize.y)
                        continue;
                    for (int dx = 0; dx < s.x; dx++)
                    {
                        int gx = p.x + dx;
                        if (gx >= 0 && gx < gridSize.x)
                            occupied[gx, gy] = true;
                    }
                }
            }
        }

        for (int y = 0; y <= gridSize.y - size.y; y++)
        {
            for (int x = 0; x <= gridSize.x - size.x; x++)
            {
                bool free = true;
                for (int dy = 0; dy < size.y && free; dy++)
                    for (int dx = 0; dx < size.x && free; dx++)
                        if (occupied[x + dx, y + dy])
                            free = false;
                if (free)
                    return new Vector2Int(x, y);
            }
        }
        diag = $"网格({gridSize.x},{gridSize.y}) 放不下({size.x}x{size.y})";
        return null;
    }
}

/// <summary>
/// 背包 UI 增量同步：直接走游戏自己的 LogicAdapter.AddOrNotifyItem / DelItem
/// （游戏捡东西/消耗物品时通知 WebUI 背包界面的同一条原生路径）。
/// OnRefreshAllBagViews 只重发游戏自己的缓存列表，看不到 mod 直写的数据（v2.2 实测），弃用。
/// </summary>
internal static class BagUiSync
{
    [DllImport("GameAssembly", EntryPoint = "il2cpp_object_new")]
    private static extern IntPtr il2cpp_object_new(IntPtr klass);

    internal static GameCore.HotUpdate.ReduxUI.LogicAdapter LA()
    {
        if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
            return null;
        return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance?._logicAdapter;
    }

    /// <summary>新增/更新界面上的物品堆（数量、位置）。</summary>
    public static void NotifyUpsert(params ItemData[] items)
    {
        var la = LA();
        if (la == null)
            return;
        foreach (var it in items)
        {
            if (it == null)
                continue;
            try
            {
                la.AddOrNotifyItem(MakeDto(it));
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning("[StackLimit999][UI同步] AddOrNotifyItem 异常: " + e.Message);
            }
        }
    }

    /// <summary>从界面上移除物品堆。</summary>
    public static void NotifyRemoved(params long[] instanceIds)
    {
        var la = LA();
        if (la == null)
            return;
        foreach (var id in instanceIds)
        {
            if (id <= 0)
                continue;
            try
            {
                la.DelItem(id);
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning("[StackLimit999][UI同步] DelItem 异常: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 全量同步一个容器的界面状态：活条目（数量>0）upsert、死条目（数量=0）移除。
    /// v2.7.4：整理合并后 JS 界面可能不应用增量通知（实测数据层已合并、界面不动，
    /// 随便一次拖拽触发原版刷新才显示）。全量推送是幂等操作，用来把显示强行追上数据。
    /// </summary>
    public static void FullSyncOwner(ItemManager im, long ownerId, string reason)
    {
        try
        {
            var list = im.GetItemDataList(ownerId);
            if (list == null)
                return;
            var upserts = new List<ItemData>();
            var removes = new List<long>();
            foreach (var it in list)
            {
                if (it == null || it.InstanceId <= 0)
                    continue;
                if (it.ItemCount > 0)
                    upserts.Add(it);
                else
                    removes.Add(it.InstanceId);
            }
            NotifyUpsert(upserts.ToArray());
            NotifyRemoved(removes.ToArray());
            StackPatches.Log.LogInfo(
                $"[StackLimit999][UI同步] 全量同步容器{ownerId}（{reason}）：upsert {upserts.Count} / 移除 {removes.Count}");
        }
        catch (Exception e)
        {
            StackPatches.Log.LogWarning("[StackLimit999][UI同步] FullSyncOwner 异常: " + e.Message);
        }
    }

    internal static ItemDataDto MakeDto(ItemData it)
    {
        var klass = Il2CppClassPointerStore<ItemDataDto>.NativeClassPtr;
        if (klass == IntPtr.Zero)
            throw new InvalidOperationException("ItemDataDto class ptr 为空");
        var dto = new ItemDataDto(il2cpp_object_new(klass));
        dto.InstanceId = it.InstanceId;
        dto.OwnerId = it.OwnerId;
        dto.ItemConfigId = it.ItemConfigId;
        dto.ItemCount = it.ItemCount;
        dto.StartTime = it.StartTime;
        dto.TimeLeft = it.TimeLeft;
        dto.ItemSize = it.ItemSize;
        dto.BagPos = it.BagPos;
        // 界面条目的主数字标签映射自 UseTimes（v2.4 实测：ItemCount 不映射到条目 count；
        // MaxUseTimes=-1 时隐藏 u/m 标签）：
        //   无使用次数的物品（MaxUseTimes<=0，材料/组件）：UseTimes=ItemCount → 显示 x{数量}；
        //   有使用次数的物品（书/肥皂/食物耐久，MaxUseTimes>0）：忠实映射 u{剩余}/{上限}，
        //   绝不能拿数量覆盖使用次数（v2.7.3 修复：书籍堆拆分后使用次数显示跟着数量跳 x2/x1）。
        if (it.MaxUseTimes > 0)
        {
            dto.UseTimes = it.UseTimes;
            dto.MaxUseTimes = it.MaxUseTimes;
        }
        else
        {
            dto.UseTimes = it.ItemCount;
            dto.MaxUseTimes = -1;
        }
        dto.TimeScale = it.TimeScale;
        dto.OriginalTimeLeft = it.OriginalTimeLeft;
        dto.InstanceVD = it.InstanceVD;
        dto.InstanceEffectEnd = it.InstanceEffectEnd;
        dto.InstanceBurnValue = it.InstanceBurnValue;
        dto.InstanceWeight = it.InstanceWeight;
        dto.Polluted = it.Polluted;
        dto.IsMapPreset = it.IsMapPreset;
        dto.PresetShelfScale = it.PresetShelfScale;
        return dto;
    }
}
/// <summary>
/// 损失补偿（v2.8.2）：按配置 RestoreLosses（"配置ID:目标总数,..."）在存档加载后
/// 核对全世界总量，差额用游戏正规 AddItem 管线补到已有堆上（与捡东西同路径）。
/// 用于弥补拆分事故丢失的物品；一次性执行，用完清空配置即可。
/// </summary>
internal static class LossRestorer
{
    private static bool _done;
    private static long _worldSeenMs;

    internal static void Tick()
    {
        if (_done)
            return;
        string spec = StackConfig.RestoreLosses.Value;
        if (string.IsNullOrWhiteSpace(spec))
        {
            _done = true;
            return;
        }
        if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated)
            return;
        var world = BaseSingleton<BattleLogicWorld>.Instance;
        var im = world == null ? null : world._ItemManager;
        if (im == null)
            return;
        if (_worldSeenMs == 0)
        {
            _worldSeenMs = System.Environment.TickCount64;
            return;
        }
        if (System.Environment.TickCount64 - _worldSeenMs < 8000)
            return; // 等世界物品注册稳定

        _done = true;
        foreach (var part in spec.Split(','))
        {
            var seg = part.Trim();
            if (seg.Length == 0)
                continue;
            var kv = seg.Split(':');
            if (kv.Length != 2 || !int.TryParse(kv[0].Trim(), out int cfgId) || !int.TryParse(kv[1].Trim(), out int target))
            {
                StackPatches.Log.LogWarning("[StackLimit999][补偿] 无法解析条目: " + seg);
                continue;
            }
            try
            {
                int cur = im.GetAvailableItemCount(cfgId);
                if (cur >= target)
                {
                    StackPatches.Log.LogInfo($"[StackLimit999][补偿] cfg{cfgId} 现有 {cur} ≥ 目标 {target}，无需补偿");
                    continue;
                }
                // 补到已有堆所在容器（AddItem 会自动并入同配置堆）
                long owner = 0;
                var existing = im.GetItemsByConfigId(cfgId);
                if (existing != null)
                {
                    foreach (var it in existing)
                    {
                        if (it != null && it.InstanceId > 0 && it.ItemCount > 0)
                        {
                            owner = it.OwnerId;
                            break;
                        }
                    }
                }
                if (owner == 0)
                {
                    StackPatches.Log.LogWarning($"[StackLimit999][补偿] cfg{cfgId} 世上没有任何存活堆，找不到投放容器，跳过（请手动处理）");
                    continue;
                }
                int missing = target - cur;
                var created = im.AddItem(owner, cfgId, missing, 0, 0, new Vector2Int(0, 0), false, false);
                int after = im.GetAvailableItemCount(cfgId);
                StackPatches.Log.LogInfo(
                    $"[StackLimit999][补偿] cfg{cfgId} 现有 {cur} → 目标 {target}，已向容器{owner}补 {missing}" +
                    $"（AddItem 返回 {(created == null ? "空(已并入现有堆)" : created.ItemConfigId + "x" + created.ItemCount)}），补后总数 {after}");
            }
            catch (Exception e)
            {
                StackPatches.Log.LogWarning($"[StackLimit999][补偿] cfg{cfgId} 异常: " + e.Message);
            }
        }
    }
}

/// <summary>
/// v2.9.17 消耗诊断（只读日志，不改任何数据）：对带使用次数的物品（食物/书，MaxUseTimes>0）
/// 记录每一次原版 SetItemCount / RemoveItem 的调用现场；v2.9.17 补丁版补挂烹饪链路
/// （OnCookingQuickMove / OnCookingDragMove / TrackCookingBagTransition / TryPreSplitForCooking /
/// TryReturnCookingIngredientToBag / RefreshCookingPendingIfSource），
/// 烹饪"整堆被一次性消耗"这类事故可以从日志直接看到卡在哪一步。
/// 注意：RemoveItem 有 1 参/2 参两个重载，必须显式指定参数类型，否则 Harmony
/// AmbiguousMatchException 会让整个探针静默失效（v2.9.17 初版实锤）。
/// </summary>

internal static class ConsumeTracePatches
{
    private static void Trace(string api, ItemData it, string detail)
    {
        try
        {
            if (it == null || it.MaxUseTimes <= 0 || it.ItemConfigId <= 0)
                return;
            StackPatches.Log.LogInfo(
                $"[StackLimit999][消耗追踪] {api} #{it.InstanceId % 1000} cfg{it.ItemConfigId} x{it.ItemCount} u{it.UseTimes}/{it.MaxUseTimes} owner{it.OwnerId} {detail}");
        }
        catch { }
    }

    private static bool TraceEnabled => StackConfig.DebugLogging.Value;

    // 注意：原版参数一律用 __0/__1…（按序号注入），interop 的真实参数名不可靠，
    // 按名字注入失败会让 Harmony 抛异常、整个探针类挂载失败。

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.SetItemCount))]
    [HarmonyPrefix]
    public static void SetItemCountPrefix(ItemData __0, int __1)
        => Trace("SetItemCount", __0, $"→ {__1}");

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.IncreaseItemCount))]
    [HarmonyPrefix]
    public static void IncreaseItemCountPrefix(ItemData __0, int __1)
        => Trace("IncreaseItemCount", __0, $"Δ {__1}");

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.RemoveItem), new[] { typeof(long) })]
    [HarmonyPrefix]
    public static void RemoveItem1Prefix(long __0)
    {
        if (!TraceEnabled)
            return;
        var im = IM;
        var it = im != null ? im.GetItemData(__0) : null;
        Trace("RemoveItem", it, "（整堆移除）");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.RemoveItem), new[] { typeof(long), typeof(long) })]
    [HarmonyPrefix]
    public static void RemoveItem2Prefix(long __0, long __1)
    {
        if (!TraceEnabled)
            return;
        var im = IM;
        var it = im != null ? im.GetItemData(__0) : null;
        Trace("RemoveItem2", it, $"（arg2={__1}）");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.AddItem), new[] { typeof(long), typeof(int), typeof(int), typeof(int), typeof(int), typeof(Vector2Int), typeof(bool), typeof(bool) })]
    [HarmonyPostfix]
    public static void AddItemPostfix(ItemData __result)
    {
        try
        {
            if (!TraceEnabled)
                return;
            if (__result != null && __result.MaxUseTimes > 0)
                StackPatches.Log.LogInfo(
                    $"[StackLimit999][消耗追踪] AddItem → #{__result.InstanceId % 1000} cfg{__result.ItemConfigId} x{__result.ItemCount} u{__result.UseTimes}/{__result.MaxUseTimes} owner{__result.OwnerId}");
        }
        catch { }
    }

    // ---------------- 烹饪链路探针 ----------------

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.OnCookingQuickMove))]
    [HarmonyPrefix]
    public static void CookingQuickMovePrefix(long __0, long __1, long __2)
    {
        if (TraceEnabled)
            StackPatches.Log.LogInfo($"[StackLimit999][烹饪链路] OnCookingQuickMove({__0}, {__1}, {__2})");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.OnCookingDragMove))]
    [HarmonyPrefix]
    public static void CookingDragMovePrefix(long __0, long __1, long __2, Vector2Int __3)
    {
        if (TraceEnabled)
            StackPatches.Log.LogInfo($"[StackLimit999][烹饪链路] OnCookingDragMove({__0}, {__1}, {__2}, ({__3.x},{__3.y}))");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.TrackCookingBagTransition))]
    [HarmonyPrefix]
    public static void TrackCookingBagTransitionPrefix(ItemData __0, long __1, long __2, Vector2Int __3)
    {
        if (!TraceEnabled)
            return;
        Trace("TrackCookingBagTransition", __0, $"from{__1}→to{__2} @({__3.x},{__3.y})");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.TryPreSplitForCooking))]
    [HarmonyPostfix]
    public static void TryPreSplitForCookingPostfix(long __0, long __1, long __result)
    {
        if (!TraceEnabled)
            return;
        var im = IM;
        var src = im != null ? im.GetItemData(__1) : null;
        StackPatches.Log.LogInfo(
            $"[StackLimit999][烹饪链路] TryPreSplitForCooking({__0}, {__1}) → 结果实例 {__result}" +
            (src != null ? $"（源堆 cfg{src.ItemConfigId} x{src.ItemCount} u{src.UseTimes}/{src.MaxUseTimes}）" : "（源堆不可读）"));
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.TryReturnCookingIngredientToBag))]
    [HarmonyPostfix]
    public static void TryReturnCookingIngredientToBagPostfix(ItemData __0, long __1, Vector2Int __2, bool __result)
    {
        if (!TraceEnabled)
            return;
        Trace("TryReturnCookingIngredientToBag", __0, $"→to{__1} @({__2.x},{__2.y}) 返回={__result}");
    }

    [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.RefreshCookingPendingIfSource))]
    [HarmonyPrefix]
    public static void RefreshCookingPendingIfSourcePrefix(long __0)
    {
        if (TraceEnabled)
            StackPatches.Log.LogInfo($"[StackLimit999][烹饪链路] RefreshCookingPendingIfSource({__0})");
    }

    private static ItemManager IM
    {
        get
        {
            if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated)
                return null;
            var world = BaseSingleton<BattleLogicWorld>.Instance;
            return world == null ? null : world._ItemManager;
        }
    }
}

public sealed class StackRewriteTicker : MonoBehaviour
{
    public StackRewriteTicker(IntPtr ptr) : base(ptr) { }
    public StackRewriteTicker() { }

    private float _accumulator;
    private float _interval = 1f; // 先用1秒节奏轮询，直到配置表就绪
    private bool _announced;
    private int _failCount;
    private float _hbSeconds;

    public void Update()
    {
        _accumulator += Time.deltaTime;

        // 心跳（60s 一条）：确认本组件在场景切换后仍然存活
        _hbSeconds += Time.deltaTime;
        if (_hbSeconds >= 60f)
        {
            _hbSeconds = 0f;
            if (StackConfig.DebugLogging.Value)
                StackPatches.Log.LogInfo("[StackLimit999][心跳] ticker 存活");
        }

        LossRestorer.Tick();

        if (StackPatches.RequestRewrite)
        {
            StackPatches.RequestRewrite = false;
            _accumulator = _interval;
        }

        // 登记过的合并/拆分请求在主线程执行（HandleCallback 里改数据会冻结游戏，实测教训）
        if (BaseSingleton<BattleLogicWorld>.IsInstanceCreated)
        {
            BagEventPatches.ProcessPendingMerges();
            BagEventPatches.ProcessPendingSplit();
            BagEventPatches.ProcessWebUiRefreshes();
        }

        if (_accumulator < _interval)
            return;
        _accumulator = 0f;

        try
        {
            int changed = StackPatches.RewriteAllData(out int total);
            if (total < 0)
            {
                if (total == -2)
                    _interval = Mathf.Max(StackConfig.RewriteIntervalSeconds.Value, 60f);
                return;
            }

            if (!_announced)
            {
                _announced = true;
                _interval = Mathf.Clamp(StackConfig.RewriteIntervalSeconds.Value, 1f, 600f);
                StackPatches.Log.LogInfo(
                    $"[StackLimit999] 配置表就绪：背包现有物品 {total} 种，直写改写 {changed} 个，堆叠上限 → {StackConfig.StackLimit.Value}（v2.6 起只改背包中实际存在的物品，不碰全表）");
            }
            else if (changed > 0)
            {
                StackPatches.Log.LogInfo(
                    $"[StackLimit999] 刷新改写 {changed} 个物品的堆叠上限 → {StackConfig.StackLimit.Value}（共 {total} 个）");
            }

            int bagChanged = StackPatches.RewriteBagBurden(out int bagTotal);
            if (bagTotal > 0 && bagChanged > 0)
                StackPatches.Log.LogInfo(
                    $"[StackLimit999] 背包负重改写 {bagChanged}/{bagTotal} 个配置 × {StackConfig.BurdenMultiplier.Value} 倍");
        }
        catch (Exception e)
        {
            _failCount++;
            if (_failCount <= 3)
                StackPatches.Log.LogWarning("[StackLimit999] rewrite pass failed: " + e.Message);
        }
    }
}
