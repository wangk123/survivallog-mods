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
        _boostedId = 0;
        try
        {
            var mult = Math.Max(1f, PlanPickPlugin.CostMultiplier.Value);
            if (mult <= 1f) return;                       // 无倍率零干预
            if (PlanState.IsTodayCandidate(talentId)) return;   // 今日候选：官方原价路径
            var cm = SurvivalLog.ModShared.BagTable.Cm();
            var cfg = cm?.Get_Config_Talent(talentId);
            if (cfg == null) return;
            _origCost = cfg.LvUpCost;
            cfg.LvUpCost = (int)Math.Round(_origCost * mult);
            _boostedId = talentId;
            PlanPickPlugin.Log.LogInfo("[PlanPick][Cost] 自选 " + talentId + " 原价 " + _origCost + " -> " + cfg.LvUpCost);
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
