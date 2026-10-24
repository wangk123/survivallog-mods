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

/// <summary>自选生存规划：RogueSelect 页注入全量规划列表，自选购买消耗翻倍（官方购买链复用）。设计见 DESIGN.md。
/// General.Enabled 总开关（2026-10-24，ModCenter 启停位）：false=移除 #ppEntry 入口+FocusTick 交还键盘；
/// 下次启动不加载（零日志零注入）。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PlanPickPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.planpick";
    public const string Name = "PlanPick";
    public const string Version = "1.3.8";

    internal static new ManualLogSource Log;
    // General.Enabled 总开关（ModCenter 启停位，2026-10-24）：false=移除入口按钮停注入（含关悬浮层、
    // 交还键盘）；下次启动不加载（不 PatchAll/不建 Ticker，零日志零注入）。
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> CostMultiplierPercent;   // 自选倍率百分比：100~300 整数，默认 200；100=原值
    internal static ConfigEntry<bool> ShowLocked;        // 未解锁项展示（置灰+原因）
    internal static ConfigEntry<bool> ShowUnpooled;      // 不入池规划（图纸/无人机）展示
    internal static ConfigEntry<bool> ListOnReady;       // 首轮列统计
    internal static ConfigEntry<bool> Verbose;

    public override void Load()
    {
        Log = base.Log;
        // General.Enabled 放最前 Bind（保证 cfg 键已生成后才可能早退）
        Enabled = Config.Bind("General", "Enabled", true, "总开关：false=隐藏入口停注入；下次启动不加载。");
        CostMultiplierPercent = Config.Bind("Cost", "CostMultiplierPercent", 200,
            new ConfigDescription("自选规划（不在今日候选名单）的生存点消耗百分比加成。整数 100~300：100=原值，200=翻倍（默认），300=三倍。不支持小数；实收=原价×百分比/100 四舍五入取整。今日候选内购买始终原价。",
                new AcceptableValueRange<int>(100, 300)));
        ShowLocked = Config.Bind("List", "ShowLocked", true, "列表展示未解锁规划（置灰+点击弹原因）。");
        ShowUnpooled = Config.Bind("List", "ShowUnpooled", true, "列表展示从不进入抽取池的规划（机器人图纸/拾荒无人机）。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true, "首次数据就绪时在日志列统计（分类计数/今日候选）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮推送输出明细。");

        if (!Enabled.Value) return; // General.Enabled=false：不 PatchAll/不建 Ticker（零日志零注入；CostPatch 未挂，官方链原价）

        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll();

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PlanTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PlanTicker>();
        var go = new GameObject("PlanPick.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PlanTicker>();
        Log.LogInfo("[PlanPick] loaded：自选规划入口待注入（自选加成 " + MultiplierPercent + "%）");
        WelcomeTip.Install(Log);
    }

    /// <summary>倍率百分比统一出口：整数 100~300（AcceptableValueRange 在读入时夹取，此处再钳防手改）。</summary>
    internal static int MultiplierPercent
    {
        get
        {
            var p = CostMultiplierPercent.Value;
            if (p < 100) p = 100;
            if (p > 300) p = 300;
            return p;
        }
    }
}

/// <summary>巡检双频：2s 全量（注入+payload 推送）；0.3s 焦点桥（输入框聚焦→SetFocused 交接键盘）。</summary>
internal sealed class PlanTicker : MonoBehaviour
{
    public PlanTicker(IntPtr ptr) : base(ptr) { }
    public PlanTicker() { }
    private float _check;
    private float _focusCheck;
    private void Update()
    {
        // IME 转运：Unity 主窗口接输入法，组合完成的汉字进 inputString（每帧收集非 ASCII）
        try
        {
            var s = UnityEngine.Input.inputString;
            if (!string.IsNullOrEmpty(s)) Injector.OfferImeChars(s);
        }
        catch { }

        _focusCheck += Time.deltaTime;
        if (_focusCheck >= 0.3f) { _focusCheck = 0; Injector.FocusTick(); }
        _check += Time.deltaTime;
        if (_check < 2f) return;
        _check = 0;
        Injector.Tick();
    }
}
