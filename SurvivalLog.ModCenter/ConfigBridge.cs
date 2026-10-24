using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 配置桥（任务 6）：面板的数据引擎。API 面为任务 4 实证定案（DESIGN §4，逐字消费）：
/// IL2CPPChainloader.Instance.Plugins 枚举 → BasePlugin.Config 直取 → cfg[ConfigDefinition]
/// 非泛型取 entry（SettingType 判型 + BoxedValue 读写，写同样触发 ClampValue+SettingChanged 链）。
/// 职责：快照（BuildSnapshot + BuildTp 第三方页·任务 14 Tier 0 通用 / 任务 15 ThirdParty
/// 清单合并定制）/ 写（TrySet——套件走 Meta 登记键+联动 writeKeys 等值，第三方走通用路由）/ 启停（SetModEnabled——套件=任务 13 直写各 mod
/// 自己的 General.Enabled；第三方=任务 14 回用 T6 时代本插件 cfg [Disabled] 名单标记（重启级，
/// 任务 16 起换 dll .disabled 真禁用））/ 消息分发（MC_OPEN→__mcRender 快照；MC_SET→TrySet→
/// __mcEvent MC_SET_OK；MC_TOGGLE_MOD→回新快照）/ Save 防抖（首写 SaveOnConfigSet=false，
/// 3s 节流 + MC_CLOSE 立即落盘后恢复原值）。
/// 线程纪律：全部 cfg 读写与分发都在 Ticker 主线程（Pump）；outbox 回调线程只入队。
/// </summary>
internal static class ConfigBridge
{
    // ---- 插件 ConfigFile 缓存（会话内不变；找不到也缓存 null 防重复扫） ----
    private static readonly Dictionary<string, ConfigFile> CfgCache = new();

    // ---- 消息入队（Eval 回调线程）→ Pump（主线程） ----
    private static readonly ConcurrentQueue<string> Inbox = new();

    // ---- Save 防抖：脏 ConfigFile → 原 SaveOnConfigSet；首写起 3s 节流落盘 ----
    private static readonly Dictionary<ConfigFile, bool> Dirty = new();
    private static float _dirtyAge = -1f; // -1 = 空闲
    private const float FlushInterval = 3f;

    private static readonly string EvidencePath =
        System.IO.Path.Combine(Paths.BepInExRootPath, "ModCenter_evidence.txt");

    // ============ 对外契约（任务 7/13/14 消费） ============

    /// <summary>全量快照：{self:{ver}, mods:[ModInfo], tp:[ModInfo]}（tp=第三方 Tier 0 通用页，任务 14）。</summary>
    internal static Snapshot BuildSnapshot()
    {
        var snap = new Snapshot { Self = new SelfInfo { Ver = ModCenterPlugin.Version } };
        foreach (var mm in Meta.Mods)
        {
            var cfg = CfgOf(mm.Guid);
            var mi = new ModInfo
            {
                Guid = mm.Guid, Name = mm.Name, En = mm.En, Desc = mm.Desc,
                // 插件不在链上（dll 级缺失）视为停用；在场以该 mod 自己的 General.Enabled 为准
                // （任务 13 真值源；entry 缺失=旧版 dll，按 Bind 默认 true 兜底）
                Enabled = cfg != null && ReadModEnabled(mm, cfg, out _)
            };
            foreach (var mg in mm.Groups)
            {
                var gi = new GroupInfo { Title = mg.Title };
                foreach (var it in mg.Items) gi.Items.Add(BuildItem(cfg, it));
                mi.Groups.Add(gi);
            }
            if (mm.OvrHost != null) mi.Ovr = ReadOvr(cfg, mm.OvrHost);
            snap.Mods.Add(mi);
        }
        BuildTp(snap); // 任务 14：Tier 0 第三方通用页
        return snap;
    }

    /// <summary>
    /// 写一项：guid 可用完整 GUID 或短名（en，大小写不敏感，如 'RobotSpeed'）；
    /// key 可为主键、任一 writeKeys 或组合展示键（联动项）。类型按 entry.SettingType 转换
    /// （float/int/bool/string/enum），联动项 writeKeys 逐键等值写；写前标脏（关该文件自动落盘）。
    /// 任务 14：guid 解析不到 Meta 时落第三方通用路由（只写对方 cfg 实际存在的键）。
    /// </summary>
    internal static bool TrySet(string guidOrName, string section, string key, string raw)
    {
        var mm = Resolve(guidOrName);
        if (mm == null)
        {
            var tp = FindTp(guidOrName);
            if (tp != null) return TrySetTp(tp, section, key, raw);
            Log("set 拒绝：未知插件 " + guidOrName); return false;
        }
        var it = FindItem(mm, section, key);
        if (it == null) { Log($"set 拒绝：{mm.En} 无登记项 {section}.{key}（只写元数据表声明过的键）"); return false; }
        var cfg = CfgOf(mm.Guid);
        if (cfg == null) { Log("set 拒绝：" + mm.En + " 不在链上"); return false; }

        var first = Entry(cfg, it.Section, it.WKeys[0]);
        if (first == null) { Log($"set 拒绝：{mm.En} {it.Section}.{it.WKeys[0]} entry 不存在（任务 8 待新增？）"); return false; }
        if (!ConvertValue(raw, first.SettingType, out var val)) { Log($"set 拒绝：'{raw}' 无法转为 {first.SettingType.Name}"); return false; }

        MarkDirty(cfg); // 先关自动落盘再写（滑条连写防抖前提，DESIGN §4.3-2）
        foreach (var wk in it.WKeys)
        {
            var e = Entry(cfg, it.Section, wk);
            if (e == null) { Log($"set 中断：{it.Section}.{wk} entry 不存在"); return false; }
            e.BoxedValue = val; // 同值写被 BepInEx 短路（不发 SettingChanged，§4.3-1）
        }
        Log($"set {mm.En} {it.K} = {raw}");
        return true;
    }

    /// <summary>
    /// 启停（任务 13 实装）：套件直写目标 mod 自己的 <c>General.Enabled</c>（节/键按
    /// MetaMod.EnabledSection/EnabledKey，默认 General:Enabled）。写入即触发该 mod 现役
    /// SettingChanged→Rerun 链（各 mod Ticker 首行 gate 消费：热关闭=按原值缓存还原并停写，
    /// 重开=重新落笔），Save 走统一防抖。旧的本插件 cfg [Disabled] 名单机制已移除（避免双轨，
    /// 快照真值源=各 mod entry 本身）。
    /// 任务 14：guid 解析不到 Meta 时走第三方分支——回用 T6 时代 [Disabled] 名单标记
    /// （重启级语义；任务 16 起换"退出时 dll 改名 .disabled"真禁用，名单届时退役）。
    /// </summary>
    internal static void SetModEnabled(string guidOrName, bool enabled)
    {
        var mm = Resolve(guidOrName);
        if (mm == null)
        {
            var tp = FindTp(guidOrName);
            if (tp != null) { SetTpEnabled(tp, enabled); return; }
            Log("toggle 拒绝：未知插件 " + guidOrName); return;
        }
        var cfg = CfgOf(mm.Guid);
        if (cfg == null) { Log("toggle 拒绝：" + mm.En + " 不在链上（dll 缺失？）"); return; }
        var en = Entry(cfg, mm.EnabledSection, mm.EnabledKey);
        if (en == null)
        { Log($"toggle 拒绝：{mm.En} 无 {mm.EnabledSection}.{mm.EnabledKey} entry（旧版 dll？）"); return; }
        if (!ConvertValue(enabled ? "true" : "false", en.SettingType, out var val))
        { Log($"toggle 拒绝：{mm.En} {mm.EnabledSection}.{mm.EnabledKey} 非 bool（{en.SettingType.Name}）"); return; }
        MarkDirty(cfg); // 先关自动落盘再写（统一防抖，同 TrySet）
        en.BoxedValue = val; // 同值写被 BepInEx 短路（不发 SettingChanged，§4.3-1）；值变化即触发目标 mod Rerun
        Log($"toggle {mm.En} → {(enabled ? "启用" : "停用")}（直写 {mm.Guid} {mm.EnabledSection}.{mm.EnabledKey}）");
    }

    /// <summary>读某 mod 的启停 entry（true/false）；entry 缺失=旧版 dll，按 Bind 默认 true 兜底。</summary>
    private static bool ReadModEnabled(MetaMod mm, ConfigFile cfg, out ConfigEntryBase entry)
    {
        entry = Entry(cfg, mm.EnabledSection, mm.EnabledKey);
        if (entry == null) return true;
        try
        {
            return Convert.ToBoolean(entry.BoxedValue, CultureInfo.InvariantCulture);
        }
        catch { return true; }
    }

    // ============ 快照细节 ============

    private static ItemInfo BuildItem(ConfigFile cfg, MetaItem it)
    {
        var e1 = cfg != null ? Entry(cfg, it.Section, it.WKeys[0]) : null;
        var ii = new ItemInfo { K = it.K, L = it.Label, B = it.Badge };
        if (e1 != null)
        {
            ii.T = TypeName(e1.SettingType);
            ii.V = e1.BoxedValue;
            ApplyRange(ii, e1); // 范围/白名单反射合入（元数据不写死为准）
        }
        else
        {
            ii.T = it.Type;   // entry 缺失（如 RoleUnlock Plan.* 待任务 8）：全走元数据兜底
            ii.V = it.Def;
        }
        if (ii.Min == null) ii.Min = it.Min;
        if (ii.Max == null) ii.Max = it.Max;
        ii.St = it.St; // 步进无反射来源，永远兜底
        if (ii.Opts == null && it.Opts != null) ii.Opts = it.Opts.ToList();
        // string 型 entry 无选项来源时按文本框渲染（任务 14；套件现役 string 项均带 Opts→sel 不受影响）
        if (ii.T == "sel" && ii.Opts == null && e1 != null && e1.SettingType == typeof(string)) ii.T = "str";
        ii.Note = it.Note ?? (it.Linked ? LinkedNote(cfg, it) : null);
        return ii;
    }

    /// <summary>范围/白名单反射合入（AcceptableValueRange/List 是 closed generic，走 GetType 反射；
    /// 任务 15 起 ThirdParty（Tier 1 清单页）共用——清单 min/max 覆盖在调用方合入后追加）。</summary>
    internal static void ApplyRange(ItemInfo ii, ConfigEntryBase e)
    {
        var av = e.Description != null ? e.Description.AcceptableValues : null;
        if (av != null)
        {
            var t = av.GetType();
            var pMin = t.GetProperty("MinValue");
            var pMax = t.GetProperty("MaxValue");
            if (pMin != null && pMax != null)
            {
                ii.Min = ToD(pMin.GetValue(av));
                ii.Max = ToD(pMax.GetValue(av));
            }
            else if (t.GetProperty("AcceptableValues") is { } pl && pl.GetValue(av) is System.Collections.IEnumerable en)
            {
                ii.Opts = en.Cast<object>()
                    .Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToList();
            }
        }
        if (ii.Opts == null && e.SettingType.IsEnum)
            ii.Opts = Enum.GetNames(e.SettingType).ToList();
    }

    /// <summary>联动项动态注记：当前 宽w×高h（v 展示宽度键值；宽高默认不一致的照实展示，拖动后同步）。</summary>
    private static string LinkedNote(ConfigFile cfg, MetaItem it)
    {
        string Fmt(ConfigEntryBase e) => e != null
            ? System.Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture)
            : "?";
        var w = cfg != null ? Entry(cfg, it.Section, it.WKeys[0]) : null;
        var h = cfg != null ? Entry(cfg, it.Section, it.WKeys[1]) : null;
        return "当前 宽" + Fmt(w) + "×高" + Fmt(h) + (it.LinkedSuffix ?? "");
    }

    private static List<OvrInfo> ReadOvr(ConfigFile cfg, MetaItem host)
    {
        var list = new List<OvrInfo>();
        var s = cfg != null ? Entry(cfg, host.Section, host.Key)?.BoxedValue as string : null;
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var part in s.Split(','))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var ix = seg.IndexOf(':');
            if (ix <= 0) continue;
            if (int.TryParse(seg.Substring(0, ix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                int.TryParse(seg.Substring(ix + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                list.Add(new OvrInfo { Id = id, Ms = ms });
        }
        return list;
    }

    private static double? ToD(object o) =>
        o == null ? (double?)null : System.Convert.ToDouble(o, CultureInfo.InvariantCulture);

    internal static string TypeName(Type t)
    {
        if (t == typeof(float) || t == typeof(double)) return "float";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
            return "int";
        if (t == typeof(bool)) return "bool";
        return "sel"; // enum / string / 其他 → 下拉
    }

    // ============ Tier 0 第三方通用页（任务 14，接入协议 docs/modcenter-integration-guide.md §二） ============

    // 第一方 GUID 前缀：套件 9 mod（Meta 已登记）/ ModCenter 自身 / 探针三件 / CabinetBurden、
    // EndlessGuard 等 Meta 未登记的本仓 mod 全在此族——一律不进第三方分组。
    private const string FirstPartyPrefix = "com.local.survivallog.";
    // 探针豁免名（BepInPlugin.Name 含其一即豁免；GUID 前缀已覆盖现役三件，此为改名/换族双保险）
    private static readonly string[] TpProbeNames = { "ActionProbe", "ViewportProbe", "ConfigDumpProbe" };

    /// <summary>
    /// 枚举 Chainloader 全插件 → 未登记且未豁免 → 通用页：有 cfg entry 逐条生成 Item
    /// （section 分组沿用对方 cfg 节名；徽标一律 cfg；Description→note）；无 entry →
    /// 仅启停形态（空 groups + desc 说明，同灾前加速页）。按 Name 排序稳定输出。
    /// </summary>
    private static void BuildTp(Snapshot snap)
    {
        Dictionary<string, PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch { }
        if (plugins == null) return;
        BepInEx.PluginInfo[] list;
        try { list = plugins.Values.OrderBy(p => p.Metadata?.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception e) { Log("warn tp 枚举失败: " + e.Message); return; }
        foreach (var info in list)
        {
            try
            {
                var md = info?.Metadata;
                if (md == null || string.IsNullOrEmpty(md.GUID) || info.Instance == null) continue;
                if (TpExempt(md.GUID, md.Name)) continue;
                snap.Tp.Add(BuildTpMod(info));
            }
            catch (Exception e) { Log("warn tp 构建跳过 " + (info?.Metadata?.GUID ?? "?") + ": " + e.Message); }
        }
        if (snap.Tp.Count > 0)
            Log("tp 快照: " + snap.Tp.Count + " 个第三方插件（" + string.Join(", ", snap.Tp.Select(m => m.En)) + "）");
    }

    /// <summary>豁免判定：Meta 登记（套件 9）/ ModCenter 自身 / 第一方 GUID 族 / 探针名。</summary>
    private static bool TpExempt(string guid, string name)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            foreach (var mm in Meta.Mods)
                if (string.Equals(mm.Guid, guid, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(guid, ModCenterPlugin.Guid, StringComparison.OrdinalIgnoreCase)) return true;
            if (guid.StartsWith(FirstPartyPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrEmpty(name))
            foreach (var p in TpProbeNames)
                if (name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>
    /// 逐插件构建第三方页：先按 Tier 0 通用页（枚举 cfg entry → 节名分组/键名标签/徽标 cfg/
    /// Description→note/AcceptableValueRange 反射，任务 14）；随后交任务 15 清单合并层
    /// <see cref="ThirdParty.Apply"/>——插件程序集内嵌 modcenter.json（mcSpec 1）有效时
    /// display/summary/groups/label/badge/min-max-step/order 全量覆盖为中文定制页
    /// （清单没列的键不出现；清单键不在 cfg → 该项隐藏），无效时保持 Tier 0 形态。
    /// </summary>
    private static ModInfo BuildTpMod(BepInEx.PluginInfo info)
    {
        var md = info.Metadata;
        var mi = new ModInfo
        {
            Guid = md.GUID,
            Name = md.Name,          // Tier 0：显示名 = 插件自报名（无中文定制，Tier 1 清单才有）
            En = md.Name,
            Enabled = TpEnabled(md.GUID), // 标记名单取反（T16 起换 dll 禁用真值源）
            Desc = "第三方 mod（Tier 0 自动识别）：通用页由其 cfg 自动生成；写入即触发其 SettingChanged，生效视该 mod。",
        };
        ConfigFile cfg = null;
        try { if (info.Instance is BasePlugin bp) cfg = bp.Config; } catch { }

        if (ThirdParty.Apply(info, mi, cfg)) return mi; // Tier 1 清单定制页（任务 15；groups 已重组）
        var defs = new List<ConfigDefinition>();
        if (cfg != null) { try { defs.AddRange(cfg.Keys); } catch { } } // Keys=现役 API（ConfigDefinitions 已过时）

        // section 分组沿用对方 cfg 节名（首现顺序）；空文件 = 仅启停形态（空 groups）
        var groups = new Dictionary<string, GroupInfo>();
        foreach (var def in defs)
        {
            if (def == null || string.IsNullOrEmpty(def.Key)) continue;
            ConfigEntryBase e = null;
            try { e = cfg[def]; } catch { }
            if (e == null) continue;
            if (!groups.TryGetValue(def.Section, out var g))
            {
                g = new GroupInfo { Title = def.Section };
                groups[def.Section] = g;
                mi.Groups.Add(g);
            }
            g.Items.Add(BuildTpItem(def, e));
        }
        if (mi.Groups.Count == 0)
            mi.Desc = "第三方 mod（Tier 0 自动识别）：无公开 cfg 配置，仅支持启用/停用（重启级）。";
        return mi;
    }

    /// <summary>逐 entry→Item：键名即标签（英文）、k=节.键、徽标一律 cfg、Description→note、
    /// AcceptableValueRange/List 反射范围/选项（无范围的 string→文本框 str）、步进兜底 整型1/浮点0.1。</summary>
    private static ItemInfo BuildTpItem(ConfigDefinition def, ConfigEntryBase e)
    {
        var ii = new ItemInfo
        {
            K = def.Section + "." + def.Key,
            L = def.Key,
            B = "cfg",
            T = TypeName(e.SettingType),
            V = e.BoxedValue,
            Note = e.Description?.Description,
        };
        ApplyRange(ii, e);
        if (ii.T == "sel" && ii.Opts == null && e.SettingType == typeof(string)) ii.T = "str";
        if (ii.St == null && ii.Min != null) ii.St = ii.T == "int" ? 1 : 0.1; // tp 无元数据步进来源
        return ii;
    }

    /// <summary>第三方启停真值源（任务 14）：本插件 cfg [Disabled]&lt;guid&gt; 标记名单取反
    /// （T6 时代机制回用；懒 Bind 幂等。任务 16 起换"退出时 dll 改名 .disabled"，名单退役）。</summary>
    private static bool TpEnabled(string guid)
    {
        try
        {
            var mk = TpMarker(guid);
            return mk == null || !mk.Value;
        }
        catch { return true; }
    }

    /// <summary>第三方停用标记 entry（懒 Bind，幂等；本插件 cfg [Disabled] 节，key=插件 GUID）。</summary>
    private static ConfigEntry<bool> TpMarker(string guid)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) return null;
        return self.Config.Bind("Disabled", guid, false,
            "第三方 mod 停用标记（true=面板已标记停用，重启级）。任务 16 起改为退出时 dll 改名 .disabled 真禁用。");
    }

    /// <summary>第三方标记名单写（重启级语义：本会话对方仍在运行，仅记录停用意图）。</summary>
    private static void SetTpEnabled(BepInEx.PluginInfo tp, bool enabled)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) { Log("toggle 拒绝：ModCenter 宿主未就绪"); return; }
        var mk = TpMarker(tp.Metadata.GUID);
        if (mk == null) { Log("toggle 拒绝：标记 entry 创建失败 " + tp.Metadata.GUID); return; }
        MarkDirty(self.Config);
        mk.Value = !enabled;
        Log($"toggle(tp) {tp.Metadata.Name} → {(enabled ? "启用" : "停用")}（[Disabled] 名单标记 {tp.Metadata.GUID}={!enabled}，重启级；任务 16 起换 dll .disabled 真禁用）");
    }

    /// <summary>第三方插件解析（完整 GUID / 插件名 / GUID 尾段，大小写不敏感；仅未豁免者）。</summary>
    private static BepInEx.PluginInfo FindTp(string guidOrName)
    {
        if (string.IsNullOrEmpty(guidOrName)) return null;
        Dictionary<string, PluginInfo> plugins = null;
        try { plugins = IL2CPPChainloader.Instance?.Plugins; } catch { }
        if (plugins == null) return null;
        foreach (var info in plugins.Values)
        {
            var md = info?.Metadata;
            if (md == null || string.IsNullOrEmpty(md.GUID)) continue;
            if (TpExempt(md.GUID, md.Name)) continue;
            if (string.Equals(md.GUID, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
            if (string.Equals(md.Name, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
            var tail = md.GUID.Substring(md.GUID.LastIndexOf('.') + 1);
            if (string.Equals(tail, guidOrName, StringComparison.OrdinalIgnoreCase)) return info;
        }
        return null;
    }

    /// <summary>第三方通用写：按 section/key 直取对方 entry（无登记表——只写实际存在的键，
    /// 指南 §二核心原则；面板发整展示键 k 原串时剥一次节前缀兜底）。写入触发其 ClampValue+
    /// SettingChanged 链（对方订阅即热响应），Save 走统一防抖。</summary>
    private static bool TrySetTp(BepInEx.PluginInfo tp, string section, string key, string raw)
    {
        ConfigFile cfg = null;
        try { if (tp.Instance is BasePlugin bp) cfg = bp.Config; } catch { }
        if (cfg == null) { Log("set 拒绝：" + tp.Metadata.Name + " 无 ConfigFile"); return false; }
        var e = Entry(cfg, section, key);
        if (e == null && !string.IsNullOrEmpty(section) && key != null
            && key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase))
            e = Entry(cfg, section, key.Substring(section.Length + 1));
        if (e == null) { Log($"set 拒绝：{tp.Metadata.Name} 无 entry {section}.{key}（只写实际存在的键）"); return false; }
        if (!ConvertValue(raw, e.SettingType, out var val)) { Log($"set 拒绝：'{raw}' 无法转为 {e.SettingType.Name}"); return false; }
        MarkDirty(cfg);
        e.BoxedValue = val;
        Log($"set(tp) {tp.Metadata.Name} {section}.{key} = {raw}");
        return true;
    }

    // ============ 底层查找 ============

    private static ConfigFile CfgOf(string guid)
    {
        if (CfgCache.TryGetValue(guid, out var c)) return c;
        try
        {
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            var info = plugins?.Values.FirstOrDefault(p => p.Metadata.GUID == guid);
            if (info?.Instance is BasePlugin bp && bp.Config != null) c = bp.Config;
        }
        catch { }
        CfgCache[guid] = c;
        return c;
    }

    internal static ConfigEntryBase Entry(ConfigFile cfg, string section, string key)
    {
        try { return cfg[new ConfigDefinition(section, key)]; }
        catch { return null; }
    }

    /// <summary>guid 或短名解析（完整 GUID / En / GUID 尾段，大小写不敏感；'RobotSpeed'、'robotspeed' 均可）。</summary>
    private static MetaMod Resolve(string guidOrName)
    {
        if (string.IsNullOrEmpty(guidOrName)) return null;
        foreach (var mm in Meta.Mods)
        {
            if (string.Equals(mm.Guid, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
            if (string.Equals(mm.En, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
            var tail = mm.Guid.Substring(mm.Guid.LastIndexOf('.') + 1);
            if (string.Equals(tail, guidOrName, StringComparison.OrdinalIgnoreCase)) return mm;
        }
        return null;
    }

    private static MetaItem FindItem(MetaMod mm, string section, string key)
    {
        foreach (var g in mm.Groups)
            foreach (var it in g.Items)
                if (Match(it, section, key)) return it;
        if (mm.OvrHost != null && Match(mm.OvrHost, section, key)) return mm.OvrHost;
        return null;
    }

    private static bool Match(MetaItem it, string section, string key)
    {
        if (!string.Equals(it.Section, section, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(it.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(it.K, key, StringComparison.OrdinalIgnoreCase)) return true; // 组合展示键
        return it.WKeys.Any(wk => string.Equals(wk, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ConvertValue(string raw, Type t, out object val)
    {
        val = null;
        if (raw == null) return false;
        try
        {
            if (t == typeof(float))
            {
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                val = f; return true;
            }
            if (t == typeof(double))
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
                val = d; return true;
            }
            if (t == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
                val = i; return true;
            }
            if (t == typeof(bool))
            {
                if (!bool.TryParse(raw, out var b)) return false;
                val = b; return true;
            }
            if (t == typeof(string)) { val = raw; return true; }
            if (t.IsEnum) { val = Enum.Parse(t, raw, true); return true; }
            // 其余整型兜底
            if (t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort))
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return false;
                val = System.Convert.ChangeType(l, t, CultureInfo.InvariantCulture); return true;
            }
            return false;
        }
        catch { return false; }
    }

    // ============ 消息泵（主线程，Ticker 每帧调） ============

    internal static void EnqueueOutboxJson(string json) => Inbox.Enqueue(json);

    internal static void Pump(float dt)
    {
        try
        {
            while (Inbox.TryDequeue(out var json)) HandleOutbox(json);
            if (Dirty.Count > 0)
            {
                _dirtyAge += dt;
                if (_dirtyAge >= FlushInterval) FlushDirty();
            }
        }
        catch (Exception e) { Log("warn 桥泵异常: " + e.Message); }
    }

    private static void HandleOutbox(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                // 逐条 try（任务 7，DESIGN §6.2 旧债收口）：批内消息互相独立，
                // 单条异常只弃该条——不弃同批后续（面板滑条+关闭常同批到达）。
                try
                {
                    var type = GetStr(el, "type");
                    switch (type)
                    {
                        case "MC_OPEN":
                            FocusTick.NotifyOpen(); // 任务 8：面板开⇒键盘门交给网页（FocusTick 0.3s 沿触发）
                            PushRender(BuildSnapshot().ToJson());
                            break;
                        case "MC_CLOSE": // 面板关闭：键盘门双还原（任务 8）+ 立即落盘全部脏 cfg（防抖收口）
                            FocusTick.NotifyClose();
                            FlushDirty();
                            break;
                        case "MC_SET":
                            HandleSet(GetStr(el, "guid"), GetStr(el, "section"), GetStr(el, "key"), el);
                            break;
                        case "MC_TOGGLE_MOD":
                            HandleToggle(GetStr(el, "guid"), el);
                            break;
                        case "MC_EVIDENCE": // 验证辅助（只读）：写证据文件供装机断言（见 DESIGN §6.4）
                            WriteEvidence(GetStr(el, "tag"));
                            break;
                        default:
                            if (!string.IsNullOrEmpty(type)) Log("忽略未知消息类型: " + type);
                            break;
                    }
                }
                catch (Exception e) { Log("warn 单条消息处理异常(" + (GetStr(el, "type") ?? "?") + "): " + e.Message); }
            }
        }
        catch (Exception e) { Log("warn 消息解析异常: " + e.Message); }
    }

    private static void HandleSet(string guid, string section, string key, JsonElement el)
    {
        var mm = Resolve(guid);
        var it = mm != null ? FindItem(mm, section, key) : null;
        var raw = el.TryGetProperty("value", out var v) ? RawOf(v) : null;
        var ok = TrySet(guid, section, key, raw);
        // 任务 14：第三方无登记项——回执 k=展示键（面板 key 发整串 k 已含节前缀，勿重复拼接）、
        // 徽标一律 cfg；任务 15：Tier 1 清单项回执徽标取清单声明（归一面板词汇，缺省 cfg）
        var k = it != null ? it.K : DisplayK(section, key);
        var badge = it != null ? it.Badge : (ThirdParty.BadgeOf(guid, DisplayK(section, key)) ?? "cfg");
        if (ok)
            PushEvent("{\"type\":\"MC_SET_OK\",\"k\":" + JsonW.Str(k) + ",\"badge\":" + JsonW.Str(badge) + "}");
        else
            PushEvent("{\"type\":\"MC_SET_FAIL\",\"k\":" + JsonW.Str(k)
                + ",\"err\":\"write rejected（详见 LogOutput）\"}");
    }

    /// <summary>展示键拼装：面板 key 一律发整展示键 k 原串（已含节前缀）——已带前缀直接用，否则补全。</summary>
    private static string DisplayK(string section, string key)
    {
        if (!string.IsNullOrEmpty(section) && key != null
            && key.StartsWith(section + ".", StringComparison.OrdinalIgnoreCase)) return key;
        return section + "." + key;
    }

    private static void HandleToggle(string guid, JsonElement el)
    {
        var enabled = el.TryGetProperty("enabled", out var v)
            && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));
        SetModEnabled(guid, enabled);
        PushRender(BuildSnapshot().ToJson()); // 回新快照（左列状态点/横幅即时反映）
    }

    private static string RawOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText()
    };

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---- C# → JS 推送（Root 上下文；panel.js 存根先存 __mcLastSnapshot/__mcLastEvent，任务 7 长成渲染） ----

    private static void PushRender(string snapshotJson) => PushJs("window.__mcRender&&window.__mcRender(" + snapshotJson + ")");

    private static void PushEvent(string eventJson) => PushJs("window.__mcEvent&&window.__mcEvent(" + eventJson + ")");

    private static void PushJs(string js)
    {
        var web = WebBridge.GetWebView();
        if (web != null) WebBridge.Exec(web, js);
    }

    // ============ Save 防抖 ============

    private static void MarkDirty(ConfigFile f)
    {
        if (!Dirty.ContainsKey(f))
        {
            Dirty[f] = f.SaveOnConfigSet;
            f.SaveOnConfigSet = false; // 滑条连写不再每写落盘（DESIGN §4.3-2 防抖前提）
        }
        if (_dirtyAge < 0f) _dirtyAge = 0f; // 首写起 3s 节流（写入不重置计时，长拖动也按 3s 批落盘）
    }

    internal static void FlushDirty()
    {
        if (Dirty.Count == 0) { _dirtyAge = -1f; return; }
        foreach (var kv in Dirty)
        {
            try { kv.Key.Save(); }
            catch (Exception e) { Log("warn cfg Save 失败: " + e.Message); }
            try { kv.Key.SaveOnConfigSet = kv.Value; }
            catch { }
        }
        Log($"cfg 落盘（防抖收口）: {Dirty.Count} 个文件");
        Dirty.Clear();
        _dirtyAge = -1f;
    }

    // ============ 验证辅助（只读；装机断言用，正常玩家不触发） ============

    // 断言锚点行（任务 13 热还原断言口径，与各 mod DESIGN 判别字段同源）：
    // 动作：9075 上厕所(QA) / 9098,9099,9100,9130,9131 机器人动作(RS；9130 与 QA 共管)
    //       / 1611 正在种植(QA BT) / 1615 浇水(QA) / 1915 空调吹风(QA)
    private static readonly int[] EvActionIds = { 9075, 9098, 9099, 9100, 9130, 9131, 1611, 1615, 1915 };
    // 包行：1/8/11 背包(BP) / 1061,1063 货舱(RS) / 110001,110009 柜子+材料架(CE尺寸)
    //       / 1004,115000 冰箱包(CE尺寸) / 3002,1023 冰箱包+门口袋(FC+CE载具)
    private static readonly int[] EvBagIds = { 1, 8, 11, 1061, 1063, 110001, 110009, 1004, 115000, 3002, 1023 };
    // FridgeChill 受管包（其 DefaultBagIds 同源；ColdRate 在 Config_Furniture 上按 BagId 过滤）
    private static readonly int[] EvFridgeBags = { 1004, 115000, 115001, 115005, 3002 };

    /// <summary>
    /// 写证据文件（任务 13 扩为多表断言快照，全部只读）：RobotSpeed 移速倍率 entry 值 +
    /// Config_RobotAttribute.MoveSpeed 全行 + 判别动作行 During + 判别包行 Size/Burden +
    /// 冰箱家具 ColdRate 统计（受管 16 行中已被压低行数/最小值）+ Talent 受限行数与花瓶 325
    /// 等级 + 储物柜白名单集合计数。装机断言在 toggle 前后各拍一轮对照（表/trace/cfg 三源之一）。
    /// </summary>
    private static void WriteEvidence(string tag)
    {
        try
        {
            var cfg = CfgOf("com.local.survivallog.robotspeed");
            var e = cfg != null ? Entry(cfg, "机器人提速", "移速倍率") : null;
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:HH:mm:ss.fff}] tag={tag}")
              .Append(" rs=" + (e != null ? System.Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture) : "?"))
              .Append(" ms=" + BridgeProbe.Table())
              .Append(" acts=" + EvActions())
              .Append(" bags=" + EvBags())
              .Append(" cold=" + EvCold())
              .Append(" talent=" + EvTalent())
              .Append(" ce=" + EvCeWhitelist());
            System.IO.File.AppendAllText(EvidencePath, sb + "\n");
            Log("evidence: " + sb);
        }
        catch (Exception ex) { Log("warn evidence 失败: " + ex.Message); }
    }

    private static GameCore.HotUpdate.ConfigManager EvCm()
    {
        if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
            return null;
        return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
    }

    private static string EvActions()
    {
        try
        {
            var d = EvCm()?._Config_Action_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            var sb = new StringBuilder();
            foreach (var id in EvActionIds)
            {
                sb.Append(id).Append('=');
                try { sb.Append(d.TryGetValue(id, out var a) && a != null ? a.During.ToString("0.#", CultureInfo.InvariantCulture) : "-").Append(' '); }
                catch { sb.Append("? "); }
            }
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    private static string EvBags()
    {
        try
        {
            var d = EvCm()?._Config_Bag_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            var sb = new StringBuilder();
            foreach (var id in EvBagIds)
            {
                sb.Append(id).Append('=');
                try
                {
                    if (!d.TryGetValue(id, out var b) || b == null) { sb.Append("- "); continue; }
                    var s = b.Size;
                    sb.Append(s != null && s.Count >= 2 ? s[0] + "x" + s[1] : "?").Append('/');
                    sb.Append(b.Burden).Append(' ');
                }
                catch { sb.Append("? "); }
            }
            return sb.ToString().Trim();
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>冰箱家具 ColdRate 统计：受管行数中已低于原生 0.2 的行数与最小值（FC 生效=16/16@0.1，还原=0/16@0.2）。</summary>
    private static string EvCold()
    {
        try
        {
            var d = EvCm()?._Config_Furniture_Dict;
            if (d == null || d.Count == 0) return "未就绪";
            int total = 0, chilled = 0;
            float min = float.MaxValue;
            var bags = new HashSet<int>(EvFridgeBags);
            foreach (var kv in d)
            {
                var f = kv.Value;
                if (f == null) continue;
                try
                {
                    if (f.ColdRate <= 0f || !bags.Contains(f.BagId)) continue;
                    total++;
                    if (f.ColdRate < 0.199f) chilled++;
                    if (f.ColdRate < min) min = f.ColdRate;
                }
                catch { }
            }
            return total == 0 ? "0" : $"{chilled}/{total}@{min.ToString("0.###", CultureInfo.InvariantCulture)}";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>Talent 受限行数（PlayerSelectId 非空=官方 123；ClearPool 生效=77）+ 花瓶 325 craft/show 等级。</summary>
    private static string EvTalent()
    {
        try
        {
            var cm = EvCm();
            if (cm == null) return "未就绪";
            var td = cm._Config_Talent_Dict;
            int restricted = -1;
            if (td != null && td.Count > 0)
            {
                restricted = 0;
                foreach (var kv in td)
                {
                    var r = kv.Value;
                    if (r == null) continue;
                    try { var p = r.PlayerSelectId; if (p != null && p.Count > 0) restricted++; }
                    catch { }
                }
            }
            var pd = cm._Config_ProductionList_Dict;
            string vase = "?";
            if (pd != null && pd.TryGetValue(325, out var v) && v != null)
            {
                try { vase = v.craft_level + "/" + v.show_level; } catch { }
            }
            return $"restricted={restricted} vase={vase}";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    /// <summary>储物柜白名单集合计数（官方 1；CabinetEverywhere 注入后 710）。</summary>
    private static string EvCeWhitelist()
    {
        try
        {
            var set = GameCore.HotUpdate.ToolCabinetConfig.CraftSourceFurnitureIds;
            return set != null ? set.Count.ToString(CultureInfo.InvariantCulture) : "null";
        }
        catch (Exception e) { return "异常:" + e.Message; }
    }

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}
