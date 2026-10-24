using System;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>速度落地与章节倍率（DESIGN §3.2/§4.2）。
/// 落地入口 = 官方按钮同款 Ac_CoreUI1_SpeedChange.SendAction（public static，非泛型）：
/// reducer RA_SpeedChange 无条件 NotifyGlobal(10001)，GameTimeManager 全链路原生生效。</summary>
internal static class SpeedLink
{
    internal static int LastSpeed = 1;   // mod 最后设置的档位（自愈用；官方侧改档不回写）

    private static bool _warnSend;
    private static bool _warnTable;

    /// <summary>调官方档位（0=暂停 1=原速 2=二倍 3=三倍 4=慢速）；越界忽略。</summary>
    internal static void SetSpeed(int speed)
    {
        if (speed < 0 || speed > 4) return;
        try
        {
            GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange.SendAction(speed);
            LastSpeed = speed;
            // 无条件打点（装机期排查用；点击频率低不刷屏）——若此行出现但无变速，嫌疑转向 SendAction 灾前空转
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] SetSpeed(" + speed + ") 已发 SendAction");
        }
        catch (Exception e)
        {
            if (_warnSend) return;
            _warnSend = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] SendAction 异常: " + e.Message);
        }
    }

    /// <summary>读灾前章节倍率：_Config_Chapter_Dict 中 WorldStateType==1 的行（当前版本 ID=1，
    /// SpeedUp1=2/SpeedUp2=32）。TrueThreeX 开启时 s2 按校正后口径返回 3（与 TrueThreeGearPatch
    /// 实际生效倍率一致，保证 JS 高亮映射命中）。读不到时按同口径回退默认。</summary>
    internal static bool TryGetChapterSpeeds(out int s1, out int s2)
    {
        s1 = 2; s2 = PreDisasterSpeedPlugin.TrueThreeX.Value ? 3 : 32;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return false;
            var cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
            var dict = cm._Config_Chapter_Dict;
            if (dict == null) return false;
            foreach (var pair in dict)
            {
                if (pair.Value == null) continue;
                if ((int)pair.Value.WorldStateType == 1)
                {
                    s1 = pair.Value.SpeedUp1;
                    s2 = PreDisasterSpeedPlugin.TrueThreeX.Value ? 3 : pair.Value.SpeedUp2;
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            if (_warnTable) return false;
            _warnTable = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 读章节表异常: " + e.Message);
            return false;
        }
    }

    // ---------- 桥接 + 自愈（装机二轮改 Harmony prefix，见 DESIGN §7） ----------

    private static bool _coreUi0Visible;
    private static bool _warnBridge;
    private static readonly Regex SpeedRegex = new Regex("\"speed\"\\s*:\\s*([0-4])", RegexOptions.Compiled);

    /// <summary>CoreUI0（灾前 HUD）当前可见——快捷键门控用。</summary>
    internal static bool CoreUi0Visible { get { return _coreUi0Visible; } }

    /// <summary>官方中继链终点观察者：CoreUI0 页 JS 点击 → Root 无条件转发 vuplex 协议(3) →
    /// WebUILayer.OnMessageFromJS typeId=3 分支 → **WebUIVm.HandleCallback(messageType, jsonData) 虚方法**
    /// （全 VM 无重写，唯一分发点；StackLimit999 同挂点生产在役）。
    /// 装机二轮曾挂 WebUILayer.OnPageMessage——反汇编证实零调用方（死方法），装机三轮实锤后换靶。
    /// prefix 纯观察：不拦截不修改原方法，未绑定事件在原方法体内被官方丢弃，与我们无关。</summary>
    [HarmonyLib.HarmonyPatch(typeof(GameCore.HotUpdate.ReduxUI.WebUIVm), "HandleCallback")]
    internal static class PageMessagePatch
    {
        private static void Prefix(string messageType, string jsonData)
        {
            try
            {
                if (messageType != "PDS_SPEED") return;
                var m = SpeedRegex.Match(jsonData ?? "");
                if (!m.Success) return;
                PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] 桥收到 speed=" + m.Groups[1].Value);
                SpeedLink.SetSpeed(int.Parse(m.Groups[1].Value));
            }
            catch
            {
                // v1.3.2 红线：静默出口必须可观测——补丁内异常打点（只记一次，防刷屏）
                if (_warnBridge) return;
                _warnBridge = true;
                PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 桥补丁解析异常（后续静默）");
            }
        }
    }

    /// <summary>CoreUI0 可见性回执（Injector 安装脚本 0=不在/隐藏 1=已装 2=本轮新装）。
    /// 可见→不可见 = 灾变过渡：mod 记录的档位 ≠1 则归 1x，防倍率带进灾后（DESIGN §4.4）。</summary>
    internal static void OnCoreUi0State(int state)
    {
        var visible = state > 0;
        if (_coreUi0Visible && !visible && LastSpeed != 1)
        {
            SetSpeed(1);
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] CoreUI0 消失（灾变过渡）——档位自动归 1x");
        }
        _coreUi0Visible = visible;
    }

    /// <summary>真三倍校正（装机四轮作者定案）：手动三键在灾前按真实 ×3 生效，睡眠跳夜保持官方 ×32。
    /// 机制：ChangeGameSpeed(speed=3) 读 GameTimeManager.ChapterSpeedUp2（灾前=32，睡眠临时加速共用同字段——
    /// 改表/改字段都会把睡觉跳夜拖慢十倍，故不可持久改）。prefix 在"灾前+三档+TrueThreeX"时临时把字段
    /// 换成 3，postfix 立即还原原值；窗口外字段恒为官方值，睡眠路径（ExecuteTempSpeedUp）不受影响。
    /// 本补丁由 Plugin.Load 手动挂载（失败仅回落官方 ×32，不拖累 mod 其余功能）。</summary>
    internal static class TrueThreeGearPatch
    {
        private static bool _swapped;
        private static int _orig;
        private static bool _warned;

        private static void Prefix(GameCore.HotUpdate.Battle.Logic.GameTimeManager __instance, ref int speed, ref int ___ChapterSpeedUp2)
        {
            _swapped = false;
            try
            {
                if (speed != 3 || !PreDisasterSpeedPlugin.TrueThreeX.Value) return;
                if ((int)__instance.WorldStateType != 1) return;   // 仅灾前章节
                if (___ChapterSpeedUp2 == 3) return;
                _orig = ___ChapterSpeedUp2;
                ___ChapterSpeedUp2 = 3;
                _swapped = true;
            }
            catch (Exception e) { Warn(e); }
        }

        private static void Postfix(ref int ___ChapterSpeedUp2)
        {
            if (!_swapped) return;
            try { ___ChapterSpeedUp2 = _orig; _swapped = false; }
            catch (Exception e) { Warn(e); }
        }

        private static void Warn(Exception e)
        {
            if (_warned) return;
            _warned = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 三倍校正异常（后续静默，三键按官方值）: " + e.Message);
        }
    }
}
