using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>灾前加速：官方五键倍速条覆盖到灾前（CoreUI0）。零 Harmony/零表改动，设计见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PreDisasterSpeedPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.predisasterspeed";
    public const string Name = "PreDisasterSpeed";
    public const string Version = "1.3.8";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Verbose;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true, "灾前加速条总开关（关闭=不注入不订阅；对已在档对局下次启动生效）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "注入/桥接明细日志。");

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PdsTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PdsTicker>();
        var go = new GameObject("PreDisasterSpeed.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PdsTicker>();
        Log.LogInfo("[PdsSpeed] loaded（Enabled=" + Enabled.Value + "）");
        WelcomeTip.Install(Log);
    }
}

/// <summary>2s 巡检：WebView 就绪→注入 page.js→推倍率 payload→保活事件桥（详见 Injector/SpeedLink）。</summary>
internal sealed class PdsTicker : MonoBehaviour
{
    public PdsTicker(IntPtr ptr) : base(ptr) { }
    public PdsTicker() { }
    private float _check;
    private void Update() { _check += Time.deltaTime; if (_check < 2f) return; _check = 0; Injector.Tick(); }
}
