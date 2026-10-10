using System;
using System.Collections.Generic;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 白名单注入：把柜子家具 ConfigId 加进 ToolCabinetConfig.CraftSourceFurnitureIds。
/// 该集合是官方"制作材料源"判定（原生唯一成员=工具柜 70006），其消费方实证有五个：
/// 工作台 AE_OpenHandMade、无人机交易 ItemManager.CollectTradeContainers、
/// 无人机站 AE_OpenDroneHubPanel、粉碎机 AE_OpenShredderPanel、温室 GreenhouseProgress。
/// 工作台自动补料 GetAllLinkedOwnerIds 遍历 背包+抽屉+CabinetTabs，柜子入列表即原生参与。
/// </summary>
internal static class CraftSourceInjector
{
    private static bool _announced;
    private static readonly HashSet<int> _added = new();

    internal static void Ensure(HashSet<int> cfgIds)
    {
        try
        {
            // 首次访问静态属性即触发 il2cpp 类初始化（cctor 构建 HashSet{70006}）
            var set = GameCore.HotUpdate.ToolCabinetConfig.CraftSourceFurnitureIds;
            if (set == null)
            {
                if (_announced) return;
                _announced = true;
                CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] CraftSourceFurnitureIds 不可访问，白名单注入未生效");
                return;
            }

            int added = 0;
            foreach (var id in cfgIds)
            {
                if (_added.Contains(id)) continue;
                try
                {
                    set.Add(id);
                    _added.Add(id);
                    added++;
                }
                catch (Exception e)
                {
                    CabinetEverywherePlugin.Log.LogWarning($"[CabinetEverywhere] 白名单 Add({id}) 失败: {e.Message}");
                }
            }

            if (added > 0 || !_announced)
            {
                _announced = true;
                CabinetEverywherePlugin.Log.LogInfo(
                    $"[CabinetEverywhere] 白名单注入完成：本次新增 {added} 条（累计 {_added.Count}，官方原有 70006 工具柜保留）");
                if (CabinetEverywherePlugin.Verbose.Value)
                    CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere] 名单：[" + string.Join(",", _added) + "]");
            }
        }
        catch (Exception e)
        {
            if (_announced) return;
            _announced = true;
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] 白名单注入异常: " + e.Message);
        }
    }
}
