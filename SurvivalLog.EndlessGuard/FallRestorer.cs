using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 旧档恢复+剥倒计时tag：postfix 挂 EndlessManager.Initialize（读档补账 ApplyFallenMapPoints 之后跑，
/// DESIGN §2.3）。四步全走原版 API（存档格式层零风险论证见 DESIGN §2.6）：
/// ①FallenMapPointIds 名单剔除非营地 ②fall_tier counter 清零 ③剥全局交易封印tag ④剥缓刑tag。
/// ③④不受名单为空早退（"无沦陷点但挂缓刑tag"正是剥tag首要场景）；④无条件执行
///（2026-10-25 作者定案：mod 在即永不沦陷，倒计时无显示意义，开关撤除）。全程 try/catch + trace，幂等（半途异常下次读档跑齐）。
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
            if (EndlessGuardPlugin.Enabled != null && !EndlessGuardPlugin.Enabled.Value) return;   // 总开关关=还原官方（不做恢复与剥tag）
            if (__instance.IsActive != true) return;   // 非无尽档空跑（IsActive 公共属性）
            var save = __0;
            var list = save.FallenMapPointIds;

            // ③④的 TagManager 先取：名单为空也要剥tag（"无沦陷点但挂缓刑tag"恰是剥tag首要场景）
            var tm = GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.IsInstanceCreated
                ? GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.Instance?._TagManager : null;

            // ④剥缓刑tag：无条件（2026-10-25 作者定案：mod 在即永不沦陷，倒计时无意义，开关撤除）
            int stripped = 0;
            if (tm != null)
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
                        if (IsStoryLockedCamp(__instance, id)) continue;   // 营地红线细化（2026-10-25 诊断定案）：仅撤离剧情链营地不恢复
                        list.RemoveAt(i);
                        GameCounter.SetCounter("endless.fall_tier_" + id, 0);
                        restored.Add(id);
                    }
                }

                // ①b 计数器补漏（2026-10-25 作者实测定案）：UI"已沦陷（第N天）"的显示与封锁读的不止名单——
                // fall_tier 计数器是名单之外的第二份沦陷账（名单可能已被历史恢复清空而计数器残留）。
                // 扫全部地图点：非营地且（计数器>0 或 仍在名单）一律恢复（清零计数器 + 移出名单）。
                try
                {
                    // interop 的 GetAll_Config_MapPoint() 返回 IEnumerable 但枚举器缺 MoveNext（il2cpp 限制），
                    // 改走字典迭代（PlanCatalog foreach 同款已验证模式）
                    var cm = SurvivalLog.ModShared.BagTable.Cm();
                    var mpDict = cm?._Config_MapPoint_Dict;
                    if (mpDict != null)
                    {
                        foreach (var kv in mpDict)
                        {
                            var mp = kv.Value;
                            if (mp == null) continue;
                            var id = mp.ID;
                            if (restored.Contains(id)) continue;
                            if (IsStoryLockedCamp(__instance, id)) continue;
                            int tier = 0;
                            try { tier = GameCounter.GetCounter("endless.fall_tier_" + id); } catch { }
                            bool inList = false;
                            if (list != null)
                                for (int j = list.Count - 1; j >= 0; j--)
                                    if (list[j] == id) { list.RemoveAt(j); inList = true; break; }
                            if (tier > 0 || inList)
                            {
                                GameCounter.SetCounter("endless.fall_tier_" + id, 0);
                                restored.Add(id);
                            }
                        }
                    }
                }
                catch (System.Exception e) { EndlessGuardPlugin.Trace("restore 计数器扫描: " + e.Message); }

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
            DumpDiag(__instance, save, tm);
        }
        catch (System.Exception e)
        {
            EndlessGuardPlugin.Trace("restore 异常（下次读档重试）: " + e.Message);
        }
    }

    /// <summary>全量诊断（2026-10-25 作者令"一次排查完"）：把沦陷显示可能读的每一份状态全部倒进
    /// BepInEx/EndlessGuard_diag.txt——名单/每点计数器/营地状态/日程档位/显示天数/全量 tag 清单。
    /// 定位"三份账清干净仍显示已沦陷"的真源头后即可撤除。</summary>
    private static void DumpDiag(EndlessManager em, GameCore.HotUpdate.GameSaveData save, TagManager tm)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("=== EndlessGuard 全量诊断 ").Append(System.DateTime.Now.ToString("HH:mm:ss")).Append(" tier=").Append(em.CurrentTier).AppendLine(" ===");
            var list = save.FallenMapPointIds;
            sb.Append("[ FallenMapPointIds 名单 ] count=").Append(list == null ? -1 : list.Count).Append(" : ");
            if (list != null) for (int i = 0; i < list.Count; i++) sb.Append(list[i]).Append(i == list.Count - 1 ? "" : ",");
            sb.AppendLine();
            if (tm != null)
            {
                var keys = tm.GetTagKeys();
                sb.Append("[ TagManager 全量 tag ] count=").Append(keys == null ? -1 : keys.Count).AppendLine();
                if (keys != null) for (int i = 0; i < keys.Count; i++) sb.Append("  ").AppendLine(keys[i]);
            }
            sb.AppendLine("[ 每图状态 ] id|name|cat|camp|inList|fallCtr|campState|baseTier|schedTier|dispDay");
            var cm = SurvivalLog.ModShared.BagTable.Cm();
            var dict = cm?._Config_MapPoint_Dict;
            if (dict != null)
            {
                var rows = new System.Collections.Generic.SortedList<int, GameCore.HotUpdate.Config_MapPoint>();
                foreach (var kv in dict) if (kv.Value != null) rows[kv.Key] = kv.Value;
                foreach (var row in rows.Values)
                {
                    var id = row.ID;
                    bool camp; try { camp = EndlessManager.IsCampMapPoint(id); } catch { camp = true; }
                    bool inL = false; if (list != null) for (int j = 0; j < list.Count; j++) if (list[j] == id) { inL = true; break; }
                    int ctr = 0; try { ctr = GameCounter.GetCounter("endless.fall_tier_" + id); } catch { }
                    int cs = -1; try { cs = em.GetCampState(id); } catch { }
                    int bt = -1; try { bt = em.GetBaseFallTier(id); } catch { }
                    int st = -1; try { st = em.GetScheduledFallTier(id); } catch { }
                    int dd = -1; try { dd = em.GetFallDisplayDay(id); } catch { }
                    sb.Append("  ").Append(id).Append('|').Append(row.Name_Local).Append('|').Append(row.PointCategory)
                      .Append('|').Append(camp ? 1 : 0).Append('|').Append(inL ? 1 : 0).Append('|').Append(ctr)
                      .Append('|').Append(cs).Append('|').Append(bt).Append('|').Append(st).Append('|').Append(dd).AppendLine();
                }
            }
            System.IO.File.AppendAllText(System.IO.Path.Combine(
                BepInEx.Paths.BepInExRootPath, "EndlessGuard_diag.txt"), sb.ToString());
            EndlessGuardPlugin.Trace("diag：全量诊断已写入 EndlessGuard_diag.txt");
        }
        catch (System.Exception e) { EndlessGuardPlugin.Trace("diag: " + e.Message); }
    }

    /// <summary>营地红线细化（2026-10-25 作者验收第三轮+全量诊断定案）：仅已进入撤离剧情链的营地
    /// （campState=Evacuated5/Lost6）不恢复；普通日程沦陷的营地照常恢复。原一刀切"营地永不恢复"
    /// 误伤了 1100 城东幸存者营地/1101 废墟回收站——诊断实锤其 campState=0（撤离链根本没跑过，
    /// 无剧情状态可冲突，与 CampStateEvacuated/Lost 的告别事件链不是一回事）。</summary>
    private static bool IsStoryLockedCamp(EndlessManager em, int id)
    {
        bool camp;
        try { camp = EndlessManager.IsCampMapPoint(id); } catch { return true; }   // fail-closed：查询异常按锁定
        if (!camp) return false;
        int st;
        try { st = em.GetCampState(id); } catch { return true; }                   // 同上
        return st == 5 || st == 6;
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
