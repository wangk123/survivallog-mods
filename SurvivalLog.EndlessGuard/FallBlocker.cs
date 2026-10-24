using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 拦沦陷：EndlessManager.FallMapPoint 是全游戏唯一沦陷执行点（唯一调用方 ApplyFallenMapPoints，
/// 反汇编 0x264CD90/0x2649140，DESIGN §2.1）。prefix 恒 false = "未新沦陷"跳过分支（test al,al; je 实证），
/// 无横幅/遗产事件/封锁写入。参数 (GameSaveData, TagManager, int, int, ref string, bool, bool) 全非泛型，interop 兼容。
/// </summary>
[HarmonyPatch]
internal static class FallBlocker
{
    internal static int BlockedCount;

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.EndlessManager), "FallMapPoint");

    private static bool Prefix()
    {
        if (!EndlessGuardPlugin.PreventNewFalls.Value) return true; // 开关关=官方原行为
        BlockedCount++;
        if (BlockedCount == 1 || BlockedCount % 10 == 0)
            EndlessGuardPlugin.Trace("FallBlocker：已拦截 " + BlockedCount + " 次沦陷（累计）");
        return false;
    }
}
