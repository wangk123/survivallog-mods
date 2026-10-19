using System;
using System.Diagnostics;
using HarmonyLib;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 工作台性能探针（2026-10-20，制作卡顿定数用）：纯测量、零行为改动。
/// Harmony 挂 RefreshRecipeList（全量重算）与 ToolTableManager.TryCreateItem（单件完成）
/// 前后打秒表，**永远放行原方法**（与已回滚的合并阀本质不同：不跳过、不改参、不推迟）。
/// 每 10 秒窗口汇总一次（次数/平均/最大毫秒 + GC 内存增量与收集计数），单次调用超阈值
/// 即刻单独记录（5 秒级卡死会被"慢调用"行直接抓到并标注是第几件制作）。
/// 开关 = Debug.Verbose（发版归一 false，不删代码）。
/// </summary>
internal static class ToolTableProbe
{
    private const float WindowSeconds = 10f;
    private const int SlowCallMs = 100;

    private static bool _installed;

    // 窗口统计（游戏逻辑全在主线程，无并发）
    private static float _windowStart;
    private static int _rrlCount, _rrlMaxMs, _craftCount, _craftMaxMs;
    private static long _rrlSumTicks; // Stopwatch.Tick 求和（避免浮点累计）
    private static long _craftSumTicks;
    private static int _gc0, _gc1, _gc2;
    private static long _memAtWindowStart;
    private static int _craftSeq; // 制作件数流水号

    private static long _rrlStart, _craftStart;
    private static int _inRrl, _inCraft; // 防重入保险（理论不会发生）

    internal static void Install()
    {
        if (_installed) return;
        try
        {
            var h = new Harmony("com.local.survivallog.cabineteverywhere.probe");

            var rrl = AccessTools.Method(typeof(GameCore.HotUpdate.ReduxUI.Reducer_Web_ToolTable),
                "RefreshRecipeList");
            if (rrl != null)
            {
                var p = new HarmonyMethod(typeof(ToolTableProbe), nameof(RrlPrefix));
                var q = new HarmonyMethod(typeof(ToolTableProbe), nameof(RrlPostfix));
                h.Patch(rrl, prefix: p, postfix: q);
            }
            else CabinetEverywherePlugin.Log.LogWarning("[CE.探针] RefreshRecipeList 未找到（版本变化？）");

            var tci = AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.ToolTableManager),
                "TryCreateItem");
            if (tci != null)
            {
                var p = new HarmonyMethod(typeof(ToolTableProbe), nameof(CraftPrefix));
                var q = new HarmonyMethod(typeof(ToolTableProbe), nameof(CraftPostfix));
                h.Patch(tci, prefix: p, postfix: q);
            }
            else CabinetEverywherePlugin.Log.LogWarning("[CE.探针] TryCreateItem 未找到（版本变化？）");

            _installed = true;
            _windowStart = UnityEngine.Time.realtimeSinceStartup;
            _memAtWindowStart = GC.GetTotalMemory(false);
            _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
            CabinetEverywherePlugin.Log.LogInfo(
                "[CE.探针] 已挂：RefreshRecipeList+TryCreateItem 纯计时（10s 窗口汇总，慢调用>=" +
                SlowCallMs + "ms 即时记录）");
        }
        catch (Exception e)
        {
            CabinetEverywherePlugin.Log.LogWarning("[CE.探针] 安装失败（不影响游戏）: " + e.Message);
        }
    }

    // ---- RefreshRecipeList ----
    private static void RrlPrefix()
    {
        if (_inRrl > 0) return;
        _inRrl++;
        _rrlStart = Stopwatch.GetTimestamp();
    }

    private static void RrlPostfix()
    {
        if (_inRrl == 0) return;
        _inRrl--;
        var ms = (int)((Stopwatch.GetTimestamp() - _rrlStart) * 1000 / Stopwatch.Frequency);
        _rrlCount++;
        _rrlSumTicks += (Stopwatch.GetTimestamp() - _rrlStart) * 1000000 / Stopwatch.Frequency; // μs 累计
        if (ms > _rrlMaxMs) _rrlMaxMs = ms;
        if (ms >= SlowCallMs)
            CabinetEverywherePlugin.Log.LogWarning(
                $"[CE.探针] 慢调用 RefreshRecipeList {ms}ms（本窗口第 {_rrlCount} 次）");
        MaybeReport();
    }

    // ---- TryCreateItem（单件制作完成）----
    private static void CraftPrefix()
    {
        if (_inCraft > 0) return;
        _inCraft++;
        _craftStart = Stopwatch.GetTimestamp();
    }

    private static void CraftPostfix()
    {
        if (_inCraft == 0) return;
        _inCraft--;
        var ms = (int)((Stopwatch.GetTimestamp() - _craftStart) * 1000 / Stopwatch.Frequency);
        _craftSeq++;
        _craftCount++;
        _craftSumTicks += (Stopwatch.GetTimestamp() - _craftStart) * 1000000 / Stopwatch.Frequency;
        if (ms > _craftMaxMs) _craftMaxMs = ms;
        if (ms >= SlowCallMs)
            CabinetEverywherePlugin.Log.LogWarning(
                $"[CE.探针] 慢完成 TryCreateItem {ms}ms ← 第 {_craftSeq} 件");
        MaybeReport();
    }

    private static void MaybeReport()
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now - _windowStart < WindowSeconds) return;

        int rrlAvgUs = _rrlCount > 0 ? (int)(_rrlSumTicks / _rrlCount) : 0;
        int craftAvgUs = _craftCount > 0 ? (int)(_craftSumTicks / _craftCount) : 0;
        long mem = GC.GetTotalMemory(false);
        float memDeltaMb = (mem - _memAtWindowStart) / 1048576f;
        int g0 = GC.CollectionCount(0) - _gc0, g1 = GC.CollectionCount(1) - _gc1, g2 = GC.CollectionCount(2) - _gc2;

        // 窗口有活动才输出（工作台没开时保持静默，避免探针自己刷屏）
        if (_rrlCount > 0 || _craftCount > 0 || g2 > 0)
            CabinetEverywherePlugin.Log.LogInfo(
                $"[CE.探针] 10s: 重算 {_rrlCount} 次 avg {rrlAvgUs / 1000.0:F1}ms max {_rrlMaxMs}ms | " +
                $"制作完成 {_craftCount} 次 avg {craftAvgUs / 1000.0:F1}ms max {_craftMaxMs}ms | " +
                $"GC 0:{g0} 1:{g1} 2:{g2} 内存{(memDeltaMb >= 0 ? "+" : "")}{memDeltaMb:F0}MB");

        _windowStart = now;
        _rrlCount = 0; _rrlSumTicks = 0; _rrlMaxMs = 0;
        _craftCount = 0; _craftSumTicks = 0; _craftMaxMs = 0;
        _memAtWindowStart = mem;
        _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
    }
}
