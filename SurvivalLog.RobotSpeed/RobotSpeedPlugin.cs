using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.RobotSpeed;

/// <summary>
/// 机器人提速（独立 mod）v1.2.0。
///
/// 三机器人（修理 WX-5 / 整理 HK / 种植 GR）统一提速：内存配置表直写 + 在场机器人直通，
/// 无 Harmony、不碰存档，删 dll 即还原；重启游戏后读档时机器人 OnInit 拿到新值生效。
///
/// 覆盖：动作变慢系数清零（对齐手速）、移速/充电/续航倍率、耗电降低、机器人专属动作时长、
/// 货舱扩容（含 WX-5/TB-3 字典盲区兜底）。
/// **明确不做（作者定版 2026-10-13）：插槽与模块相关的一切修改**——官方扩展槽开放门控是
/// 硬编码的"装着插槽扩展模块"判断，数量/补发/代装/门控强写四方案实测后全部否决，课题封存待独立新 mod。
///
/// 实证基础（Il2CppDumper + Iced 反汇编 + 三轮装机实测）：
/// - 动作时长公式（ActionComponent.PlayAction）：
///   EffectiveDuring = Config_Action.During × max(0.1, 1 + WorkSpeedRatio) ÷ (1 + Σ工速模块)
///   → Config_RobotAttribute.WorkSpeedRatio 是官方"机器人比人慢"加成（=1 即 2 倍时长），
///     置 0 = 动作时长对齐手动；工速模块的加速除数继续生效。
/// - 模块加成与基础值天然分离（RecalcNumericModules 反汇编）：
///   充电cfg = 表值×(1+Σ快充模块)、电量上限 = 表值×(1+Σ电池模块)、移速模块走独立属性增量。
///   本 mod 只改表基础值 → 模块加成全部照常叠加（"倍率=初始系数调整"语义）。
///   绝不直接写机器人内部字段（0x84/0x88 等会在模块重算时被覆盖回 表值×模块）。
/// - RobotAttrs 读取守卫：BatteryTrips/TripCost/ChargeTripsPerHour 有 &gt;0 门槛
///   （≤0 回退硬编码默认值），故 TripCost 减半下限夹到 1；WorkSpeedRatio/MoveSpeed 裸透传。
/// - 机器人专属动作行（与玩家不共用）：9098 去取 / 9099 去放 / 9100 放下 /
///   9130 正在种植 / 9131 去取(种植取种)；During 单位游戏秒（TimeScale=120，300=2.5 真实秒）。
///   回座 9101 等行为 -1 哨兵（走到为止），绝不改。
/// - 货舱容量 = Config_Bag 行 Size（官方"以 Bag 表为准"，实时直读改完即生效）。
///   CargoExpandBagIds 字典只有 HK 两行（1061 基础/1063 扩展档）→ HK 走字典双行放大；
///   WX-5/TB-3 靠在场机器人直通（agentList 扫描读 _cargoBaseBagId）兜底。
/// - 全部幂等：进程内首次读到的值缓存为原值，轮询永远按 原值×倍率 重写
///  （配置表可能随切场景/读档整体重载回原值，轮询自动恢复；世界就绪首轮 0.5s 快扫抢在
///   玩家打开面板前完成货舱改写，之后回落 5s）。
/// - General.Enabled 总开关（2026-10-24，ModCenter 启停位）：false=按原值缓存一次性还原全部
///   已改表值并停写；下次启动不创建 Ticker（零日志零改写）。cfg 写入经 SettingChanged 热生效。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class RobotSpeedPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.robotspeed";
    public const string Name = "RobotSpeed";
    public const string Version = "1.3.8"; // 由 publish.ps1 随 version.json 同步

    internal static ManualLogSource Log;

    // ---- 配置项（默认翻倍；1 或 false = 不改/恢复原值）----
    // General.Enabled 总开关（ModCenter 启停位，2026-10-24）：false=按原值缓存还原全部已改表值
    // 并停写；下次启动不加载（不创建 Ticker，零日志）。
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> WorkSpeedZero;  // WorkSpeedRatio → 0（开关语义，无倍率）
    internal static ConfigEntry<float> MoveSpeedMult; // 移速倍率
    internal static ConfigEntry<float> ChargeRateMult;// 充电速率倍率
    internal static ConfigEntry<float> BatteryMult;   // 续航倍率
    internal static ConfigEntry<float> TripCostMult;  // 每趟耗电：按此倍率降低（2=耗电减半）
    internal static ConfigEntry<float> ActionTimeMult;// 机器人专属动作时长：除以此倍率
    internal static ConfigEntry<float> CargoSlotsMult;// 货舱宽度倍率（格数同倍）

    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;

        // General.Enabled 放最前 Bind（保证 cfg 键已生成后才可能早退）
        Enabled = Config.Bind("General", "Enabled", true, "总开关：false=还原官方原值并停写；下次启动不加载。");

        const string sec = "机器人提速";
        WorkSpeedZero = Config.Bind(sec, "动作速度对齐手动", true,
            "把官方给机器人动作内置的变慢系数 WorkSpeedRatio 置 0（机器人动作时长与手动操作一致）。\n" +
            "工速模块的加速效果不受影响，继续在基础上叠加。\ntrue=开启，false=恢复官方原值。");
        MoveSpeedMult = Config.Bind(sec, "移速倍率", 2f,
            "机器人移动速度倍率（作用于机型基础移速；移速模块加成照常叠加）。\n1=不变。");
        ChargeRateMult = Config.Bind(sec, "充电速率倍率", 2f,
            "充电速率倍率（每趟电量的回充时间等比缩短；快充模块加成照常叠加）。\n1=不变。");
        BatteryMult = Config.Bind(sec, "续航倍率", 2f,
            "电池续航趟数倍率（电池模块加成照常叠加）。\n1=不变。");
        TripCostMult = Config.Bind(sec, "耗电降低倍率", 2f,
            "每趟耗电按此倍率降低：2=耗电减半，4=四分之一。下限保护：至少为 1。\n" +
            "1=不变。想保留原版充电压力就设 1。");
        ActionTimeMult = Config.Bind(sec, "动作时长倍率", 2f,
            "机器人专属动作（去取/去放/放下/正在种植）时长除以此倍率。\n" +
            "下限保护：单动作不低于 0.3 真实秒（36 游戏秒），防动画/音效爆闪。\n" +
            "只影响机器人专属动作行，不碰玩家动作（修理/农业等共用行请用 QuickAction）。1=不变。");
        CargoSlotsMult = Config.Bind(sec, "货舱格数倍率", 2f,
            "机器人货舱宽度倍率（格数同倍，默认 2 格→4 格；扩舱模块换用的大货舱档不受影响）。\n" +
            "1=不变。机器人面板打开一次即可看到新格数。");

        Config.SettingChanged += (object s, SettingChangedEventArgs e) => { Rerun = true; };

        if (!Enabled.Value) return; // General.Enabled=false：不启 Ticker、零日志（表值本会话从未改过）

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RobotTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<RobotTicker>();
        var go = new GameObject("RobotSpeed.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<RobotTicker>();
        Log.LogInfo($"[RobotSpeed] loaded v{Version}：机器人提速（表直写：动作系数/移速/充电/续航/耗电/动作时长/货舱；不含插槽与模块）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }
}

internal sealed class RobotTicker : MonoBehaviour
{
    public RobotTicker(IntPtr ptr) : base(ptr) { }
    public RobotTicker() { }

    private float _check;
    private bool _announced;
    // General.Enabled 门：本轮会话是否已按配置落笔过（false 化时据此触发一次性还原）
    private bool _applied;

    // 幂等原值缓存（key: 配置行主键）
    private static readonly Dictionary<int, int> OrigBattery = new();
    private static readonly Dictionary<int, int> OrigTripCost = new();
    private static readonly Dictionary<int, int> OrigCharge = new();
    private static readonly Dictionary<int, float> OrigWorkRatio = new();
    private static readonly Dictionary<int, float> OrigMoveSpeed = new();
    private static readonly Dictionary<int, float> OrigActionDuring = new();
    private static readonly Dictionary<int, int> OrigBagW = new();
    private static readonly Dictionary<int, int> OrigBagH = new();

    // 机器人专属动作行（9101 回座 = -1 哨兵绝不在列）。
    // 注意 9130 正在种植实际与玩家共用（v1.2.0 误判专属；让位守卫保证与 QuickAction 不打架）
    private static readonly int[] RobotActionIds = { 9098, 9099, 9100, 9130, 9131 };
    // 动作时长下限（游戏秒；0.3 真实秒 × TimeScale 120）
    private const float MinActionDuring = 36f;

    // 全量 0.5s 轮询：① 进档首轮抢在玩家打开面板前完成货舱兜底改写；② 装卸模块会瞬间重算
    // 门控位为关（面板立即显示槽关闭），0.5s 内补开并脏推送刷新面板——5s 节奏实测观感为"失效"。
    // 各轮扫描均为轻量遍历（几张小表 + agentList 直扫），0.5s 无压力。
    public void Update()
    {
        // General.Enabled 总开关：false=立即按原值缓存还原全部已改表值并停写（Load 阶段 false 时
        // Ticker 不会创建，本分支覆盖"运行中被关闭"——ModCenter 写 cfg 经 SettingChanged 热生效）。
        if (!RobotSpeedPlugin.Enabled.Value)
        {
            if (_applied)
            {
                var rl = new List<string>();
                if (RestoreAll(rl)) // 表未就绪返回 false，下一帧重试
                {
                    _applied = false;
                    RobotSpeedPlugin.Log.LogInfo("[RobotSpeed] General.Enabled=false：已还原官方原值并停写"
                        + (rl.Count > 0 ? $"（回写 {rl.Count} 处）" : "（表已在原值）"));
                    foreach (var l in rl.Take(30)) RobotSpeedPlugin.Log.LogInfo("[RobotSpeed] " + l);
                }
            }
            return;
        }

        var due = RobotSpeedPlugin.Rerun || _check >= 0.5f;
        if (!due) { _check += Time.deltaTime; return; }
        _check = 0f;

        GameCore.HotUpdate.ConfigManager cm;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return;
            cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return; }
        if (cm == null) return;

        var lines = new List<string>();
        int changes = 0;

        changes += ApplyRobotAttributes(cm, lines);
        changes += ApplyRobotActions(cm, lines);
        changes += ApplyCargoBags(cm, lines);
        ApplyLiveRobots(cm, lines);
        RobotSpeedPlugin.Rerun = false;
        _applied = true; // 已按当前配置落笔（General.Enabled 关闭时据此触发还原）

        if (changes > 0 || lines.Count > 0)
        {
            foreach (var l in lines.Take(60)) RobotSpeedPlugin.Log.LogInfo("[RobotSpeed] " + l);

            if (!_announced)
            {
                _announced = true;
            RobotSpeedPlugin.Log.LogInfo(
                $"[RobotSpeed] v{RobotSpeedPlugin.Version} 生效：系数清零={RobotSpeedPlugin.WorkSpeedZero.Value} " +
                $"移速×{RobotSpeedPlugin.MoveSpeedMult.Value:0.##} 充电×{RobotSpeedPlugin.ChargeRateMult.Value:0.##} " +
                $"续航×{RobotSpeedPlugin.BatteryMult.Value:0.##} 耗电÷{RobotSpeedPlugin.TripCostMult.Value:0.##} " +
                $"动作÷{RobotSpeedPlugin.ActionTimeMult.Value:0.##} 货舱×{RobotSpeedPlugin.CargoSlotsMult.Value:0.##}（本次改写 {changes} 处；不含插槽与模块）");
            try
            {
                var head = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RobotSpeed {RobotSpeedPlugin.Version} 首次生效（改写 {changes} 处）：",
                    "换算：动作 During 单位=游戏秒，÷120=真实秒；充电=游戏小时制（秒/趟=3600÷速率）",
                    "---- 改写明细（原值 -> 当前值）----"
                };
                System.IO.File.WriteAllLines(
                    System.IO.Path.Combine(Paths.BepInExRootPath, "RobotSpeedTrace.txt"),
                    head.Concat(lines));
            }
            catch { }
            }
        }
    }

    /// <summary>机型属性表：WorkSpeedRatio→0、移速/充电/续航倍率、耗电降低。返回改写数。</summary>
    private static int ApplyRobotAttributes(GameCore.HotUpdate.ConfigManager cm, List<string> lines)
    {
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RobotAttribute> dict = null;
        try { dict = cm._Config_RobotAttribute_Dict; } catch { }
        if (dict == null || dict.Count == 0) return 0;

        int changed = 0;
        var workZero = RobotSpeedPlugin.WorkSpeedZero.Value;
        var mMove = Sanitize(RobotSpeedPlugin.MoveSpeedMult.Value);
        var mCharge = Sanitize(RobotSpeedPlugin.ChargeRateMult.Value);
        var mBatt = Sanitize(RobotSpeedPlugin.BatteryMult.Value);
        var mTrip = Sanitize(RobotSpeedPlugin.TripCostMult.Value);

        foreach (var pair in dict)
        {
            var cfg = pair.Value;
            if (cfg == null) continue;
            int id = pair.Key;

            try
            {
                OrigWorkRatio.TryAdd(id, cfg.WorkSpeedRatio);
                OrigMoveSpeed.TryAdd(id, cfg.MoveSpeed);
                OrigBattery.TryAdd(id, cfg.BatteryTrips);
                OrigTripCost.TryAdd(id, cfg.TripCost);
                OrigCharge.TryAdd(id, cfg.ChargeTripsPerHour);

                float wantRatio = workZero ? 0f : OrigWorkRatio[id];
                float wantMove = OrigMoveSpeed[id] * mMove;
                int wantBatt = Math.Max(1, (int)Math.Round(OrigBattery[id] * mBatt, MidpointRounding.AwayFromZero));
                // >0 守卫：官方对 ≤0 回退硬编码默认，故下限夹 1
                int wantTrip = Math.Max(1, (int)Math.Round(OrigTripCost[id] / mTrip, MidpointRounding.AwayFromZero));
                int wantCharge = Math.Max(1, (int)Math.Round(OrigCharge[id] * mCharge, MidpointRounding.AwayFromZero));

                if (Math.Abs(cfg.WorkSpeedRatio - wantRatio) > 1e-6f) { cfg.WorkSpeedRatio = wantRatio; changed++; lines.Add($"ROBOT {id}({cfg.Comment}) WorkSpeedRatio {OrigWorkRatio[id]:0.###} -> {wantRatio:0.###}"); }
                if (Math.Abs(cfg.MoveSpeed - wantMove) > 1e-6f) { cfg.MoveSpeed = wantMove; changed++; lines.Add($"ROBOT {id}({cfg.Comment}) MoveSpeed {OrigMoveSpeed[id]:0.###} -> {wantMove:0.###}"); }
                if (cfg.BatteryTrips != wantBatt) { cfg.BatteryTrips = wantBatt; changed++; lines.Add($"ROBOT {id}({cfg.Comment}) BatteryTrips {OrigBattery[id]} -> {wantBatt}"); }
                if (cfg.TripCost != wantTrip) { cfg.TripCost = wantTrip; changed++; lines.Add($"ROBOT {id}({cfg.Comment}) TripCost {OrigTripCost[id]} -> {wantTrip}"); }
                if (cfg.ChargeTripsPerHour != wantCharge) { cfg.ChargeTripsPerHour = wantCharge; changed++; lines.Add($"ROBOT {id}({cfg.Comment}) ChargeTripsPerHour {OrigCharge[id]} -> {wantCharge}"); }
            }
            catch (Exception e) { lines.Add($"WARN ROBOT {id} 改写异常: {e.Message}"); }
        }
        return changed;
    }

    /// <summary>机器人专属动作行：During = 原值 ÷ 倍率，下限 36 游戏秒，只减不增。</summary>
    private static int ApplyRobotActions(GameCore.HotUpdate.ConfigManager cm, List<string> lines)
    {
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict = null;
        try { dict = cm._Config_Action_Dict; } catch { }
        if (dict == null || dict.Count == 0) return 0;

        var mult = Sanitize(RobotSpeedPlugin.ActionTimeMult.Value);
        int changed = 0;
        foreach (var id in RobotActionIds)
        {
            if (!dict.TryGetValue(id, out var act) || act == null)
            { lines.Add($"ACTION {id} 不在表中，跳过"); continue; }

            try
            {
                float cur = act.During;
                OrigActionDuring.TryAdd(id, cur);
                float orig = OrigActionDuring[id];
                if (orig <= 0f) { lines.Add($"ACTION {id}({Name(act)}) During={orig}（哨兵/瞬时），跳过"); continue; }

                float want = Math.Max(MinActionDuring, orig / mult);
                if (want > orig) want = orig; // 只减不增（倍率 <1 时也不放慢，防误设）
                // 让位守卫（2026-10-17）：9130 正在种植与玩家共用（QuickAction 也提速它，
                // v1.2.0"机器人专属"的判断有误）；当前值已被提得更快（≤目标）时不动——
                // 否则两个 mod 每 5 秒互相覆盖（实测日志各刷 200+ 条）。
                if (cur <= want + 1e-6f) continue;
                if (Math.Abs(cur - want) > 1e-6f)
                { act.During = want; changed++; lines.Add($"ACTION {id}({Name(act)}) During {orig:0.#} -> {want:0.#} 游戏秒（÷{mult:0.##}）"); }
            }
            catch (Exception e) { lines.Add($"WARN ACTION {id} 改写异常: {e.Message}"); }
        }
        return changed;
    }

    /// <summary>
    /// 货舱：CargoExpandBagIds 键=基础货舱行、值=装扩舱模块后换用的扩展档行，
    /// 两排同倍放大宽度（官方"模块档更大"的递进关系保持），装模块不会盖掉 mod 效果。
    /// </summary>
    private static int ApplyCargoBags(GameCore.HotUpdate.ConfigManager cm, List<string> lines)
    {
        var mult = Sanitize(RobotSpeedPlugin.CargoSlotsMult.Value);

        var bagIds = new HashSet<int>();
        try
        {
            var map = GameCore.HotUpdate.Battle.Logic.RobotComponent.CargoExpandBagIds;
            if (map != null)
                foreach (var kv in map) { bagIds.Add(kv.Key); bagIds.Add(kv.Value); }
        }
        catch (Exception e) { lines.Add("WARN CargoExpandBagIds 读取失败（货舱项本次跳过）: " + e.Message); }
        if (bagIds.Count == 0)
        { lines.Add("货舱：未取到货舱 bagId 名单（CargoExpandBagIds 空），跳过"); return 0; }

        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Bag> dict = null;
        try { dict = cm._Config_Bag_Dict; } catch { }
        if (dict == null || dict.Count == 0) return 0;

        int changed = 0;
        foreach (var id in bagIds)
            changed += RewriteBagRow(dict, id, mult, lines);

        // 尺寸白名单懒重建（与 BagExpand 同机制；机器人货舱 UI/容量校验共用该白名单）
        if (changed > 0)
        { try { cm._bagSizeSet = null; } catch (Exception e) { lines.Add("WARN _bagSizeSet 置空失败: " + e.Message); } }
        return changed;
    }

    /// <summary>幂等放大单条 Config_Bag 行宽度（原值缓存，重复执行永远按 原值×倍率）。返回 1=发生改写。</summary>
    private static int RewriteBagRow(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Bag> dict,
        int id, float mult, List<string> lines)
    {
        try
        {
            if (!dict.TryGetValue(id, out var bag) || bag == null) return 0;
            var size = bag.Size;
            if (size == null || size.Count < 2) return 0;
            int w0 = size[0], h0 = size[1];
            if (w0 <= 0 || h0 <= 0) return 0;
            OrigBagW.TryAdd(id, w0);
            OrigBagH.TryAdd(id, h0);

            int wantW = Math.Max(1, (int)Math.Round(OrigBagW[id] * mult, MidpointRounding.AwayFromZero));
            if (w0 != wantW)
            {
                size[0] = wantW;
                lines.Add($"BAG {id}({Name(bag)}) Size {w0}x{h0} -> {wantW}x{h0}（货舱格数 ×{mult:0.##}）");
                return 1;
            }
        }
        catch (Exception e) { lines.Add($"WARN BAG {id} 改写异常: {e.Message}"); }
        return 0;
    }

    /// <summary>
    /// General.Enabled=false 还原：把已改写的表值按原值缓存全部回写官方原值
    /// （机型属性五字段 / 机器人动作行 / 货舱行宽高）。返回 false=配置表尚未就绪（下轮重试）。
    /// 边界：已加载机器人的内部快照字段（0x84/0x88 等）不回滚——模块重算或下次读档自然回
    /// 官方值，与"删 dll 即还原"同边界；9130 等与 QuickAction 共用行按各自缓存独立管理。
    /// </summary>
    private static bool RestoreAll(List<string> lines)
    {
        GameCore.HotUpdate.ConfigManager cm;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return false;
            cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return false; }
        if (cm == null) return false;

        int n = 0;
        bool bagChanged = false;

        try
        {
            var dict = cm._Config_RobotAttribute_Dict;
            if (dict != null)
            {
                foreach (var kv in OrigWorkRatio)
                {
                    if (!dict.TryGetValue(kv.Key, out var c) || c == null) continue;
                    if (Math.Abs(c.WorkSpeedRatio - kv.Value) > 1e-6f)
                    { c.WorkSpeedRatio = kv.Value; n++; lines.Add($"ROBOT {kv.Key}({c.Comment}) WorkSpeedRatio -> {kv.Value:0.###}（还原）"); }
                }
                foreach (var kv in OrigMoveSpeed)
                {
                    if (!dict.TryGetValue(kv.Key, out var c) || c == null) continue;
                    if (Math.Abs(c.MoveSpeed - kv.Value) > 1e-6f)
                    { c.MoveSpeed = kv.Value; n++; lines.Add($"ROBOT {kv.Key}({c.Comment}) MoveSpeed -> {kv.Value:0.###}（还原）"); }
                }
                foreach (var kv in OrigBattery)
                {
                    if (!dict.TryGetValue(kv.Key, out var c) || c == null) continue;
                    if (c.BatteryTrips != kv.Value)
                    { c.BatteryTrips = kv.Value; n++; lines.Add($"ROBOT {kv.Key}({c.Comment}) BatteryTrips -> {kv.Value}（还原）"); }
                }
                foreach (var kv in OrigTripCost)
                {
                    if (!dict.TryGetValue(kv.Key, out var c) || c == null) continue;
                    if (c.TripCost != kv.Value)
                    { c.TripCost = kv.Value; n++; lines.Add($"ROBOT {kv.Key}({c.Comment}) TripCost -> {kv.Value}（还原）"); }
                }
                foreach (var kv in OrigCharge)
                {
                    if (!dict.TryGetValue(kv.Key, out var c) || c == null) continue;
                    if (c.ChargeTripsPerHour != kv.Value)
                    { c.ChargeTripsPerHour = kv.Value; n++; lines.Add($"ROBOT {kv.Key}({c.Comment}) ChargeTripsPerHour -> {kv.Value}（还原）"); }
                }
            }
        }
        catch { }

        try
        {
            var adict = cm._Config_Action_Dict;
            if (adict != null)
                foreach (var kv in OrigActionDuring)
                {
                    if (kv.Value <= 0f) continue;
                    if (!adict.TryGetValue(kv.Key, out var a) || a == null) continue;
                    if (Math.Abs(a.During - kv.Value) > 1e-6f)
                    { a.During = kv.Value; n++; lines.Add($"ACTION {kv.Key}({Name(a)}) During -> {kv.Value:0.#}（还原）"); }
                }
        }
        catch { }

        try
        {
            var bdict = cm._Config_Bag_Dict;
            if (bdict != null)
                foreach (var kv in OrigBagW)
                {
                    if (!bdict.TryGetValue(kv.Key, out var bag) || bag == null) continue;
                    var size = bag.Size;
                    if (size == null || size.Count < 2) continue;
                    int w0 = size[0], h0 = size[1];
                    int wantW = kv.Value, wantH = OrigBagH.TryGetValue(kv.Key, out var oh) ? oh : h0;
                    if (w0 != wantW || h0 != wantH)
                    {
                        size[0] = wantW;
                        size[1] = wantH;
                        n++; bagChanged = true;
                        lines.Add($"BAG {kv.Key}({Name(bag)}) Size {w0}x{h0} -> {wantW}x{wantH}（还原）");
                    }
                }
        }
        catch { }

        if (bagChanged) { try { cm._bagSizeSet = null; } catch { } } // 尺寸白名单懒重建
        return true;
    }

    // 插槽/模块相关修改：作者定版不做（2026-10-13）。
    // 结论存档：官方扩展槽开放门控=硬编码"装着 SlotExpand(11) 模块"（IsModuleSlotOpen 的
    // _moduleTypeMask & 0x800），配置绕不过；数量上限=RobotModuleExpandSlots 设置快照。
    // 数量改写 / 补发模块 / GM 代装 / 门控位强写四个方案全部实测后被否决或受限，
    // 课题封存待独立新 mod（完整实证见仓库记忆与 DESIGN.md）。

    /// <summary>
    /// 在场机器人直通处理（世界就绪后每轮轻扫，只在有改写时记日志）：
    /// 货舱兜底——CargoExpandBagIds 只登记了 HK 整理机器人（1061/1063），
    /// WX-5 维修 / TB-3 种植的货舱是别的 Bag 行，枚举机器人读 _cargoBaseBagId 逐一放大
    /// （agentList 单遍直扫，与原生 GetFurnituresWithBag 同数据源）。
    /// Config_Bag.Size 三层实时直读，改完即生效（面板重开一次刷新）。
    /// </summary>
    private static void ApplyLiveRobots(GameCore.HotUpdate.ConfigManager cm, List<string> lines)
    {
        GameCore.HotUpdate.Battle.Logic.BattleLogicWorld world;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated) return;
            world = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance;
        }
        catch { return; }
        var agentList = world?._AgentManager?.agentList;
        if (agentList == null || agentList.Count == 0) return;

        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Bag> bagDict = null;
        try { bagDict = cm._Config_Bag_Dict; } catch { }
        var cargoMult = Sanitize(RobotSpeedPlugin.CargoSlotsMult.Value);

        for (int i = 0; i < agentList.Count; i++)
        {
            GameCore.HotUpdate.Battle.Logic.DynamicNpc npc = null;
            try { npc = agentList[i]?.TryCast<GameCore.HotUpdate.Battle.Logic.DynamicNpc>(); } catch { }
            if (npc == null) continue;
            bool isRobot = false; try { isRobot = npc.IsRobot; } catch { }
            if (!isRobot) continue;

            var rc = npc.robotComponent;
            if (rc == null) continue;

            // 货舱兜底（字典名单覆盖不到的机型）。
            // 插槽/模块相关修改作者定版不做（官方门控=硬编码模块位，装卸模块会重算，
            // 字段强制方案观感为"卸载后失效"被否决）——该课题封存待独立新 mod。
            if (bagDict != null && cargoMult != 1f)
            {
                int bagId = 0; try { bagId = rc._cargoBaseBagId; } catch { }
                if (bagId > 0)
                {
                    int n = RewriteBagRow(bagDict, bagId, cargoMult, lines);
                    if (n > 0) { try { cm._bagSizeSet = null; } catch { } }
                }
            }
        }
    }

    private static float Sanitize(float v) => v <= 0f ? 1f : v;

    private static string Name(GameCore.HotUpdate.Config_Action a)
    { try { return a.Name_Local ?? a.Name ?? "?"; } catch { return "?"; } }

    private static string Name(GameCore.HotUpdate.Config_Bag b)
    { try { return b.Name_Local ?? b.Name ?? "?"; } catch { return "?"; } }
}
