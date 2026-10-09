using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.QuickAction;

/// <summary>
/// 动作提速（独立 mod 5）v2.0.0：按 ActionType 官方分类统一改写 During。
///
/// 实证基础（2026-10-07 运行时 EffectDump + Il2CppDumper/反汇编，详见 docs）：
/// - 官方枚举 GameCore.HotUpdate.ActionType：Food=1 Rest=2 Entertainment=3 Exercise=4
///   Social=5 Treatment=6 Move=7(未用) Other=8 Free=9(未用) RepairFurniture=10；
///   表中另有 0 = 枚举无名默认值（家具功能桶）。
/// - 收益结算：Config_Effect 三段式 Start/Interval/End。Interval=周期结算（每 N 游戏秒
///   结算一次，如 瑜伽每600秒 士气+2/体力-1.67），总收益∝动作时长——**运行时守卫**：
///   动作 EffectConfigID 指向的效果任一 Interval 字段非零 ⇒ 永不改动该动作。
///   （实测黑名单 74 条：修理/加固本体系10、生吃冷冻32、跳舞/游戏机5、锻炼/发电3、
///   Social 全部5、Other 内部行为18。）
/// - Other(8)=官方大杂烩：真交互与内部行为混居。引用轴判定：动作被
///   Config_FurnitureFunc.ActionIds（家具按钮）或 Config_Item.UseAction（物品）引用
///   ⇒ 玩家可触发，可提速；三处皆不引用 ⇒ 内部行为（情绪播报/呕吐腹泻等），不动。
/// - 禁改：Rest=2（睡觉 During 兼任时间推进）、Social=5（全部周期结算）、
///   During<=0 的哨兵值动作（三者均不提供开关，防误设）。
/// - 机制不变：内存表直写 Config_Action.During（游戏秒），无 Harmony 不碰存档；
///   探索 TimeScale=120；只减不增守卫；0=恢复原值；Overrides 兜底优先级最高
///   （v2.1：覆盖表先于分类判定，可命中引用轴漏掉的家具本体 BT 交互，如上厕所 9075；
///   五分类默认统一 500，旧 cfg 一次性迁移 DefaultsV21）。
/// - 已实证无周期收益（可安全提速）：看书 100003xxx/100000004、空调吹风 1915（End 型
///   士气+5/健康+5）、上厕所 9075（效果全 0，收益走内急解除）、浇水 1615（植物状态另算）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class QuickActionPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.quickaction";
    public const string Name = "QuickAction";
    public const string Version = "2.1.1";

    /// <summary>探索时钟倍速（Config_Chapter 实测 120；游戏更新后体感异常再改此处重编译）。</summary>
    internal const int Scale = 120;

    internal static ManualLogSource Log;

    // 五个分类开关（毫秒；0=恢复原值）
    internal static ConfigEntry<int> ItemMs;    // 物品使用：Food=1 ∪ Treatment=6（吃/喝/品尝/吞咽/药品）
    internal static ConfigEntry<int> FurnMs;    // 家具功能：type=0（拆封包裹/种植/烹饪/升级/改装/救援建造）
    internal static ConfigEntry<int> MaintMs;   // 房屋维护：RepairFurniture=10（陷阱/安装家具/移动/拆除/出门）
    internal static ConfigEntry<int> MiscMs;    // 杂项交互：Other=8 ∩ 被家具按钮或物品引用（搜查/翻找/开关/拾取/家务）
    internal static ConfigEntry<int> FunMs;     // 娱乐锻炼：Entertainment=3 ∪ Exercise=4（看书/听音乐/按摩/运动/洗澡）
    internal static ConfigEntry<string> Overrides;

    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;

        // 安装引导勾选结果（ASCII 引导文件 BepInEx\config\quickaction.boot.ini，读后即删；
        // 仅作下述 Bind 的默认值——玩家已有 cfg 键永远优先，重装不覆盖调好的数值）
        var boot = ReadBoot();
        const string gsec = "分类修改";
        ItemMs = Config.Bind(gsec, "物品使用Ms", boot.item,
            "吃/喝/品尝/吞咽/药品等入口动作（Food+Treatment 分类，约 2750 条可改）。\n" +
            "挂周期结算效果的（32 条生吃冷冻类）自动跳过。0=恢复原值。");
        FurnMs = Config.Bind(gsec, "家具功能Ms", boot.furn,
            "家具按钮动作（分类 0，约 900 条：拆封包裹/种植/烹饪/升级大门/改装/救援建造）。\n" +
            "周期结算类自动跳过。0=恢复原值。");
        MaintMs = Config.Bind(gsec, "房屋维护Ms", boot.maint,
            "房屋维护动作（RepairFurniture 分类，约 78 条可改：布置/安装/移动/拆除陷阱、安装家具）。\n" +
            "修理/加固本体系 10 条为周期结算（收益∝耗时），自动跳过。0=恢复原值。");
        MiscMs = Config.Bind(gsec, "杂项交互Ms", boot.misc,
            "杂项交互（Other 分类中玩家可触发的，约 135 条：搜查/翻找/查看/开关电器/拾取/家务）。\n" +
            "内部行为（情绪播报/呕吐腹泻/系统动作）不被家具或物品引用，自动跳过。0=恢复原值。");
        FunMs = Config.Bind(gsec, "娱乐锻炼Ms", boot.fun,
            "娱乐与锻炼（Entertainment+Exercise 分类，约 173 条可改：看书/听音乐/按摩/运动/洗澡/空调吹风）。\n" +
            "跳舞/游戏机/锻炼/发电等周期结算自动跳过。0=恢复原值。");

        // v2.1 默认值迁移：五个分类统一默认 500（v2.0 里家具功能/娱乐锻炼默认 0）。
        // 只把"仍是旧默认 0"的项抬到 500；玩家主动设回 0 的在本次迁移后照常生效。
        var migrated = Config.Bind("迁移", "DefaultsV21", false,
            "v2.1 默认值迁移标记（五个分类开关统一默认 500）。勿手改。");
        if (!migrated.Value)
        {
            try
            {
                if (FurnMs.Value == 0) FurnMs.Value = 500;
                if (FunMs.Value == 0) FunMs.Value = 500;
            }
            catch { }
            migrated.Value = true;
        }

        Overrides = Config.Bind("其他", "Overrides", "",
            "上面没列到的动作单独覆盖：ID:毫秒 逗号分隔（优先级最高）。\n" +
            "例：100009001:500（观察便签）。ID:0 = 恢复原值。完整名单看 BepInEx\\ActionList.txt\n" +
            "（含类型列；删掉该文件下次启动会重新生成）。");

        Config.SettingChanged += (object s, BepInEx.Configuration.SettingChangedEventArgs e) => { Rerun = true; };

        StripLegacySections();

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<QuickTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<QuickTicker>();
        var go = new GameObject("QuickAction.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<QuickTicker>();
        Log.LogInfo($"[QuickAction] loaded v{Version}：分类引擎（物品使用/家具功能/房屋维护/杂项交互/娱乐锻炼 + 周期效果守卫 + 引用轴）");
    }

    /// <summary>
    /// 读安装引导写入的勾选结果（quickaction.boot.ini，ASCII：itemMs/furnMs/maintMs/miscMs/funMs），
    /// 读后删除。缺失或解析失败时用与引导默认勾选一致的内置默认值。
    /// </summary>
    private static (int item, int furn, int maint, int misc, int fun) ReadBoot()
    {
        var def = (item: 500, furn: 500, maint: 500, misc: 500, fun: 500);
        try
        {
            var p = System.IO.Path.Combine(Paths.BepInExRootPath, "config", "quickaction.boot.ini");
            if (!System.IO.File.Exists(p)) return def;
            var vals = new Dictionary<string, int>();
            foreach (var l in System.IO.File.ReadAllLines(p))
            {
                var seg = l.Trim();
                var i = seg.IndexOf('=');
                if (i <= 0) continue;
                if (int.TryParse(seg.Substring(i + 1).Trim(), out var v))
                    vals[seg.Substring(0, i).Trim().ToLowerInvariant()] = v;
            }
            try { System.IO.File.Delete(p); } catch { }
            return (
                vals.TryGetValue("itemms", out var a) ? a : def.item,
                vals.TryGetValue("furnms", out var b) ? b : def.furn,
                vals.TryGetValue("maintms", out var c) ? c : def.maint,
                vals.TryGetValue("miscms", out var d) ? d : def.misc,
                vals.TryGetValue("funms", out var e) ? e : def.fun);
        }
        catch { return def; }
    }

    /// <summary>
    /// 清理 cfg：整体移除 v1.x 的遗留段——[动作·xxx]（77 逐项）与 [整类设置]（4 整类），
    /// 以及 BepInEx 自动生成的「# Default value」行。[其他] 段保留（Overrides 键延续）。
    /// </summary>
    private void StripLegacySections()
    {
        try
        {
            var p = Config.ConfigFilePath;
            if (!System.IO.File.Exists(p)) return;
            var lines = System.IO.File.ReadAllLines(p);
            var kept = new List<string>();
            bool skipping = false;
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (t.StartsWith("["))
                {
                    skipping = t.StartsWith("[动作·", StringComparison.Ordinal) || t == "[整类设置]";
                    if (skipping) continue;
                }
                if (!skipping && !l.StartsWith("# Default value", StringComparison.Ordinal))
                    kept.Add(l);
            }
            if (kept.Count != lines.Length)
                System.IO.File.WriteAllLines(p, kept, new UTF8Encoding(false));
        }
        catch (Exception e) { Log.LogWarning("[QuickAction] cfg 清理失败: " + e.Message); }
    }

    /// <summary>解析覆盖表 "ID:毫秒,..."。value&lt;=0 表示恢复原值。</summary>
    internal static Dictionary<int, int> ParseOverrides(string s)
    {
        var dict = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(s)) return dict;
        foreach (var part in s.Split(',', ';'))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var idx = seg.LastIndexOf(':');
            if (idx <= 0) continue;
            if (!int.TryParse(seg.Substring(0, idx), out var id)) continue;
            if (!int.TryParse(seg.Substring(idx + 1), out var ms)) continue;
            dict[id] = ms;
        }
        return dict;
    }
}

internal sealed class QuickTicker : MonoBehaviour
{
    public QuickTicker(IntPtr ptr) : base(ptr) { }
    public QuickTicker() { }

    private float _check;
    private bool _announced;
    private bool _listWritten;

    // key: actionId -> 原始 During（幂等：永远按原值判断/恢复）
    private static readonly Dictionary<int, float> OrigDuring = new();

    public void Update()
    {
        var due = QuickActionPlugin.Rerun || _check >= 5f;
        if (!due) { _check += Time.deltaTime; return; }
        _check = 0f;

        var cm = GetCm();
        if (cm == null) return;
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict = null;
        try { dict = cm._Config_Action_Dict; } catch { }
        if (dict == null || dict.Count == 0) return;
        // 守卫依赖的三张表（效果/家具功能/物品）必须与动作表同时就绪，否则空集会导致守卫失效
        try
        {
            if (cm._Config_Effect_Dict == null || cm._Config_Effect_Dict.Count == 0) return;
            if (cm._Config_FurnitureFunc_Dict == null || cm._Config_FurnitureFunc_Dict.Count == 0) return;
            if (cm._Config_Item_Dict == null || cm._Config_Item_Dict.Count == 0) return;
        }
        catch { return; }

        if (!_listWritten)
        {
            var listPath = System.IO.Path.Combine(Paths.BepInExRootPath, "ActionList.txt");
            bool stale = true;
            try
            {
                if (System.IO.File.Exists(listPath))
                {
                    using var r = new System.IO.StreamReader(listPath);
                    stale = (r.ReadLine() ?? "").IndexOf("Type", StringComparison.Ordinal) < 0;
                }
            }
            catch { }
            if (stale || !System.IO.File.Exists(listPath))
            {
                _listWritten = true;
                WriteActionList(dict, listPath);
            }
            else _listWritten = true;
        }

        var (scanned, changed, restored, cat, skipped, lines) = Apply(dict, cm);
        QuickActionPlugin.Rerun = false;

        if (changed > 0 || restored > 0)
            foreach (var l in lines.Take(80)) QuickActionPlugin.Log.LogInfo("[QuickAction] " + l);

        if (!_announced)
        {
            _announced = true;
            QuickActionPlugin.Log.LogInfo(
                $"[QuickAction] v{QuickActionPlugin.Version} 生效：物品使用×{cat.item} 家具功能×{cat.furn} " +
                $"房屋维护×{cat.maint} 杂项交互×{cat.misc} 娱乐锻炼×{cat.fun}；恢复 {restored}" +
                $"（表 {scanned} 条；守卫跳过：周期效果 {skipped.interval}、禁改类 {skipped.banned}、" +
                $"内部行为 {skipped.unref}、瞬时 {skipped.instant}）。");
            try
            {
                var head = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] QuickAction {QuickActionPlugin.Version} 分类引擎首次生效（表 {scanned} 条）：",
                    $"分类改写：物品使用×{cat.item}、家具功能×{cat.furn}、房屋维护×{cat.maint}、杂项交互×{cat.misc}、娱乐锻炼×{cat.fun}；恢复 {restored}",
                    $"守卫跳过：周期结算效果（收益∝耗时）{skipped.interval}、禁改类（睡觉/社交）{skipped.banned}、内部行为（无引用 Other）{skipped.unref}、瞬时哨兵（During<=0）{skipped.instant}",
                    "换算：目标毫秒 ÷1000 ×" + QuickActionPlugin.Scale + " = 游戏秒（During）",
                    "---- 改写明细（原值 -> 当前值）----"
                };
                System.IO.File.WriteAllLines(
                    System.IO.Path.Combine(Paths.BepInExRootPath, "QuickActionTrace.txt"),
                    head.Concat(lines));
            }
            catch { }
        }
    }

    private static GameCore.HotUpdate.ConfigManager GetCm()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return null; }
    }

    private static string NameOf(GameCore.HotUpdate.Config_Action a)
    {
        try { return a.Name_Local ?? a.Name ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// BT 硬编码玩家交互动作（家具本体交互链：Config_Furniture.ActionId 全=2 通用开场，
    /// 具体动作在 Battle.Logic.E_Action_* 行为节点硬编码，不被 FurnitureFunc/Item 任何表引用
    /// ——引用轴结构性盲区）。全部经 EffectDump 实证无周期结算收益（效果全 0 或 End 型）。
    /// 运行时监视（ActionWatch：BattleLogicWorld._ActionManager.AgentActionSourceDict）发现新动作后在此补充。
    /// </summary>
    internal static readonly HashSet<int> BtInteractIds = new()
    {
        9075,         // 上厕所（马桶家具本体，E_Action_Furniture_Bar_Toilet）
        1611, 9130,   // 正在种植（农活链 播种1610→正在种植→翻土1618，玩家种植的主体动作 7.5 秒）
    };

    /// <summary>
    /// 分类判定（每轮扫描现算；游戏更新新增内容自动归类）。
    /// 返回 0=不在任何可改分类（禁改/守卫），1=物品使用, 2=家具功能, 3=房屋维护, 4=杂项交互, 5=娱乐锻炼。
    /// </summary>
    private static int CategoryOf(int id, int type, HashSet<int> referenced)
    {
        if (type == 2 || type == 5) return 0;                       // Rest / Social：禁改
        if (type == 1 || type == 6) return 1;                       // Food / Treatment
        if (type == 0) return 2;                                    // 家具功能桶
        if (type == 10) return 3;                                   // RepairFurniture
        if (type == 3 || type == 4) return 5;                       // Entertainment / Exercise
        if (type == 8 && (referenced.Contains(id) || BtInteractIds.Contains(id))) return 4;  // Other ∩ (引用 ∪ BT交互名单)
        return 0;
    }

    /// <summary>
    /// 周期结算效果集（按收益方向二分，2026-10-09 EffectDump 全表实证）：
    /// ItemAttrInterval/BuffInterval 是"每 N 游戏秒结算一次"的周期长度参数（恒正），
    /// 不参与收益方向判断；方向只看数值字段（Item*/Buff* 的 Satiety/Morale/...Interval）。
    /// - positive：任一数值 > 0（正收益∝耗时）——永不改动；
    /// - drain：数值全 ≤0 且至少一个 < 0（纯消耗型，如上厕所 112 每周期饱食-5、
    ///   结束固定 +士气+5/+健康+3）——加速对玩家有利，但仅 Other(8) 交互放行；
    ///   修理(T10)/发电(T4)等劳动型核心收益（修复量/发电量）∝时长且结算在
    ///   Effect 表外，同样保持拦截；
    /// - 周期字段非零但数值全 0：语义未知，保守并入 positive。
    /// </summary>
    private static (HashSet<int> positive, HashSet<int> drain) BuildIntervalEffects(GameCore.HotUpdate.ConfigManager cm)
    {
        var positive = new HashSet<int>();
        var drain = new HashSet<int>();
        try
        {
            foreach (var kv in cm._Config_Effect_Dict)
            {
                var e = kv.Value;
                if (e == null) continue;
                try
                {
                    bool hasPos = e.ItemSatietyInterval > 0f || e.ItemMoraleInterval > 0f || e.ItemStaminaInterval > 0f ||
                                  e.ItemHealthInterval > 0f || e.ItemVitalityInterval > 0f ||
                                  e.BuffSatietyInterval > 0f || e.BuffMoraleInterval > 0f || e.BuffStaminaInterval > 0f ||
                                  e.BuffHealthInterval > 0f || e.BuffVitalityInterval > 0f;
                    bool hasNeg = e.ItemSatietyInterval < 0f || e.ItemMoraleInterval < 0f || e.ItemStaminaInterval < 0f ||
                                  e.ItemHealthInterval < 0f || e.ItemVitalityInterval < 0f ||
                                  e.BuffSatietyInterval < 0f || e.BuffMoraleInterval < 0f || e.BuffStaminaInterval < 0f ||
                                  e.BuffHealthInterval < 0f || e.BuffVitalityInterval < 0f;
                    bool hasPeriod = e.ItemAttrInterval != 0f || e.BuffInterval != 0f;
                    if (!hasPos && !hasNeg && !hasPeriod) continue;
                    if (hasPos || (hasPeriod && !hasNeg)) positive.Add(kv.Key);
                    else drain.Add(kv.Key);
                }
                catch { }
            }
        }
        catch { }
        return (positive, drain);
    }

    /// <summary>引用集：被家具功能按钮（ActionIds）或物品使用（UseAction）引用的动作 ID。</summary>
    private static HashSet<int> BuildReferenced(GameCore.HotUpdate.ConfigManager cm)
    {
        var set = new HashSet<int>();
        try
        {
            foreach (var kv in cm._Config_FurnitureFunc_Dict)
            {
                var f = kv.Value;
                if (f == null) continue;
                try
                {
                    var ids = f.ActionIds;
                    if (ids != null)
                        for (int i = 0; i < ids.Count; i++)
                            set.Add(ids[i]);
                }
                catch { }
            }
        }
        catch { }
        try
        {
            foreach (var kv in cm._Config_Item_Dict)
            {
                var it = kv.Value;
                if (it == null) continue;
                try { var ua = it.UseAction; if (ua != 0) set.Add(ua); } catch { }
            }
        }
        catch { }
        return set;
    }

    private static (int scanned, int changed, int restored,
        (int item, int furn, int maint, int misc, int fun) cat,
        (int interval, int banned, int unref, int instant) skipped,
        List<string> lines) Apply(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict,
        GameCore.HotUpdate.ConfigManager cm)
    {
        var lines = new List<string>();
        var overrides = QuickActionPlugin.ParseOverrides(QuickActionPlugin.Overrides.Value);
        var (positiveEffects, drainEffects) = BuildIntervalEffects(cm);
        var referenced = BuildReferenced(cm);

        int itemMs = QuickActionPlugin.ItemMs.Value, furnMs = QuickActionPlugin.FurnMs.Value,
            maintMs = QuickActionPlugin.MaintMs.Value, miscMs = QuickActionPlugin.MiscMs.Value,
            funMs = QuickActionPlugin.FunMs.Value;

        int scanned = 0, restored = 0;
        int cItem = 0, cFurn = 0, cMaint = 0, cMisc = 0, cFun = 0;
        int sInterval = 0, sBanned = 0, sUnref = 0, sInstant = 0;
        try
        {
            foreach (var kv in dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                scanned++;

                float cur = 0f;
                try { cur = a.During; } catch { continue; }

                OrigDuring.TryAdd(kv.Key, cur);
                float orig = OrigDuring[kv.Key];

                // 周期结算守卫：正收益∝耗时的动作绝不改动（若历史会话被改过则自愈恢复）
                int ecid = 0;
                try { ecid = a.EffectConfigID; } catch { }
                if (ecid != 0 && positiveEffects.Contains(ecid))
                {
                    sInterval++;
                    if (Math.Abs(cur - orig) > 1e-6f) { a.During = orig; restored++; }
                    continue;
                }
                if (ecid != 0 && drainEffects.Contains(ecid))
                {
                    // 纯消耗型周期效果（数值全≤0，如上厕所 112 饱食-5/周期）：
                    // 仅 Other(8) 且玩家可触发（引用 ∪ BT 交互名单）放行提速——上得快=消耗少，
                    // 固定 End 收益照拿；其余类型（修理/加固/发电/娱乐等）核心收益∝时长，照旧拦截
                    int dtype = 0;
                    try { dtype = a.ActionType; } catch { }
                    if (!(dtype == 8 && (referenced.Contains(kv.Key) || BtInteractIds.Contains(kv.Key))))
                    {
                        sInterval++;
                        if (Math.Abs(cur - orig) > 1e-6f) { a.During = orig; restored++; }
                        continue;
                    }
                }

                if (orig <= 0f) { sInstant++; continue; }   // During<=0 哨兵值

                int atype = 0;
                try { atype = a.ActionType; } catch { }

                // 覆盖表：用户显式指定，优先于分类判定——可命中引用轴漏掉的动作
                // （如上厕所 9075 等家具本体 BT 交互，不被任何配置表引用）。
                // 唯一仍高于它的是上面的周期结算守卫（收益∝耗时的动作绝不碰）。
                if (overrides.TryGetValue(kv.Key, out var oms))
                {
                    float otarget = oms <= 0 ? orig : oms / 1000f * QuickActionPlugin.Scale;
                    if (Math.Abs(cur - otarget) > 1e-6f)
                    {
                        a.During = otarget;
                        bool orestore = Math.Abs(otarget - orig) <= 1e-6f;
                        if (orestore) restored++;
                        if (lines.Count < 400)
                            lines.Add($"ACTION {kv.Key}({NameOf(a)}) During {cur:0.###} -> {otarget:0.###}" +
                                      (orestore ? "（恢复原值）" : $"（{(otarget / QuickActionPlugin.Scale * 1000):0}ms·覆盖表）"));
                    }
                    continue;
                }

                int cat = CategoryOf(kv.Key, atype, referenced);
                if (cat == 0)
                {
                    if (atype == 8) sUnref++; else sBanned++;
                    if (Math.Abs(cur - orig) > 1e-6f) { a.During = orig; restored++; }
                    continue;
                }

                float target = orig;
                string tag = null;
                {
                    var ms = cat switch { 1 => itemMs, 2 => furnMs, 3 => maintMs, 4 => miscMs, 5 => funMs, _ => 0 };
                    if (ms <= 0) target = orig;
                    else
                    {
                        // 只减不增：原本更快的动作不会被放慢
                        target = Math.Min(orig, ms / 1000f * QuickActionPlugin.Scale);
                        if (Math.Abs(target - orig) <= 1e-6f) tag = null;
                        else tag = cat == 1 ? "物品使用" : cat == 2 ? "家具功能" : cat == 3 ? "房屋维护"
                                 : cat == 4 ? "杂项交互" : "娱乐锻炼";
                    }
                }

                if (Math.Abs(cur - target) > 1e-6f)
                {
                    a.During = target;
                    bool isRestore = Math.Abs(target - orig) <= 1e-6f;
                    if (isRestore) restored++;
                    else
                    {
                        if (tag == "物品使用") cItem++;
                        else if (tag == "家具功能") cFurn++;
                        else if (tag == "房屋维护") cMaint++;
                        else if (tag == "杂项交互") cMisc++;
                        else if (tag == "娱乐锻炼") cFun++;
                    }
                    if (lines.Count < 400)
                        lines.Add($"ACTION {kv.Key}({NameOf(a)}) During {cur:0.###} -> {target:0.###}" +
                                  (isRestore ? "（恢复原值）" : $"（{(target / QuickActionPlugin.Scale * 1000):0}ms·{tag}）"));
                }
            }
        }
        catch (Exception e) { lines.Add("WARN 扫描中断: " + e.Message); }

        return (scanned, cItem + cFurn + cMaint + cMisc + cFun, restored,
            (cItem, cFurn, cMaint, cMisc, cFun), (sInterval, sBanned, sUnref, sInstant), lines);
    }

    /// <summary>ActionList.txt：全表（ID|名称|类型|原始During游戏秒|真实秒），按耗时降序。
    /// 含类型列的 v2 格式；检测到旧格式（无 Type 列）会重写一次。</summary>
    private static void WriteActionList(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict, string path)
    {
        try
        {
            using var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false));
            w.WriteLine("== Config_Action 全表（ID|名称|ActionType|原始During游戏秒|真实秒按TimeScale=120）。删掉本文件下次启动会重新生成 ==");
            var rows = new List<(float d, string line)>();
            foreach (var kv in dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                float d = 0f; string n = "?"; int t = 0;
                try { d = a.During; n = a.Name_Local ?? a.Name ?? "?"; t = a.ActionType; } catch { }
                rows.Add((d, $"{kv.Key}|{n}|{t}|{d:0.###}|{d / 120f:0.##}"));
            }
            rows.Sort((x, y) => y.d.CompareTo(x.d));
            foreach (var r in rows) w.WriteLine(r.line);
            QuickActionPlugin.Log.LogInfo($"[QuickAction] ActionList.txt 已生成（{rows.Count} 条，v2 含类型列）");
        }
        catch (Exception e) { QuickActionPlugin.Log.LogWarning("[QuickAction] ActionList 写入失败: " + e.Message); }
    }
}
