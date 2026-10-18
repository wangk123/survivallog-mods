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

namespace SurvivalLog.FridgeChill;

/// <summary>
/// 冰箱冷藏增强（独立 mod 4/4）：保鲜时长按倍数延长。
///
/// 实证（反汇编 ComputeItemTimeScale / FoodPreservationRules）：
/// - 家具保鲜时长倍率 = 1 / Config_Furniture.ColdRate（ColdRate=0.2 → 保质 ×5）
/// - 天赋（Buff/AE_FridgeColdEnhance）按比例压低 ColdRate，下限 0.1，与本 mod 改表独立叠加
/// - 界面上的"保鲜率"显示（Round(1/ColdRate)）读同一字段，会自动跟随
/// 本 mod 对冰箱包名单内的家具把 ColdRate ÷ KeepMult（默认 2 = 保质期翻倍）。
/// 注意：只影响单家具冷藏；冷链房间（FridgeAreaMap/areaScale）与堆肥箱不在范围内。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class FridgeChillPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.fridgechill";
    public const string Name = "FridgeChill";
    public const string Version = "1.3.6";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> KeepMult;
    internal static ConfigEntry<float> MaxTotalMult;
    internal static ConfigEntry<string> BagIds;
    internal static ConfigEntry<bool> Verbose;

    internal static readonly int[] DefaultBagIds = { 1004, 115000, 115001, 115005, 3002 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        KeepMult = Config.Bind("Chill", "KeepMult", 2f,
            "保鲜倍数：2 = 食物在冰箱里的保质期翻倍（ColdRate÷2）。1 = 不改。");
        MaxTotalMult = Config.Bind("Chill", "MaxTotalMult", 100f,
            "总保鲜倍数上限：表值改写与天赋/规划等加成合并后的封顶。默认 100；10 = 原生上限 ×10（即不抬高）。保鲜关闭（KeepMult≤1）时本项不生效。");
        BagIds = Config.Bind("Chill", "BagIds", "",
            "冰箱 BagId 名单（逗号分隔；留空用默认 1004,115000,115001,115005,3002，可追加）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        KeepMult.SettingChanged += (object s2, System.EventArgs e2) => { Rerun = true; };
        BagIds.SettingChanged += reload;
        MaxTotalMult.SettingChanged += (object s3, System.EventArgs e3) =>
            Log.LogInfo($"[FridgeChill] 总保鲜封顶调整为 ×{Math.Max(10f, MaxTotalMult.Value):0.##}（下次计算即生效）");

        // 抬高保鲜总上限（TalentCapPatch）：原生 MinColdRate=0.1 会把本 mod 表值之上的
        // 天赋加成钳为零（2026-10-18 反汇编实证，详见该类注释与 DESIGN.md §2）。
        try
        {
            var harmony = new Harmony(Guid);
            harmony.PatchAll();
            Log.LogInfo($"[FridgeChill] 天赋上限补丁已挂：总保鲜封顶 ×{Math.Max(10f, MaxTotalMult.Value):0.##}" +
                        $"（原生 ×10；表值改写与天赋/规划加成恢复纯乘法叠加）");
        }
        catch (Exception e)
        {
            Log.LogError($"[FridgeChill] 天赋上限补丁挂载失败，回落为原生 ×10 封顶（天赋加成会被钳制但无其他影响）：{e.Message}");
        }

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ChillTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ChillTicker>();
        var go = new GameObject("FridgeChill.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ChillTicker>();
        Log.LogInfo($"[FridgeChill] loaded：保鲜 ×{KeepMult.Value}，总封顶 ×{Math.Max(10f, MaxTotalMult.Value):0.##}");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    internal static HashSet<int> BuildTargets()
    {
        var set = new HashSet<int>(DefaultBagIds);
        foreach (var id in BagTable.ParseIds(BagIds.Value)) set.Add(id);
        return set;
    }
}

internal sealed class ChillTicker : MonoBehaviour
{
    public ChillTicker(IntPtr ptr) : base(ptr) { }
    public ChillTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 只改系数（ColdRate），绝不直接改物品字段（v1.1.0 教训：动 Data_Item.TimeScale
        // 会污染游戏进出容器换算，菠菜 13 天一进一出变 6.5 天）。
        // 已在冰箱里的旧物品：下次进出容器时游戏按新系数重算，速率自然翻倍。
        if (FridgeChillPlugin.KeepMult.Value <= 1.01f) return;

        _check += Time.deltaTime;
        var due = FridgeChillPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = FridgeChillPlugin.BuildTargets();
        var (scanned, changed, lines) = BagTable.RewriteFridgeChill(
            targets, FridgeChillPlugin.KeepMult.Value, FridgeChillPlugin.Verbose.Value);
        // v1.1.0 存档修复器已于 2026-10-18 彻底移除（实测误伤合法天赋档 0.5→1.0、
        // 显示保质翻倍；"绝不写 Data_Item"红线自此零例外）。

        if (scanned < 0) { FridgeChillPlugin.Rerun = true; return; }
        FridgeChillPlugin.Rerun = false;

        if (changed > 0 || FridgeChillPlugin.Verbose.Value)
        {
            foreach (var l in lines) FridgeChillPlugin.Log.LogInfo("[FridgeChill] " + l);
        }

        if (!_announced && scanned >= 0)
        {
            _announced = true;
            FridgeChillPlugin.Log.LogInfo(
                $"[FridgeChill] 生效：{scanned} 个冰箱家具受管（原生保鲜 ×5 → 现 ×{5 * FridgeChillPlugin.KeepMult.Value:0.#}，仅改系数；总封顶 ×{Math.Max(10f, FridgeChillPlugin.MaxTotalMult.Value):0.##}，天赋/规划可继续叠加）。");
        }
    }
}

/// <summary>
/// 保鲜总上限抬高（2026-10-18 反汇编实证后落地）。
///
/// 原生公式（FoodPreservationRules.ApplyFridgeColdEnhance，@0x292E200）：
///   coldRate >= 1 或 talentRatio <= 0 → 原样返回 coldRate（不经过下限）；
///   否则 → max(coldRate × (1 − talentRatio), 0.1)。   // 0.1=MinColdRate，乘完天赋后才封底
/// 后果：本 mod 表值 0.1 已坐在下限上，任何天赋比率都被钳回 0.1（天赋零收益、冰箱显示恒 ×10）。
/// 0.1 是编译期常量（.rdata 单槽位，被全代码段 178 处共享，不可直改），故用 Harmony 等价重算。
///
/// 本补丁：下限换成 1/MaxTotalMult（默认 0.01 = ×100）。mod 与天赋严格乘法正交；
/// MaxTotalMult=10 时与原生完全等价（安全回退位）。任何异常/关闭状态交还原生结果。
/// 注意：ApplyColdChainEnhance 与本函数同一段原生代码（编译器合并相同实现），只 patch 本名
/// 即覆盖冰箱/冷链房间两条通道的全部调用点（冷链封顶同步抬高，属一致行为，非削弱）；
/// 仓储(AE_StorageShelfLife)/背包(AE_BackpackShelfLife)通道不经过此函数，零影响。
/// </summary>
[HarmonyPatch(typeof(GameCore.HotUpdate.FoodPreservationRules),
    nameof(GameCore.HotUpdate.FoodPreservationRules.ApplyFridgeColdEnhance))]
internal static class TalentCapPatch
{
    static void Postfix(ref float __result, float coldRate, float talentRatio)
    {
        try
        {
            var keep = FridgeChillPlugin.KeepMult;
            var max = FridgeChillPlugin.MaxTotalMult;
            if (keep == null || max == null) return;
            if (keep.Value <= 1.01f) return;            // 保鲜关闭 → 原生语义
            float maxTotal = max.Value;
            if (maxTotal < 10f) maxTotal = 10f;         // 不允许低于原生 ×10（防止反向收紧）
            float floor = 1f / maxTotal;
            float want = (coldRate >= 1f || talentRatio <= 0f)
                ? coldRate
                : coldRate * Math.Max(0f, 1f - talentRatio);
            if (want < floor) want = floor;             // 统一执行总封顶
            __result = want;
        }
        catch { /* 任何异常保持 __result = 原生结果 */ }
    }
}
