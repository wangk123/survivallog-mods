using System;
using System.Collections.Generic;
using SurvivalLog.ModShared;

namespace SurvivalLog.CabinetTagFilter;

/// <summary>
/// 家中容器枚举（筛选用）：单遍直扫 AgentManager.agentList，凡 BagId&gt;0 的家具
/// （柜子/冰箱/置物架/材料架/床头柜/载具…）全部收录。
/// 语义=「不是带该标签的容器就藏页签」，名单宁全勿缺：没有页签的容器进了名单也无害
/// （无页签可藏）；反之缺了（如冰箱）就会出现"常驻藏不掉"。
/// 与储藏柜直达（CabinetEverywhere）的注入名单无关——那边的黑名单只决定"注入哪些柜子页签"，
/// 本筛选对所有容器页签一视同仁；两个 mod 可各自独立安装。
/// 顺序即性能：TryCast+配置查包（廉价，滤掉绝大多数）→ 主图严格相等短路 → 仅非主图走 IsHomeMap（重）。
/// </summary>
internal static class ContainerScan
{
    internal sealed class ContainerInst
    {
        public long OwnerId;
        public int[] TagIds = Array.Empty<int>();
        /// <summary>材料架（MaterialRackComponent）：不能打标签但只能放材料，筛选时补"材料"伪标签。</summary>
        public bool IsMaterialRack;
    }

    private static readonly List<ContainerInst> _home = new();
    internal static IReadOnlyList<ContainerInst> Home => _home;

    private static bool _loggedOnce;

    internal static void Refresh()
    {
        _home.Clear();
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return;
            var world = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance;
            var am = world?._AgentManager;
            var agentList = am?.agentList;
            if (agentList == null || agentList.Count == 0) return;

            var cm = BagTable.Cm();
            var dict = BagTable.FurnDict(cm);
            if (dict == null) return; // 配置表未就绪

            int mapId = am.GetHomeMapId();

            for (int i = 0; i < agentList.Count; i++)
            {
                GameCore.HotUpdate.Battle.Logic.Furniture furn = null;
                try { furn = agentList[i]?.TryCast<GameCore.HotUpdate.Battle.Logic.Furniture>(); } catch { }
                if (furn == null) continue;

                long ownerId = 0; int cfgId = 0;
                try { ownerId = furn.InstanceId; cfgId = furn.AgentConfigId; } catch { continue; }
                if (ownerId == 0 || cfgId == 0) continue;

                GameCore.HotUpdate.Config_Furniture cfg = null;
                try { dict.TryGetValue(cfgId, out cfg); } catch { }
                int bagId = 0;
                try { bagId = cfg != null ? cfg.BagId : 0; } catch { }
                if (bagId <= 0) continue; // 非容器家具

                int furnMap = 0;
                try { furnMap = furn.MapConfigId; } catch { }
                bool isHome = furnMap == mapId; // 主图直接过（绝大多数）
                if (!isHome)
                {
                    try { isHome = am.IsHomeMap(furnMap); } catch { } // 家地图组判定（地下室等）
                    if (!isHome) continue;
                }

                var inst = new ContainerInst { OwnerId = ownerId };
                FillTags(inst, furn);
                _home.Add(inst);
            }

            if (!_loggedOnce && _home.Count > 0)
            {
                _loggedOnce = true;
                CabinetTagFilterPlugin.Log.LogInfo("[CabinetTagFilter] 家中容器 " + _home.Count + " 个进入筛选名单（含冰箱/柜子/置物架/材料架）");
            }
        }
        catch (Exception e)
        {
            CabinetTagFilterPlugin.Log.LogWarning("[CabinetTagFilter] 容器枚举失败: " + e.Message);
        }
    }

    /// <summary>
    /// 读容器的官方标签（FurnitureTagComponent.TagIds）+ 是否材料架（MaterialRackComponent）。
    /// agentComponentDictionary 是 Dictionary&lt;Il2CppType, BaseEntity&gt;，按 Type 索引直达组件
    /// （无泛型/枚举坑）；无组件/异常一律空值兜底。
    /// </summary>
    private static void FillTags(ContainerInst inst, GameCore.HotUpdate.Battle.Logic.Furniture furn)
    {
        try
        {
            var dict = furn.agentComponentDictionary;
            if (dict == null) return;
            var raw = dict[Il2CppInterop.Runtime.Il2CppType.From(
                typeof(GameCore.HotUpdate.Battle.Logic.FurnitureTagComponent))];
            var comp = raw?.TryCast<GameCore.HotUpdate.Battle.Logic.FurnitureTagComponent>();
            var ids = comp?.TagIds;
            if (ids != null && ids.Count > 0)
            {
                var arr = new int[ids.Count];
                for (int i = 0; i < ids.Count; i++) arr[i] = ids[i];
                inst.TagIds = arr;
            }
        }
        catch { /* 标签读不到=无标签 */ }
        inst.IsMaterialRack = HasComponent(furn,
            typeof(GameCore.HotUpdate.Battle.Logic.MaterialRackComponent));
    }

    private static bool HasComponent(GameCore.HotUpdate.Battle.Logic.Furniture furn, Type type)
    {
        try
        {
            var dict = furn.agentComponentDictionary;
            return dict != null && dict[Il2CppInterop.Runtime.Il2CppType.From(type)] != null;
        }
        catch { return false; }
    }
}
