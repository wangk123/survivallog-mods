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

/// <summary>灾前加速：官方五键倍速条覆盖到灾前（CoreUI0）。零表改动；桥=Harmony prefix 观察者，设计见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PreDisasterSpeedPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.predisasterspeed";
    public const string Name = "PreDisasterSpeed";
    public const string Version = "1.3.8";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> TrueThreeX;
    internal static ConfigEntry<bool> Hotkeys;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true, "灾前加速条总开关（关闭=不注入不挂钩；对已在档对局下次启动生效）。");
        TrueThreeX = Config.Bind("General", "TrueThreeX", true,
            "手动三键在灾前按真实×3生效（临时换档校正；睡眠跳夜保持官方×32 不变）。false=官方表值×32（三键≈快进档）。");
        Hotkeys = Config.Bind("Hotkeys", "Enabled", true,
            "快捷键（仅灾前 HUD 显示期间生效，键位与官方一致）：~=慢速 1=原速 2=二倍 3=三倍 4=暂停。注意：网页输入框聚焦时按键仍会触发。");
        Verbose = Config.Bind("Debug", "Verbose", false, "注入/payload 明细日志。");

        var harmony = new HarmonyLib.Harmony(Guid);
        harmony.PatchAll();
        TryPatchTrueThree(harmony);

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PdsTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PdsTicker>();
        var go = new GameObject("PreDisasterSpeed.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PdsTicker>();
        Log.LogInfo("[PdsSpeed] loaded（Enabled=" + Enabled.Value + "，桥=HandleCallback prefix，TrueThreeX=" + TrueThreeX.Value + "）");
        WelcomeTip.Install(Log);
    }

    /// <summary>真三倍补丁手动挂载：失败只回落官方 ×32 并打点，不拖累 PatchAll 已挂的桥补丁。</summary>
    private static void TryPatchTrueThree(HarmonyLib.Harmony harmony)
    {
        try
        {
            var orig = HarmonyLib.AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.GameTimeManager), "ChangeGameSpeed");
            var pre = new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(SpeedLink.TrueThreeGearPatch), "Prefix"));
            var post = new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(SpeedLink.TrueThreeGearPatch), "Postfix"));
            harmony.Patch(orig, prefix: pre, postfix: post);
            Log.LogInfo("[PdsSpeed] 三倍校正补丁已挂（手动三键灾前=×3；睡眠保持官方×32）");
        }
        catch (Exception e)
        {
            Log.LogWarning("[PdsSpeed] 三倍校正补丁挂载失败，三键保持官方×32: " + e.Message);
        }
    }
}

/// <summary>2s 巡检：WebView 就绪→注入 page.js→推倍率 payload（点击回程走 Harmony prefix 桥）。
/// 每帧：快捷键轮询（仅灾前 HUD 可见期间，键位同官方 ~ / 1 / 2 / 3 / 4）。</summary>
internal sealed class PdsTicker : MonoBehaviour
{
    public PdsTicker(IntPtr ptr) : base(ptr) { }
    public PdsTicker() { }
    private float _check;
    private static readonly KeyCode[] GearKeys = { KeyCode.BackQuote, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };
    private static readonly int[] GearSpeeds = { 4, 1, 2, 3, 0 };
    private void Update()
    {
        if (PreDisasterSpeedPlugin.Hotkeys.Value && SpeedLink.CoreUi0Visible)
        {
            for (var i = 0; i < GearKeys.Length; i++)
                if (Input.GetKeyDown(GearKeys[i])) { SpeedLink.SetSpeed(GearSpeeds[i]); break; }
        }
        _check += Time.unscaledDeltaTime; if (_check < 2f) return; _check = 0; Injector.Tick();
    }
}
