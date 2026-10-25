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
    internal static ConfigEntry<bool> ExtendScavengePlans;

    public override void Load()
    {
        Log = base.Log;
        PreventNewFalls = Config.Bind("Fall", "PreventNewFalls", true, "拦截全部新沦陷（三模式通吃）。");
        RestoreFallenPoints = Config.Bind("Fall", "RestoreFallenPoints", true, "读档后恢复已沦陷的交易点/探险图（营地按剧情链状态区分；恢复例程幂等）。");
        ExtendScavengePlans = Config.Bind("Plans", "ExtendScavengePlans", true, "注入 11 条拾遗扩展规划（超市·六~十/医院·三~五/学校·三~五，D100-D300）+ 洞察注册表 + 篮子/降档。");

        // 逐类挂载+异常隔离（2026-10-25 事故教训：PatchAll 整包挂载时单个补丁 IL 编译错会炸掉整个
        // Load()——Ticker/loaded 行全没执行，"补丁在跑轮询全无"的残废态。逐类隔离后坏一个不连坐）。
        var harmony = new HarmonyLib.Harmony(Guid);
        foreach (var t in typeof(EndlessGuardPlugin).Assembly.GetTypes())
        {
            if (!t.IsClass || !t.IsDefined(typeof(HarmonyLib.HarmonyPatch), false)) continue;
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Trace("patch 挂载失败 " + t.Name + ": " + e.Message); }
        }

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<GuardTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<GuardTicker>();
        var go = new GameObject("EndlessGuard.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GuardTicker>();

        Trace("loaded：cfg=" + PreventNewFalls.Value + "," + RestoreFallenPoints.Value + "," + ExtendScavengePlans.Value);
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

/// <summary>2s 轮询：表注入与注册表追加在 Cm() 就绪后执行（早于 PlanPick 目录构建=首次开规划页）。
/// Update 必须 public + 双构造器（RoleUnlock RoleTicker 实证模式：interop 注入类的引擎回调
/// 只桥接 public 生命周期方法——2026-10-25 探针实锤 private 版在场景切换后停止回调，
/// "每2s复查"全链哑火即买卡后追加波不生效的根因）。心跳行进 trace 供存活自证。</summary>
internal sealed class GuardTicker : MonoBehaviour
{
    private float _acc;
    private int _beats;
    public GuardTicker(IntPtr p) : base(p) { }
    public GuardTicker() { }
    public void Update()
    {
        _acc += UnityEngine.Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;
        _beats++;
        if (_beats % 30 == 0) EndlessGuardPlugin.Trace("heartbeat tick=" + _beats);   // 60s 心跳：Ticker 存活铁证
        try { ScavengePlans.Tick(); } catch (Exception e) { EndlessGuardPlugin.Trace("ticker ScavengePlans: " + e.Message); }
    }
}
