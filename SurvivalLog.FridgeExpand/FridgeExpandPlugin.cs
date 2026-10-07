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

namespace SurvivalLog.FridgeExpand;

/// <summary>
/// 冰箱扩容（独立 mod 3/4）。
/// 默认名单（覆盖游戏内全部可获得的冰箱）：1004（双门冰箱/食堂冰箱/医用冷藏柜共用）、
/// 115000（双开门冰箱）、115001（冰柜/豪华双门共用）、3002（小冰箱）。
/// 场景装饰类（非卖品巨型冷冻柜 2001 等）未含，需要可加 ExtraIds。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class FridgeExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.fridgeexpand";
    public const string Name = "FridgeExpand";
    public const string Version = "1.0.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> WidthMult;
    internal static ConfigEntry<float> HeightMult;
    internal static ConfigEntry<string> ExtraIds;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> Verbose;

    internal static readonly int[] DefaultIds = { 1004, 115000, 115001, 3002 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        WidthMult = Config.Bind("Expand", "WidthMult", 1.5f, "宽度倍数（四舍五入取整）。");
        HeightMult = Config.Bind("Expand", "HeightMult", 1.5f, "高度倍数（四舍五入取整）。");
        ExtraIds = Config.Bind("Expand", "ExtraIds", "",
            "追加的 BagId（逗号分隔）。默认：1004,115000,115001,3002。");
        ExcludeIds = Config.Bind("Expand", "ExcludeIds", "", "排除的 BagId（优先级最高）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        WidthMult.SettingChanged += reload;
        HeightMult.SettingChanged += reload;
        ExtraIds.SettingChanged += reload;
        ExcludeIds.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<FridgeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<FridgeTicker>();
        var go = new GameObject("FridgeExpand.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<FridgeTicker>();
        Log.LogInfo($"[FridgeExpand] loaded：宽×{WidthMult.Value} 高×{HeightMult.Value}");
    }

    internal static HashSet<int> BuildTargets()
    {
        var set = new HashSet<int>(DefaultIds);
        foreach (var id in BagTable.ParseIds(ExtraIds.Value)) set.Add(id);
        foreach (var id in BagTable.ParseIds(ExcludeIds.Value)) set.Remove(id);
        return set;
    }
}

internal sealed class FridgeTicker : MonoBehaviour
{
    public FridgeTicker(IntPtr ptr) : base(ptr) { }
    public FridgeTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 持续轮询幂等改写（防读档/切场景配置表重载覆盖）
        _check += Time.deltaTime;
        var due = FridgeExpandPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = FridgeExpandPlugin.BuildTargets();
        var (scanned, changed, lines) = BagTable.RewriteSize(
            targets, FridgeExpandPlugin.WidthMult.Value, FridgeExpandPlugin.HeightMult.Value,
            FridgeExpandPlugin.Verbose.Value);

        if (scanned < 0) { FridgeExpandPlugin.Rerun = true; return; }
        FridgeExpandPlugin.Rerun = false;

        if (changed > 0 || FridgeExpandPlugin.Verbose.Value)
            foreach (var l in lines) FridgeExpandPlugin.Log.LogInfo("[FridgeExpand] " + l);

        if (!_announced && scanned > 0)
        {
            _announced = true;
            FridgeExpandPlugin.Log.LogInfo(
                $"[FridgeExpand] 生效：{scanned} 条配置受管（宽×{FridgeExpandPlugin.WidthMult.Value} 高×{FridgeExpandPlugin.HeightMult.Value}）。");
        }
    }
}
