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

namespace SurvivalLog.BackpackExpand;

/// <summary>
/// 背包扩容（独立 mod 1/4）。
/// 目标 BagId：1,2,5,6,7（普通背包+登山包天赋链） 8,9,10,11（仓管背包天赋链）。
/// 天赋链整链统一改写，链内尺寸相对关系保持 → 天赋换包照常工作；
/// 负重 = Round((Burden+ExtraBurden)×(1+天赋比例)+固定值)，基础与天赋独立叠加。
/// 负重为快照值：改表/改配置后需重新读档才体现到玩家。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class BackpackExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.backpackexpand";
    public const string Name = "BackpackExpand";
    public const string Version = "1.0.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> WidthMult;
    internal static ConfigEntry<float> HeightMult;
    internal static ConfigEntry<int> BurdenMult;
    internal static ConfigEntry<string> ExtraIds;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> Verbose;

    internal static readonly int[] DefaultIds = { 1, 2, 5, 6, 7, 8, 9, 10, 11 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        WidthMult = Config.Bind("Expand", "WidthMult", 1.5f,
            "宽度倍数（四舍五入取整）。1.5 = +50%。宽高同时放大可避免界面单边过长被压缩。");
        HeightMult = Config.Bind("Expand", "HeightMult", 1.5f, "高度倍数（四舍五入取整）。");
        BurdenMult = Config.Bind("Expand", "BurdenMult", 2,
            "负重倍数（仅背包；天赋的额外负重/比例加成不受影响）。需重新读档生效。");
        ExtraIds = Config.Bind("Expand", "ExtraIds", "",
            "追加的 BagId（逗号分隔）。默认名单：1,2,5,6,7,8,9,10,11（普通/仓管/登山包）。");
        ExcludeIds = Config.Bind("Expand", "ExcludeIds", "", "排除的 BagId（优先级最高）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细（含未变化项）。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        WidthMult.SettingChanged += reload;
        HeightMult.SettingChanged += reload;
        BurdenMult.SettingChanged += reload;
        ExtraIds.SettingChanged += reload;
        ExcludeIds.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BackpackTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<BackpackTicker>();
        var go = new GameObject("BackpackExpand.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BackpackTicker>();
        Log.LogInfo($"[BackpackExpand] loaded：宽×{WidthMult.Value} 高×{HeightMult.Value} 负重×{BurdenMult.Value}");
    }

    internal static HashSet<int> BuildTargets()
    {
        var set = new HashSet<int>(DefaultIds);
        foreach (var id in BagTable.ParseIds(ExtraIds.Value)) set.Add(id);
        foreach (var id in BagTable.ParseIds(ExcludeIds.Value)) set.Remove(id);
        return set;
    }
}

internal sealed class BackpackTicker : MonoBehaviour
{
    public BackpackTicker(IntPtr ptr) : base(ptr) { }
    public BackpackTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 持续轮询幂等改写：配置表在切场景/读档时可能整体重载（对象重建、回到原值），
        // 一次式改写会被覆盖；周期性按 原值×倍数 重写则始终有效（旧 mod 验证过的模式）。
        _check += Time.deltaTime;
        var due = BackpackExpandPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = BackpackExpandPlugin.BuildTargets();
        var (scannedS, changedS, linesS) = BagTable.RewriteSize(
            targets, BackpackExpandPlugin.WidthMult.Value, BackpackExpandPlugin.HeightMult.Value,
            BackpackExpandPlugin.Verbose.Value);
        var (scannedB, changedB, linesB) = BagTable.RewriteBurden(
            targets, Math.Max(1, BackpackExpandPlugin.BurdenMult.Value), BackpackExpandPlugin.Verbose.Value);

        if (scannedS < 0) { BackpackExpandPlugin.Rerun = true; return; } // 配置表未就绪，立即重试

        BackpackExpandPlugin.Rerun = false;

        if (changedS > 0 || changedB > 0 || BackpackExpandPlugin.Verbose.Value)
        {
            foreach (var l in linesS) BackpackExpandPlugin.Log.LogInfo("[BackpackExpand] " + l);
            foreach (var l in linesB) BackpackExpandPlugin.Log.LogInfo("[BackpackExpand] " + l);
        }

        if (!_announced && scannedS > 0)
        {
            _announced = true;
            BackpackExpandPlugin.Log.LogInfo(
                $"[BackpackExpand] 生效：{scannedS} 条配置受管（宽×{BackpackExpandPlugin.WidthMult.Value} 高×{BackpackExpandPlugin.HeightMult.Value} 负重×{BackpackExpandPlugin.BurdenMult.Value}）。负重需重新读档体现。");
        }
    }
}
