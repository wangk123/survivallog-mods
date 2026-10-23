using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.PlanPick;

/// <summary>自选生存规划：RogueSelect 页注入全量规划列表，自选购买消耗翻倍（官方购买链复用）。设计见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PlanPickPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.planpick";
    public const string Name = "PlanPick";
    public const string Version = "1.3.8";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<float> CostMultiplier;   // 自选倍率（1=原价；<1 按 1 处理）
    internal static ConfigEntry<bool> ShowLocked;        // 未解锁项展示（置灰+原因）
    internal static ConfigEntry<bool> ShowUnpooled;      // 不入池规划（图纸/无人机）展示
    internal static ConfigEntry<bool> ListOnReady;       // 首轮列统计
    internal static ConfigEntry<bool> Verbose;

    public override void Load()
    {
        Log = base.Log;
        CostMultiplier = Config.Bind("Cost", "CostMultiplier", 2f,
            "自选规划（不在今日候选名单）的生存点消耗倍率。默认 2=翻倍；1=免代价。今日候选内购买始终原价。");
        ShowLocked = Config.Bind("List", "ShowLocked", true, "列表展示未解锁规划（置灰+点击弹原因）。");
        ShowUnpooled = Config.Bind("List", "ShowUnpooled", true, "列表展示从不进入抽取池的规划（机器人图纸/拾荒无人机）。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true, "首次数据就绪时在日志列统计（分类计数/今日候选）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮推送输出明细。");

        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll();

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PlanTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PlanTicker>();
        var go = new GameObject("PlanPick.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PlanTicker>();
        Log.LogInfo("[PlanPick] loaded：自选规划入口待注入（倍率 x" + CostMultiplier.Value + "）");
        WelcomeTip.Install(Log);
    }
}

/// <summary>2s 巡检：WebView 就绪→注入 page.js→推数据（详见 Injector）。</summary>
internal sealed class PlanTicker : MonoBehaviour
{
    public PlanTicker(IntPtr ptr) : base(ptr) { }
    public PlanTicker() { }
    private float _check;
    private void Update() { _check += Time.deltaTime; if (_check < 2f) return; _check = 0; Injector.Tick(); }
}
