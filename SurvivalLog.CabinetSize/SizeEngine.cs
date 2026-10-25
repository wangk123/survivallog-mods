using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using SurvivalLog.ModShared;

namespace SurvivalLog.CabinetSize;

/// <summary>受管集发现 + 值解析 + 幂等改写 + trace 自证。机制与实证依据见 DESIGN.md §2/§5。</summary>
internal static class SizeEngine
{
    /// <summary>黑名单 34 包（沿用 CabinetExpand 2026-10-13 口径，理由留档其 DESIGN §5；机器槽/非储物/边界容器/纯场景搜刮）。</summary>
    internal static readonly int[] DefaultExcludeIds =
    {
        5001, 5002, 5003, 5004, 5005, 5006, 5011,       // 厨房电器（格子=烹饪槽位语义）
        5007, 5008, 5009, 5014, 5015, 5017, 5020,       // 老鼠笼/粉碎机/口粮压制机/暖房工程台/酿酒桶
        115004, 115002, 1050, 1062,                     // 堆肥箱/吊篮(种植)/通信设备
        10001, 10004,                                   // 床/垃圾桶
        1010, 2003, 1060, 1014, 1008, 1009, 115003, 3003, // 纸箱共包/门窗口暂存/无人机/货车/手推车
        110007, 3001, 2001, 2002, 2004, 10003,          // 纯场景搜刮容器
    };

    /// <summary>受管集：家具自动发现（BagId&gt;0）+ 载具（Config_Vehicle 全档 TrunkBagId + 门口袋 1023）− 黑名单 − cfg ExcludeIds + 个性化清单 id（最后注入，绕过黑名单——对纸箱/机器槽等同样生效）。null=配置表未就绪。</summary>
    internal static HashSet<int> DiscoverManaged(List<string> notes, out bool vehReady)
    {
        vehReady = false;
        var cm = BagTable.Cm();
        var furn = BagTable.FurnDict(cm);
        if (furn == null || furn.Count == 0) return null;

        var set = new HashSet<int>();
        foreach (var pair in furn)
        {
            var f = pair.Value;
            if (f == null) continue;
            int bagId = 0;
            try { bagId = f.BagId; } catch { continue; }
            if (bagId > 0) set.Add(bagId);
        }

        try
        {
            var vdict = cm._Config_Vehicle_Dict;
            if (vdict != null && vdict.Count > 0)
            {
                vehReady = true;
                foreach (var pair in vdict)
                {
                    var v = pair.Value;
                    if (v == null) continue;
                    int trunkId = 0;
                    try { trunkId = v.TrunkBagId; } catch { continue; }
                    if (trunkId > 0 && set.Add(trunkId))
                        notes?.Add($"{pair.Key}({VehName(v)})→包{trunkId}({BagName(trunkId)})");
                }
            }
        }
        catch { }
        set.Add(CabinetSizePlugin.DoorBoxBagId);

        foreach (var id in DefaultExcludeIds) set.Remove(id);
        foreach (var id in BagTable.ParseIds(CabinetSizePlugin.ExcludeIds.Value)) set.Remove(id);
        foreach (var id in Overrides.Keys) set.Add(id); // 个性化清单：最显式意图，最后注入绕过黑名单
        return set;
    }

    // ── 个性化覆盖清单（BagOverrides，2026-10-25 作者令"补个性化功能，类似动作加速"）──

    /// <summary>个性化清单解析缓存（每轮 Ticker RefreshOverrides 刷新；写入=ModCenter 面板清单块/记事本改 cfg）。</summary>
    internal static readonly Dictionary<int, (int w, int h)> Overrides = new();

    /// <summary>解析 "id:WxH,id:WxH"（容忍大小写 x/空格；坏条目静默跳过）。</summary>
    internal static Dictionary<int, (int w, int h)> ParseOverrides(string raw)
    {
        var map = new Dictionary<int, (int, int)>();
        if (string.IsNullOrWhiteSpace(raw)) return map;
        foreach (var part in raw.Split(','))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var ix = seg.IndexOf(':');
            if (ix <= 0) continue;
            if (!int.TryParse(seg.Substring(0, ix), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0) continue;
            var dims = seg.Substring(ix + 1).Trim();
            var x = dims.IndexOf('x'); if (x < 0) x = dims.IndexOf('X');
            if (x <= 0) continue;
            if (!int.TryParse(dims.Substring(0, x), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var w) || w <= 0) continue;
            if (!int.TryParse(dims.Substring(x + 1), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var h) || h <= 0) continue;
            map[id] = (w, h);
        }
        return map;
    }

    /// <summary>每轮改写前刷新 Overrides（键值未变零开销；面板写 SettingChanged→Rerun 下一轮即拾取）。</summary>
    internal static void RefreshOverrides()
    {
        var raw = CabinetSizePlugin.BagOverrides == null ? "" : CabinetSizePlugin.BagOverrides.Value;
        var parsed = ParseOverrides(raw);
        if (parsed.Count == Overrides.Count)
        {
            bool same = true;
            foreach (var kv in parsed)
                if (!Overrides.TryGetValue(kv.Key, out var cur) || cur != kv.Value) { same = false; break; }
            if (same) return;
        }
        Overrides.Clear();
        foreach (var kv in parsed) Overrides[kv.Key] = kv.Value;
    }

    private static string VehName(GameCore.HotUpdate.Config_Vehicle v)
    {
        try { return v.Name_Local ?? v.Name ?? "?"; } catch { return "?"; }
    }

    internal static string BagName(int bagId)
    {
        try
        {
            var cm = BagTable.Cm();
            var dict = BagTable.BagDict(cm);
            if (dict != null && dict.TryGetValue(bagId, out var bag) && bag != null)
                return BagTable.Name(bag);
        }
        catch { }
        return "?";
    }

    // ── 值解析：个性化覆盖 ＞ 13 包绑定键 ＞ 玩家自加节 ＞ 原生格数档位 ＞ 不缩水守卫 ＞ 全局夹顶 ──

    internal static string TierOf(int area) =>
        area <= 60 ? "小" : area <= 99 ? "中" : area <= 149 ? "大" : "特大";

    internal static (int w, int h, string src) Resolve(int id, int nativeW, int nativeH)
    {
        int w, h; string src;
        if (Overrides.TryGetValue(id, out var ov)) { w = ov.w; h = ov.h; src = "个性化"; }
        else if (CabinetSizePlugin.BagBindings.TryGetValue(id, out var bb)) { w = bb.W.Value; h = bb.H.Value; src = "cfg显式"; }
        else if (BagSections.Entries.TryGetValue(id, out var e)) { w = e.w; h = e.h; src = "cfg自加"; }
        else
        {
            var tier = TierOf(nativeW * nativeH);
            src = tier + "档";
            (w, h) = tier switch
            {
                "小" => (CabinetSizePlugin.SmallW.Value, CabinetSizePlugin.SmallH.Value),
                "中" => (CabinetSizePlugin.MediumW.Value, CabinetSizePlugin.MediumH.Value),
                "大" => (CabinetSizePlugin.LargeW.Value, CabinetSizePlugin.LargeH.Value),
                _ => (CabinetSizePlugin.XLargeW.Value, CabinetSizePlugin.XLargeH.Value),
            };
        }
        w = Math.Max(w, nativeW); h = Math.Max(h, nativeH); // 不缩水：显式 0 经此变原生（0=恢复原生的实现点）；未来新包原生超档位时保护
        if (CabinetSizePlugin.MaxWidth.Value > 0) w = Math.Min(w, CabinetSizePlugin.MaxWidth.Value);
        if (CabinetSizePlugin.MaxHeight.Value > 0) h = Math.Min(h, CabinetSizePlugin.MaxHeight.Value);
        return (w, h, src);
    }

    // ── 幂等绝对值改写（会话首见即原生；单写者=本 mod）──

    private static readonly Dictionary<int, int> NativeW = new();
    private static readonly Dictionary<int, int> NativeH = new();

    /// <summary>逐包改写到目标值。verbose=true 时未变更包也输出 原生→目标（ListOnReady 用）。返回日志行。</summary>
    internal static List<string> RewriteManaged(HashSet<int> ids, bool verbose)
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = cm == null ? null : BagTable.BagDict(cm);
        if (dict == null) return lines;
        int changed = 0;
        foreach (var pair in dict)
        {
            if (!ids.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            Il2CppSystem.Collections.Generic.List<int> size = null;
            try { size = bag.Size; } catch { }
            if (size == null || size.Count < 2) continue;
            int w0 = size[0], h0 = size[1];
            if (w0 <= 0 || h0 <= 0) continue;
            if (!NativeW.TryGetValue(pair.Key, out var nw)) { nw = w0; NativeW[pair.Key] = w0; }
            if (!NativeH.TryGetValue(pair.Key, out var nh)) { nh = h0; NativeH[pair.Key] = h0; }

            var (tw, th, src) = Resolve(pair.Key, nw, nh);
            if (w0 != tw || h0 != th)
            {
                size[0] = tw;
                size[1] = th;
                changed++;
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) {w0}x{h0} -> {tw}x{th}（{src}）");
            }
            else if (verbose)
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) 已是 {w0}x{h0}（原生 {nw}x{nh}，{src}）");
        }
        if (changed > 0) BagTable.InvalidateBagSizeIndex(cm, lines);
        return lines;
    }

    /// <summary>自证 trace 文件（日志强杀不落盘，实机验证一律看它）。路径 BepInEx\CabinetSize_trace.txt。</summary>
    internal static void WriteTrace(string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "CabinetSize_trace.txt"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + content,
                new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>
    /// General.Enabled=false 热关还原（2026-10-25，T10 模式）：重新发现受管集，全部回写原生值
    /// （NativeW/NativeH 会话缓存——首见即原生，本 mod 是唯一写者）。表未就绪返回 false（调用方
    /// 下一帧重试）；名单按当前 cfg 重算（先 ExcludeIds 再热关的包不在还原面，与套件同边界）。
    /// </summary>
    internal static bool RestoreAll(List<string> lines, out int changedOut)
    {
        changedOut = 0;
        var ids = DiscoverManaged(null, out _);
        if (ids == null) return false;
        var cm = BagTable.Cm();
        var dict = cm == null ? null : BagTable.BagDict(cm);
        if (dict == null) return false;
        int changed = 0;
        foreach (var pair in dict)
        {
            if (!ids.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            Il2CppSystem.Collections.Generic.List<int> size = null;
            try { size = bag.Size; } catch { }
            if (size == null || size.Count < 2) continue;
            int w0 = size[0], h0 = size[1];
            if (!NativeW.TryGetValue(pair.Key, out var nw)) { nw = w0; NativeW[pair.Key] = w0; }
            if (!NativeH.TryGetValue(pair.Key, out var nh)) { nh = h0; NativeH[pair.Key] = h0; }
            if (w0 != nw || h0 != nh)
            {
                size[0] = nw;
                size[1] = nh;
                changed++;
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) {w0}x{h0} -> {nw}x{nh}（还原原生）");
            }
        }
        if (changed > 0) BagTable.InvalidateBagSizeIndex(cm, lines);
        changedOut = changed;
        return true;
    }
}

/// <summary>读档诊断：DiagOnOpen=true 时列出全部容器 BagConfigId；载具三包实例初始化 always-on 打点（每包每次会话一行）。</summary>
internal static class BagInitDiag
{
    private static readonly System.Collections.Generic.HashSet<int> Seen = new();
    private static readonly System.Collections.Generic.HashSet<int> Watched = new();
    private static readonly System.Collections.Generic.HashSet<int> VehicleWatch = new() { 1021, 1022, 1023 };

    [HarmonyLib.HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.BagComponent), "OnInit")]
    [HarmonyLib.HarmonyPostfix]
    private static void Postfix(GameCore.HotUpdate.Battle.Logic.BagComponent __instance, int bagConfigId)
    {
        try
        {
            if (VehicleWatch.Contains(bagConfigId) && Watched.Add(bagConfigId))
                CabinetSizePlugin.Log.LogInfo(
                    $"[CabinetSize][载具] 包 {bagConfigId}（{SizeEngine.BagName(bagConfigId)}）实例初始化，扩容已生效");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize][载具] 诊断读取失败: " + e.Message); }

        if (CabinetSizePlugin.DiagOnOpen == null || !CabinetSizePlugin.DiagOnOpen.Value) return;
        try
        {
            if (!Seen.Add(bagConfigId)) return;
            int w = 0, h = 0;
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict != null && dict.TryGetValue(bagConfigId, out var bag) && bag != null)
            {
                var sz = bag.Size;
                if (sz != null && sz.Count >= 2) { w = sz[0]; h = sz[1]; }
            }
            CabinetSizePlugin.Log.LogInfo(
                $"[CabinetSize][诊断] 容器初始化 bagConfigId={bagConfigId}（{SizeEngine.BagName(bagConfigId)}，当前 {w}x{h}，初始包={__instance.GetInitialBagConfigId()}）");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize][诊断] OnInit 读取失败: " + e.Message); }
    }
}
