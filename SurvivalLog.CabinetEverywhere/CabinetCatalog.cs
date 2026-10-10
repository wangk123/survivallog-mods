using System;
using System.Collections.Generic;
using SurvivalLog.ModShared;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>柜子家具的发现（配置层）与家中实例枚举（运行时）。</summary>
internal static class CabinetCatalog
{
    /// <summary>发现的柜子家具 ConfigId（配置层，重扫幂等重建）。</summary>
    internal static HashSet<int> ConfigIds = new();

    /// <summary>家中的柜子实例（ownerId = Furniture.InstanceId，页签/取包都用它）。</summary>
    internal sealed class CabinetInst
    {
        public long OwnerId;
        public int ConfigId;
        public string Name;
    }

    private static List<CabinetInst> _homeInstances = new();
    private static readonly HashSet<string> WarnedOnce = new();

    private static void WarnOnce(string key, string msg)
    {
        if (WarnedOnce.Add(key))
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] " + msg);
    }

    /// <summary>
    /// 扫描 Config_Furniture：有储物包(BagId&gt;0) 且 家具类型=储藏(FurnitureType==2)
    /// 且 非冷链电器（否则本来就有"冰箱"页签），再套用户 Include/Exclude。
    /// 返回 null 表示配置表未就绪。
    /// </summary>
    internal static HashSet<int> RebuildConfigIds()
    {
        var cm = BagTable.Cm();
        var dict = BagTable.FurnDict(cm);
        if (dict == null || dict.Count == 0) return null;

        var result = new HashSet<int>();
        var excludeCfg = BagTable.ParseIds(CabinetEverywherePlugin.ExcludeFurnitureIds.Value);
        var includeCfg = BagTable.ParseIds(CabinetEverywherePlugin.IncludeFurnitureIds.Value);
        var excludeBag = BagTable.ParseIds(CabinetEverywherePlugin.ExcludeBagIds.Value);

        foreach (var pair in dict)
        {
            var f = pair.Value;
            if (f == null) continue;

            int bagId = 0, ftype = 0;
            bool isElec = false; int elecType = 0; float cold = 0f;
            try
            {
                bagId = f.BagId; ftype = f.FurnitureType;
                isElec = f.IsElectrical; elecType = f.ElectricalType; cold = f.ColdRate;
            }
            catch { continue; }

            bool coldChain = isElec && elecType == 1 && cold > 0f; // 与原生 CollectFridges 同判定
            bool want;
            if (includeCfg.Contains(pair.Key)) want = true;
            else if (excludeCfg.Contains(pair.Key)) want = false;
            else want = bagId > 0 && ftype == 2 && !coldChain && !excludeBag.Contains(bagId);

            if (want) result.Add(pair.Key);
        }

        ConfigIds = result;
        return result;
    }

    /// <summary>日志用：全部候选家具的 id/名称/BagId（含被默认排除的置物架提示）。</summary>
    internal static List<string> DescribeAll()
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = BagTable.FurnDict(cm);
        if (dict == null) { lines.Add("配置表未就绪"); return lines; }

        var excludeBag = BagTable.ParseIds(CabinetEverywherePlugin.ExcludeBagIds.Value);
        foreach (var pair in dict)
        {
            var f = pair.Value;
            if (f == null) continue;
            int bagId = 0, ftype = 0;
            try { bagId = f.BagId; ftype = f.FurnitureType; } catch { continue; }
            if (bagId <= 0) continue;

            string tag;
            if (ConfigIds.Contains(pair.Key)) tag = "入选";
            else if (excludeBag.Contains(bagId)) tag = "排除(BagId名单)";
            else tag = "排除(非储藏类/冷链)";
            lines.Add($"家具 {pair.Key}（{BagTable.FurnName(f)}，BagId={bagId}，Type={ftype}）→ {tag}");
        }
        return lines;
    }

    /// <summary>枚举家中"带包家具"并过滤出柜子实例。与原生枚举同源：AgentManager.GetFurnituresWithBag(homeMapId,…)。</summary>
    internal static void RefreshHomeInstances()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return;
            var world = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance;
            var am = world?._AgentManager;
            if (am == null) return;

            int mapId = am.GetHomeMapId();
            // 与原生 CollectFridges 调用形态一致：(mapId, true, false, false)
            var list = am.GetFurnituresWithBag(mapId, true, false, false);
            if (list == null) return;

            var cm = BagTable.Cm();
            var dict = BagTable.FurnDict(cm);
            var fresh = new List<CabinetInst>();
            foreach (var furn in list)
            {
                if (furn == null) continue;
                long ownerId = 0; int cfgId = 0; bool bagLocked = false;
                try
                {
                    ownerId = furn.InstanceId;
                    cfgId = furn.AgentConfigId;
                    bagLocked = furn.IsBagLocked;
                }
                catch { continue; }

                if (!ConfigIds.Contains(cfgId)) continue;
                if (bagLocked)
                {
                    if (CabinetEverywherePlugin.Verbose.Value)
                        CabinetEverywherePlugin.Log.LogInfo($"[CabinetEverywhere] 跳过上锁容器 {cfgId}({ownerId})");
                    continue;
                }

                string name = "?";
                GameCore.HotUpdate.Config_Furniture cfg = null;
                try { dict?.TryGetValue(cfgId, out cfg); } catch { }
                if (cfg != null) name = BagTable.FurnName(cfg);

                fresh.Add(new CabinetInst { OwnerId = ownerId, ConfigId = cfgId, Name = name });
            }
            _homeInstances = fresh;
            if (!_instanceLogged && fresh.Count >= 0)
            {
                _instanceLogged = true;
                var names = new List<string>();
                foreach (var c in fresh) names.Add($"{c.Name}({c.OwnerId})");
                CabinetEverywherePlugin.Log.LogInfo(
                    $"[CabinetEverywhere] 家中柜子实例 {fresh.Count} 个：{string.Join("、", names)}（进入烹饪/老鼠笼/酿酒界面时自动注入页签）");
            }
        }
        catch (Exception e)
        {
            WarnOnce("refresh", "家中柜子枚举失败: " + e.Message);
        }
    }

    private static bool _instanceLogged;

    internal static IReadOnlyList<CabinetInst> HomeInstances => _homeInstances;
}
