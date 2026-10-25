using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 追加波诊断探针（2026-10-25 作者验收第四轮："买卡后柜子没上货/地图没刷新红点"）：
/// 挂家具刷新链三函数，把中间值全部倒进 EndlessGuard_diag.txt——
/// ①RefreshForCurrentMap：进图时 发现等级/快照计数器/基础回填计数器/洞察卡清单
/// ②AppendDiscoveryWave：追加波触发时的 容器数+等级
/// ③FillRandomLoot：三图柜子池(8006-8010/8012-8026)的实际填充 (池,数量,discoverLv,guaranteeLv,返回值)
/// 定位后撤除。
/// </summary>
internal static class LootDiag
{
    private static readonly System.Text.StringBuilder Buf = new();
    private static void W(string s)
    {
        lock (Buf) { Buf.Append('[').Append(System.DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ").AppendLine(s); }
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "EndlessGuard_diag.txt"), "[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s + "\n"); } catch { }
    }

    private static int CurMap()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.IsInstanceCreated) return 0;
            var em = GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.Instance?._ExploreManager;
            return em == null ? 0 : em.MapConfigId;
        }
        catch { return 0; }
    }

    [HarmonyPatch]
    private static class RefreshDiag
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(FurnitureLootManager), "RefreshForCurrentMap");
        private static void Postfix()
        {
            try
            {
                var map = CurMap();
                var sb = new System.Text.StringBuilder();
                sb.Append("RefreshForCurrentMap ← map=").Append(map);
                try { sb.Append(" 发现等级=").Append(ExploreDiscoveryTools.GetDiscoveryLv(map)); } catch (System.Exception e) { sb.Append(" 发现等级=ERR:").Append(e.Message); }
                try { sb.Append(" 快照=").Append(GameCounter.GetCounter("furnitureloot.discoverlv." + map)); } catch { }
                try { sb.Append(" 基础回填=").Append(GameCounter.GetCounter("furnitureloot.basefill." + map)); } catch { }
                Il2CppSystem.Collections.Generic.List<int> cards = null;
                try
                {
                    cards = ExploreDiscoveryTools.GetInsightCards(map);
                    sb.Append(" 卡表=[");
                    if (cards != null) for (int i = 0; i < cards.Count; i++) sb.Append(cards[i]).Append(i == cards.Count - 1 ? "" : ",");
                    sb.Append(']');
                }
                catch (System.Exception e) { sb.Append(" 卡表=ERR:").Append(e.Message); }
                // 持有态终审（2026-10-25）：GetDiscoveryLv 数等级用的就是 HasActivated——
                // 直接 dump 每张卡的 HasActivated 结果 + Redux 已购名单，一次定案"买了但没持有"在哪层
                try
                {
                    var role = (GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.IsInstanceCreated ? GameCore.HotUpdate.BaseSingleton<BattleLogicWorld>.Instance?._AgentManager : null)?.GetLeadingRole();
                    // interop 泛型限制（记忆实证）：GetAgentComponent<T> 仅 <object> 预编译实例可用，TryCast 落型
                    var compObj = role == null ? null : GameCore.HotUpdate.Battle.Logic.AgentTools.GetAgentComponent<GameCore.HotUpdate.Battle.Logic.BaseComponent>(role);
                    var spc = compObj?.TryCast<SurvivalPlanningComponent>();
                    sb.Append(" HasActivated=[");
                    if (spc != null && cards != null)
                        for (int i = 0; i < cards.Count; i++) sb.Append(cards[i]).Append(':').Append(spc.HasActivated(cards[i]) ? 1 : 0).Append(i == cards.Count - 1 ? "" : " ");
                    sb.Append(']');
                }
                catch (System.Exception e) { sb.Append(" HasActivated=ERR:").Append(e.Message); }
                try
                {
                    var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
                    var dataTree = rus?.reduxStoreLayer?.dataTree;
                    if (dataTree != null && dataTree.TryGetValue(Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Talent)), out var raw))
                    {
                        var st = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Talent>();
                        var ids = st?.SurvivalPlanningIds;
                        sb.Append(" Redux已购=[");
                        if (ids != null) for (int i = 0; i < ids.Count; i++) sb.Append(ids[i]).Append(i == ids.Count - 1 ? "" : ",");
                        sb.Append(']');
                    }
                }
                catch (System.Exception e) { sb.Append(" Redux=ERR:").Append(e.Message); }
                W(sb.ToString());
            }
            catch (System.Exception e) { W("RefreshDiag: " + e.Message); }
        }
    }

    [HarmonyPatch]
    private static class WaveDiag
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(FurnitureLootManager), "AppendDiscoveryWave");
        private static void Postfix(Il2CppSystem.Collections.Generic.List<Furniture> __0, int __1)
        {
            try { W("AppendDiscoveryWave 容器数=" + (__0 == null ? -1 : __0.Count) + " 等级=" + __1); }
            catch (System.Exception e) { W("WaveDiag: " + e.Message); }
        }
    }

    [HarmonyPatch]
    private static class FillDiag
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(FurnitureLootTools), "FillRandomLoot");
        private static void Postfix(int __result, int __1, int __2, int __5, int __6)
        {
            try
            {
                if (__1 < 8006 || __1 > 8026) return;   // 只记三图柜子池
                W("FillRandomLoot 池=" + __1 + " 请求=" + __2 + " discoverLv=" + __5 + " guaranteeLv=" + __6 + " 实填=" + __result);
            }
            catch { }
        }
    }
}
