using System;

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
}
