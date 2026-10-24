using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// [Bag.&lt;id&gt;] 节自解析：12 常用包默认条目首跑写入 + 玩家自加节即时识别（mtime 监听，每轮 Ticker Poll）。
/// 键名用 ASCII（Bag.110001）——与 BepInEx 绑定键（Global/Tier/Debug）分治：本类只认自己的节，
/// BepInEx 保存 cfg 时对未知节按 orphan 原样回写（注释行可能被丢弃，柜名以 README/ListOnReady 日志为准）。
/// </summary>
internal static class BagSections
{
    internal static readonly Dictionary<int, (int w, int h)> Entries = new();

    /// <summary>预设 v1 的 12 包默认值（DESIGN §5.1 阶梯口径 A）。调值=新增 PresetHistory 版本，勿改历史行。</summary>
    internal static readonly (int id, int w, int h)[] PresetV1 =
    {
        (110002, 10, 12), // 小型置物架   原生 6x8   小档
        (110001, 14, 12), // 中型置物架   原生 8x10  中档
        (110000, 18, 12), // 大型置物架   原生 12x10 大档
        (110008, 20, 12), // 超大型置物架 原生 15x12 特大档
        (110009, 20, 12), // 材料架       原生 20x12 特大档（顶格保持）
        (1004,   10, 12), // 双门冰箱     原生 8x7   小档
        (115000, 10, 12), // 双开门冰箱   原生 8x7   小档
        (115001, 14, 12), // 冰柜         原生 8x10  中档
        (115005, 18, 12), // 大容量冰柜   原生 12x10 大档
        (1021,   10, 12), // 自行车车筐   原生 5x4（反推）小档
        (1022,   14, 12), // 轿车后备箱   原生 10x8（反推）中档
        (1023,   18, 12), // SUV后备箱+门口塑料袋 原生 12x10 大档
    };

    /// <summary>当前预设版本。调默认值时 bump 并在 PresetHistory 追加新版本行。</summary>
    internal const int CurrentPreset = 1;

    /// <summary>预设史（版本→12 包默认值）。迁移语义：文件条目等于旧版本默认 → 自动改写为新版本默认；玩家改过的（不等于任何旧默认）原样保留。</summary>
    internal static readonly Dictionary<int, (int id, int w, int h)[]> PresetHistory = new()
    {
        { 1, PresetV1 },
    };

    private static string _path;
    private static DateTime _lastMtime = DateTime.MinValue;
    private static int _lastHash;

    private static readonly Regex SectionRe = new(@"^\s*\[\s*Bag\.(\d+)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WidthRe = new(@"^\s*Width\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightRe = new(@"^\s*Height\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Init(string cfgPath)
    {
        _path = cfgPath;
        EnsureDefaultSections();
        MigratePreset();
        Reload();
    }

    /// <summary>首跑写入：文件不存在或没有任何 [Bag. 节时，追加 12 包默认节（一次性；之后 BepInEx 重写文件也会按 orphan 保留这些节）。</summary>
    private static void EnsureDefaultSections()
    {
        try
        {
            var text = File.Exists(_path) ? File.ReadAllText(_path) : "";
            if (text.Contains("[Bag.")) return;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(";; ===== 柜子容量：常用 12 包显式宽高（Width/Height 任一改 0 = 该包恢复官方原值；给其他柜子加条目见 README）=====");
            foreach (var (id, w, h) in PresetV1)
                sb.AppendLine().AppendLine("[Bag." + id + "]").AppendLine("Width = " + w).AppendLine("Height = " + h);
            File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 默认节写入失败（不影响其余功能）: " + e.Message); }
    }

    /// <summary>预设迁移：Preset 键 &lt; 当前版本时，把等于旧默认的文件条目改写为新默认（cfg 首跑固化坑的对策）。当前只有 v1，升级到 v2+ 时自然生效。</summary>
    private static void MigratePreset()
    {
        try
        {
            int from = CabinetSizePlugin.Preset.Value;
            if (from >= CurrentPreset || !File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            for (int ver = from + 1; ver <= CurrentPreset; ver++)
            {
                if (!PresetHistory.TryGetValue(ver, out var table) || !PresetHistory.TryGetValue(ver - 1, out var prev)) continue;
                foreach (var (id, nw, nh) in table)
                {
                    var (pw, ph, _) = Array.Find(prev, t => t.id == id);
                    foreach (var nl in new[] { "\r\n", "\n" }) // 容忍 BepInEx 重写时可能的换行风格差异
                    {
                        var oldBlock = SectionText(id, pw, ph, nl);
                        if (text.Contains(oldBlock))
                        {
                            text = text.Replace(oldBlock, SectionText(id, nw, nh, nl));
                            break;
                        }
                    }
                }
            }
            File.WriteAllText(_path, text, new UTF8Encoding(true));
            CabinetSizePlugin.Preset.Value = CurrentPreset;
            CabinetSizePlugin.Log.LogInfo($"[CabinetSize] 预设已迁移到 v{CurrentPreset}（未手动改过的条目已跟新默认）");
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 预设迁移失败（可手动改 cfg）: " + e.Message); }
    }

    private static string SectionText(int id, int w, int h, string nl) =>
        "[Bag." + id + "]" + nl + "Width = " + w + nl + "Height = " + h;

    private static void Reload()
    {
        Entries.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            int cur = -1, w = 0, h = 0; bool hasW = false, hasH = false;
            foreach (var raw in File.ReadAllLines(_path))
            {
                var line = raw.TrimEnd();
                var m = SectionRe.Match(line);
                if (m.Success) { Flush(); cur = int.Parse(m.Groups[1].Value); w = h = 0; hasW = hasH = false; continue; }
                if (cur <= 0) continue;
                var mw = WidthRe.Match(line); if (mw.Success) { w = int.Parse(mw.Groups[1].Value); hasW = true; continue; }
                var mh = HeightRe.Match(line); if (mh.Success) { h = int.Parse(mh.Groups[1].Value); hasH = true; }
            }
            Flush();
            void Flush()
            {
                if (cur > 0 && hasW && hasH && w > 0 && h > 0) Entries[cur] = (w, h); // 任一 ≤0/缺失 = 恢复原生（不收录）
            }
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] cfg 解析失败: " + e.Message); }
        _lastMtime = File.Exists(_path) ? File.GetLastWriteTime(_path) : DateTime.MinValue;
        _lastHash = Hash();
    }

    private static int Hash()
    {
        int hash = Entries.Count;
        foreach (var kv in Entries) hash = hash * 31 ^ (kv.Key * 397 + kv.Value.w * 29 + kv.Value.h);
        return hash;
    }

    /// <summary>每轮 Ticker 调用：文件 mtime 变化则重解析；条目集变化返回 true（触发立即改写）。</summary>
    internal static bool Poll()
    {
        try
        {
            if (!File.Exists(_path) || File.GetLastWriteTime(_path) == _lastMtime) return false;
            int prev = _lastHash;
            Reload();
            return _lastHash != prev;
        }
        catch { return false; }
    }
}
