using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SurvivalLog.PlanPick;

/// <summary>规划元数据条目。语义唯一真源：prototype/gen_data.js（游戏更新后重跑对照）。</summary>
internal sealed class PlanEntry
{
    public int Id; public string Name = "", Desc = "", Icon = "", Cat = "function", Sub; // Sub=null→独立
    public int Cost; public bool IsReget; public int? UnlockDay;                 // null=不入池
    public List<int> PrereqIds = new(); public List<string> SpecialDesc = new(); public List<int> RoleIds = new();
    public int ConditionSetId;                                             // 官方特殊条件判定用（0=无条件）
}

internal static class PlanCatalog
{
    internal static List<PlanEntry> Plans;          // 表就绪后构建一次
    internal static readonly List<string> BuildLog = new();
    private static int _builtTalentCount = -1;      // 构建时的 Talent 表行数；行数变=表被重建或他 mod 注入 → 目录重建

    internal static bool Build(GameCore.HotUpdate.ConfigManager cm)
    {
        var talentDict = cm._Config_Talent_Dict; var groupDict = cm._Config_RandomGroup_Dict;
        var dailyDict = cm._Config_DailyRandom_Dict; var condDict = cm._Config_ConditionSet_Dict;
        if (talentDict == null || groupDict == null || dailyDict == null || condDict == null) return false;
        if (Plans != null)
        {
            // 失效复核（2026-10-25 465 项事故）：目录曾"构建即终身缓存"，注入晚到/表重建都追不进——
            // 自选规划整局缺 11 条注入卡。改按行数复核：EndlessGuard 挂官方装表管道随行注入后行数
            // 在装表步进内就位（本复核是二道防线），官方重跑 Initialize 重建表也能捕捉。
            // 行数不变视为内容未变跳过（每轮一次 Count 原生调用，开销可忽略；interop 包装实例
            // 每次访问可能新造，ReferenceEquals 不可靠，故用行数不用引用比对）。
            if (talentDict.Count == _builtTalentCount) return true;
            BuildLog.Add("Talent 表行数 " + _builtTalentCount + "→" + talentDict.Count + "，目录重建");
            Plans = null;
        }
        try { BuildCore(talentDict, groupDict, dailyDict, condDict); _builtTalentCount = talentDict.Count; return true; }
        // 每 2s 重试下 BuildLog 只保留首条异常（与上一条相同则不重复添加，防无限增长）
        catch (Exception e)
        {
            Plans = null;
            var msg = "构建异常: " + e.Message;
            if (BuildLog.Count == 0 || BuildLog[BuildLog.Count - 1] != msg) BuildLog.Add(msg);
            return false;
        }
    }

    private static void BuildCore(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Talent> talentDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> groupDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_DailyRandom> dailyDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_ConditionSet> condDict)
    {
        // ---- 1) 名单：Type==2 && Available==1（审计定稿，与官方可抽集合一致）----
        var rows = new List<GameCore.HotUpdate.Config_Talent>();
        foreach (var kv in talentDict) { var r = kv.Value; if (r.Type == 2 && r.Available == 1) rows.Add(r); }

        // ---- 2) 池最早出现日：DailyRandom.RandomPlanID ∪ SpecifiedPlanID，组→日取首见 ----
        var poolFirstDay = new Dictionary<int, int>();
        foreach (var kv in dailyDict)
        {
            var r = kv.Value; int day = r.Day;
            foreach (var g in r.RandomPlanID) if (g != 0 && !poolFirstDay.ContainsKey(g)) poolFirstDay[g] = day;
            foreach (var g in r.SpecifiedPlanID) if (g != 0 && !poolFirstDay.ContainsKey(g)) poolFirstDay[g] = day;
        }
        var planDay = new Dictionary<int, int>();
        foreach (var kv in groupDict)
        {
            var g = kv.Value; if (!poolFirstDay.TryGetValue(g.ID, out var d)) continue;
            foreach (var id in g.IdList)
                if (!planDay.TryGetValue(id, out var cur) || d < cur) planDay[id] = d;
        }

        // ---- 3) 分类（与 gen_data.js 同序）：物资→扩建→属性→功能 ----
        var materialIds = new HashSet<int>();
        foreach (var kv in groupDict) { var g = kv.Value; if (g.ID == 1005 || g.ID == 1009) foreach (var id in g.IdList) materialIds.Add(id); }
        var expandPool = new HashSet<int>();
        foreach (var kv in groupDict) { var g = kv.Value; if (g.ID == 9122) foreach (var id in g.IdList) expandPool.Add(id); }
        string CatOf(GameCore.HotUpdate.Config_Talent r) =>
            materialIds.Contains(r.ID) ? "material"
            : (r.Name_Local ?? "").StartsWith("扩建·") || expandPool.Contains(r.ID) ? "expand"
            : (r.ID >= 30000 && r.ID < 31000) ? "attribute" : "function";

        // ---- 4) 二级：前置链系列（LCP 剥尾 Lv/·）；扩建取区域段；专属家具位 ----
        var condById = new Dictionary<int, GameCore.HotUpdate.Config_ConditionSet>();
        foreach (var kv in condDict) condById[kv.Key] = kv.Value;
        var t2all = new HashSet<int>();
        foreach (var kv in talentDict) if (kv.Value.Type == 2) t2all.Add(kv.Key);
        var parent = new Dictionary<int, int>();   // 仅 PlanLvGate 单前置链
        foreach (var r in rows)
        {
            if (r.ConditionSetId == 0 || !condById.TryGetValue(r.ConditionSetId, out var c)) continue;
            var tn = c.TalentNeed; if (tn == null || tn.Count != 1) continue;
            if (t2all.Contains(tn[0]) && (c.Source ?? "").StartsWith("PlanLvGate")) parent[r.ID] = tn[0];
        }
        int RootOf(int id) { var seen = new HashSet<int>(); while (parent.TryGetValue(id, out var p) && seen.Add(id)) id = p; return id; }
        var chains = new Dictionary<int, List<GameCore.HotUpdate.Config_Talent>>();
        foreach (var r in rows) { var k = RootOf(r.ID); if (!chains.TryGetValue(k, out var l)) chains[k] = l = new(); l.Add(r); }
        var series = new Dictionary<int, string>();
        foreach (var ch in chains.Values)
        {
            if (ch.Count < 2) continue;
            var lcp = LcpNames(ch);
            lcp = System.Text.RegularExpressions.Regex.Replace(lcp, @"\s*Lv[\s·]*$", "");
            lcp = System.Text.RegularExpressions.Regex.Replace(lcp, @"[\s·]+$", "");
            if (lcp.Length < 2) lcp = System.Text.RegularExpressions.Regex.Replace(ch[0].Name_Local ?? "", @" Lv\d+$", "");
            foreach (var r in ch) series[r.ID] = lcp;
        }

        // ---- 5) 输出 ----
        Plans = new List<PlanEntry>();
        foreach (var r in rows)
        {
            var e = new PlanEntry { Id = r.ID, Name = r.Name_Local ?? "", Desc = r.Dec_Local ?? "",
                Icon = r.WebIcon ?? "", Cost = r.LvUpCost, IsReget = r.IsReget, Cat = CatOf(r), ConditionSetId = r.ConditionSetId };
            e.UnlockDay = planDay.TryGetValue(r.ID, out var ud) ? ud : (int?)null;
            if (r.ConditionSetId != 0 && condById.TryGetValue(r.ConditionSetId, out var c))
            {
                var tn = c.TalentNeed;
                if (tn != null) for (int i = 0; i < tn.Count; i++) e.PrereqIds.Add(tn[i]);
                if (c.SurvivalToDay > 0) e.SpecialDesc.Add("生存至第" + c.SurvivalToDay + "天");
                if (c.NeedFurnitureIds != null && c.NeedFurnitureIds.Count > 0) e.SpecialDesc.Add("需持有特定家具");
                if (c.EventTag != null && c.EventTag.Count > 0) e.SpecialDesc.Add("需解锁特定地点/事件");
                if (c.DefenseLevel > 0) e.SpecialDesc.Add("需防御等级" + c.DefenseLevel);
                if (c.NeedTrapObtainable != null && c.NeedTrapObtainable.Count > 0) e.SpecialDesc.Add("需可制作特定防御装置");
                if (c.BuffNeed != null && c.BuffNeed.Count > 0) e.SpecialDesc.Add("需特定状态");
                if (c.HistoryCount > 0) e.SpecialDesc.Add("需历史记录");
            }
            var ps = r.PlayerSelectId;
            if (ps != null) for (int i = 0; i < ps.Count; i++) e.RoleIds.Add(ps[i]);
            // 二级
            if (e.Cat == "expand")
            {
                if ((r.Name_Local ?? "").StartsWith("扩建·"))
                {
                    var seg = r.Name_Local.Split('·').Length > 1 ? r.Name_Local.Split('·')[1] : "";
                    e.Sub = System.Text.RegularExpressions.Regex.Replace(seg, @"(大型位|中型位|小型位|桌面位)$", "");
                    if (e.Sub.Length == 0) e.Sub = seg;
                }
                else if (e.RoleIds.Count > 0) e.Sub = "专属家具位";
            }
            if (e.Sub == null) e.Sub = series.TryGetValue(r.ID, out var s) ? s : null;
            Plans.Add(e);
        }
        var byCat = Plans.GroupBy(p => p.Cat);
        foreach (var g in byCat) BuildLog.Add(g.Key + " " + g.Count() + " 项");
        BuildLog.Add("不入池 " + Plans.Count(p => p.UnlockDay == null) + " 项");
    }

    private static string LcpNames(List<GameCore.HotUpdate.Config_Talent> ch)
    {
        var s = ch[0].Name_Local ?? "";
        foreach (var r in ch) { var n = r.Name_Local ?? ""; int i = 0; while (i < s.Length && i < n.Length && s[i] == n[i]) i++; s = s.Substring(0, i); }
        return s;
    }
}
