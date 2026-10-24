using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.EndlessGuard;

/// <summary>无尽守卫：拦沦陷/旧档恢复/剥倒计时tag/生存规划扩展。机制与实证见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class EndlessGuardPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.endlessguard";
    public const string Name = "EndlessGuard";
    public const string Version = "1.3.8"; // 跟随套件 version.json（作者定号）

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> PreventNewFalls;
    internal static ConfigEntry<bool> RestoreFallenPoints;
    internal static ConfigEntry<bool> StripPendingFallTags;
    internal static ConfigEntry<bool> ExtendScavengePlans;
    internal static ConfigEntry<float> RichnessStep;

    public override void Load()
    {
        Log = base.Log;
        PreventNewFalls = Config.Bind("Fall", "PreventNewFalls", true, "拦截全部新沦陷（三模式通吃）。");
        RestoreFallenPoints = Config.Bind("Fall", "RestoreFallenPoints", true, "读档后恢复已沦陷的交易点/探险图（营地除外，恢复例程幂等）。");
        StripPendingFallTags = Config.Bind("Fall", "StripPendingFallTags", true, "剥掉沦陷倒计时 tag（前缀运行时读，读不到自动降级跳过）。");
        ExtendScavengePlans = Config.Bind("Plans", "ExtendScavengePlans", true, "注入 11 条拾遗扩展规划（超市·六~十/医院·三~五/学校·三~五，D100-D300）+ 洞察注册表 + 篮子/降档。");
        RichnessStep = Config.Bind("Plans", "RichnessStep", 0.3f, "数量层每张已购新卡的期望份数加成（官方探索物资难度值之上叠加；0=纯官方难度）。");

        var harmony = new HarmonyLib.Harmony(Guid);
        // 各补丁类由后续功能块自带 [HarmonyPatch]，统一在此 PatchAll
        harmony.PatchAll(typeof(EndlessGuardPlugin).Assembly);

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<GuardTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<GuardTicker>();
        var go = new GameObject("EndlessGuard.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GuardTicker>();

        Trace("loaded：cfg=" + PreventNewFalls.Value + "," + RestoreFallenPoints.Value + "," + StripPendingFallTags.Value + "," + ExtendScavengePlans.Value + ",step=" + RichnessStep.Value);
        Log.LogInfo("[EndlessGuard] loaded（拦沦陷/恢复/剥tag/规划扩展）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    /// <summary>自证文件（硬规则9：不依赖 LogOutput.log）。每行带时间戳。</summary>
    internal static void Trace(string line)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Paths.BepInExRootPath, "EndlessGuard_trace.txt"), $"[{DateTime.Now:HH:mm:ss}] {line}\n"); }
        catch { }
    }
}

/// <summary>2s 轮询：表注入与注册表追加在 Cm() 就绪后执行（早于 PlanPick 目录构建=首次开规划页）。</summary>
internal sealed class GuardTicker : MonoBehaviour
{
    internal static volatile bool PlansInjected;
    private float _acc;
    public GuardTicker(IntPtr p) : base(p) { }
    private void Update()
    {
        _acc += UnityEngine.Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;
        try { ScavengePlans.Tick(); } catch (Exception e) { EndlessGuardPlugin.Trace("ticker ScavengePlans: " + e.Message); }
    }
}
