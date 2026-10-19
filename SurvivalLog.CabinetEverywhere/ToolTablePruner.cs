using System;
using System.Collections.Generic;
using GameCore.HotUpdate.ReduxUI;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 工作台 CabinetTabs 修剪：让原生扫描集合 = 界面所见（2026-10-20 制作卡顿治理）。
///
/// 背景：原生开台（RA_Open←AE_OpenHandMade.Run）从 CraftSourceFurnitureIds（类型集合）
/// 全量重建 CabinetTabs，每次重算按该列表逐容器取物品扫描（G12：成本=Σ容器物品行）。
/// 类型集合表达不了实例标签，故此前实现为"全量进列表 + JS 藏显示"——显示收窄了，扫描没收窄。
/// 本组件在开台后按视图谓词修剪列表（与 tagfilter.page.js viewAllows 同语义），标签数据
/// 变化时对账（移出新隐藏、按登记加回新可见）。
///
/// 索引语义（JS TAB_SWITCH{j+2} / activeLeftTab&lt;2 实证）：activeLeftTab 0/1=背包/抽屉
/// （不在 CabinetTabs，原生 GetAllLinkedOwnerIds 另加），柜子页签从 +2 起。修剪后重排全部
/// Index=列表位次并平移 ActiveLeftTab；活动页签本身不剪（下次开台自然收敛）。
/// Browser 视图不剪（交易/无人机站语义，工作台视图=Browser 时同样全量）。
/// 零 Harmony，纯状态列表操作（TabInjector 同手法）。
/// 日志：汇总行常开；逐条明细走 Debug.Verbose（发版关开关即静默，不删代码）。
/// </summary>
internal static class ToolTablePruner
{
    private sealed class RegEntry
    {
        public long OwnerId;
        public int ConfigId;
        public string Name;
        public bool IsRack;
    }

    private static readonly Il2CppSystem.Type TtType =
        Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_ToolTable));

    // 本轮会话登记（开台快照的全量底，用于标签变化后加回）
    private static readonly Dictionary<long, RegEntry> _registry = new();
    private static bool _wasActive;
    private static string _lastSig = "";

    private static readonly HashSet<string> Warned = new();
    private static void WarnOnce(string key, string msg)
    {
        if (Warned.Add(key))
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere.工作台] " + msg);
    }

    internal static void Poll()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            if (rus == null) return;

            bool active;
            try { active = rus.IsUIActive(TtType); }
            catch { return; }
            if (!active)
            {
                if (_wasActive) { _wasActive = false; _registry.Clear(); _lastSig = ""; }
                return;
            }

            var st = rus.GetStateByUI(TtType)?.TryCast<State_Web_ToolTable>();
            var tabs = st?.CabinetTabs;
            if (tabs == null) return;
            if (_registry.Count == 0 && tabs.Count == 0) return; // 开台未建表，等下轮

            if (!_wasActive)
            {
                _wasActive = true;
                _lastSig = "";
            }

            // 开台（或原生重建）首次见到条目 → 登记全量底
            int before = _registry.Count;
            foreach (var e in Snapshot(tabs))
                if (!_registry.ContainsKey(e.OwnerId))
                    _registry[e.OwnerId] = e;
            if (_registry.Count != before)
                Log($"开台登记柜页签 {_registry.Count} 条（明细开 Debug.Verbose；背包/工作台抽屉不在列表内）");

            Reconcile(rus, st, tabs);
        }
        catch (Exception e)
        {
            WarnOnce("poll", "修剪轮询异常: " + e.Message);
        }
    }

    private static IEnumerable<RegEntry> Snapshot(
        ObservableCollections.ObservableList<Data_Web_ToolTable_CabinetTab> tabs)
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            if (t == null) continue;
            long owner = 0;
            string name = null;
            try { owner = t.OwnerId?.Value ?? 0; name = t.Name?.Value; } catch { }
            if (owner == 0) continue;
            bool rack = false; int cfg = 0;
            try { rack = t.IsRack?.Value ?? false; cfg = t.FurnitureConfigId; } catch { }
            yield return new RegEntry { OwnerId = owner, ConfigId = cfg, Name = name ?? ("#" + owner), IsRack = rack };
        }
    }

    // ---- 对账：期望集（视图谓词）vs 列表现状，收敛后一次重推 ----
    private static void Reconcile(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus, State_Web_ToolTable st,
        ObservableCollections.ObservableList<Data_Web_ToolTable_CabinetTab> tabs)
    {
        string view = CurrentView();
        var desired = new HashSet<long>();
        var verdicts = new List<string>(); // Verbose 明细
        var tagCtx = TagContext.Build();
        foreach (var kv in _registry)
        {
            bool ok = Allows(view, kv.Value, tagCtx, out string why);
            if (ok) desired.Add(kv.Key);
            verdicts.Add($"{kv.Value.Name}({kv.Key})={(ok ? "留" : "剪")}:{why}");
        }

        // 签名比对：无变化不动作不重推（避免 0.5s 轮询变成常量推送源）
        var sig = view + "|" + string.Join(",", desired);
        if (sig == _lastSig && ListMatches(tabs, desired)) return;

        // 活动页签（activeLeftTab>=2 时指向列表位次）本轮不剪，防原生活动态错位
        int active = 0;
        try { active = st.ActiveLeftTab?.Value ?? 0; } catch { }
        long activeOwner = 0;
        if (active >= 2 && active - 2 < tabs.Count)
        {
            var at = tabs[active - 2];
            try { activeOwner = at?.OwnerId?.Value ?? 0; } catch { }
        }

        int removed = 0;
        for (int i = tabs.Count - 1; i >= 0; i--)
        {
            long owner = 0;
            try { owner = tabs[i]?.OwnerId?.Value ?? 0; } catch { }
            if (owner == 0 || desired.Contains(owner) || owner == activeOwner) continue;
            tabs.RemoveAt(i);
            removed++;
        }

        // 重排 Index=列表位次（JS 与原生均按位次切换）
        for (int i = 0; i < tabs.Count; i++)
        {
            try { tabs[i].Index = i; } catch { }
        }

        // 平移活动页签到同 owner 新位次
        if (activeOwner != 0)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                long owner = 0;
                try { owner = tabs[i]?.OwnerId?.Value ?? 0; } catch { }
                if (owner == activeOwner)
                {
                    if (active != i + 2)
                    {
                        try { st.ActiveLeftTab.Value = i + 2; } catch { }
                    }
                    break;
                }
            }
        }

        // 加回新可见（登记有底：名称/配置/IsRack 原样重建）
        var have = new HashSet<long>();
        for (int i = 0; i < tabs.Count; i++)
        {
            try { var o = tabs[i]?.OwnerId?.Value ?? 0; if (o != 0) have.Add(o); } catch { }
        }
        int added = 0;
        foreach (var kv in _registry)
        {
            if (!desired.Contains(kv.Key) || have.Contains(kv.Key)) continue;
            try
            {
                var t = new Data_Web_ToolTable_CabinetTab();
                t.Index = tabs.Count;
                t.OwnerId = new R3.ReactiveProperty<long>(kv.Key);
                t.Name = new R3.ReactiveProperty<string>(kv.Value.Name);
                t.IsNew = new R3.ReactiveProperty<bool>(false);
                t.ShortCount = new R3.ReactiveProperty<int>(0);
                t.IsRack = new R3.ReactiveProperty<bool>(kv.Value.IsRack);
                t.FurnitureConfigId = kv.Value.ConfigId;
                tabs.Add(t);
                added++;
            }
            catch (Exception e)
            {
                WarnOnce("add_" + kv.Key, $"页签加回失败 ownerId={kv.Key}: {e.Message}");
            }
        }

        _lastSig = sig;

        if (removed > 0 || added > 0)
        {
            Log($"视图={view} 修剪 {removed} 条 / 加回 {added} 条，扫描集=所见={tabs.Count} 条" +
                $"（登记 {_registry.Count}，背包/抽屉不在内）");
            if (CabinetEverywherePlugin.Verbose.Value)
                foreach (var v in verdicts) LogDetail(v);

            // 补发原生全量刷新（Ac_ToolTable_RefreshBag.SendAction()→RA_RefreshBag：
            // RefreshBagItems+RefreshRecipeList+UpdateMaxCraftCount，纯刷新无取料副作用；
            // 注意带 bool 参的 SendAction 属 Ac_ToolTable_RefreshBagUI→RA_RefreshBagUI 自动回填，勿混淆）：
            // 开台时"可做数量"是按修剪前全量算的，不补算则界面残留旧数字、
            // 实际制作按修剪后列表取料——数量对不上（2026-10-19/20 作者实测发现）
            try
            {
                GameCore.HotUpdate.ReduxUI.Ac_ToolTable_RefreshBag.SendAction();
            }
            catch (Exception e)
            {
                WarnOnce("refresh", "修剪后刷新派发失败（数量可能滞后到下次袋变更）: " + e.Message);
            }
            TabInjector.Repush(rus, TtType);
        }
    }

    private static bool ListMatches(ObservableCollections.ObservableList<Data_Web_ToolTable_CabinetTab> tabs,
        HashSet<long> desired)
    {
        var cur = new HashSet<long>();
        for (int i = 0; i < tabs.Count; i++)
        {
            try { var o = tabs[i]?.OwnerId?.Value ?? 0; if (o != 0) cur.Add(o); } catch { }
        }
        if (cur.Count != desired.Count) return false;
        foreach (var o in cur) if (!desired.Contains(o)) return false;
        return true;
    }

    private static string CurrentView()
    {
        return CabinetEverywherePlugin.ViewToolTable.Value switch
        {
            ScenarioView.Food => "Food",
            ScenarioView.Material => "Material",
            _ => "Browser",
        };
    }

    /// <summary>与 tagfilter.page.js viewAllows 同语义：Browser 全留；Material=材料/工具标签+材料架；
    /// Food=食物域(dim1-4)。ownerId 不在容器表（背包/抽屉/未知）→ 留（JS 对 undefined 同样放行）。</summary>
    private static bool Allows(string view, RegEntry e, TagContext ctx, out string why)
    {
        why = "browser全留";
        if (view == "Browser") return true;

        var inst = FindContainer(e.OwnerId);
        if (inst == null) { why = "非容器表成员(背包/抽屉等)"; return true; }

        if (view == "Material")
        {
            if (inst.IsMaterialRack) { why = "材料架伪标签"; return true; }
            if (ctx.MatTagId != 0 && Array.IndexOf(inst.TagIds, ctx.MatTagId) >= 0) { why = "材料标签"; return true; }
            if (ctx.ToolTagId != 0 && Array.IndexOf(inst.TagIds, ctx.ToolTagId) >= 0) { why = "工具标签"; return true; }
            why = "无材料/工具标签";
            return false;
        }

        // Food：任一标签落在 dim1-4（matchesFood 同款）
        foreach (var tid in inst.TagIds)
            if (ctx.IsFoodTag(tid)) { why = "食物域标签"; return true; }
        if (inst.IsMaterialRack) { why = "材料架(食物视图不放行)"; return false; }
        why = "无食物域标签";
        return false;
    }

    private static CabinetCatalog.ContainerInst FindContainer(long ownerId)
    {
        var list = CabinetCatalog.AllContainers;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c != null && c.OwnerId == ownerId) return c;
        }
        return null;
    }

    private static void Log(string msg) => CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere.工作台] " + msg);
    private static void LogDetail(string msg) => CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere.工作台.明细] " + msg);

    /// <summary>标签上下文（材料/工具标签 id + 食物域判定），每轮对账时重建（标签库极小，开销可忽略）。</summary>
    private sealed class TagContext
    {
        public int MatTagId;
        public int ToolTagId;
        private HashSet<int> _foodTags;

        public static TagContext Build()
        {
            var ctx = new TagContext();
            try
            {
                var cm = SurvivalLog.ModShared.BagTable.Cm();
                var dict = cm?._Config_FurnitureTag_Dict;
                if (dict != null)
                {
                    var food = new HashSet<int>();
                    foreach (var pair in dict)
                    {
                        var t = pair.Value;
                        if (t == null) continue;
                        try
                        {
                            if (t.IconKey == "material") ctx.MatTagId = t.ID;
                            if (t.IconKey == "tool") ctx.ToolTagId = t.ID;
                            if (t.Dim >= 1 && t.Dim <= 4) food.Add(t.ID);
                        }
                        catch { }
                    }
                    ctx._foodTags = food;
                }
            }
            catch { }
            return ctx;
        }

        public bool IsFoodTag(int tagId) => _foodTags != null && _foodTags.Contains(tagId);
    }
}
