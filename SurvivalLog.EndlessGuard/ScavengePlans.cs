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
/// 生存规划扩展：把 11 条 Talent/ConditionSet 行注进内存表 + 新卡 ID 追加进洞察注册表活引用
/// + 功能4b 内容层（ApplyContent：篮子物品按 itemId 扩池挂门槛 / 官方封印货原值校验后降档，见文末）。
/// 时机：GuardTicker 每 2s 检查 Cm() 就绪且装表完成后注入（2026-10-26 两连事故定案：async 状态机
/// MoveNext 一律禁挂——d__256 步进注入=装表破坏重试循环、d__0/EnterMain 续体注入=启动序列卡死黑屏，
/// 见 EndlessGuardPlugin GuardTicker 头注；巡检的静默失败期天然避开装表窗口=其稳定性来源）。
/// PlanPick 侧目录已加行数失效复核，注入晚到亦可自愈。
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
    private static readonly Dictionary<long, int> OrigGates = new();   // poolId*100000+itemId -> 首读原门槛（幂等防护之二）
    internal static int InjectedCount;   // 自证

    /// <summary>GuardTicker 2s 巡检入口（2026-10-26 两连事故后回归并定案：async 状态机 MoveNext
    /// 禁挂——装表中注入=Initialize 重试循环；装表后续体注入=启动卡死黑屏，见 EndlessGuardPlugin
    /// GuardTicker 头注）。全部幂等（哨兵判重）。
    /// 巡检时代的事故（2026-10-25 晚）：注入完成于 22:26:31，而 PlanPick 目录构建（构建后永不重建）
    /// 抢先落在 :29~:31 的帧隙窗口 → 冻结在官方 465 项，自选规划整局看不到 11 条注入卡——
    /// 已由 PlanPick 侧目录行数失效复核根治（注入晚到/表重建，目录自动重建收敛）。</summary>
    internal static void OnConfigStep()
    {
        if (EndlessGuardPlugin.Enabled != null && !EndlessGuardPlugin.Enabled.Value) return;   // 总开关关=不注入（已生效改动保留，重启干净）
        var cm = BagTable.Cm();
        if (cm == null) return;                 // 单例未建，下步步进再试
        // 方案B（2026-10-25 作者验收第二轮定案）：UI 沦陷显示的第三份账=Config_EndlessTier 三张点名表
        //（当前阶段≥点名排定阶段即显示"已沦陷"，与 FallMapPoint 是否真执行无关——FallBlocker 拦了执行
        // 但显示层照读日程）。清空三张表=日程源头消失（倒计时/已沦陷显示/调度三合一），挂 PreventNewFalls 门。
        if (EndlessGuardPlugin.PreventNewFalls.Value) ClearTierFallLists(cm);
        if (!EndlessGuardPlugin.ExtendScavengePlans.Value) return;
        if (!_loaded) { LoadJson(); if (!_loaded) return; }
        // 哨兵复查（保留巡检时代的结论，官方重试/换语言会重跑 Initialize 整体重建 _Config_*_Dict）：
        // 每步校验首行/首篮是否还在，被冲掉即重放；Inject/ApplyContent 内部逐行 ContainsKey/IndexOf 幂等。
        var tDict = cm._Config_Talent_Dict;
        if (tDict == null || !tDict.ContainsKey(Plans[0].TalentId)) Inject(cm);
        var rgDict = cm._Config_RandomGroup_Dict;
        if (rgDict != null && rgDict.TryGetValue(Baskets[0].PoolId, out var sentinelPool)
            && sentinelPool != null && IndexOf(sentinelPool.IdList, Baskets[0].Items[0].ItemId) < 0)
            ApplyContent();
        AppendRegistry();                       // 注册表可能被 EnsureRegistry 重载覆盖：随步进补（Contains 幂等，开销可忽略）
    }

    /// <summary>清空 Config_EndlessTier 的 FallMapPoints/StoryFallMapPoints/DroneFallMapPoints（每轮复查，
    /// 表重建后自动重清；稳态静默）。这三张表是沦陷调度+UI"已沦陷/倒计时"显示的日程源头（DESIGN §2.1）。</summary>
    private static void ClearTierFallLists(GameCore.HotUpdate.ConfigManager cm)
    {
        try
        {
            var dict = cm._Config_EndlessTier_Dict;
            if (dict == null) return;
            int tiers = 0, entries = 0;
            foreach (var kv in dict)
            {
                var row = kv.Value;
                if (row == null) continue;
                var changed = false;
                var a = row.FallMapPoints; if (a != null && a.Count > 0) { entries += a.Count; a.Clear(); changed = true; }
                var b = row.StoryFallMapPoints; if (b != null && b.Count > 0) { entries += b.Count; b.Clear(); changed = true; }
                var c = row.DroneFallMapPoints; if (c != null && c.Count > 0) { entries += c.Count; c.Clear(); changed = true; }
                if (changed) tiers++;
            }
            if (tiers > 0)
                EndlessGuardPlugin.Trace("tier清空：" + tiers + " 个阶段的点名表共 " + entries + " 条沦陷排期已抹除");
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("tier清空: " + e.Message); }
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
                // upsert：行不存在则建；显示字段无条件拍平（2026-10-25 作者实机截图实锤：官方 T 列表把 Name/Dec
                // 当本地化键走 GetLocalText→GetLocalTxt，键 miss 时 mov rax,rbx 原样透传输入串。写键名则显示键名，
                // 直接写字面中文则透传显示中文，无需注入本地化表）
                GameCore.HotUpdate.Config_Talent t;
                if (!tDict.TryGetValue(p.TalentId, out t))
                {
                    t = new GameCore.HotUpdate.Config_Talent();
                    t.ID = p.TalentId; t.TalentID = p.TalentId; t.Lv = 1; t.Available = 1;
                    t.DemoTwoMode = 0; t.EndlessMode = 0; t.IsReget = false;
                    t.Type = 2; t.ShowType = 2;
                    t.LvParaShow = ""; t.LvParaShow_Local = "";
                    t.BuffID = 0; t.BuffRefresh = 0;
                    t.ConditionSetId = p.CondId;
                    t.PlayerSelectId = new Il2CppSystem.Collections.Generic.List<int>();   // 官方行原值=[]=不限角色
                    tDict[p.TalentId] = t;
                    InjectedCount++;
                }
                t.Name = p.Name; t.Name_Local = p.Name;
                t.Dec = t.Dec_Local = "搜索能力增强，可以发现更多被忽略的物资（发现等级+" + p.Level + "档）";
                t.Icon = icon.Item1; t.WebIcon = icon.Item2;
                t.LvUpCost = p.Cost;

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
    /// 追加后 GetDiscoveryLv 自动数到新卡（HasActivated 循环），官方追加波（AppendDiscoveryWave）下轮进图自动上货。
    /// 2026-10-25 二连丢卡（·七/学校·三）反汇编定案追加反向表：AddBuff 主门（0x3018A75 gate）=
    /// BuffID&gt;0 ∨ id==32111 ∨ GetInsightMapForCard&gt;0 ∨ 在静态数组；GetInsightMapForCard(0x2826440)
    /// 只查 _mapByCard 反向表——缺反向项→官方购买链扣点后【静默不加卡】。反向表同款每轮补（幂等）。</summary>
    private static string _lastRegErr;   // 步进频率下表未齐的 NRE 每步必发：同文只打一次防刷屏（表齐后自然停）
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
            AppendReverseMap();
        }
        catch (Exception e) { var m = "registry: " + e.Message; if (m != _lastRegErr) { _lastRegErr = m; EndlessGuardPlugin.Trace(m); } }
    }

    /// <summary>_mapByCard（卡→图，静态偏移 0x18）反射追加。interop 静态引用字段以属性或字段形态暴露，两种都试。
    /// 每轮重取实例（防 EnsureRegistry 重建换对象后写进陈旧字典），句柄缓存。</summary>
    private static System.Reflection.PropertyInfo _revMapPi;
    private static System.Reflection.FieldInfo _revMapFi;
    private static bool _revMapTried;
    private static void AppendReverseMap()
    {
        try
        {
            if (!_revMapTried)
            {
                _revMapTried = true;
                var t = typeof(GameCore.HotUpdate.Battle.Logic.ExploreDiscoveryTools);
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                _revMapPi = t.GetProperty("_mapByCard", F);
                if (_revMapPi == null) _revMapFi = t.GetField("_mapByCard", F);
                if (_revMapPi == null && _revMapFi == null) { EndlessGuardPlugin.Trace("registry：_mapByCard 反射不可达（GetProperty/GetField 双失败）"); }
            }
            var d = (_revMapPi != null ? _revMapPi.GetValue(null) : _revMapFi?.GetValue(null))
                as Il2CppSystem.Collections.Generic.Dictionary<int, int>;
            if (d == null) return;
            int added = 0;
            foreach (var p in Plans)
                if (!d.ContainsKey(p.TalentId)) { d[p.TalentId] = p.MapId; added++; }
            if (added > 0) EndlessGuardPlugin.Trace("registry：反向表(卡→图) +" + added + " 项——AddBuff 购买门已通");
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("registry 反向表: " + e.Message); }
    }

    // ---- 功能4b 内容层落表（DESIGN §2.5.3）：篮子扩池 + 官方封印货降档。只改进程内存表，零存档写入 ----
    // 幂等三重防护（2026-10-25 闩锁改复查后全为可重入设计）：IndexOf 池内去重 / OrigGates 降档记首读原值按原值推导 /
    // 门槛已是目标值则跳写。表被游戏重建冲掉时 Tick 复查哨兵项自动重放（见 Tick 注释）。
    internal static void ApplyContent()
    {
        try
        {
            var cm = BagTable.Cm();
            var rgDict = cm?._Config_RandomGroup_Dict;
            if (rgDict == null) return;                 // 表字典未就绪：下轮 Tick 复查重试

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

            // 篮子：逐项去重追加（池追加后 IdList/CountList/DiscoverLvList 三表同步）
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
            // 稳态静默：仅在本次实际改动时输出（每 2s 复查重入，防 trace 刷屏）
            if (added > 0 || demoted > 0)
                EndlessGuardPlugin.Trace("content：篮子追加 " + added + " 项 + 降档 " + demoted + " 项已对齐");
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
