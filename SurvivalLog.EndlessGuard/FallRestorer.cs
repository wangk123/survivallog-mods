using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 旧档恢复+剥倒计时tag：postfix 挂 EndlessManager.Initialize（读档补账 ApplyFallenMapPoints 之后跑，
/// DESIGN §2.3）。四步全走原版 API（存档格式层零风险论证见 DESIGN §2.6）：
/// ①FallenMapPointIds 名单剔除非营地 ②fall_tier counter 清零 ③剥全局交易封印tag ④剥缓刑tag。
/// 全程 try/catch + trace，幂等（半途异常下次读档跑齐）。
/// </summary>
[HarmonyPatch]
internal static class FallRestorer
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EndlessManager), "Initialize");

    private static void Postfix(EndlessManager __instance, GameCore.HotUpdate.GameSaveData __0)
    {
        try
        {
            if (!EndlessGuardPlugin.RestoreFallenPoints.Value) return;
            if (__instance == null || __0 == null) return;
            if (__instance.IsActive != true) return;   // 非无尽档空跑（IsActive 公共属性）
            var save = __0;
            var list = save.FallenMapPointIds;
            if (list == null || list.Count == 0) { EndlessGuardPlugin.Trace("restore：名单为空，无事可做"); return; }

            var restored = new List<int>();
            for (int i = list.Count - 1; i >= 0; i--)   // 倒序 Remove 安全
            {
                var id = list[i];
                bool isCamp = false;
                try { isCamp = EndlessManager.IsCampMapPoint(id); } catch { }
                if (isCamp) continue;                    // 营地永不恢复（DESIGN §6 红线）
                list.RemoveAt(i);
                GameCounter.SetCounter("endless.fall_tier_" + id, 0);
                restored.Add(id);
            }

            // ③全局交易封印：只要恢复了任意点就剥（幂等：HasTag 防呆）
            var tm = GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.IsInstanceCreated
                ? GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.Instance?._TagManager : null;
            if (tm != null && restored.Count > 0 && tm.HasTag("EndlessAllTradePointsFallen"))
            {
                var tags = new Il2CppSystem.Collections.Generic.List<string>();
                tags.Add("EndlessAllTradePointsFallen");
                tm.RemoveTags(tags);
            }

            // ④剥缓刑tag（前缀运行时读私有静态字段，读不到降级）
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

            EndlessGuardPlugin.Trace($"restore：恢复 {restored.Count} 点 [{string.Join(",", restored)}]，剥缓刑tag {stripped} 条");
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
