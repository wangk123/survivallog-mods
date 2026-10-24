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
            if (PreDisasterSpeedPlugin.Verbose.Value)
                PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] SetSpeed(" + speed + ")");
        }
        catch (Exception e)
        {
            if (_warnSend) return;
            _warnSend = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] SendAction 异常: " + e.Message);
        }
    }

    /// <summary>读灾前章节倍率：_Config_Chapter_Dict 中 WorldStateType==1 的行（当前版本 ID=1，
    /// SpeedUp1=2/SpeedUp2=32）。读不到返回 false 并保持官方基线 2/32。</summary>
    internal static bool TryGetChapterSpeeds(out int s1, out int s2)
    {
        s1 = 2; s2 = 32;
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
                    s2 = pair.Value.SpeedUp2;
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

    // ---------- 桥接 + 自愈（Task 5） ----------

    private static Vuplex.WebView.IWebView _bridged;
    private static bool _coreUi0Visible;
    private static bool _warnBridge;
    private static readonly Regex SpeedRegex = new Regex("\"speed\"\\s*:\\s*([0-4])", RegexOptions.Compiled);

    /// <summary>订阅 Vuplex MessageEmitted（与官方 WebUILayer.OnMessageFromJS 并行收，多播互不干扰）。
    /// WebView 实例变更时对新实例重挂；旧实例随页面销毁，无需退订。</summary>
    internal static void EnsureBridge(Vuplex.WebView.IWebView web)
    {
        if (ReferenceEquals(_bridged, web)) return;
        try
        {
            // interop 无事件元数据（add_/remove_ 访问器）且 Il2Cpp 委托非 CLR 委托：
            // 经 add_MessageEmitted + DelegateSupport.ConvertDelegate 编组（CabinetEverywhere 同款），订阅机制与处理器不变。
            web.add_MessageEmitted(DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler<Vuplex.WebView.EventArgs<string>>>(
                new EventHandler<Vuplex.WebView.EventArgs<string>>(OnMessageEmitted)));
            _bridged = web;
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] MessageEmitted 桥已挂");
        }
        catch (Exception e)
        {
            if (_warnBridge) return;
            _warnBridge = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 订阅 MessageEmitted 异常: " + e.Message);
        }
    }

    /// <summary>官方协议 `typeId\x1EpageId\x1EeventType\x1Ejson`（Root.html 中继，DESIGN §3.5）。
    /// 只认 3/CoreUI0/PDS_SPEED，其余消息原样放过不碰。</summary>
    private static void OnMessageEmitted(object sender, Vuplex.WebView.EventArgs<string> e)
    {
        try
        {
            var msg = e.Value;
            if (string.IsNullOrEmpty(msg)) return;
            var p = msg.Split('\x1E');
            if (p.Length < 4 || p[0] != "3" || p[1] != "CoreUI0" || p[2] != "PDS_SPEED") return;
            var m = SpeedRegex.Match(p[3]);
            if (!m.Success) return;
            SetSpeed(int.Parse(m.Groups[1].Value));
        }
        catch
        {
            // v1.3.2 红线：静默出口必须可观测——桥内异常打点（只记一次，防刷屏）
            if (_warnBridge) return;
            _warnBridge = true;
            PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 桥内解析异常（后续静默）");
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
}
