using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;

namespace SurvivalLog.PlanPick;

/// <summary>
/// 运行时状态读取 + payload 组装。
/// 已实证访问路径（gamekb 2026-10-23_26157732 dump）：
/// - Redux: BaseSingleton&lt;ReduxUISystem&gt;.Instance.reduxStoreLayer.dataTree.TryGetValue(Il2CppType.From(typeof(State_X)), out BaseReduxState)（CabinetCatalog 同款）
/// - State_UI_RogueSelect: Day(int) / CachedSurvivalPlanIds(List&lt;int&gt; 今日候选，刷新后重建)
/// - State_Web_RogueSelect: OwnPoints(ReactiveProperty&lt;int&gt; → .Value)
/// - State_Data_Talent: SurvivalPlanningIds(List&lt;int&gt; 已激活)
/// - ConfigManager.GetCurrentPlayerSelectId() 静态；BattleLogicWorld._ConditionSetManager 内部属性（interop 可直读）
/// - ConditionSetManager.CheckConditionSet(int, long=0, bool=false) → bool（官方特殊条件判定）
/// </summary>
internal static class PlanState
{
    private static HashSet<int> _today;      // 今日候选（含刷新后）
    private static int _day = -1;
    private static int _points;

    internal static int CurrentDay() { ReadStates(); return _day; }
    internal static bool IsTodayCandidate(int id) { ReadStates(); return _today != null && _today.Contains(id); }

    /// <summary>读 RogueSelect 的 Web 状态（页面真正消费的状态：Day/今日候选/点数全在这）。
    /// 经 GetStateByUI(WebUI类型) 取（CabinetEverywhere ToolTablePruner/TabInjector 同款先例，
    /// 先例键全部是 WebUI_*——原生 UI_* 不在该注册表，装机实测 GetStateByUI(UI_RogueSelect) 返回 null）。</summary>
    private static void ReadStates()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var web = rus?.GetStateByUI(Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.WebUI_RogueSelect)))
                ?.TryCast<GameCore.HotUpdate.ReduxUI.State_Web_RogueSelect>();
            if (web == null) return;
            _day = web.Day;
            _points = web.OwnPoints != null ? web.OwnPoints.Value : _points;
            var cached = web.CachedSurvivalPlanIds;             // il2cpp List<int>
            if (cached != null)
            {
                var set = new HashSet<int>();
                for (int i = 0; i < cached.Count; i++) set.Add(cached[i]);
                _today = set;
            }
        }
        catch { /* 保持旧值 */ }
    }

    /// <summary>payload 前置条件未就绪时的一次性打点（同一原因只打一次；v1.3.2 判定链全打点之训）。</summary>
    private static string _lastFailReason;
    private static void FailOnce(string reason)
    {
        if (reason == _lastFailReason) return;
        _lastFailReason = reason;
        PlanPickPlugin.Log.LogInfo("[PlanPick] 数据未就绪，暂不推送：" + reason);
    }

    /// <summary>组装 payload JSON。返回 false=依赖未就绪（不推送）。</summary>
    internal static bool TryBuildPayload(out string json, out List<string> logs)
    {
        json = null; logs = new List<string>();
        var cm = SurvivalLog.ModShared.BagTable.Cm();
        if (cm == null) { FailOnce("ConfigManager 未就绪"); return false; }
        if (!PlanCatalog.Build(cm)) { FailOnce("目录构建失败（" + (PlanCatalog.BuildLog.Count > 0 ? PlanCatalog.BuildLog[PlanCatalog.BuildLog.Count - 1] : "表未就绪") + "）"); return false; }
        ReadStates();
        if (_day < 0) { FailOnce("State_Web_RogueSelect 未读到（天数）——规划页未开过或 GetStateByUI 失败"); return false; }

        int role = 1; try { role = GameCore.HotUpdate.ConfigManager.GetCurrentPlayerSelectId(); } catch { }
        var points = _points;   // ReadStates 已从 State_Web_RogueSelect.OwnPoints 读
        HashSet<int> activated = null;
        try
        {
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;   // Data 状态走 dataTree（State_Data_Item 先例）
            GameCore.HotUpdate.ReduxUI.BaseReduxState raw = null;
            dataTree?.TryGetValue(Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Talent)), out raw);
            var talent = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Talent>();
            if (talent?.SurvivalPlanningIds != null)
            {
                activated = new HashSet<int>();
                var sp = talent.SurvivalPlanningIds;
                for (int i = 0; i < sp.Count; i++) activated.Add(sp[i]);
            }
        }
        catch { }
        if (activated == null) { FailOnce("State_Data_Talent 未读到（已激活名单）"); return false; }
        _lastFailReason = null;   // 成功后清零：下次失败再打一次（比如换档重来）

        // 条件管理器（特殊条件判定）
        GameCore.HotUpdate.Battle.Logic.ConditionSetManager csm = null;
        try { csm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance?._ConditionSetManager; } catch { }

        // 角色名表
        var roleNames = new Dictionary<int, string>();
        try { foreach (var kv in cm._Config_PlayerSelect_Dict) roleNames[kv.Key] = kv.Value.Name_Local ?? ("角色" + kv.Key); } catch { }

        var percent = PlanPickPlugin.MultiplierPercent;   // 整数百分比 100~300，payload 直传整数
        var showLocked = PlanPickPlugin.ShowLocked.Value; var showUnpooled = PlanPickPlugin.ShowUnpooled.Value;

        var sb = new StringBuilder(64 * 1024);
        var roleCache = new Dictionary<int, List<int>>();   // 本次 payload 构建内的表现值缓存
        sb.Append("{\"plans\":[");
        int n = 0, shown = 0;
        foreach (var p in PlanCatalog.Plans)
        {
            n++;
            if (p.UnlockDay == null && !showUnpooled) continue;
            // lockState: bought/locked/ok（点数是否够 JS 侧用 points 判，不在此算）
            string state; var reasons = new List<string>();
            if (activated.Contains(p.Id)) state = "bought";
            else
            {
                if (p.UnlockDay != null && _day < p.UnlockDay.Value) reasons.Add("第 " + p.UnlockDay.Value + " 天解锁（当前第 " + _day + " 天）");
                foreach (var pre in p.PrereqIds) if (!activated.Contains(pre))
                    {
                        var preName = PlanCatalog.Plans.Find(x => x.Id == pre)?.Name ?? ("#" + pre);
                        reasons.Add("需先激活「" + preName + "」");
                    }
                // 角色锁：读运行时表现值（非目录快照）——RoleUnlock 等互通类 mod 清了内存表
                // PlayerSelectId（空=不限，与官方 IsRoleAllowed 同语义）即自动放行；
                // 只装本 mod 时表现值=官方原值，照锁。读失败按目录原值（fail-closed）。
                var liveRoles = LiveRoleIds(cm, p.Id, roleCache);
                var roleIds = liveRoles ?? p.RoleIds;
                if (roleIds.Count > 0 && !roleIds.Contains(role))
                    reasons.Add("仅限「" + JoinRoleNames(roleIds, roleNames) + "」的专属规划");
                if (p.SpecialDesc.Count > 0)
                {
                    if (csm != null)
                    {
                        // 特殊条件走官方判定；无条件(0)视为满足；判定异常按未满足（展示原因，不误放行）
                        bool ok;
                        try { ok = p.ConditionSetId == 0 || csm.CheckConditionSet(p.ConditionSetId); }
                        catch { ok = false; }
                        if (!ok) reasons.AddRange(p.SpecialDesc);
                    }
                    else reasons.AddRange(p.SpecialDesc);
                }
                state = reasons.Count > 0 ? (showLocked ? "locked" : "skip") : "ok";
            }
            if (state == "skip") continue;
            if (n > 1 && shown > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(p.Id)
              .Append(",\"name\":\"").Append(JsEscapePublic(p.Name)).Append("\",\"desc\":\"").Append(JsEscapePublic(p.Desc)).Append('"')
              .Append(",\"icon\":\"").Append(JsEscapePublic(p.Icon)).Append("\",\"cost\":").Append(p.Cost)
              .Append(",\"isReget\":").Append(p.IsReget ? "true" : "false")
              .Append(",\"cat\":\"").Append(p.Cat).Append("\",\"sub\":").Append(p.Sub == null ? "null" : "\"" + JsEscapePublic(p.Sub) + "\"")
              .Append(",\"unlockDay\":").Append(p.UnlockDay == null ? "null" : p.UnlockDay.Value.ToString())
              .Append(",\"today\":").Append(_today != null && _today.Contains(p.Id) ? "true" : "false")
              .Append(",\"exclusive\":").Append(p.RoleIds.Count > 0 ? "true" : "false")
              .Append(",\"state\":\"").Append(state).Append("\",\"reasons\":[");
            for (int i = 0; i < reasons.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(JsEscapePublic(reasons[i])).Append('"'); }
            sb.Append("]}");
            shown++;
        }
        sb.Append("],\"categories\":[{\"key\":\"attribute\",\"name\":\"属性成长\"},{\"key\":\"function\",\"name\":\"功能强化\"},")
          .Append("{\"key\":\"expand\",\"name\":\"家具扩建\"},{\"key\":\"material\",\"name\":\"物资贸易\"}]")
          .Append(",\"today\":[");
        if (_today != null)
        {
            var ids = new List<int>(_today); ids.Sort();
            for (int i = 0; i < ids.Count; i++) { if (i > 0) sb.Append(','); sb.Append(ids[i]); }
        }
        sb.Append("],\"points\":").Append(points).Append(",\"mult\":").Append(percent)
          .Append(",\"day\":").Append(_day).Append(",\"role\":").Append(role)
          .Append(",\"roleNames\":{");
        int rn = 0; foreach (var kv in roleNames) { if (rn++ > 0) sb.Append(','); sb.Append(kv.Key).Append(":\"").Append(JsEscapePublic(kv.Value)).Append('"'); }
        sb.Append("}}");
        json = sb.ToString();
        logs.Add("规划 " + shown + "/" + n + " 项（今日候选 " + (_today == null ? 0 : _today.Count) + "，第 " + _day + " 天，点数 " + points + "，加成 " + percent + "%）");
        return shown > 0;
    }

    /// <summary>读某规划运行时 PlayerSelectId 表现值（互通类 mod 可能已清空；空列表=不限）。null=读取失败，调用方回退目录原值。</summary>
    private static List<int> LiveRoleIds(GameCore.HotUpdate.ConfigManager cm, int planId, Dictionary<int, List<int>> cache)
    {
        if (cache.TryGetValue(planId, out var hit)) return hit;
        List<int> result = null;
        try
        {
            var ps = cm.Get_Config_Talent(planId)?.PlayerSelectId;
            if (ps != null)
            {
                result = new List<int>();
                for (int i = 0; i < ps.Count; i++) result.Add(ps[i]);
            }
        }
        catch { result = null; }
        cache[planId] = result;
        return result;
    }

    private static string JoinRoleNames(List<int> ids, Dictionary<int, string> names)
    {
        var parts = new List<string>();
        foreach (var id in ids) parts.Add(names.TryGetValue(id, out var n) ? n : ("角色" + id));
        return string.Join(" / ", parts);
    }

    internal static string JsEscapePublic(string s) { /* 最小 JSON 转义（Injector 嵌 page.js 复用） */
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
