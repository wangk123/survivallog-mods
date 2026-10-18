using System;
using System.Collections.Generic;

namespace SurvivalLog.ModShared;

/// <summary>
/// Config_Bag / Config_Furniture 内存表直写工具（4 个扩容 mod 共享源码，各自编译为独立 dll）。
///
/// 实证依据（2026-10-01/02 反汇编 + 运行时 dump）：
/// - Config_Bag.Size = List&lt;int&gt;，[0]=宽 [1]=高；逻辑/天赋/UI 三层实时直读，无快照
/// - Config_Bag.Burden 是 BagComponent.OnInit/SetBagConfigId 的快照；改表后需重新读档
/// - Config_Furniture.ColdRate = 腐烂速率，保鲜时长 = 1/ColdRate（ComputeItemTimeScale 直读 0x108）
/// - ConfigManager._bagSizeSet 尺寸白名单懒重建，改 Size 后置 null 即可
/// - 全部幂等：首次读到的值记为原值，重复执行永远按 原值×倍数。
/// </summary>
public static class BagTable
{
    // 进程内原值（key: bagConfigId / furnitureConfigId）
    private static readonly Dictionary<long, int> OrigW = new();
    private static readonly Dictionary<long, int> OrigH = new();
    private static readonly Dictionary<long, int> OrigBurden = new();
    private static readonly Dictionary<long, float> OrigCold = new();

    public static GameCore.HotUpdate.ConfigManager Cm()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return null; }
    }

    public static Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Bag> BagDict(GameCore.HotUpdate.ConfigManager cm)
    {
        try { return cm._Config_Bag_Dict; }
        catch { return null; }
    }

    public static Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Furniture> FurnDict(GameCore.HotUpdate.ConfigManager cm)
    {
        try { return cm._Config_Furniture_Dict; }
        catch { return null; }
    }

    public static HashSet<int> ParseIds(string s)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(s)) return set;
        foreach (var part in s.Split(',', ';'))
            if (int.TryParse(part.Trim(), out var id))
                set.Add(id);
        return set;
    }

    /// <summary>宽高倍数取整（AwayFromZero：7×1.5=10.5→11）。返回 null 表示该条无法处理。</summary>
    public static (int w, int h)? WantSize(int cfgId, int origW, int origH, float wMult, float hMult)
    {
        if (origW <= 0 || origH <= 0) return null;
        return ((int)Math.Round(origW * wMult, MidpointRounding.AwayFromZero),
                (int)Math.Round(origH * hMult, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// 对名单内 Config_Bag 改写 Size。返回 (scanned, changed, logLines)。
    /// 未就绪时 scanned=-1。origOverride 允许调用方预置原值（不需要）。
    /// </summary>
    public static (int, int, List<string>) RewriteSize(HashSet<int> targets, float wMult, float hMult, bool verbose)
    {
        var lines = new List<string>();
        var cm = Cm();
        if (cm == null) return (-1, 0, lines);
        var dict = BagDict(cm);
        if (dict == null || dict.Count == 0) return (-1, 0, lines);

        int scanned = 0, changed = 0;
        foreach (var pair in dict)
        {
            if (!targets.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            scanned++;

            Il2CppSystem.Collections.Generic.List<int> size = null;
            try { size = bag.Size; } catch { }
            if (size == null || size.Count < 2)
            {
                lines.Add("BAG " + pair.Key + " 无 Size，跳过");
                continue;
            }

            int w0 = size[0], h0 = size[1];
            OrigW.TryAdd(pair.Key, w0);
            OrigH.TryAdd(pair.Key, h0);

            var want = WantSize(pair.Key, OrigW[pair.Key], OrigH[pair.Key], wMult, hMult);
            if (want == null) continue;

            if (w0 != want.Value.w || h0 != want.Value.h)
            {
                size[0] = want.Value.w;
                size[1] = want.Value.h;
                changed++;
                lines.Add($"BAG {pair.Key}({Name(bag)}) {w0}x{h0} -> {want.Value.w}x{want.Value.h}");
            }
            else if (verbose)
                lines.Add($"BAG {pair.Key}({Name(bag)}) 已是 {w0}x{h0}（原 {OrigW[pair.Key]}x{OrigH[pair.Key]}）");
        }

        InvalidateBagSizeIndex(cm, lines);
        return (scanned, changed, lines);
    }

    /// <summary>对名单内 Config_Bag 改写 Burden（负重快照需重新读档体现）。</summary>
    public static (int, int, List<string>) RewriteBurden(HashSet<int> targets, int mult, bool verbose)
    {
        var lines = new List<string>();
        var cm = Cm();
        if (cm == null) return (-1, 0, lines);
        var dict = BagDict(cm);
        if (dict == null || dict.Count == 0) return (-1, 0, lines);

        int scanned = 0, changed = 0;
        foreach (var pair in dict)
        {
            if (!targets.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            scanned++;

            int raw = 0;
            try { raw = bag.Burden; } catch { }
            if (raw <= 0) continue;

            OrigBurden.TryAdd(pair.Key, raw);
            int wantB = OrigBurden[pair.Key] * mult;
            if (raw != wantB)
            {
                bag.Burden = wantB;
                changed++;
                lines.Add($"BAG {pair.Key}({Name(bag)}) 负重 {OrigBurden[pair.Key]} -> {wantB}");
            }
            else if (verbose)
                lines.Add($"BAG {pair.Key} 负重已是 {raw}（原 {OrigBurden[pair.Key]}）");
        }
        return (scanned, changed, lines);
    }

    /// <summary>
    /// 对 BagId ∈ bagTargets 且 ColdRate&gt;0 的家具改写冷藏强度。
    /// keepMult = 保鲜倍数（2 = 保质期翻倍 = ColdRate÷2）。
    /// </summary>
    public static (int, int, List<string>) RewriteFridgeChill(HashSet<int> bagTargets, float keepMult, bool verbose)
    {
        var lines = new List<string>();
        var cm = Cm();
        if (cm == null) return (-1, 0, lines);
        var dict = FurnDict(cm);
        if (dict == null || dict.Count == 0) return (-1, 0, lines);
        if (keepMult <= 0.01f) return (0, 0, lines);

        int scanned = 0, changed = 0;
        foreach (var pair in dict)
        {
            var f = pair.Value;
            if (f == null) continue;

            int bagId = 0; float cold = 0f;
            try { bagId = f.BagId; cold = f.ColdRate; } catch { }
            if (!bagTargets.Contains(bagId) || cold <= 0f) continue;
            scanned++;

            OrigCold.TryAdd(pair.Key, cold);
            float want = OrigCold[pair.Key] / keepMult;
            if (Math.Abs(cold - want) > 1e-6f)
            {
                f.ColdRate = want;
                changed++;
                lines.Add($"FURN {pair.Key}({FurnName(f)}) ColdRate {OrigCold[pair.Key]:0.###} -> {want:0.###}（保鲜 ×{keepMult:0.##}）");
            }
            else if (verbose)
                lines.Add($"FURN {pair.Key}({FurnName(f)}) ColdRate 已是 {want:0.###}");
        }
        return (scanned, changed, lines);
    }

    /// <summary>尺寸白名单懒重建：置空让下一次 HasBagSize 按改写后的表重建。</summary>
    public static void InvalidateBagSizeIndex(GameCore.HotUpdate.ConfigManager cm, List<string> lines)
    {
        try { cm._bagSizeSet = null; }
        catch (Exception e) { lines.Add("WARN _bagSizeSet 置空失败: " + e.Message); }
    }

    // v1.1.0 存档修复器（RepairV11TimeScale）已于 2026-10-18 彻底移除：
    // 它是"绝不写 Data_Item"红线下唯一的特许例外，且判定区间 [0.30,0.55] 只按事故值 0.385
    // 标定、未覆盖合法天赋档位——2026-10-18 实测误伤 6 件 ts=0.5 的物品（显示保质翻倍）。
    // 自此全套件零物品写入、零存档写入，无任何例外。

    public static string Name(GameCore.HotUpdate.Config_Bag bag)
    {
        try { return bag.Name_Local ?? bag.Name ?? "?"; }
        catch { return "?"; }
    }

    public static string FurnName(GameCore.HotUpdate.Config_Furniture f)
    {
        try { return f.Name_Local ?? f.Name ?? "?"; }
        catch { return "?"; }
    }
}
