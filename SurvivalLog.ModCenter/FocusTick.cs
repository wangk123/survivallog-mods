using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 键盘门复用（任务 8）：面板打开期间把键盘交给网页（覆盖 ID 数字框等可键入），关闭双还原。
/// 机制照抄 PlanPick/Injector.cs:100-140 十二轮定案（装机实证英文通道唯一开关）：
/// - 开（上升沿）：WebUILayer.SetKeyboardEnabled(true)（裸键/英文进浏览器）+ 记录进入前
///   imeCompositionMode 后设 On（中文 IME 组合由 Vuplex 直投浏览器，十一轮实证）。
/// - 关（下降沿）：SetKeyboardEnabled(false) + imeCompositionMode 精确还原进入前值。
/// API 签名实证 dump.cs：SetKeyboardEnabled(bool) / get_IsKeyboardEnabled（public）。
/// 与 PlanPick 的门控判据差异：PlanPick 是官方页 IsPageActive 三候选；本面板不是官方页，
/// 门控 = 面板开合状态（MC_OPEN/MC_CLOSE 消息，T5/T7 已打通 outbox；ConfigBridge 主线程
/// Pump 调 NotifyOpen/NotifyClose 置位）。0.3s 周期由 ModCenterTicker 驱动（unscaled，
/// 暂停菜单打开也要照走），状态变化沿触发；异常只 Warn 不抛。
/// 自证（硬规则 9）：每次沿写一行 BepInEx/ModCenter_kb.txt（open→enabled/imeOn，
/// close→restored/原值），CDP/人工轮询该文件即装机断言——CDP 合成键不受 Vuplex 门控，
/// 不能作门开的证据。
/// </summary>
internal static class FocusTick
{
    private static bool _panelOpen;   // 面板开合状态（MC_OPEN/MC_CLOSE 消息沿）
    private static bool _kbHanded;    // 键盘门当前已交出（沿去重，同 PlanPick _kbHanded）
    private static bool _imeSaved;    // _prevIme 为本次开面板会话记录（层丢失后重交接不覆盖原值）
    private static UnityEngine.IMECompositionMode _prevIme = UnityEngine.IMECompositionMode.Auto;

    private static readonly HashSet<string> Warned = new();

    private static string KbPath => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "ModCenter_kb.txt");

    // ---- 状态入口（ConfigBridge.HandleOutbox 主线程 Pump 调；与 Tick 同线程，无竞态） ----

    internal static void NotifyOpen() => _panelOpen = true;

    internal static void NotifyClose() => _panelOpen = false;

    /// <summary>Load 时清旧自证文件（每轮运行从空文件开始，验证读数干净）。</summary>
    internal static void Reset()
    {
        try { if (System.IO.File.Exists(KbPath)) System.IO.File.Delete(KbPath); } catch { }
    }

    /// <summary>0.3s 巡检（ModCenterTicker）：按 _panelOpen 与 _kbHanded 的沿交接/还原。</summary>
    internal static void Tick()
    {
        var layer = GetLayer();
        if (layer == null)
        {
            // 同 PlanPick（Injector.cs:112）：层不可用即无从调 SetKeyboardEnabled，只复位标志。
            // 差异（本面板非官方页，状态由 JS 消息驱动）：层没了 ⇒ WebView/JS 也没了 ⇒ 面板必已
            // 消亡且永远等不到 MC_CLOSE——须就地还原 IME 静态并复位开合状态，防键盘门漏开
            // （键盘门标志随层销毁；新层默认门关，无需也无法对死层还原）。
            if (_kbHanded)
            {
                _kbHanded = false;
                TryRestoreIme("layer-lost");
            }
            _panelOpen = false;
            return;
        }

        if (_panelOpen && !_kbHanded)
        {
            _kbHanded = true;
            try
            {
                layer.SetKeyboardEnabled(true);
                // 只在本次开面板会话首次交接时记录进入前值：层短暂丢失后重交接时当前值即
                // 已还原的原值，再记会把 On 记成原值（PlanPick 未区分；此处"精确还原"要求闭环）。
                if (!_imeSaved) { _prevIme = UnityEngine.Input.imeCompositionMode; _imeSaved = true; }
                UnityEngine.Input.imeCompositionMode = UnityEngine.IMECompositionMode.On;
                Kb($"OPEN kb={KbState(layer)} ime {_prevIme}->On");
            }
            catch (Exception e)
            {
                _kbHanded = false;
                Warn("hand", "SetKeyboardEnabled(true) 异常: " + e.Message);
                return;
            }
            Warned.Remove("returned"); // 允许下一轮关闭再打一次状态日志
            ModCenterPlugin.Log("面板打开，键盘交给网页（SetKeyboardEnabled + IME On，原值 " + _prevIme + "）");
        }
        else if (!_panelOpen && _kbHanded)
        {
            _kbHanded = false;
            try
            {
                layer.SetKeyboardEnabled(false);
                UnityEngine.Input.imeCompositionMode = _prevIme;
                _imeSaved = false;
                Kb($"CLOSE kb={KbState(layer)} ime->{_prevIme}");
            }
            catch (Exception e) { Warn("return", "SetKeyboardEnabled(false) 异常: " + e.Message); }
            Warned.Remove("hand");
            ModCenterPlugin.Log("面板关闭，键盘交还游戏（IME 还原 " + _prevIme + "）");
        }
    }

    /// <summary>层丢失收尾：IME 静态是 Unity 侧的，层死了也要还原（键盘门随层销毁，无需调）。</summary>
    private static void TryRestoreIme(string why)
    {
        try
        {
            UnityEngine.Input.imeCompositionMode = _prevIme;
            _imeSaved = false;
            Kb($"ORPHAN({why}) ime->{_prevIme} kb-dies-with-layer");
            Warn(why, "层丢失时面板仍开：IME 已就地还原 " + _prevIme + "（键盘门随层销毁；面板视为消亡）");
        }
        catch (Exception e) { Warn(why, "层丢失还原 IME 异常: " + e.Message); }
    }

    /// <summary>读回键盘门实际状态（get_IsKeyboardEnabled，自证用；读失败只记 ?）。</summary>
    private static string KbState(GameCore.HotUpdate.ReduxUI.WebUILayer layer)
    {
        try { return layer.IsKeyboardEnabled ? "on" : "off"; }
        catch { return "?"; }
    }

    /// <summary>ViewportProbe/PlanPick 同款取层路径（ReduxUISystem → GetWebUILayer）。</summary>
    private static GameCore.HotUpdate.ReduxUI.WebUILayer GetLayer()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance?.GetWebUILayer();
        }
        catch { return null; }
    }

    private static void Kb(string line)
    {
        try { System.IO.File.AppendAllText(KbPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n"); } catch { }
    }

    private static void Warn(string key, string msg)
    {
        if (Warned.Add(key))
            ModCenterPlugin.Log("warn " + msg);
    }
}
