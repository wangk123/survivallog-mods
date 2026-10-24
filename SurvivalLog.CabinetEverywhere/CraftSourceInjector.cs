using System;
using System.Collections.Generic;
using BepInEx;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 白名单注入：把柜子家具 ConfigId 加进 ToolCabinetConfig.CraftSourceFurnitureIds。
/// 该集合是官方"制作材料源"判定（原生唯一成员=工具柜 70006），其消费方实证有五个：
/// 工作台 AE_OpenHandMade、无人机交易 ItemManager.CollectTradeContainers、
/// 无人机站 AE_OpenDroneHubPanel、粉碎机 AE_OpenShredderPanel、温室 GreenhouseProgress。
/// 工作台自动补料 GetAllLinkedOwnerIds 遍历 背包+抽屉+CabinetTabs，柜子入列表即原生参与。
///
/// 对称移除（2026-10-24，General.Enabled 启停门）：_added 记录本会话注入的全部 ID，
/// RemoveAll 逐条从集合 Remove 后清空——禁用路径（General.Enabled=false）与
/// EnableCraftSource=false 路径都调用；已打开界面重开后收敛（原生重建页签，无需主动刷）。
/// 官方原有成员（70006 工具柜）不在 _added 内，永不误删。
/// </summary>
internal static class CraftSourceInjector
{
    private static bool _announced;
    private static readonly HashSet<int> _added = new();
    // 空操作 trace 单次哨兵（RemoveAll 幂等被巡检高频调用，只落一次证据防刷屏）
    private static bool _noopTraced;

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
                    // 只认领"真新增"：Add 返回 false=集合里本来就有（如官方原生 70006 工具柜
                    // 本身在柜子名单内）——不进 _added，RemoveAll 才不会误删官方成员（对称性前提）
                    if (set.Add(id))
                    {
                        _added.Add(id);
                        added++;
                    }
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
                if (added > 0)
                    Trace($"Ensure: 本次新增 {added}，_added={_added.Count}，白名单集合={SafeCount(set)}");
            }
        }
        catch (Exception e)
        {
            if (_announced) return;
            _announced = true;
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] 白名单注入异常: " + e.Message);
        }
    }

    /// <summary>
    /// 对称移除：把本会话注入的白名单 ID 全部从 CraftSourceFurnitureIds 移除并清空 _added。
    /// 幂等：_added 为空时静默空操作（含被巡检高频调用的场景），每会话仅首次空操作落一条
    /// trace 自证集合官方态。被移除失败残留的 ID 保留在 _added（下轮再试）。
    /// </summary>
    internal static void RemoveAll()
    {
        try
        {
            var set = GameCore.HotUpdate.ToolCabinetConfig.CraftSourceFurnitureIds;
            if (_added.Count == 0)
            {
                if (!_noopTraced)
                {
                    _noopTraced = true;
                    Trace($"RemoveAll(noop): 无本会话注入，白名单集合={SafeCount(set)}（官方态）");
                }
                return;
            }

            int before = SafeCount(set);
            var removed = new List<int>();
            var stuck = new List<int>();
            if (set != null)
            {
                foreach (var id in _added)
                {
                    try { if (set.Remove(id)) removed.Add(id); else stuck.Add(id); }
                    catch (Exception) { stuck.Add(id); }
                }
            }
            _added.Clear();
            foreach (var id in stuck) _added.Add(id); // 移除失败者保留下轮重试

            int after = SafeCount(set);
            CabinetEverywherePlugin.Log.LogInfo(
                $"[CabinetEverywhere] 白名单对称移除：移除 {removed.Count} 条（集合 {before}->{after}，官方原有 70006 工具柜保留）");
            Trace($"RemoveAll: 移除 {removed.Count} 条 [{string.Join(",", removed)}]，集合 {before}->{after}，_added 剩 {_added.Count}");
        }
        catch (Exception e)
        {
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere] 白名单对称移除异常: " + e.Message);
        }
    }

    private static int SafeCount(Il2CppSystem.Collections.Generic.HashSet<int> set)
    {
        try { return set?.Count ?? -1; }
        catch { return -1; }
    }

    /// <summary>自证 trace（BepInEx 日志强杀不落盘，状态变化落文件；仅状态迁移时写，低频）。</summary>
    private static void Trace(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Paths.BepInExRootPath, "CE_gate_trace.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { }
    }
}
