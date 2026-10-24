using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using SurvivalLog.ModShared;

namespace SurvivalLog.EndlessGuard;

internal sealed class PlanRow
{
    public string Name; public int TalentId, CondId, Day, Prereq, Cost, MapId, Level;
}

/// <summary>篮子行（功能4b）：往 PoolId 池追加 Items（挂 Level 门槛）。itemId 显式定位，不走名字通道（2026-10-24 裁决）。</summary>
internal sealed class BasketRow
{
    public int MapId, Level, PoolId; public List<BasketItem> Items;
}

internal sealed class BasketItem
{
    public int ItemId, Copies;
}

/// <summary>降档行（功能4b）：官方封印货 FromLevel→ToLevel（原值校验后才写，Note 供 trace）。</summary>
internal sealed class DemoteRow
{
    public int PoolId, ItemId, FromLevel, ToLevel; public string Note;
}

/// <summary>
/// 生存规划扩展：读档期把 11 条 Talent/ConditionSet 行注进内存表 + 新卡 ID 追加进洞察注册表活引用
/// + 功能4b 内容层（ApplyContent：篮子物品按 itemId 扩池挂门槛 / 官方封印货原值校验后降档，见文末）。
/// 时机：Ticker 每 2s 检查 Cm() 就绪即注（远早于 PlanPick 目录构建=首次开规划页，DESIGN §5）。
/// 幂等：dict.ContainsKey 防重注；注册表 Contains 防重加。表是进程内存态，不写任何存档（DESIGN §2.6）。
/// interop 绑定按 mdump 实测修正（2026-10-24，tools/_t5tal.txt / _t5cond.txt）：
/// - ConditionSet 的 EventTag/EventNoTag/BuffTag/EventTagOr/SymbolNeed/SymbolNoNeed 是 List&lt;string&gt;（非 List&lt;int&gt;）；
/// - 补 BagBurnFurnitureIDs 空 List&lt;int&gt;（官方消费方直接 .Count，null 即 NRE）；
/// - PlayerSelectId 用空 List（官方行原值=[]=不限角色；null 赋值interop不稳且消费方可能直读 .Count）；
/// - LvParaShow/LvParaShow_Local 置 ""（官方行原值为空串非 null）。
/// </summary>
internal static class ScavengePlans
{
    internal static List<PlanRow> Plans = new();
    internal static List<BasketRow> Baskets = new();     // 功能4b：篮子扩池（ApplyContent 一次追加）
    internal static List<DemoteRow> Demotions = new();   // 功能4b：官方封印货降档（原值校验后写 toLevel）
    private static bool _loaded;
    private static bool _injected;
    private static bool _contentDone;                    // 功能4b：篮子只追加一次（幂等防护之一）
    private static readonly Dictionary<long, int> OrigGates = new();   // poolId*100000+itemId -> 首读原门槛（幂等防护之二）
    internal static int InjectedCount;   // 自证

    internal static void Tick()
    {
        if (!EndlessGuardPlugin.ExtendScavengePlans.Value) return;
        if (!_loaded) { LoadJson(); if (!_loaded) return; }
        var cm = BagTable.Cm();
        if (cm == null) return;                 // 表未就绪，下轮再试
        if (!_injected && Inject(cm)) { _injected = true; }
        if (_injected && !_contentDone) ApplyContent();   // rgDict 未就绪/中途异常 → 下轮重试（Task5 字典判空重试同款；三重幂等防护兜底，_contentDone 落定即止）
        AppendRegistry();                       // 注册表可能被 EnsureRegistry 重载覆盖：每轮补（Contains 幂等，开销可忽略）
    }

    private static void LoadJson()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SurvivalLog.EndlessGuard.scavenge_plans.json");
            if (s == null) { EndlessGuardPlugin.Trace("plans：内嵌资源缺失（csproj 检查 EmbeddedResource）"); return; }
            // 零外部依赖：System.Text.Json（net6 自带，套件不引 Newtonsoft）；解析 plans/baskets/demotions 三段
            //（nativeUnlocks 是离线校验专用数据——原生门槛已由官方埋好，运行时无需消费）
            using var doc = System.Text.Json.JsonDocument.Parse(new StreamReader(s).ReadToEnd());
            foreach (var p in doc.RootElement.GetProperty("plans").EnumerateArray())
            {
                Plans.Add(new PlanRow
                {
                    Name = p.GetProperty("name").GetString(),
                    TalentId = p.GetProperty("talentId").GetInt32(),
                    CondId = p.GetProperty("condId").GetInt32(),
                    Day = p.GetProperty("day").GetInt32(),
                    Prereq = p.GetProperty("prereq").GetInt32(),
                    Cost = p.GetProperty("cost").GetInt32(),
                    MapId = p.GetProperty("mapId").GetInt32(),
                    Level = p.GetProperty("level").GetInt32()
                });
            }
            foreach (var b in doc.RootElement.GetProperty("baskets").EnumerateArray())
            {
                var row = new BasketRow
                {
                    MapId = b.GetProperty("mapId").GetInt32(),
                    Level = b.GetProperty("level").GetInt32(),
                    PoolId = b.GetProperty("poolId").GetInt32(),
                    Items = new List<BasketItem>()
                };
                foreach (var it in b.GetProperty("items").EnumerateArray())
                    row.Items.Add(new BasketItem { ItemId = it.GetProperty("itemId").GetInt32(), Copies = it.GetProperty("copies").GetInt32() });
                Baskets.Add(row);
            }
            foreach (var d in doc.RootElement.GetProperty("demotions").EnumerateArray())
            {
                Demotions.Add(new DemoteRow
                {
                    PoolId = d.GetProperty("poolId").GetInt32(),
                    ItemId = d.GetProperty("itemId").GetInt32(),
                    FromLevel = d.GetProperty("fromLevel").GetInt32(),
                    ToLevel = d.GetProperty("toLevel").GetInt32(),
                    Note = d.GetProperty("note").GetString()
                });
            }
            _loaded = true;
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("plans LoadJson: " + e.Message); }
    }

    /// <summary>注入内存表。返回 false=表字典尚未装载（Cm() 单例就绪≠dict 就绪，2026-10-24 装机实锤：
    /// 启动 7s 时 _Config_*_Dict 仍 null，NRE 被吞后旧版置 _injected=true 永不重试），下轮 Tick 重试。
    /// 行级幂等：talent/cond 各自 ContainsKey 判重（半途异常重试时，已入行的 cond 补建不被 talent 判重挡死）。</summary>
    private static bool Inject(GameCore.HotUpdate.ConfigManager cm)
    {
        var tDict = cm._Config_Talent_Dict;
        var cDict = cm._Config_ConditionSet_Dict;
        if (tDict == null || cDict == null) return false;
        try
        {
            var icon = tDict.TryGetValue(31601, out var proto) ? (proto.Icon, proto.WebIcon) : ("", "");
            foreach (var p in Plans)
            {
                if (!tDict.ContainsKey(p.TalentId))
                {
                    var t = new GameCore.HotUpdate.Config_Talent();
                    t.ID = p.TalentId; t.TalentID = p.TalentId; t.Lv = 1; t.Available = 1;
                    t.DemoTwoMode = 0; t.EndlessMode = 0; t.IsReget = false;
                    t.Name = "Talent_Name_" + p.TalentId; t.Name_Local = p.Name;
                    t.Dec = "Talent_Dec_" + p.TalentId; t.Dec_Local = "搜索能力增强，可以发现更多被忽略的物资（发现等级+" + p.Level + "档）";
                    t.Type = 2; t.ShowType = 2; t.Icon = icon.Item1; t.WebIcon = icon.Item2;
                    t.LvParaShow = ""; t.LvParaShow_Local = "";
                    t.LvUpCost = p.Cost; t.BuffID = 0; t.BuffRefresh = 0;
                    t.ConditionSetId = p.CondId;
                    t.PlayerSelectId = new Il2CppSystem.Collections.Generic.List<int>();   // 官方行原值=[]=不限角色
                    tDict[p.TalentId] = t;
                    InjectedCount++;
                }

                if (cDict.ContainsKey(p.CondId)) continue;
                var c = NewCondSet(p.CondId);
                c.Source = "PlanLvGate_" + p.TalentId;    // PlanPick 系列推导靠此前缀（DESIGN §5 要素4）
                c.TalentNeed.Add(p.Prereq);
                c.SurvivalToDay = p.Day;                  // 卡面"生存至第N天"（PlanCatalog.cs:112）
                cDict[p.CondId] = c;
            }
            EndlessGuardPlugin.Trace("plans 注入 " + InjectedCount + "/" + Plans.Count + " 行（icon=" + icon.Item2 + "）");
            return true;
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("plans Inject: " + e.Message); return false; }
    }

    /// <summary>ConditionSet 全列表字段必须非 null（官方消费方直接 .Count，null 即 NRE）。照 gamekb 列清单逐个建空表
    ///（int 字段 il2cpp 构造默认 0；List&lt;string&gt;/List&lt;int&gt; 类型以 interop mdump 实测为准，见类头注释）。</summary>
    private static GameCore.HotUpdate.Config_ConditionSet NewCondSet(int id)
    {
        var c = new GameCore.HotUpdate.Config_ConditionSet();
        c.ID = id;
        c.EventTag = new Il2CppSystem.Collections.Generic.List<string>();
        c.EventNoTag = new Il2CppSystem.Collections.Generic.List<string>();
        c.AttriKey = new Il2CppSystem.Collections.Generic.List<int>();
        c.Compare = new Il2CppSystem.Collections.Generic.List<int>();
        c.Number = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapAttriKey = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapCompare = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapNumber = new Il2CppSystem.Collections.Generic.List<int>();
        c.ItemNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.BagRequire = new Il2CppSystem.Collections.Generic.List<int>();
        c.BagSatietyFurnitureIDs = new Il2CppSystem.Collections.Generic.List<int>();
        c.BuffTag = new Il2CppSystem.Collections.Generic.List<string>();
        c.EventTagOr = new Il2CppSystem.Collections.Generic.List<string>();
        c.BuffNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.SymbolNeed = new Il2CppSystem.Collections.Generic.List<string>();
        c.SymbolNoNeed = new Il2CppSystem.Collections.Generic.List<string>();
        c.TalentNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureType = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureIds = new Il2CppSystem.Collections.Generic.List<int>();
        c.BagBurnFurnitureIDs = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureObtainable = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedTrapObtainable = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedItemCategory = 0; c.CounterKey = "";
        c.SurvivalToDay = 0; c.OpenTimeStart = 0; c.OpenTimeEnd = 0;
        c.SelfModelState = ""; c.SelfModelStateNot = "";
        return c;
    }

    /// <summary>注册表活引用追加：GetInsightCards 返回字典内的 List（0x2826350 实证非副本），Add 即生效。
    /// 追加后 GetDiscoveryLv 自动数到新卡（HasActivated 循环），官方追加波（AppendDiscoveryWave）下轮进图自动上货。</summary>
    private static void AppendRegistry()
    {
        try
        {
            foreach (var g in Plans.GroupBy(p => p.MapId))
            {
                var cards = GameCore.HotUpdate.Battle.Logic.ExploreDiscoveryTools.GetInsightCards(g.Key);
                if (cards == null) continue;
                foreach (var p in g)
                    if (!cards.Contains(p.TalentId)) { cards.Add(p.TalentId); EndlessGuardPlugin.Trace("registry：图" + g.Key + " +卡 " + p.TalentId); }
            }
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("registry: " + e.Message); }
    }

    /// <summary>该图已购新卡数（数量层 Task 7 消费）。Redux 已购名单 PlanPick 同款读取；读不到返回 0（fail-safe 无加成）。</summary>
    internal static int OwnedNewCount(int mapId)
    {
        try
        {
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;
            if (dataTree == null) return 0;
            dataTree.TryGetValue(Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Talent)), out var raw);
            var st = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Talent>();
            var ids = st?.SurvivalPlanningIds;
            if (ids == null) return 0;
            var set = new HashSet<int>();
            for (int i = 0; i < ids.Count; i++) set.Add(ids[i]);
            var n = 0; foreach (var p in Plans) if (p.MapId == mapId && set.Contains(p.TalentId)) n++;
            return n;
        }
        catch { return 0; }
    }

    // ---- 功能4b 内容层落表（DESIGN §2.5.3）：篮子扩池 + 官方封印货降档。只改进程内存表，零存档写入 ----
    // 幂等三重防护：_contentDone 篮子一次追加 / OrigGates 降档记首读原值按原值推导 / IndexOf 池内去重。
    internal static void ApplyContent()
    {
        try
        {
            var cm = BagTable.Cm();
            var rgDict = cm?._Config_RandomGroup_Dict;
            if (rgDict == null) return;                 // 表字典未就绪：_contentDone 不落定，Tick 下轮重试

            // 降档：记进程内首读原值，永远按 原值=fromLevel 的声明校验后写 toLevel（RoleUnlock 幂等模式）
            int demoted = 0;   // 实写计数（终审建议修3：Demotions.Count 声明数在表漂移跳过时会误导取证）
            foreach (var d in Demotions)
            {
                if (!rgDict.TryGetValue(d.PoolId, out var g)) { EndlessGuardPlugin.Trace("content：缺池 " + d.PoolId); continue; }
                var idx = IndexOf(g.IdList, d.ItemId);
                if (idx < 0) { EndlessGuardPlugin.Trace("content：池" + d.PoolId + " 缺物品 " + d.ItemId + "（表漂移？跳过）"); continue; }
                var key = d.PoolId * 100000L + d.ItemId;
                if (!OrigGates.TryGetValue(key, out var orig)) { orig = g.DiscoverLvList[idx]; OrigGates[key] = orig; }
                if (orig != d.FromLevel) { EndlessGuardPlugin.Trace($"content：{d.Note} 原门槛 Lv{orig}≠声明 Lv{d.FromLevel}，跳过"); continue; }
                if (g.DiscoverLvList[idx] != d.ToLevel) { g.DiscoverLvList[idx] = d.ToLevel; demoted++; }
            }

            // 篮子：一次追加（_contentDone 防重复；池追加后 IdList/CountList/DiscoverLvList 三表同步）
            if (!_contentDone)
            {
                int added = 0;
                foreach (var b in Baskets)
                {
                    if (!rgDict.TryGetValue(b.PoolId, out var g)) { EndlessGuardPlugin.Trace("content：缺池 " + b.PoolId); continue; }
                    foreach (var it in b.Items)
                    {
                        if (IndexOf(g.IdList, it.ItemId) >= 0) continue;   // 去重（check 脚本已离线保证）
                        g.IdList.Add(it.ItemId);
                        g.CountList.Add(it.Copies);
                        g.DiscoverLvList.Add(b.Level);
                        added++;
                    }
                }
                _contentDone = true;
                EndlessGuardPlugin.Trace("content：篮子追加 " + added + " 项 + 降档 " + demoted + " 项已对齐");
            }
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("content: " + e.Message); }
    }

    private static int IndexOf(Il2CppSystem.Collections.Generic.List<int> list, int v)
    {
        if (list == null) return -1;
        for (int i = 0; i < list.Count; i++) if (list[i] == v) return i;
        return -1;
    }
}
