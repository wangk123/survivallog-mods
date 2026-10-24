using System;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ModCenter;

/// <summary>
/// MOD 管理中心（宿主插件）：主菜单/ESC 菜单入口 + WebUI 面板 + 套件各 mod 开关桥。
/// 三层架构与迭代史见 DESIGN.md；菜单锚点单源见本目录 ANCHORS.md（T1，改挂钩点须同步）。
/// 任务 3：宿主入口 + Ticker 心跳 + 日志门/自证通道；任务 4：反射桥探针（BridgeProbe，cfg
/// Debug.Probe 门控，结论固化 DESIGN.md「反射桥 API 实证记录」）；任务 5：注入骨架（WebBridge
/// 通道 + MenuEntryInjector 双锚点入口 + panel.js 空壳面板 + outbox 轮询，完整面板任务 7）。
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

    // 任务 4：反射桥诊断探针开关（默认关）。true 时启动 30s 后由 Ticker 主线程跑一轮，
    // 证据落 BepInEx/ModCenter_probe.txt；结论（确切 API/类型/坑）见 DESIGN.md。
    internal static ConfigEntry<bool> DebugProbe;

    public override void Load()
    {
        Instance = this;
        Logger = base.Log;

        DebugProbe = Config.Bind("Debug", "Probe", false,
            "反射桥 API 诊断探针（默认关，仅供开发验证）。true 时启动 30 秒后对 RobotSpeed『机器人提速/移速倍率』\n" +
            "执行一轮：枚举插件 → 取 ConfigFile → 读原值 → 写 3 → 等 5 秒验内存表热重写（4.8×3=14.4）→ 写回原值并落盘。\n" +
            "证据追加到 BepInEx/ModCenter_probe.txt。玩家长开无意义，验完请改回 false。");

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ModCenterTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ModCenterTicker>();
        var go = new GameObject("ModCenter.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ModCenterTicker>();

        Log("loaded");
        Trace($"loaded v{Version}（注入骨架：菜单入口+空壳面板+outbox；完整面板待任务 7）");
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
/// 宿主心跳：每 2s（unscaled，主菜单/暂停均照走）。任务 5 起接入注入巡检（WebBridge.GetWebView
/// → MenuEntryInjector.Tick：入口+面板幂等安装 + 面板 outbox 轮询）。
/// 另挂任务 4 反射桥探针泵（ProbePump，Debug.Probe 门控）。
/// </summary>
internal sealed class ModCenterTicker : MonoBehaviour
{
    public ModCenterTicker(IntPtr ptr) : base(ptr) { }
    public ModCenterTicker() { }

    private float _acc;

    // 探针状态机：-1 未启用/已结束；0 等 30s（A 枚举+读+写）；1 等 5s（B 验表+写回）；2 等 3s（C 终验+Save）
    private int _probePhase;
    private float _probeAge;

    public void Update()
    {
        ProbePump();

        _acc += Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;

        // 任务 5：注入巡检（入口+面板幂等自愈）+ outbox 轮询；WebView 未就绪（主菜单前/重建中）静默跳过本轮
        try
        {
            var web = WebBridge.GetWebView();
            if (web != null)
                MenuEntryInjector.Tick(web);
        }
        catch (Exception e)
        {
            ModCenterPlugin.Log("warn 注入心跳异常: " + e.Message);
        }
    }

    /// <summary>探针泵：全部阶段都在本 Update（Unity 主线程）执行，与 RobotSpeed Ticker 同线程上下文。</summary>
    private void ProbePump()
    {
        if (_probePhase < 0) return;
        var gate = ModCenterPlugin.DebugProbe;
        if (gate == null || !gate.Value) { _probePhase = -1; return; }

        _probeAge += Time.unscaledDeltaTime;
        switch (_probePhase)
        {
            case 0 when _probeAge >= 30f:
                _probeAge = 0f; BridgeProbe.PhaseA(); _probePhase = 1; break;
            case 1 when _probeAge >= 5f:
                _probeAge = 0f; BridgeProbe.PhaseB(); _probePhase = 2; break;
            case 2 when _probeAge >= 3f:
                BridgeProbe.PhaseC(); _probePhase = -1; break;
        }
    }
}

/// <summary>
/// 任务 4 反射桥 API 实证探针（cfg Debug.Probe 门控，一轮三拍：A 写 3 → B 验表/写回 → C 终验落盘）。
/// 靶：RobotSpeed『机器人提速/移速倍率』（float，默认 2）。表原值 MoveSpeed=4.8（三机型 2003/2004/2005），
/// 故倍率 2→表 9.6；探针写 3→表 14.4；写回 2→表回 9.6。全部证据追加写 BepInEx/ModCenter_probe.txt。
/// 确切可用 API/类型转换/线程上下文/踩坑结论固化于 DESIGN.md「反射桥 API 实证记录」，任务 6 逐字消费。
/// </summary>
internal static class BridgeProbe
{
    private const string TargetGuid = "com.local.survivallog.robotspeed";
    private const string Section = "机器人提速";
    private const string Key = "移速倍率";

    private static ConfigFile _cfg;
    private static ConfigEntry<float> _entry;
    private static float _orig;

    private static string ProbePath => System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_probe.txt");

    private static void P(string line)
    {
        try { System.IO.File.AppendAllText(ProbePath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
        ModCenterPlugin.Log("probe " + line);
    }

    /// <summary>A（Load 后 30s）：枚举 Chainloader 插件 → 取 RobotSpeed ConfigFile → 读原值 → 写 3f。</summary>
    public static void PhaseA()
    {
        try
        {
            try { if (System.IO.File.Exists(ProbePath)) System.IO.File.Delete(ProbePath); } catch { }

            // 第一环：本机构 BepInEx（IL2CPP BE 构型）无静态 Chainloader.PluginInfos，
            // 实例字典在 BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins（Dictionary<string, PluginInfo>）
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            if (plugins == null) { P("A FAIL：IL2CPPChainloader.Instance/Plugins 为 null"); return; }
            P($"A 枚举到 {plugins.Count} 个插件: {string.Join(", ", plugins.Values.Select(p => p.Metadata.GUID))}");

            var info = plugins.Values.FirstOrDefault(p => p.Metadata.GUID == TargetGuid);
            if (!(info?.Instance is BasePlugin bp)) { P("A FAIL：RobotSpeed 实例未找到或非 BasePlugin"); return; }

            // 第二环：BasePlugin.Config 是 public getter 的公共属性，强类型直取、零反射
            _cfg = bp.Config;

            // 第三环：泛型 TryGetEntry(section, key, out ConfigEntry<T>)（T 必须与 Bind 时一致，float）
            if (!_cfg.TryGetEntry(Section, Key, out _entry)) { P($"A FAIL：TryGetEntry({Section}/{Key}) 未命中"); return; }

            _orig = _entry.Value;
            P($"A entry 读原值 Value={_orig:0.###}（期望 2）；SaveOnConfigSet={_cfg.SaveOnConfigSet}");
            P($"A 写前表快照: {Table()}");

            // 第四环：typed Value 写 → ConfigFile.SettingChanged → RobotSpeed.Rerun=true（0.5s 内重写表）
            _entry.Value = 3f;
            P("A 已写 Value=3（期望 RobotSpeed Ticker ≤0.5s 把表 MoveSpeed 重写为 4.8×3=14.4）");
        }
        catch (Exception e) { P("A 异常: " + e); }
    }

    /// <summary>B（写 3 后 5s）：验 entry 与内存表已被目标 mod 热重写，随后写回原值。</summary>
    public static void PhaseB()
    {
        if (_entry == null) { P("B SKIP：A 阶段未取得 entry"); return; }
        try
        {
            P($"B 5s 后 entry.Value={_entry.Value:0.###}（期望 3）");
            P($"B 5s 后表快照: {Table()}（期望三行 14.4 = 热重写实证）");
            _entry.Value = _orig;
            P($"B 已写回原值 {_orig:0.###}（期望 Ticker ≤0.5s 内表回 4.8×2=9.6）");
        }
        catch (Exception e) { P("B 异常: " + e); }
    }

    /// <summary>C（写回后 3s）：终验 entry/表已还原，主动 Save 落盘防脏值残留。</summary>
    public static void PhaseC()
    {
        if (_entry == null) { P("C SKIP：A 阶段未取得 entry"); return; }
        try
        {
            P($"C 终验 entry.Value={_entry.Value:0.###}（期望 2）");
            P($"C 终验表快照: {Table()}（期望三行 9.6 已还原）");
            _cfg.Save(); // SaveOnConfigSet=true 时每写即存，此处兜底确保盘上=原值
            P("C ConfigFile.Save 已调；探针 DONE");
        }
        catch (Exception e) { P("C 异常: " + e); }
    }

    /// <summary>读内存表 Config_RobotAttribute 全行 MoveSpeed（读法与 RobotSpeedPlugin 现役代码一致）。</summary>
    private static string Table()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return "ConfigManager 未就绪";
            var cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
            var dict = cm._Config_RobotAttribute_Dict;
            if (dict == null || dict.Count == 0) return "Config_RobotAttribute 空";
            var sb = new StringBuilder();
            foreach (var kv in dict)
                sb.Append(kv.Key).Append('(').Append(kv.Value.Comment).Append(")MoveSpeed=")
                  .Append(kv.Value.MoveSpeed.ToString("0.###")).Append(' ');
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "表读异常: " + e.Message; }
    }
}
