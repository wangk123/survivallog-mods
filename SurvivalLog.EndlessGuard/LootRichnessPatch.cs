using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate;                    // BaseSingleton 所在命名空间（brief 疏漏，实证 CS0103）
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 数量层（DESIGN §2.5.4 v5 定稿）：官方探索物资难度值 + RichnessStep×该图已购新卡数。
/// 挂官方 RollExploreLootCopies（地面/柜子/追加波三路自动全生效），零私有刷新曲线。
/// __result==0（难度跳过）不救回；非三图不加成。
/// </summary>
[HarmonyPatch]
internal static class LootRichnessPatch
{
    private static readonly System.Random Rng = new();

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(DifficultyTools), "RollExploreLootCopies");

    private static void Postfix(ref int __result)
    {
        try
        {
            var step = EndlessGuardPlugin.RichnessStep.Value;
            if (step <= 0f || __result <= 0) return;      // 难度跳过的件不救回
            var map = CurMap();
            if (map != 1002 && map != 1004 && map != 1005) return;
            var owned = ScavengePlans.OwnedNewCount(map);
            if (owned <= 0) return;
            var bonus = step * owned;
            var whole = (int)bonus;
            if (Rng.NextDouble() < bonus - whole) whole++;
            if (whole > 0) __result += whole;
        }
        catch { /* fail-safe：任何异常=本件不加成，官方结果原样 */ }
    }

    private static int CurMap()
    {
        if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated) return 0;
        var em = BaseSingleton<BattleLogicWorld>.Instance?._ExploreManager;
        return em == null ? 0 : em.MapConfigId;
    }
}
