using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ModCenter;

/// <summary>
/// MOD 管理中心（宿主插件）：主菜单/ESC 菜单入口 + WebUI 面板 + 套件各 mod 开关桥。
/// 三层架构与迭代史见 DESIGN.md；菜单锚点单源见本目录 ANCHORS.md（T1，改挂钩点须同步）。
/// 本文件是任务 3 骨架：宿主入口 + Ticker 心跳 + 日志门/自证通道；注入与面板逻辑自任务 5/7 起填充。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ModCenterPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.modcenter";
    public const string Name = "ModCenter";
    public const string Version = "1.3.8"; // 跟随套件 version.json（作者定号）

    public static ModCenterPlugin Instance { get; private set; }

    // 日志源快照：后续任务统一经 Log(msg) 静态门输出，不自持 ManualLogSource
    // （字段不能与 Log(string) 方法同名，故命名 Logger；new 显式遮蔽 BasePlugin.Log 实例属性）
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Instance = this;
        Logger = base.Log;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModCenterTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ModCenterTicker>();
        var go = new GameObject("ModCenter.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ModCenterTicker>();

        Log("loaded");
        Trace($"loaded v{Version}（骨架：Ticker 已挂，注入逻辑待任务 5+）");
        SurvivalLog.ModShared.WelcomeTip.Install(Logger);
    }

    /// <summary>静态日志门：全部面板/桥接代码的统一输出口。</summary>
    public static new void Log(string msg) => Logger?.LogMessage("[ModCenter] " + msg);

    /// <summary>
    /// 自证文件（硬规则 9：不依赖 LogOutput.log，强杀不落盘）。落在游戏 BepInEx 根目录
    /// （本机即 E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt），
    /// 用 Paths.BepInExRootPath 而非写死盘符——本 mod 随套件发布到其他玩家机器也要能写。
    /// </summary>
    internal static void Trace(string line)
    {
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_loaded.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}\n");
        }
        catch { }
    }
}

/// <summary>
/// 宿主心跳：每 2s（unscaled，主菜单/暂停均照走）。任务 5 起填：
/// 主菜单/ESC 菜单注入巡检 + 面板 outbox 轮询（__mcOutbox/__mcDrain，计划既定契约）。
/// </summary>
internal sealed class ModCenterTicker : MonoBehaviour
{
    public ModCenterTicker(IntPtr ptr) : base(ptr) { }
    public ModCenterTicker() { }

    private float _acc;

    public void Update()
    {
        _acc += Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;
        // Task 5 起填：注入巡检 + outbox 轮询
    }
}
