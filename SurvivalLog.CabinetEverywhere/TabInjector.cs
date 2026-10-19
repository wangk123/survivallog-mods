using System;
using System.Collections.Generic;
using GameCore.HotUpdate.ReduxUI;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 页签注入：向 烹饪/老鼠笼/酿酒桶/颜料桶 的 Redux 状态 BagTabs（ObservableList）追加柜子页签。
/// 四个界面的容器过滤是内联"冷链电器"判定（不走白名单），因此走状态层；
/// 切换路径 ApplySwitchBag→GetBagData(ownerId) 已实证无类型过滤，Locked=false 即可切换。
/// 轮询幂等：界面每次重推 tabs（打开/刷新）后若柜子页签缺失则自动补挂。
/// </summary>
internal static class TabInjector
{
    private sealed class UiSession
    {
        public readonly HashSet<long> Injected = new(); // 本轮会话已注入的 ownerId（防重复/防刷屏）
        public bool WasActive;
    }

    private static readonly Dictionary<string, UiSession> Sessions = new();

    // interop 侧的 UI 类型（IsUIActive/GetStateByUI 参数为 Il2CppSystem.Type）
    private static readonly Il2CppSystem.Type CookingType = Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_Cooking));
    private static readonly Il2CppSystem.Type RatCageType = Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_RatCage));
    private static readonly Il2CppSystem.Type BrewType = Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_Brew));
    private static readonly Il2CppSystem.Type DyeType = Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_Dye));

    private static UiSession Session(string key) => Sessions.TryGetValue(key, out var s) ? s : Sessions[key] = new UiSession();

    internal static void PollAll()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            if (rus == null) return;

            if (CabinetEverywherePlugin.EnableCooking.Value)
                PollCooking(rus);
            if (CabinetEverywherePlugin.EnableRatCage.Value)
                PollRatCage(rus);
            if (CabinetEverywherePlugin.EnableBrew.Value)
                PollBrew(rus);
            if (CabinetEverywherePlugin.EnableDye.Value)
                PollDye(rus);
        }
        catch (Exception e)
        {
            WarnOnce("poll", "页签轮询异常: " + e.Message);
        }
    }

    // ---- 烹饪 ----
    private static void PollCooking(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus)
    {
        var s = Session("cooking");
        bool active = SafeActive(rus, CookingType);
        if (!active) { s.WasActive = false; return; }

        if (!s.WasActive) { s.WasActive = true; s.Injected.Clear(); }
        var st = rus.GetStateByUI(CookingType)?.TryCast<State_Web_Cooking>();
        var tabs = st?.BagTabs;
        if (tabs == null || tabs.Count == 0) return;

        var have = CollectOwners(tabs, t => t?.OwnerId?.Value ?? 0);
        var added = AddMissing("烹饪", s, have, tabs.Count,
            (ownerId, name, index) =>
            {
                var tab = new Data_Web_Cooking_BagTab();
                tab.Index = index;
                tab.OwnerId = new R3.ReactiveProperty<long>(ownerId);
                tab.Name = new R3.ReactiveProperty<string>(name);
                tab.Locked = new R3.ReactiveProperty<bool>(false);
                tab.LockText = new R3.ReactiveProperty<string>("");
                tab.IsNew = new R3.ReactiveProperty<bool>(false);
                tabs.Add(tab);
            });
        if (added > 0) Repush(rus, CookingType);
    }

    // ---- 老鼠笼 ----
    private static void PollRatCage(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus)
    {
        var s = Session("ratcage");
        bool active = SafeActive(rus, RatCageType);
        if (!active) { s.WasActive = false; return; }

        if (!s.WasActive) { s.WasActive = true; s.Injected.Clear(); }
        var st = rus.GetStateByUI(RatCageType)?.TryCast<State_Web_RatCage>();
        var tabs = st?.BagTabs;
        if (tabs == null || tabs.Count == 0) return;

        var have = CollectOwners(tabs, t => t?.OwnerId?.Value ?? 0);
        var added = AddMissing("老鼠笼", s, have, tabs.Count,
            (ownerId, name, index) =>
            {
                var tab = new Data_Web_RatCage_BagTab();
                tab.Index = index;
                tab.OwnerId = new R3.ReactiveProperty<long>(ownerId);
                tab.Name = new R3.ReactiveProperty<string>(name);
                tab.Locked = new R3.ReactiveProperty<bool>(false);
                tabs.Add(tab);
            });
        if (added > 0) Repush(rus, RatCageType);
    }

    // ---- 酿酒桶 ----
    private static void PollBrew(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus)
    {
        var s = Session("brew");
        bool active = SafeActive(rus, BrewType);
        if (!active) { s.WasActive = false; return; }

        if (!s.WasActive) { s.WasActive = true; s.Injected.Clear(); }
        var st = rus.GetStateByUI(BrewType)?.TryCast<State_Web_Brew>();
        var tabs = st?.BagTabs;
        if (tabs == null || tabs.Count == 0) return;

        var have = CollectOwners(tabs, t => t?.OwnerId?.Value ?? 0);
        var added = AddMissing("酿酒", s, have, tabs.Count,
            (ownerId, name, index) =>
            {
                var tab = new Data_Web_Brew_BagTab();
                tab.Index = index;
                tab.OwnerId = new R3.ReactiveProperty<long>(ownerId);
                tab.Name = new R3.ReactiveProperty<string>(name);
                tab.Locked = new R3.ReactiveProperty<bool>(false);
                tabs.Add(tab);
            });
        if (added > 0) Repush(rus, BrewType);
    }

    // ---- 颜料桶 ----
    private static void PollDye(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus)
    {
        var s = Session("dye");
        bool active = SafeActive(rus, DyeType);
        if (!active) { s.WasActive = false; return; }

        if (!s.WasActive) { s.WasActive = true; s.Injected.Clear(); }
        var st = rus.GetStateByUI(DyeType)?.TryCast<State_Web_Dye>();
        var tabs = st?.BagTabs;
        if (tabs == null || tabs.Count == 0) return;

        var have = CollectOwners(tabs, t => t?.OwnerId?.Value ?? 0);
        var added = AddMissing("颜料桶", s, have, tabs.Count,
            (ownerId, name, index) =>
            {
                var tab = new Data_Web_Dye_BagTab();
                tab.Index = index;
                tab.OwnerId = new R3.ReactiveProperty<long>(ownerId);
                tab.Name = new R3.ReactiveProperty<string>(name);
                tab.Locked = new R3.ReactiveProperty<bool>(false);
                tabs.Add(tab);
            });
        if (added > 0) Repush(rus, DyeType);
    }

    // ---- 公共 ----
    private static bool SafeActive(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus, Il2CppSystem.Type t)
    {
        try { return rus.IsUIActive(t); }
        catch { return false; }
    }

    private static HashSet<long> CollectOwners<T>(ObservableCollections.ObservableList<T> tabs, Func<T, long> getOwner)
    {
        var set = new HashSet<long>();
        // interop 集合的 GetEnumerator 不可直接 foreach，走索引器
        for (int i = 0; i < tabs.Count; i++)
        {
            var id = getOwner(tabs[i]);
            if (id != 0) set.Add(id);
        }
        return set;
    }

    /// <summary>
    /// 注入后触发界面重推：WebUIVm 绑定是脏标记制（MarkAllMsgDirty → OnMsgChange 序列化推送 JS），
    /// ObservableList.Add 后必须走一遍 VM 的重推，JS 才会收到 bagTabs（TriggerStateSubscribe 实证无效）。
    /// ToolTablePruner 修剪 CabinetTabs 后同用此通道。
    /// </summary>
    internal static void Repush(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus, Il2CppSystem.Type uiType)
    {
        try
        {
            var layer = rus.GetWebUILayer();
            GameCore.HotUpdate.ReduxUI.WebUIVm vm = null;
            if (layer != null && layer.GetWebUI(uiType, out vm) && vm != null)
            {
                vm.MarkAllMsgDirty();
                vm.OnMsgChange();
            }
        }
        catch (Exception e) { WarnOnce("repush", "UI 重推失败: " + e.Message); }
    }

    private static int AddMissing(string uiName, UiSession s, HashSet<long> have, int currentCount,
        Action<long, string, int> create)
    {
        var cabinets = CabinetCatalog.HomeInstances;
        int added = 0, index = currentCount;
        var names = new List<string>();
        foreach (var c in cabinets)
        {
            if (have.Contains(c.OwnerId) || s.Injected.Contains(c.OwnerId)) continue;
            try
            {
                create(c.OwnerId, c.Name, index++);
                s.Injected.Add(c.OwnerId);
                added++;
                names.Add($"{c.Name}({c.OwnerId})");
            }
            catch (Exception e)
            {
                WarnOnce("add_" + uiName + "_" + c.OwnerId, $"{uiName} 页签注入失败 ownerId={c.OwnerId}: {e.Message}");
            }
        }
        if (added > 0)
            CabinetEverywherePlugin.Log.LogInfo($"[CabinetEverywhere] {uiName} 界面注入 {added} 个柜子页签：{string.Join("、", names)}");
        return added;
    }

    private static readonly HashSet<string> WarnedOnce = new();
    private static void WarnOnce(string key, string msg)
    {
        if (WarnedOnce.Add(key))
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] " + msg);
    }
}
