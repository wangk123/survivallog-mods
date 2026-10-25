using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// [Bag.&lt;id&gt;] 节双轨（2026-10-25 v4 起）：
/// ① 13 常用包（12 + 工具柜 10007）升级为 **BepInEx 正式绑定键**（插件 Bind，见 CabinetSizePlugin.BindConfig）——
/// BepInEx 原生读写/落盘，ModCenter 反射桥零改造直达；孤儿陈旧问题（见下）对这 13 包根除。
/// ② 玩家自加 [Bag.&lt;其他id&gt;] 节仍走本类文件自解析（mtime 监听，每轮 Ticker Poll）。
/// BepInEx cfg 机制实证（部署版 6.0.0-dev.2025.12.31 反查上游 commit 577eba4 源码）：
/// Save() **不重读磁盘**，未绑定节按 orphan 字典（**加载时**快照）回写——外部改文件后一旦有绑定键
/// 落盘，orphan 节值会被打回加载时值（这就是旧"注释被挤掉"的根因，旧版靠 MaintainBagComments
/// 补注释自愈、该机制已随 v4 绑定制退役删除；玩家自加节的"记事本改+面板改绑定键"组合同理存在
/// 打回风险，属已知限制见 DESIGN §9）。绑定节无此问题（Save 永远写内存现值）。
/// 同一监听周期还热改绑定键：[Global]/[Tier.*] 与 13 包 [Bag.*] 记事本改动即时生效（ApplyHotKeys）。
/// </summary>
internal static class BagSections
{
    /// <summary>玩家自加节解析结果（v4 起 13 常用包为绑定键不依赖此表；本表仍会收录它们，仅作兜底展示，解析权威=BagBindings）。</summary>
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

    /// <summary>预设 v3（作者 2026-10-25 定稿表：冰箱族上移——双门/双开门→中、冰柜→大、大容量冰柜→特大；载具三项脱离档位值独立定值 12x6/16x8/18x10。v3 未发布前连续并入 115000 升中与载具定值，首发即终表）。</summary>
    internal static readonly (int id, int w, int h, string note)[] PresetV3 =
    {
        (110002, 14, 10, ";; 小型置物架（原生 6x8｜小档）"),
        (110001, 16, 12, ";; 中型置物架（原生 8x10｜中档）"),
        (110000, 18, 14, ";; 大型置物架（原生 12x10｜大档）"),
        (110008, 20, 16, ";; 超大型置物架（原生 15x12｜特大档）"),
        (110009, 20, 16, ";; 材料架（原生 20x12｜特大档）"),
        (1004,   16, 12, ";; 双门冰箱（原生 8x7｜中档）"),
        (115000, 16, 12, ";; 双开门冰箱（原生 8x7｜中档）"),
        (115001, 18, 14, ";; 冰柜（原生 8x10｜大档）"),
        (115005, 20, 16, ";; 大容量冰柜（原生 12x10｜特大档）"),
        (1021,   12, 6,  ";; 自行车车筐（原生 5x4｜默认 12x6）"),
        (1022,   16, 8,  ";; 小汽车后备箱（原生 10x8｜默认 16x8）"),
        (1023,   18, 10, ";; SUV后备箱+门口塑料袋（原生 12x10｜默认 18x10）"),
    };

    /// <summary>预设 v4（2026-10-25 作者令：工具柜 10007 加入显式清单为独立专属块；12 包值沿 v3 不变。
    /// v4 起 13 包升级为 BepInEx 绑定键：节头 ;; 注释退役（说明文案进 Bind 描述=## 注释，BepInEx 原生维护），
    /// 此处 note 仅作绑定描述源（TrimStart ';;'）与历史留档。</summary>
    internal static readonly (int id, int w, int h, string note)[] PresetV4 =
    {
        (110002, 14, 10, ";; 小型置物架（原生 6x8｜小档）"),
        (110001, 16, 12, ";; 中型置物架（原生 8x10｜中档，游戏内'工具柜'家具70006共用此包）"),
        (110000, 18, 14, ";; 大型置物架（原生 12x10｜大档）"),
        (110008, 20, 16, ";; 超大型置物架（原生 15x12｜特大档）"),
        (110009, 20, 16, ";; 材料架（原生 20x12｜特大档）"),
        (1004,   16, 12, ";; 双门冰箱（原生 8x7｜中档）"),
        (115000, 16, 12, ";; 双开门冰箱（原生 8x7｜中档）"),
        (115001, 18, 14, ";; 冰柜（原生 8x10｜大档）"),
        (115005, 20, 16, ";; 大容量冰柜（原生 12x10｜特大档）"),
        (1021,   12, 6,  ";; 自行车车筐（原生 5x4｜默认 12x6）"),
        (1022,   16, 8,  ";; 小汽车后备箱（原生 10x8｜默认 16x8）"),
        (1023,   18, 10, ";; SUV后备箱+门口塑料袋（原生 12x10｜默认 18x10）"),
        (10007,  14, 10, ";; 工具柜（原生 6x7｜小档，独立专属块）"),
    };

    /// <summary>当前预设版本。调默认值时 bump 并在 PresetHistory 追加新版本行。</summary>
    internal const int CurrentPreset = 4;

    /// <summary>预设史（版本→13 包默认值）。迁移语义（v4 起条目制）：绑定 entry 值等于上一版默认 → 改写为新版默认；
    /// 玩家改过的（不等于任何旧默认）原样保留。v3 及以前为孤儿节文本制时代（迁移逻辑同语义，见 MigratePreset 注释）。</summary>
    internal static readonly Dictionary<int, (int id, int w, int h, string note)[]> PresetHistory = new()
    {
        { 1, PresetV1 },
        { 2, PresetV2 },
        { 3, PresetV3 },
        { 4, PresetV4 },
    };

    private static string _path;
    private static DateTime _lastMtime = DateTime.MinValue;
    private static int _lastHash;

    /// <summary>绑定 entry 最近一次被进程内赋值的时间（SettingChanged 沿记录；含 ModCenter 面板写、
    /// ApplyHotKeys 自身赋值）。ApplyHotKeys 的陈旧文件闸用：文件 mtime 早于它 = 文件内容落后于
    /// 内存，跳过热改应用（2026-10-25 T17 实锤的跨 mod 竞态：ModCenter 防抖落盘间隙里第二条面板
    /// 写会被本 mod 的文件监听用旧盘值覆盖回去，用户第二次调整静默丢失）。</summary>
    private static DateTime _lastBindChangeUtc = DateTime.MinValue;

    /// <summary>SettingChanged 沿调用（reload 处理器内）：记录内存侧最新赋值时刻。</summary>
    internal static void NoteBindChange() => _lastBindChangeUtc = DateTime.UtcNow;

    private static readonly Regex SectionRe = new(@"^\s*\[\s*Bag\.(\d+)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WidthRe = new(@"^\s*Width\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightRe = new(@"^\s*Height\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnySectionRe = new(@"^\s*\[\s*([^\]]+?)\s*]", RegexOptions.IgnoreCase | RegexOptions.Compiled); // 任意节头（Global/Tier.*/Bag.*/Debug）
    private static readonly Regex IntKeyValRe = new(@"^\s*([A-Za-z0-9_]+)\s*=\s*""?(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static void Init(string cfgPath)
    {
        _path = cfgPath;
        MigratePreset();
        EnsureDefaultSections();
        Reload();
    }

    /// <summary>缺节兜底落盘：文件缺任何一个当前预设 13 包的 [Bag.*] 节 → Config.Save() 一次
    /// （BepInEx 会把全部绑定节（含 ## 描述注释）规范写出；MigratePreset 改 Preset 键本身就会触发
    /// SaveOnConfigSet 自动落盘，本方法兜"版本已最新但节缺失"（如玩家手删）与首跑两种情况）。
    /// v3 及以前为手写追加 12 节（EnsureDefaultSections 文本版），v4 绑定制后退役。</summary>
    private static void EnsureDefaultSections()
    {
        try
        {
            var text = File.Exists(_path) ? File.ReadAllText(_path) : "";
            foreach (var (id, _, _, _) in PresetV4)
                if (!text.Contains("[Bag." + id + "]"))
                {
                    CabinetSizePlugin.Cfg.Save();
                    CabinetSizePlugin.Log.LogInfo($"[CabinetSize] cfg 缺 [Bag.{id}] 节，已按绑定键默认值补写落盘");
                    return;
                }
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 缺节兜底落盘失败（不影响其余功能）: " + e.Message); }
    }

    /// <summary>预设迁移（v4 起条目制）：Preset 键 &lt; 当前版本时，把**绑定 entry 值等于上一版默认**的条目改写为新版默认
    /// （cfg 首跑固化坑的对策）。绑定键在 Bind 阶段已从文件载入现值，此处直接比内存值——旧版文本制时代的
    /// "孤儿节文本替换"不再需要（v1/v2/v3 老文件同样先被 Bind 读成 entry 再比较，语义等价且对 BepInEx 重写免疫）。
    /// 新版新增包（上一版表无对应行，如 v4 的 10007）无旧默认可比 → 保持 Bind 默认不动。
    /// 迁移后写 Preset=CurrentPreset → SettingChanged → SaveOnConfigSet 自动落盘（顺带把新节写全）。</summary>
    private static void MigratePreset()
    {
        try
        {
            int from = CabinetSizePlugin.Preset.Value;
            if (from >= CurrentPreset) return;
            var applied = new List<string>();
            for (int ver = from + 1; ver <= CurrentPreset; ver++)
            {
                if (!PresetHistory.TryGetValue(ver, out var table) || !PresetHistory.TryGetValue(ver - 1, out var prev)) continue;
                foreach (var (id, nw, nh, _) in table)
                {
                    if (!CabinetSizePlugin.BagBindings.TryGetValue(id, out var be)) continue;
                    var (pw, ph, _, _) = Array.Find(prev, t => t.id == id);
                    if (pw <= 0) continue; // 上一版无此包（新增）：Bind 默认即终值
                    if (be.W.Value == pw && be.H.Value == ph && (be.W.Value != nw || be.H.Value != nh))
                    {
                        be.W.Value = nw;
                        be.H.Value = nh;
                        applied.Add($"{id} {pw}x{ph}->{nw}x{nh}");
                    }
                }
            }
            CabinetSizePlugin.Preset.Value = CurrentPreset;
            CabinetSizePlugin.Log.LogInfo($"[CabinetSize] 预设已迁移到 v{CurrentPreset}"
                + (applied.Count > 0 ? "（未手动改过的条目已跟新默认：" + string.Join("、", applied) + "）" : "（13 包显式清单就位，工具柜 10007 已加入）"));
        }
        catch (Exception e) { CabinetSizePlugin.Log.LogWarning("[CabinetSize] 预设迁移失败（可手动改 cfg）: " + e.Message); }
    }

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

    /// <summary>绑定键热改：[Global] MaxWidth/MaxHeight、[Tier.*] Width/Height 与 13 包 [Bag.*] Width/Height 记事本改动即时生效。
    /// 解析值与绑定 entry 不等才赋值（entry.Value 赋值 → SettingChanged → 既有 reload 处理器置 Rerun=true，下轮 Ticker 落地）；
    /// 相等不动——BepInEx 被触发的计划性存档会重写文件（mtime 变→再解析→值相等），自然收敛，无每轮重复赋值。
    /// 不解析/不回写 Preset（迁移走 MigratePreset 专用路径）、ExcludeIds、Debug 键。
    /// v4 注：13 包节已是绑定键，BepInEx 自身不改文件值（orphan 打回只影响未绑定节），玩家记事本改值由本表拾取。</summary>
    private static void ApplyHotKeys()
    {
        try
        {
            if (!File.Exists(_path)) return;
            // 陈旧文件闸（T17）：文件 mtime 早于最近一次内存赋值 = 盘值落后于内存（ModCenter 面板写
            // 防抖落盘的间隙），跳过——否则旧盘值会覆盖尚未落盘的新值（第二次调整静默丢失实锤）。
            // 正常玩家记事本热改：mtime 晚于任何内存赋值，不受影响。
            if (File.GetLastWriteTimeUtc(_path) < _lastBindChangeUtc) return;
            var map = new List<(string key, BepInEx.Configuration.ConfigEntry<int> entry)>
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
            foreach (var kv in CabinetSizePlugin.BagBindings)
            {
                map.Add(("bag." + kv.Key + "|width", kv.Value.W));
                map.Add(("bag." + kv.Key + "|height", kv.Value.H));
            }
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
                int hit = map.FindIndex(p => p.key == sectionKey + "|" + km.Groups[1].Value.ToLowerInvariant());
                if (hit < 0) continue; // 节+键不在热改表（玩家自加 [Bag.*] 节走 Entries 解析）→ 不动
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

    /// <summary>每轮 Ticker 调用：文件 mtime 变化则重解析；条目集变化返回 true（触发立即改写）。
    /// 重解析后顺带绑定键热改（ApplyHotKeys——含 13 包 [Bag.*] 绑定键的记事本改动）。
    /// v4 删 MaintainBagComments：13 包节已是绑定键（## 描述注释由 BepInEx 原生维护，不再被挤掉），
    /// 玩家自加节无已知文案可维护——注释自愈机制整体退役。</summary>
    internal static bool Poll()
    {
        try
        {
            if (!File.Exists(_path) || File.GetLastWriteTime(_path) == _lastMtime) return false;
            int prev = _lastHash;
            Reload();
            ApplyHotKeys();
            return _lastHash != prev;
        }
        catch { return false; }
    }
}
