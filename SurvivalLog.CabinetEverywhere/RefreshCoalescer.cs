using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 工作台袋刷新合并（2026-10-20，作者定版设计"一次做一次算"）：
/// 单件完成的同帧里，材料移除×N + 产物入包×M 各自触发一次全量重算（实测 10 件=53 次，
/// G4/G14 级联）。本组件在事件入口 Ac_ToolTable_RefreshBag.SendAction()（无参，袋变更→
/// RA_RefreshBag→RefreshRecipeList 的风暴源头）做**帧尾合并**：同帧多次触发只挂一个待办，
/// 下一帧由 AccessTicker 统一补派一次——补派走原生完整派发管线，界面自然重推（与已回滚
/// 合并阀的本质区别：不在计算层跳过、不脱离派发上下文）。
/// 每件完成 = RA_Result 内部重算 1 次（不碰）+ 合并后袋刷新 1 次 = 最多 2 次/件。
/// 显示滞后仅一帧（16~33ms）；点配方/制作/收产物走原路径零影响。
/// 随 General.Enabled 总开关（2026-10-24）：false 时 prefix 放行官方刷新（禁用即回到
/// 官方逐次派发；Load 阶段禁用则补丁不装），其余行为默认开启随套件发布。
/// </summary>
internal static class RefreshCoalescer
{
    private static bool _installed;
    private static volatile bool _pending;
    private static bool _flushing; // 补派中直通，防无限延迟自环
    private static MethodBase _send;

    internal static void Install()
    {
        if (_installed) return;
        try
        {
            _send = AccessTools.Method(typeof(GameCore.HotUpdate.ReduxUI.Ac_ToolTable_RefreshBag),
                "SendAction", Type.EmptyTypes);
            if (_send == null)
            {
                CabinetEverywherePlugin.Log.LogWarning("[CE.合并] Ac_ToolTable_RefreshBag.SendAction() 未找到，合并未生效");
                return;
            }

            var h = new Harmony("com.local.survivallog.cabineteverywhere.coalesce");
            h.Patch(_send, prefix: new HarmonyMethod(typeof(RefreshCoalescer), nameof(SendPrefix)));
            _installed = true;
            CabinetEverywherePlugin.Log.LogInfo("[CE.合并] 已挂：袋刷新帧尾合并（同帧多次→下帧一次）");
        }
        catch (Exception e)
        {
            CabinetEverywherePlugin.Log.LogWarning("[CE.合并] 安装失败（不影响游戏）: " + e.Message);
        }
    }

    private static bool SendPrefix()
    {
        // General.Enabled 停用门（2026-10-24）：false=放行官方刷新（不合并不吞帧；
        // _pending 恒 false，Flush 同步空转）。Load 阶段禁用时补丁根本不装。
        if (CabinetEverywherePlugin.Enabled == null || !CabinetEverywherePlugin.Enabled.Value)
            return true;
        if (_flushing)
            return true; // 补派中直通，防无限延迟自环
        _pending = true;
        return false; // 本帧吞掉，下一帧 Flush 统一补派
    }

    /// <summary>AccessTicker.Update 每帧最先调用（0.5s 门闩之前）。</summary>
    internal static void Flush()
    {
        if (!_installed || !_pending) return;
        _pending = false;
        _flushing = true;
        try
        {
            _send.Invoke(null, null);
        }
        catch (Exception e)
        {
            CabinetEverywherePlugin.Log.LogWarning("[CE.合并] 补派失败: " + e.Message);
        }
        finally
        {
            _flushing = false;
        }
    }
}
