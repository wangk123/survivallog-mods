using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
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
    public const string Version = "1.0.1";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> KeepMult;
    internal static ConfigEntry<string> BagIds;
    internal static ConfigEntry<bool> Verbose;

    internal static readonly int[] DefaultBagIds = { 1004, 115000, 115001, 3002 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        KeepMult = Config.Bind("Chill", "KeepMult", 2f,
            "保鲜倍数：2 = 食物在冰箱里的保质期翻倍（ColdRate÷2）。1 = 不改。");
        BagIds = Config.Bind("Chill", "BagIds", "",
            "冰箱 BagId 名单（逗号分隔；留空用默认 1004,115000,115001,3002，可追加）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        KeepMult.SettingChanged += (object s2, System.EventArgs e2) => { Rerun = true; };
        BagIds.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ChillTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ChillTicker>();
        var go = new GameObject("FridgeChill.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ChillTicker>();
        Log.LogInfo($"[FridgeChill] loaded：保鲜 ×{KeepMult.Value}");
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
        // 一次性修复 v1.1.0 写坏并存档的 TimeScale（0.385 区段 ×2 还原；冰箱正确值 0.0385 不碰）
        var (repaired, repairLines) = BagTable.RepairV11TimeScale();

        if (scanned < 0) { FridgeChillPlugin.Rerun = true; return; }
        FridgeChillPlugin.Rerun = false;

        if (changed > 0 || repaired > 0 || FridgeChillPlugin.Verbose.Value)
        {
            foreach (var l in lines) FridgeChillPlugin.Log.LogInfo("[FridgeChill] " + l);
            foreach (var l in repairLines) FridgeChillPlugin.Log.LogInfo("[FridgeChill] " + l);
        }

        if (!_announced && scanned >= 0)
        {
            _announced = true;
            FridgeChillPlugin.Log.LogInfo(
                $"[FridgeChill] 生效：{scanned} 个冰箱家具受管（原生保鲜 ×5 → 现 ×{5 * FridgeChillPlugin.KeepMult.Value:0.#}，仅改系数）。" +
                (repaired > 0 ? $" 已修复 v1.1.0 存档损伤 {repaired} 件物品的保质显示（如菠菜 6.5→13 天）。" : ""));
        }
    }
}
