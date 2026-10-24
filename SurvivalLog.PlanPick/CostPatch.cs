using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.PlanPick;

/// <summary>
/// 自选消耗翻倍：prefix 判"不在今日候选"→临时把该行 LvUpCost ×倍率（内存表编辑，RoleUnlock 同款）；
/// finalizer 无条件恢复（异常路径也还原）。官方候选内购买零干预（原生零接触承诺，DESIGN §3）。
/// </summary>
[HarmonyPatch]
internal static class CostPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.SurvivalResultsManager), "OnBuySurvivalPlan");

    private static int _boostedId;
    private static int _origCost;

    private static void Prefix(int talentId)
    {
        // 自愈：上次提升若未收尾（理论同步重入），先还原该行再继续（Task6 评审加固）
        if (_boostedId != 0)
            try { var stale = SurvivalLog.ModShared.BagTable.Cm()?.Get_Config_Talent(_boostedId); if (stale != null) stale.LvUpCost = _origCost; }
            catch { }
        _boostedId = 0;
        try
        {
            var percent = PlanPickPlugin.MultiplierPercent;          // 整数百分比 100~300（作者定稿口径）
            if (percent <= 100) return;                              // 100=原值零干预
            if (PlanState.IsTodayCandidate(talentId)) return;        // 今日候选：官方原价路径
            var cm = SurvivalLog.ModShared.BagTable.Cm();
            var cfg = cm?.Get_Config_Talent(talentId);
            if (cfg == null) return;
            _origCost = cfg.LvUpCost;
            // 实收=原价×百分比/100 四舍五入取整（Floor(x+0.5)=半值向上，与 page.js Math.round 双端一致）
            cfg.LvUpCost = (int)Math.Floor(_origCost * percent / 100.0 + 0.5);
            _boostedId = talentId;
            PlanPickPlugin.Log.LogInfo("[PlanPick][Cost] 自选 " + talentId + " 原价 " + _origCost + " -> " + cfg.LvUpCost + "（" + percent + "%）");
        }
        catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick][Cost] prefix 异常: " + e.Message); }
    }

    private static void Finalizer()
    {
        if (_boostedId == 0) return;
        try
        {
            var cfg = SurvivalLog.ModShared.BagTable.Cm()?.Get_Config_Talent(_boostedId);
            if (cfg != null) { cfg.LvUpCost = _origCost; }
            PlanPickPlugin.Log.LogInfo("[PlanPick][Cost] 已恢复 " + _boostedId + " 原价 " + _origCost);
        }
        catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick][Cost] finalizer 异常: " + e.Message); }
        _boostedId = 0;
    }
}
