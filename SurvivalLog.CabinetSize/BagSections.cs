using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// [Bag.&lt;id&gt;] 节自解析：12 常用包默认条目首跑写入 + 玩家自加节即时识别（mtime 监听，每轮 Ticker Poll）。
/// 键名用 ASCII（Bag.110001）——与 BepInEx 绑定键（Global/Tier/Debug）分治：本类只认自己的节；
/// BepInEx 保存 cfg 时对未知节按 orphan 原样回写，但会把节间 ;; 注释挤离节头（孤儿行实锤）——MaintainBagComments 自动补回。
/// 同一监听周期还热改绑定键：[Global] MaxWidth/MaxHeight 与 [Tier.*] Width/Height 记事本改动即时生效（ApplyHotKeys）。
/// </summary>
internal static class BagSections
{
    internal static readonly Dictionary<int, (int w, int h)> Entries = new();

    /// <summary>预设 v1 的 12 包默认值（DESIGN §5.1 阶梯口径 A）。第 4 元=节头注释文案（作者口径照抄；
    /// MaintainBagComments 按精确文本识别，勿改措辞）。调值=新增 PresetHistory 版本，勿改历史行。</summary>
    internal static readonly (int id, int w, int h, string note)[] PresetV1 =
    {
        (110002, 10, 12, ";; 小型置物架（原生 6x8｜小档）"),
        (110001, 14, 12, ";; 中型置物架（原生 8x10｜中档）"),
        (110000, 18, 12, ";; 大型置物架（原生 12x10｜大档）"),
        (110008, 20, 12, ";; 超大型置物架（原生 15x12｜特大档）"),
        (110009, 20, 12, ";; 材料架（原生 20x12｜特大档，顶格保持原生大小）"),
        (1004,   10, 12, ";; 双门冰箱（原生 8x7｜小档）"),
        (115000, 10, 12, ";; 双开门冰箱（原生 8x7｜小档）"),
        (115001, 14, 12, ";; 冰柜（原生 8x10｜中档）"),
        (115005, 18, 12, ";; 大容量冰柜（原生 12x10｜大档）"),
        (1021,   10, 12, ";; 自行车车筐（原生 5x4｜小档）"),
        (1022,   14, 12, ";; 小汽车后备箱（原生 10x8｜中档）"),
        (1023,   18, 12, ";; SUV后备箱+门口塑料袋（原生 12x10｜大档）"),
    };

    /// <summary>预设 v2（作者 2026-10-25 实测定稿：小 14x10 / 中 16x12 / 大 18x14 / 特大 20x16）。</summary>
    internal static readonly (int id, int w, int h, string note)[] PresetV2 =
    {
        (110002, 14, 10, ";; 小型置物架（原生 6x8｜小档）"),
        (110001, 16, 12, ";; 中型置物架（原生 8x10｜中档）"),
        (110000, 18, 14, ";; 大型置物架（原生 12x10｜大档）"),
        (110008, 20, 16, ";; 超大型置物架（原生 15x12｜特大档）"),
        (110009, 20, 16, ";; 材料架（原生 20x12｜特大档）"),
        (1004,   14, 10, ";; 双门冰箱（原生 8x7｜小档）"),
        (115000, 14, 10, ";; 双开门冰箱（原生 8x7｜小档）"),
        (115001, 16, 12, ";; 冰柜（原生 8x10｜中档）"),
        (115005, 18, 14, ";; 大容量冰柜（原生 12x10｜大档）"),
        (1021,   14, 10, ";; 自行车车筐（原生 5x4｜小档）"),
        (1022,   16, 12, ";; 小汽车后备箱（原生 10x8｜中档）"),
        (1023,   18, 14, ";; SUV后备箱+门口塑料袋（原生 12x10｜大档）"),
    };

    /// <summary>预设 v3（作者 2026-10-25 二次定档：冰箱族整体上移——双门冰箱升中、冰柜/轿车后备箱升大、大容量冰柜/SUV+门口袋升特大；档位值沿用 v2）。</summary>
    internal static readonly (int id, int w, int h, string note)[] PresetV3 =
    {
        (110002, 14, 10, ";; 小型置物架（原生 6x8｜小档）"),
        (110001, 16, 12, ";; 中型置物架（原生 8x10｜中档）"),
        (110000, 18, 14, ";; 大型置物架（原生 12x10｜大档）"),
        (110008, 20, 16, ";; 超大型置物架（原生 15x12｜特大档）"),
        (110009, 20, 16, ";; 材料架（原生 20x12｜特大档）"),
        (1004,   16, 12, ";; 双门冰箱（原生 8x7｜中档）"),
        (115000, 14, 10, ";; 双开门冰箱（原生 8x7｜小档）"),
        (115001, 18, 14, ";; 冰柜（原生 8x10｜大档）"),
        (115005, 20, 16, ";; 大容量冰柜（原生 12x10｜特大档）"),
        (1021,   14, 10, ";; 自行车车筐（原生 5x4｜小档）"),
        (1022,   18, 14, ";; 小汽车后备箱（原生 10x8｜大档）"),
        (1023,   20, 16, ";; SUV后备箱+门口塑料袋（原生 12x10｜特大档）"),
    };

    /// <summary>当前预设版本。调默认值时 bump 并在 PresetHistory 追加新版本行。</summary>
    internal const int CurrentPreset = 3;

    /// <summary>预设史（版本→12 包默认值）。迁移语义：文件条目等于旧版本默认 → 自动改写为新版本默认；玩家改过的（不等于任何旧默认）原样保留。</summary>
    internal static readonly Dictionary<int, (int id, int w, int h, string note)[]> PresetHistory = new()
    {
        { 1, PresetV1 },
        { 2, PresetV2 },
        { 3, PresetV3 },
    };

    private static string _path;
    private static DateTime _lastMtime = DateTime.MinValue;
    private static int _lastHash;

    private static readonly Regex SectionRe = new(@"^\s*\[\s*Bag\.(\d+)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WidthRe = new(@"^\s*Width\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightRe = new(@"^\s*Height\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnySectionRe = new(@"^\s*\[\s*([^\]]+?)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled); // 任意节头（Global/Tier.*/Bag.*/Debug）
    private static readonly Regex IntKeyValRe = new(@"^\s*([A-Za-z0-9_]+)\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Init(string cfgPath)
    {
        _path = cfgPath;
        EnsureDefaultSections();
        MigratePreset();
        Reload();
        MaintainBagComments(); // 跨重启自愈：上次运行 BepInEx 重写挤掉的节注释，本次启动即补回（已在=不写）
    }

    /// <summary>首跑写入：文件不存在或没有任何 [Bag. 节时，追加 12 包默认节（一次性，含节头 ;; 注释；
    /// 之后 BepInEx 重写文件也会按 orphan 保留这些节，注释若被挤掉由 MaintainBagComments 补回）。</summary>
    private static void EnsureDefaultSections()
    {
        try
        {
            var text = File.Exists(_path) ? File.ReadAllText(_path) : "";
            if (text.Contains("[Bag.")) return;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(";; ===== 柜子容量：常用 12 包显式宽高（Width/Height 任一改 0 = 该包恢复官方原值；给其他柜子加条目见 README）=====");
            foreach (var (id, w, h, note) in PresetHistory.TryGetValue(CurrentPreset, out var cur) ? cur : PresetV1)
                sb.AppendLine().AppendLine(note).AppendLine("[Bag." + id + "]").AppendLine("Width = " + w).AppendLine("Height = " + h);
            File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 默认节写入失败（不影响其余功能）: " + e.Message); }
    }

    /// <summary>预设迁移：Preset 键 &lt; 当前版本时，把等于旧默认的文件条目改写为新默认（cfg 首跑固化坑的对策）。v2=2026-10-25 作者实测定稿（小14x10/中16x12/大18x14/特大20x16）；v3=同日二次定档（冰箱族整体上移一档，档位值沿用 v2）。</summary>
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
                foreach (var (id, nw, nh, _) in table)
                {
                    var (pw, ph, _, _) = Array.Find(prev, t => t.id == id);
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

    /// <summary>绑定键热改：[Global] MaxWidth/MaxHeight 与 [Tier.Small|Medium|Large|XLarge] Width/Height 记事本改动即时生效。
    /// 解析值与绑定 entry 不等才赋值（entry.Value 赋值 → SettingChanged → 既有 reload 处理器置 Rerun=true，下轮 Ticker 落地）；
    /// 相等不动——BepInEx 被触发的计划性存档会重写文件（mtime 变→再解析→值相等），自然收敛，无每轮重复赋值。
    /// 不解析/不回写 Preset（迁移走 MigratePreset 专用路径）、ExcludeIds、Debug 键。</summary>
    private static void ApplyHotKeys()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var map = new (string key, BepInEx.Configuration.ConfigEntry<int> entry)[]
            {
                ("global|maxwidth", CabinetSizePlugin.MaxWidth),
                ("global|maxheight", CabinetSizePlugin.MaxHeight),
                ("tier.small|width", CabinetSizePlugin.SmallW),
                ("tier.small|height", CabinetSizePlugin.SmallH),
                ("tier.medium|width", CabinetSizePlugin.MediumW),
                ("tier.medium|height", CabinetSizePlugin.MediumH),
                ("tier.large|width", CabinetSizePlugin.LargeW),
                ("tier.large|height", CabinetSizePlugin.LargeH),
                ("tier.xlarge|width", CabinetSizePlugin.XLargeW),
                ("tier.xlarge|height", CabinetSizePlugin.XLargeH),
            };
            string section = "", sectionKey = "";
            var applied = new List<string>();
            foreach (var raw in File.ReadAllLines(_path))
            {
                var line = raw.TrimEnd();
                var sm = AnySectionRe.Match(line);
                if (sm.Success) { section = sm.Groups[1].Value.Trim(); sectionKey = section.ToLowerInvariant(); continue; }
                var t = line.TrimStart();
                if (t.Length == 0 || t[0] == '#' || t[0] == ';') continue; // ##/;;/# 注释行不解析
                var km = IntKeyValRe.Match(line);
                if (!km.Success) continue;
                int hit = Array.FindIndex(map, p => p.key == sectionKey + "|" + km.Groups[1].Value.ToLowerInvariant());
                if (hit < 0) continue; // 节+键不在热改表（含 [Bag.*] 内的 Width/Height）→ 不动
                var entry = map[hit].entry;
                int parsed = int.Parse(km.Groups[2].Value);
                if (entry == null || parsed == entry.Value) continue; // 相等不动（防循环的关键闸）
                int old = entry.Value;
                entry.Value = parsed;
                applied.Add(section + "." + km.Groups[1].Value + " " + old + "->" + parsed);
            }
            if (applied.Count > 0)
                CabinetSizePlugin.Log.LogInfo("[CabinetSize] cfg 热改已应用：" + string.Join("、", applied));
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 绑定键热改解析失败: " + e.Message); }
    }

    /// <summary>[Bag.*] 节头 ;; 注释自动补回：BepInEx 重写 cfg 会把节间注释挤离节头（孤儿行实锤）。
    /// 每次重解析后检查 12 已知包：注释未紧邻节头则重写文件补回。只增删已知文案的注释行（游离孤儿/重复行一并清掉），
    /// 其余行（含 BepInEx 托管的 ##/# 说明与键值行、玩家自加节内容）逐字保留；玩家自加节无文案可写，不处理。</summary>
    private static void MaintainBagComments()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            string nl = text.Contains("\r\n") ? "\r\n" : "\n"; // 保持原换行风格
            var lines = new List<string>(text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            var noteOf = new Dictionary<int, string>();
            var idOf = new Dictionary<string, int>();
            foreach (var (id, _, _, note) in PresetHistory.TryGetValue(CurrentPreset, out var cur2) ? cur2 : PresetV1)
            { noteOf[id] = note; idOf[note] = id; } // 补回/比对用当前版本文案
            foreach (var kv in PresetHistory) // 历史版本文案也登记：换版后旧文案的游离行可在后续周期被清理
                foreach (var (id2, _, _, note2) in kv.Value)
                    if (!idOf.ContainsKey(note2)) idOf[note2] = id2;
            bool dirty = false;
            // 1) 清游离行：已知文案但下一行不是其对应 [Bag.<id>] 节头（被挤进邻近节的孤儿/重复行）
            for (int j = lines.Count - 1; j >= 0; j--)
            {
                if (!idOf.TryGetValue(lines[j].Trim(), out int id)) continue;
                if (j + 1 >= lines.Count || !IsBagHeader(lines[j + 1], id)) { lines.RemoveAt(j); dirty = true; }
            }
            // 2) 补缺失：节头上无紧邻注释则插入（倒序扫描，Insert 不影响待处理的更小下标）
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var m = SectionRe.Match(lines[i]);
                if (!m.Success || !noteOf.TryGetValue(int.Parse(m.Groups[1].Value), out var note)) continue;
                if (i > 0 && lines[i - 1].Trim() == note) continue; // 已紧邻：不重写（作者手工维护的注释走此路径）
                lines.Insert(i, note);
                dirty = true;
            }
            if (!dirty) return;
            File.WriteAllText(_path, string.Join(nl, lines), new UTF8Encoding(true));
            _lastMtime = File.GetLastWriteTime(_path); // 自身写入不触发多余重解析（注释行不进 Entries，_lastHash 不变）
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 节注释补回失败（不影响功能）: " + e.Message); }
    }

    private static bool IsBagHeader(string line, int id)
    {
        var m = SectionRe.Match(line);
        return m.Success && int.Parse(m.Groups[1].Value) == id;
    }

    /// <summary>每轮 Ticker 调用：文件 mtime 变化则重解析；条目集变化返回 true（触发立即改写）。
    /// 重解析后顺带绑定键热改（ApplyHotKeys）与 [Bag.*] 节注释补回（MaintainBagComments）。</summary>
    internal static bool Poll()
    {
        try
        {
            if (!File.Exists(_path) || File.GetLastWriteTime(_path) == _lastMtime) return false;
            int prev = _lastHash;
            Reload();
            ApplyHotKeys();
            MaintainBagComments();
            return _lastHash != prev;
        }
        catch { return false; }
    }
}
