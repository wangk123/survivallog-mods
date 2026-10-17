using System;
using System.Collections.Generic;
using SurvivalLog.ModShared;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>柜子家具的发现（配置层）与家中容器统一枚举（运行时：单遍扫描产出 全容器名单 + 柜子子集，直达/筛选共用）。</summary>
internal static class CabinetCatalog
{
    /// <summary>发现的柜子家具 ConfigId（配置层，重扫幂等重建）。</summary>
    internal static HashSet<int> ConfigIds = new();

    /// <summary>
    /// 家中容器实例（ownerId = Furniture.InstanceId，页签/取包/筛选都用它）。
    /// Kind：'c'=柜子（直达注入子集）/ 'f'=冰箱（冷链电器，走原生冰箱页签）/ 'r'=材料架（专属界面，不能打标签）。
    /// </summary>
    internal sealed class ContainerInst
    {
        public long OwnerId;
        public int ConfigId;
        public string Name;
        public char Kind;
        public int[] TagIds = Array.Empty<int>();
        /// <summary>材料架（MaterialRackComponent）：不能打标签但只能放材料，筛选时补"材料"伪标签。</summary>
        public bool IsMaterialRack;
    }

    /// <summary>
    /// 非储物功能包（与 cfg ExcludeBagIds 默认名单同源同维护；游戏更新加新功能包时两处同步）。
    /// cfg 名单用户可调、本常量随游戏版本兜底——运行时门槛两个都查，任一命中即跳过。
    /// 这些会出现在功能界面的页签数组里（如无人机交易的中控台页签）——误收进筛选名单又无
    /// 匹配标签会被当柜子藏掉，官方 A/D 步进切过去即被弹回＝卡死闪一下（2026-10-15 实测）。
    /// 排除=永不参与筛选=页签常显。
    /// </summary>
    private static readonly HashSet<int> NonStorageBagIds = new()
    {
        5001, 5002, 5003, 5004, 5005, 5006, 5011, // 厨房电器：燃料炉/电烤箱/微波炉/燃气灶/榨汁机/咖啡机/电火锅
        5007, 5008, 5009, 5014, 5015, 5017, 5020, 115004, // 机器功能槽/老鼠笼/粉碎机/口粮压制机/暖房工程台/酿酒桶
        3001, 1050, 1008, 1060, 1014, 10001, 10004, // 围栏/吊篮/无人机/门/窗/床/垃圾桶
    };

    private static List<ContainerInst> _homeInstances = new();  // 柜子子集（直达注入用）
    private static List<ContainerInst> _allContainers = new();  // 全容器（筛选用）
    private static readonly HashSet<string> WarnedOnce = new();

    private static void WarnOnce(string key, string msg)
    {
        if (WarnedOnce.Add(key))
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] " + msg);
    }

    /// <summary>
    /// 扫描 Config_Furniture：默认收录**全部带储物包**的家具（不按 FurnitureType 分类——实测
    /// Type=0/3 里也有大量真容器：置物架/衣柜/抽屉柜/床头柜/纸箱/金属柜等），仅排除
    /// 非储物包（用户 cfg 的 ExcludeBagIds，默认=炉具/无人机/门窗/床/垃圾桶）与冷链电器
    /// （冰箱本来就有冰箱页签）。再套用户 Include/Exclude（家具粒度优先）。
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

            int bagId = 0;
            bool isElec = false; int elecType = 0; float cold = 0f;
            try
            {
                bagId = f.BagId;
                isElec = f.IsElectrical; elecType = f.ElectricalType; cold = f.ColdRate;
            }
            catch { continue; }

            bool coldChain = isElec && elecType == 1 && cold > 0f; // 与原生 CollectFridges 同判定
            bool want;
            if (includeCfg.Contains(pair.Key)) want = true;
            else if (excludeCfg.Contains(pair.Key)) want = false;
            else want = bagId > 0 && !coldChain && !excludeBag.Contains(bagId);

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

    /// <summary>
    /// 统一容器扫描（5s 一遍，原直达/筛选两个 5s 扫描合一）：**单遍直扫 AgentManager.agentList**
    /// （与原生 GetFurnituresWithBag 同一数据源，但不走其"主角当前楼层"过滤；也不再用
    /// GetFurnituresByConfigId 逐配置反查——833 配置×每次调用性能不可接受）。同一次遍历产出两份名单：
    /// AllContainers——家中全部储物容器（柜子/冰箱/材料架+标签，TagFilter 筛选用）；
    /// HomeInstances——其中的柜子子集（kind=='c' 且 ConfigIds 名单命中，TabInjector 直达注入用，
    /// 语义与合并前一致；冰箱走原生冰箱页签、材料架有专属界面，均不注入）。全楼层收录。
    /// 顺序即性能：TryCast → 配置查包/名单（廉价，滤掉 ~90% 家具）→ 主图严格相等短路 →
    /// 仅非主图走 IsHomeMap（重）→ 通过者才读类别/标签（组件字典/电器属性最贵，放最后）。
    /// </summary>
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
            var agentList = am.agentList;
            if (agentList == null || agentList.Count == 0) return;

            var cm = BagTable.Cm();
            var dict = BagTable.FurnDict(cm);
            var excludeBagCfg = BagTable.ParseIds(CabinetEverywherePlugin.ExcludeBagIds.Value);
            var all = new List<ContainerInst>();
            var fresh = new List<ContainerInst>();
            var extraNames = new List<string>(); // 不在原生枚举结果里的（楼层过滤导致），日志用
            int withBagCount = 0;

            Il2CppSystem.Collections.Generic.List<GameCore.HotUpdate.Battle.Logic.Furniture> withBag = null;
            try { withBag = am.GetFurnituresWithBag(mapId, true, false, false); } catch { }
            var nativeIds = new HashSet<long>();
            if (withBag != null)
                foreach (var f in withBag) { long v = 0; try { v = f.InstanceId; } catch { } if (v != 0) nativeIds.Add(v); }

            for (int i = 0; i < agentList.Count; i++)
            {
                GameCore.HotUpdate.Battle.Logic.Furniture furn = null;
                try { furn = agentList[i]?.TryCast<GameCore.HotUpdate.Battle.Logic.Furniture>(); } catch { }
                if (furn == null) continue;

                long ownerId = 0; int cfgId = 0;
                try { ownerId = furn.InstanceId; cfgId = furn.AgentConfigId; } catch { continue; }
                if (ownerId == 0) continue;

                // 廉价门槛（顺序即性能）：配置查包 bagId>0，且不在 cfg ExcludeBagIds（用户可调）与
                // NonStorageBagIds（随游戏版本维护）任一名单——先滤掉绝大多数家具，再做地图判定
                // （IsHomeMap 较重，且用严格相等短路——绝大多数家具就在主图上），最后才读其余字段。
                GameCore.HotUpdate.Config_Furniture cfg = null;
                try { dict?.TryGetValue(cfgId, out cfg); } catch { }
                int bagId = 0;
                try { if (cfg != null) bagId = cfg.BagId; } catch { }
                if (bagId <= 0 || excludeBagCfg.Contains(bagId) || NonStorageBagIds.Contains(bagId))
                {
                    if (DumpRequested)
                    {
                        int fmD = 0; bool lockedD = false;
                        try { fmD = furn.MapConfigId; lockedD = furn.IsBagLocked; } catch { }
                        _dumpLines.Add($"  家具 {cfgId}（{FurnNameSafe(dict, cfgId)}）owner=0x{ownerId:X} map={fmD} locked={lockedD} → 排除(无包/非储物名单)");
                    }
                    continue;
                }

                int furnMap = 0;
                try { furnMap = furn.MapConfigId; } catch { }
                bool isHomeMap = furnMap == mapId; // 主图直接过（绝大多数）
                if (!isHomeMap)
                {
                    try { isHomeMap = am.IsHomeMap(furnMap); } catch { } // 家地图组判定（地下室等）
                    if (!isHomeMap) continue;
                }

                // 类别+标签（仅对通过门槛的家中容器读——组件字典索引/电器属性最贵，放最后）
                bool isElec = false; int elecType = 0; float cold = 0f;
                try { if (cfg != null) { isElec = cfg.IsElectrical; elecType = cfg.ElectricalType; cold = cfg.ColdRate; } } catch { }
                bool coldChain = isElec && elecType == 1 && cold > 0f; // 与原生 CollectFridges 同判定
                bool rack = HasComponent(furn, typeof(GameCore.HotUpdate.Battle.Logic.MaterialRackComponent));
                char kind = rack ? 'r' : (coldChain ? 'f' : 'c');

                var inst = new ContainerInst
                {
                    OwnerId = ownerId,
                    ConfigId = cfgId,
                    Name = FurnNameSafe(dict, cfgId),
                    Kind = kind,
                    IsMaterialRack = rack,
                };
                inst.TagIds = ReadTagIds(furn);
                all.Add(inst);

                // 直达柜子子集判定（保持旧语义）：kind=='c' 且 ConfigIds 名单命中才进 _homeInstances
                if (kind == 'c' && ConfigIds.Contains(cfgId))
                {
                    fresh.Add(inst);
                    if (nativeIds.Contains(ownerId)) withBagCount++;
                    else extraNames.Add($"{inst.Name}(0x{ownerId:X})");
                }
            }

            // 诊断：全量列出 agentList 中的家具（不过滤），标注各判定结果——定位"被过滤掉的新柜子"
            if (DumpRequested)
            {
                try
                {
                    int n = 0;
                    for (int i = 0; i < agentList.Count && n < 400; i++)
                    {
                        GameCore.HotUpdate.Battle.Logic.Furniture f = null;
                        try { f = agentList[i]?.TryCast<GameCore.HotUpdate.Battle.Logic.Furniture>(); } catch { }
                        if (f == null) continue;
                        long v = 0; int c = 0; int fm = 0; bool locked = false; int floor = -1; bool isHome = false;
                        try
                        {
                            v = f.InstanceId; c = f.AgentConfigId; fm = f.MapConfigId; locked = f.IsBagLocked;
                            floor = am.GetAgentFloor(v);
                            isHome = am.IsHomeMap(fm);
                        }
                        catch { }
                        n++;
                        _dumpLines.Add($"  家具 {c}（{FurnNameSafe(dict, c)}）owner=0x{v:X} map={fm}(IsHomeMap={isHome}) floor={floor} locked={locked} 入选={ConfigIds.Contains(c)} 严格同图={fm == mapId}");
                    }
                    _dumpLines.Add($"  [全量] agentList 中家具共 {n} 条（上限400截断），GetHomeMapId={mapId}，柜子名单收录 {fresh.Count}");
                }
                catch (Exception e) { _dumpLines.Add("  [全量] 失败: " + e.Message); }
            }

            // 变化时才打，防 5 秒刷屏
            var freshIds = new List<long>();
            foreach (var c in fresh) freshIds.Add(c.OwnerId);
            var extraKey = string.Join(",", freshIds);
            if (extraKey != _lastExtraKey)
            {
                _lastExtraKey = extraKey;
                if (extraNames.Count > 0)
                    CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere] 全楼层补充 " + extraNames.Count + " 个（不在原生'当前楼层'枚举内）：" + string.Join("、", extraNames));
            }

            int prevN = _homeInstances.Count;
            _homeInstances = fresh;
            _allContainers = all;
            if (prevN != fresh.Count)
                CabinetEverywherePlugin.Log.LogInfo($"[CabinetEverywhere] 家中柜子实例 {prevN} → {fresh.Count} 个");

            // 镜像补全（根因修复）：新摆放家具的包要重读档才会进 Redux BagCache（GetBagData 对其直接索引，
            // 缺失即抛异常 → 页签点切换静默失败）。此处为缺失的柜子补 BagCache[owner]=bagConfigId + 空 OwnerCache，
            // 与读档后状态一致。
            try
            {
                var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
                var store = rus?.reduxStoreLayer;
                var dataTree = store?.dataTree;
                GameCore.HotUpdate.ReduxUI.BaseReduxState raw = null;
                dataTree?.TryGetValue(Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Item)), out raw);
                var itemState = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Item>();
                var bagCache = itemState?.BagCache;
                var ownerCache = itemState?.OwnerCache;
                if (bagCache != null)
                {
                    var repaired = new List<string>();
                    foreach (var c in fresh)
                    {
                        bool inCache = false;
                        try { inCache = bagCache.ContainsKey(c.OwnerId); } catch { }
                        if (inCache) continue;

                        int bagCfgId = 0;
                        GameCore.HotUpdate.Config_Furniture fcfg = null;
                        try { dict?.TryGetValue(c.ConfigId, out fcfg); } catch { }
                        if (fcfg != null) try { bagCfgId = fcfg.BagId; } catch { }
                        if (bagCfgId <= 0) continue;

                        bagCache[c.OwnerId] = bagCfgId;
                        if (ownerCache != null)
                        {
                            bool hasOwner = false;
                            try { hasOwner = ownerCache.ContainsKey(c.OwnerId); } catch { }
                            if (!hasOwner)
                                ownerCache[c.OwnerId] = new Il2CppSystem.Collections.Generic.Dictionary<long, GameCore.HotUpdate.ReduxUI.Data_Item>();
                        }
                        repaired.Add($"{c.Name}({c.OwnerId}→bag {bagCfgId})");
                    }
                    if (repaired.Count > 0)
                        CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere] 补全 " + repaired.Count + " 个新柜子的包镜像（BagCache/OwnerCache）：" + string.Join("、", repaired));
                }
            }
            catch (Exception e) { WarnOnce("mirror", "包镜像补全失败: " + e.Message); }

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
            WarnOnce("refresh", "家中容器枚举失败: " + e.Message);
        }
    }

    internal static volatile bool DumpRequested;
    private static readonly List<string> _dumpLines = new();
    private static string _lastExtraKey = "";

    private static string FurnNameSafe(Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Furniture> dict, int cfgId)
    {
        try
        {
            GameCore.HotUpdate.Config_Furniture cfg = null;
            dict?.TryGetValue(cfgId, out cfg);
            return cfg != null ? BagTable.FurnName(cfg) : "?";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 读容器的官方标签（FurnitureTagComponent.TagIds，原 ContainerScan.FillTags 的标签读取）。
    /// agentComponentDictionary 是 Dictionary&lt;Il2CppType, BaseEntity&gt;，按 Type 索引直达组件
    /// （无泛型/枚举坑）；无组件/异常一律空数组兜底。
    /// </summary>
    private static int[] ReadTagIds(GameCore.HotUpdate.Battle.Logic.Furniture furn)
    {
        try
        {
            var dict = furn.agentComponentDictionary;
            if (dict == null) return Array.Empty<int>();
            var raw = dict[Il2CppInterop.Runtime.Il2CppType.From(
                typeof(GameCore.HotUpdate.Battle.Logic.FurnitureTagComponent))];
            var comp = raw?.TryCast<GameCore.HotUpdate.Battle.Logic.FurnitureTagComponent>();
            var ids = comp?.TagIds;
            if (ids != null && ids.Count > 0)
            {
                var arr = new int[ids.Count];
                for (int i = 0; i < ids.Count; i++) arr[i] = ids[i];
                return arr;
            }
        }
        catch { /* 标签读不到=无标签 */ }
        return Array.Empty<int>();
    }

    /// <summary>组件存在性（agentComponentDictionary 按 Type 索引直达，无组件/异常一律 false）。</summary>
    private static bool HasComponent(GameCore.HotUpdate.Battle.Logic.Furniture furn, Type type)
    {
        try
        {
            var dict = furn.agentComponentDictionary;
            return dict != null && dict[Il2CppInterop.Runtime.Il2CppType.From(type)] != null;
        }
        catch { return false; }
    }

    /// <summary>诊断：列出本次枚举的全部带包家具明细（DumpFurniture 置 true 触发一次）。</summary>
    internal static void FlushDump()
    {
        if (!DumpRequested) return;
        if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
            return; // 等进档后再消费，避免主菜单空转清标志
        if (_homeInstances.Count == 0 && _dumpLines.Count == 0)
            return; // 加载屏阶段世界已建但家具未生成（曾在此被空消费掉标志），等首份有效数据
        DumpRequested = false;
        CabinetEverywherePlugin.Log.LogInfo(
            $"[CabinetEverywhere][诊断] 带包家具明细（GetHomeMapId 映射）共 {_dumpLines.Count + _homeInstances.Count} 条：");
        foreach (var l in _dumpLines) CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere][诊断] " + l);
        _dumpLines.Clear();
        foreach (var c in _homeInstances)
            CabinetEverywherePlugin.Log.LogInfo($"[CabinetEverywhere][诊断]   入选 家具 {c.ConfigId}（{c.Name}）owner={c.OwnerId}(0x{c.OwnerId:X})");
    }

    private static bool _instanceLogged;

    /// <summary>家中全部储物容器（柜子/冰箱/材料架+标签，同一次遍历产出）——TagFilter 筛选用。</summary>
    internal static IReadOnlyList<ContainerInst> AllContainers => _allContainers;

    /// <summary>柜子子集（kind=='c' 且 ConfigIds 名单命中，同一次遍历产出）——直达注入用，语义与合并前一致。</summary>
    internal static IReadOnlyList<ContainerInst> HomeInstances => _homeInstances;
}
