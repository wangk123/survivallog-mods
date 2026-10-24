using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 旧档恢复+剥倒计时tag：postfix 挂 EndlessManager.Initialize（读档补账 ApplyFallenMapPoints 之后跑，
/// DESIGN §2.3）。四步全走原版 API（存档格式层零风险论证见 DESIGN §2.6）：
/// ①FallenMapPointIds 名单剔除非营地 ②fall_tier counter 清零 ③剥全局交易封印tag ④剥缓刑tag。
/// ③④不受名单为空早退（"无沦陷点但挂缓刑tag"正是剥tag首要场景）；④另只受 StripPendingFallTags 独立门控
///（不受 RestoreFallenPoints 早退，2026-10-24 终审建议修1）。全程 try/catch + trace，幂等（半途异常下次读档跑齐）。
/// </summary>
[HarmonyPatch]
internal static class FallRestorer
{
    // 参数类型数组钉死（Task 8 收官裁决）。interop mdump 实测（tools/_t8.txt）：Void(GameSaveData, GameMode)
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EndlessManager), "Initialize",
            new[] { typeof(GameCore.HotUpdate.GameSaveData), typeof(GameCore.HotUpdate.GameMode) });

    private static void Postfix(EndlessManager __instance, GameCore.HotUpdate.GameSaveData __0)
    {
        try
        {
            if (__instance == null || __0 == null) return;
            if (__instance.IsActive != true) return;   // 非无尽档空跑（IsActive 公共属性）
            var save = __0;
            var list = save.FallenMapPointIds;

            // ③④的 TagManager 先取：名单为空也要剥tag（"无沦陷点但挂缓刑tag"恰是剥tag首要场景）
            var tm = GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.IsInstanceCreated
                ? GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.Instance?._TagManager : null;

            // ④剥缓刑tag：独立门控（只受 StripPendingFallTags 管，不受 Restore 早退——终审建议修1；
            // "Restore=false 但要剥倒计时"的非默认配置从此生效）
            int stripped = 0;
            if (EndlessGuardPlugin.StripPendingFallTags.Value && tm != null)
            {
                var prefix = ReadDeferredFallPrefix();
                if (prefix != null)
                {
                    var keys = tm.GetTagKeys();
                    var doomed = new Il2CppSystem.Collections.Generic.List<string>();
                    for (int i = 0; i < keys.Count; i++)
                        if (keys[i] != null && keys[i].StartsWith(prefix)) doomed.Add(keys[i]);
                    if (doomed.Count > 0) { tm.RemoveTags(doomed); stripped = doomed.Count; }
                }
            }

            // ①②恢复+③剥封印tag：RestoreFallenPoints 门控（③留此门内=正确语义：保持沦陷时不剥封印，终审定性）
            var restored = new List<int>();
            int sealStripped = 0;
            if (EndlessGuardPlugin.RestoreFallenPoints.Value)
            {
                // ①②恢复名单（名单空=跳过恢复，继续往下剥tag，不早退）
                if (list != null)
                {
                    for (int i = list.Count - 1; i >= 0; i--)   // 倒序 Remove 安全
                    {
                        var id = list[i];
                        bool isCamp = true;    // fail-closed：查询异常按营地处理=留沦陷（终审建议修2，§6 红线保守方向）
                        try { isCamp = EndlessManager.IsCampMapPoint(id); } catch { }
                        if (isCamp) continue;                    // 营地永不恢复（DESIGN §6 红线）
                        list.RemoveAt(i);
                        GameCounter.SetCounter("endless.fall_tier_" + id, 0);
                        restored.Add(id);
                    }
                }

                // ③剥全局交易封印tag：挂了就剥（该tag只由官方"全交易点沦陷"时写入；FallBlocker已拦新增；幂等）
                if (tm != null && tm.HasTag("EndlessAllTradePointsFallen"))
                {
                    var tags = new Il2CppSystem.Collections.Generic.List<string>();
                    tags.Add("EndlessAllTradePointsFallen");
                    tm.RemoveTags(tags);
                    sealStripped = 1;
                }
            }

            EndlessGuardPlugin.Trace($"restore：恢复 {restored.Count} 点 [{string.Join(",", restored)}]，剥封印tag {sealStripped}，剥缓刑tag {stripped} 条");
        }
        catch (System.Exception e)
        {
            EndlessGuardPlugin.Trace("restore 异常（下次读档重试）: " + e.Message);
        }
    }

    /// <summary>TAG_DEFERRED_FALL_PREFIX 私有静态字段运行时读（不硬编码，抗表更新；读失败返回 null 降级）。</summary>
    private static string ReadDeferredFallPrefix()
    {
        try
        {
            var f = typeof(EndlessManager).GetField("TAG_DEFERRED_FALL_PREFIX",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return f?.GetValue(null) as string;
        }
        catch { return null; }
    }
}
