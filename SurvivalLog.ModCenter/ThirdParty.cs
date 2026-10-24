using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx.Configuration;

namespace SurvivalLog.ModCenter;

/// <summary>
/// Tier 1 声明式清单（任务 15）：第三方 mod 在其插件程序集内嵌 <c>modcenter.json</c>
/// （mcSpec 1，字段表 = docs/modcenter-integration-guide.md §三，行为规范单源）获得
/// 中文定制页。本类是 Tier 0 通用页（ConfigBridge.BuildTp，任务 14）之上的发现与合并层：
///
/// - 发现：插件实例 <c>GetType().Assembly.GetManifestResourceNames()</c> 匹配
///   <c>modcenter.json</c>（精确名或 <c>*.modcenter.json</c> 后缀——指南 csproj 片段不带
///   LogicalName 时资源实名为 <c>根命名空间.modcenter.json</c>，两种都要认）；
///   同一程序集多份清单**取第一个**，其余忽略并记日志（指南 §三行为约定）。
/// - 校验容错（指南承诺"绝不报错"）：JSON 解析失败 / mcSpec 缺失或不认识（≠1）/
///   groups 整段缺失 → **整单忽略降级 Tier 0**；清单键在实际 cfg 不存在 → 该项隐藏
///   （容忍版本差异）；display/summary 缺项逐字段回退 Tier 0 值；组无 title / 项无 key
///   → 跳过该组/该项并记日志。
/// - 合并渲染：display/summary 覆盖显示名与简介；groups 全量重组（Tier 0"cfg 节名分组/
///   键名即标签"被清单替代——**清单没列的键不出现**）；item 的 label/note/badge/
///   min-max-step/order 逐项覆盖 Tier 0 推断值（范围覆盖优先于 AcceptableValueRange 反射，
///   缺省回落反射值——指南 §三字段表）。badge 词汇表归一：指南 now/reload/cold/缺省 cfg →
///   面板契约 now/reload/reboot/cfg（Payload.ItemInfo.B 口径，cold→reboot）。
/// - softDisable：清单声明暂存（<see cref="SoftDisableOf"/>），任务 16 启停两层语义消费。
/// - 写路由零改动：Tier 1 项展示键仍是 节.键（面板发整串 k，TrySetTp 剥前缀兜底既有链）；
///   MC_SET_OK 回执 badge 经 <see cref="BadgeOf"/> 取清单徽标（缺省 cfg）。
/// 线程纪律：只在 ConfigBridge.Pump 主线程（快照构建）被调；清单逐插件解析一次入缓存。
/// </summary>
internal static class ThirdParty
{
    /// <summary>当前认识的协议版本（指南 §七接口冻结：mcSpec 1）。</summary>
    private const int McSpecSupported = 1;

    // 清单缓存（会话内不变）：guid → 解析成功的清单；_none = 已查过且无有效清单（防重复扫）
    private static readonly Dictionary<string, McManifest> Cache = new();
    private static readonly HashSet<string> None = new();

    private sealed class McManifest
    {
        internal string Id, Display, Summary;
        internal bool SoftDisable;
        internal List<McGroup> Groups = new();
        internal Dictionary<string, string> Badges = new(); // 展示键 k → 归一徽标（Apply 渲染时填充，MC_SET_OK 回执查表）
    }

    private sealed class McGroup
    {
        internal string Title;
        internal double? Order;
        internal List<McItem> Items = new();
    }

    private sealed class McItem
    {
        internal string Key, Label, Note, Badge; // Key = "Section:Key"（指南格式）
        internal double? Min, Max, Step, Order;
    }

    // ============ 对外契约 ============

    /// <summary>
    /// 把清单合并进第三方通用页 ModInfo（ConfigBridge.BuildTpMod 调用）：
    /// 无有效清单（返回 false）时调用方保持 Tier 0 形态。cfg 允许 null（无 ConfigFile 的
    /// mod 也能有清单——全部项隐藏，落仅启停形态；display/summary 照常生效）。
    /// </summary>
    internal static bool Apply(BepInEx.PluginInfo info, ModInfo mi, ConfigFile cfg)
    {
        var md = info.Metadata;
        var man = ManifestOf(info);
        if (man == null) return false;
        mi.Tier = 1; // 快照标记（面板/fixture 判别 Tier 1 定制页；Payload 增量字段）
        if (!string.IsNullOrEmpty(man.Display)) mi.Name = man.Display;
        if (!string.IsNullOrEmpty(man.Summary)) mi.Desc = man.Summary;
        if (!string.IsNullOrEmpty(man.Id) && !string.Equals(man.Id, md.GUID, StringComparison.OrdinalIgnoreCase))
            Log($"清单 id '{man.Id}' 与插件 GUID '{md.GUID}' 不一致（指南建议一致；按内嵌程序集归属继续应用）");

        int hidden = 0;
        foreach (var g in man.Groups.OrderBy(x => x.Order)) // order 权重小者在前（缺省 0；LINQ 稳定排序）
        {
            var gi = new GroupInfo { Title = g.Title };
            foreach (var it in g.Items.OrderBy(x => x.Order))
            {
                var e = Split(it.Key, out var section, out var key)
                    ? (cfg != null ? ConfigBridge.Entry(cfg, section, key) : null)
                    : null;
                if (e == null) { hidden++; continue; } // 清单键不在实际 cfg → 隐藏（容忍版本差异）
                var ii = BuildItem(it, section, key, e);
                gi.Items.Add(ii);
                man.Badges[ii.K] = ii.B; // 渲染时填回执查表（HandleSet→BadgeOf；未列/隐藏项缺省 cfg）
            }
            if (gi.Items.Count > 0) mi.Groups.Add(gi); // 整组项全隐藏 → 空组不渲染
        }
        if (hidden > 0)
            Log($"清单(tp) {md.GUID}: {hidden} 个清单键不在其 cfg（隐藏，容忍版本差异）");
        return true;
    }

    /// <summary>清单声明的 softDisable（任务 16 启停两层语义消费；无清单=false）。</summary>
    internal static bool SoftDisableOf(string guid) =>
        Cache.TryGetValue(guid ?? "", out var m) && m != null && m.SoftDisable;

    /// <summary>清单徽标（展示键 k → 归一面板徽标；Tier 0 / 未列项 → null，回执缺省 cfg）。</summary>
    internal static string BadgeOf(string guid, string displayK) =>
        displayK != null && Cache.TryGetValue(guid ?? "", out var m) && m != null
            && m.Badges.TryGetValue(displayK, out var b) ? b : null;

    // ============ Tier 2 深度对接（任务 16；指南 §四，Contracts 见 Contracts/ModCenterIntegration.cs） ============

    /// <summary>
    /// 第三方运行时状态（QueryStates 回报，duck-typing 读出后归一为托管副本；
    /// 空串 Key 代表整个 mod，State 只认 ok——非 ok 一律按 failed 展示 Reason）。
    /// </summary>
    internal sealed class McState
    {
        internal string Key, State, Reason;
    }

    /// <summary>发现的 Tier 2 约定类型句柄（反射按名 duck-typing——不同程序集各自编入的
    /// 接口类型不可强转，禁止 cast；Degraded=OnEnabledChanged 曾返回 false，本会话降级重启级）。</summary>
    private sealed class T2Integration
    {
        internal System.Reflection.MethodInfo OnEnabled;  // static bool OnEnabledChanged(bool)
        internal System.Reflection.MethodInfo Query;      // static Xxx QueryStates()（可选）
        internal bool Degraded;
    }

    private static readonly Dictionary<string, T2Integration> T2Cache = new();
    private static readonly HashSet<string> T2None = new();

    /// <summary>
    /// 约定类型发现：插件程序集内 <c>ModCenterIntegration</c>（先精确全局名，后按简单名兜底扫描——
    /// 允许作者套命名空间），要求 public static <c>bool OnEnabledChanged(bool)</c>；
    /// 静态属性 <c>McSpec</c> 在场且 ≠1 → 不认识，降级 Tier 1/0（指南 §七接口冻结）。
    /// QueryStates 可选。逐插件解析一次入缓存；任何缺失/异常都只是"没有 Tier 2"，绝不报错。
    /// </summary>
    private static T2Integration T2Of(BepInEx.PluginInfo info)
    {
        var guid = info.Metadata?.GUID;
        if (string.IsNullOrEmpty(guid)) return null;
        if (T2Cache.TryGetValue(guid, out var hit)) return hit;
        if (T2None.Contains(guid)) return null;

        T2Integration t2 = null;
        try
        {
            var asm = info.Instance.GetType().Assembly;
            var t = asm.GetType("ModCenterIntegration", false);
            if (t == null)
            {
                foreach (var ty in asm.GetTypes())
                    if (ty.Name == "ModCenterIntegration") { t = ty; break; }
            }
            if (t != null)
            {
                var on = t.GetMethod("OnEnabledChanged",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                    new[] { typeof(bool) }, null);
                if (on == null || on.ReturnType != typeof(bool))
                    Log($"Tier2(tp) {guid}: 类型 ModCenterIntegration 在场但缺 static bool OnEnabledChanged(bool)——按 Tier 1/0 对待");
                else
                {
                    var specProp = t.GetProperty("McSpec",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (specProp != null)
                    {
                        var specOk = false;
                        try { specOk = Convert.ToInt32(specProp.GetValue(null), CultureInfo.InvariantCulture) == McSpecSupported; }
                        catch { }
                        if (!specOk)
                        {
                            Log($"Tier2(tp) {guid}: McSpec 不识别——按 Tier 1/0 对待（接口冻结：mcSpec 1）");
                            t = null;
                        }
                    }
                    if (t != null)
                    {
                        t2 = new T2Integration
                        {
                            OnEnabled = on,
                            Query = t.GetMethod("QueryStates",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null,
                                Type.EmptyTypes, null),
                        };
                        Log($"Tier2(tp) {guid}: 约定类型命中（OnEnabledChanged 就绪，QueryStates {(t2.Query != null ? "就绪" : "缺席(可选)")}）——软停回调生效");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {guid}: 发现异常（按 Tier 1/0 对待）: {e.Message}");
            t2 = null;
        }
        if (t2 == null) T2None.Add(guid);
        else T2Cache[guid] = t2;
        return t2;
    }

    /// <summary>软停能力（面板两层语义判据，任务 16）：Tier 2 约定在场且未降级，或清单声明 softDisable。</summary>
    internal static bool SoftOf(BepInEx.PluginInfo info)
    {
        var t2 = T2Of(info);
        return (t2 != null && !t2.Degraded) || SoftDisableOf(info.Metadata?.GUID);
    }

    /// <summary>
    /// 启停回调（主线程 Pump 调）：enabled=false 对方应立即还原全部行为，true 恢复。
    /// 返回 true=对方已处理（软停语义）；无约定/异常/返回 false → false（调用方降级重启级语义），
    /// 返回 false 时本会话标记 Degraded（后续快照 Soft 同步回落）。
    /// </summary>
    internal static bool FireEnabledChanged(BepInEx.PluginInfo info, bool enabled)
    {
        var t2 = T2Of(info);
        if (t2 == null || t2.Degraded && !enabled) return false; // 已降级后不再打扰；enable 重试交给下次真启用
        try
        {
            var r = t2.OnEnabled.Invoke(null, new object[] { enabled });
            var ok = r is bool b && b;
            if (!ok && !enabled)
            {
                t2.Degraded = true;
                Log($"Tier2(tp) {info.Metadata?.GUID}: OnEnabledChanged(false) 返回 false——本会话降级重启级语义");
            }
            return ok;
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {info.Metadata?.GUID}: OnEnabledChanged({enabled}) 异常（降级重启级）: {e.Message}");
            if (!enabled) t2.Degraded = true;
            return false;
        }
    }

    /// <summary>状态回报（快照构建时调；无约定/异常/缺席 → null）。上限 64 项防病态清单。</summary>
    internal static List<McState> QueryStatesOf(BepInEx.PluginInfo info)
    {
        var t2 = T2Of(info);
        if (t2?.Query == null) return null;
        try
        {
            var r = t2.Query.Invoke(null, Array.Empty<object>());
            if (r is not System.Collections.IEnumerable en) return null;
            var list = new List<McState>();
            foreach (var o in en)
            {
                if (o == null || list.Count >= 64) continue;
                var ty = o.GetType();
                string P(string name) => ty.GetProperty(name)?.GetValue(o) as string;
                list.Add(new McState { Key = P("Key") ?? "", State = P("State") ?? "ok", Reason = P("Reason") ?? "" });
            }
            return list;
        }
        catch (Exception e)
        {
            Log($"Tier2(tp) {info.Metadata?.GUID}: QueryStates 异常（忽略）: {e.Message}");
            return null;
        }
    }

    // ============ 清单发现与解析 ============

    private static McManifest ManifestOf(BepInEx.PluginInfo info)
    {
        var guid = info.Metadata?.GUID;
        if (string.IsNullOrEmpty(guid)) return null;
        if (Cache.TryGetValue(guid, out var hit)) return hit;
        if (None.Contains(guid)) return null;

        var res = FindResource(info);
        var man = res != null ? Parse(res.Stream, guid) : null;
        if (man == null) None.Add(guid);
        else
        {
            Cache[guid] = man;
            Log($"清单(tp) {guid}: 命中资源 '{res.Name}'"
                + (res.Dups > 1 ? $"（同程序集 {res.Dups} 份 modcenter.json，取第一个，其余忽略）" : "")
                + $"——mcSpec {McSpecSupported} 定制页生效（{man.Groups.Count} 组）");
        }
        return man;
    }

    private sealed class ResInfo
    {
        internal string Name;
        internal Stream Stream;
        internal int Dups;
    }

    /// <summary>资源发现：精确名 modcenter.json 或 *.modcenter.json 后缀（均忽略大小写），
    /// 多份取第一个（GetManifestResourceNames 数组序）。</summary>
    private static ResInfo FindResource(BepInEx.PluginInfo info)
    {
        try
        {
            var asm = info.Instance.GetType().Assembly;
            string first = null;
            int dups = 0;
            foreach (var n in asm.GetManifestResourceNames())
            {
                var isMc = string.Equals(n, "modcenter.json", StringComparison.OrdinalIgnoreCase)
                    || (n.Length > 15 && n.EndsWith(".modcenter.json", StringComparison.OrdinalIgnoreCase));
                if (!isMc) continue;
                dups++;
                if (first == null) first = n;
            }
            if (first == null) return null;
            var s = asm.GetManifestResourceStream(first);
            if (s == null) return null;
            return new ResInfo { Name = first, Stream = s, Dups = dups };
        }
        catch (Exception e)
        {
            Log($"warn 清单(tp) {info.Metadata?.GUID} 资源发现异常: {e.Message}");
            return null;
        }
    }

    /// <summary>解析与校验（JsonDocument，套件入向解析同款纪律）。任何不合格 → null（降级 Tier 0）。</summary>
    private static McManifest Parse(Stream stream, string guid)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { Log($"清单(tp) {guid} 根非对象，忽略"); return null; }
            if (!root.TryGetProperty("mcSpec", out var specEl)
                || specEl.ValueKind != JsonValueKind.Number
                || !specEl.TryGetInt32(out var spec)
                || spec != McSpecSupported)
            {
                Log($"清单(tp) {guid} mcSpec 不识别（{Raw(specEl)}）——整单忽略，按 Tier 0 通用页兜底");
                return null;
            }
            var man = new McManifest
            {
                Id = GetS(root, "id"),
                Display = GetS(root, "display"),
                Summary = GetS(root, "summary"),
                SoftDisable = root.TryGetProperty("softDisable", out var sd) && sd.ValueKind == JsonValueKind.True,
            };
            if (!root.TryGetProperty("groups", out var gsEl) || gsEl.ValueKind != JsonValueKind.Array)
            {
                Log($"清单(tp) {guid} 缺 groups 段——整单忽略，按 Tier 0 通用页兜底");
                return null;
            }
            foreach (var ge in gsEl.EnumerateArray())
            {
                if (ge.ValueKind != JsonValueKind.Object) continue;
                var title = GetS(ge, "title");
                if (string.IsNullOrEmpty(title)) { Log($"清单(tp) {guid} 组缺 title，跳过该组"); continue; }
                var g = new McGroup { Title = title, Order = GetN(ge, "order") };
                if (ge.TryGetProperty("items", out var itsEl) && itsEl.ValueKind == JsonValueKind.Array)
                    foreach (var ie in itsEl.EnumerateArray())
                    {
                        if (ie.ValueKind != JsonValueKind.Object) continue;
                        var key = GetS(ie, "key");
                        if (string.IsNullOrEmpty(key)) { Log($"清单(tp) {guid} '{title}' 有项缺 key，跳过该项"); continue; }
                        g.Items.Add(new McItem
                        {
                            Key = key, Label = GetS(ie, "label"), Note = GetS(ie, "note"), Badge = GetS(ie, "badge"),
                            Min = GetN(ie, "min"), Max = GetN(ie, "max"), Step = GetN(ie, "step"),
                            Order = GetN(ie, "order"),
                        });
                    }
                man.Groups.Add(g);
            }
            return man;
        }
        catch (Exception e)
        {
            Log($"清单(tp) {guid} 解析失败（{e.Message}）——整单忽略，按 Tier 0 通用页兜底");
            return null;
        }
    }

    // ============ 合并细节 ============

    /// <summary>清单项 → ItemInfo：类型/当前值/选项来自 cfg entry 反射（Tier 0 同源），
    /// label/note/badge/min-max-step 按清单覆盖（缺省回落 Tier 0 推断值）。</summary>
    private static ItemInfo BuildItem(McItem it, string section, string key, ConfigEntryBase e)
    {
        var ii = new ItemInfo
        {
            K = section + "." + key,
            L = string.IsNullOrEmpty(it.Label) ? key : it.Label, // 缺 label 回落键名（Tier 0 同形）
            B = NormBadge(it.Badge),
            T = ConfigBridge.TypeName(e.SettingType),
            V = e.BoxedValue,
            Note = it.Note ?? e.Description?.Description, // 清单定制 note 优先于 entry Description
        };
        ConfigBridge.ApplyRange(ii, e); // AcceptableValueRange/List 反射（缺省范围来源）
        if (it.Min != null) ii.Min = it.Min; // 清单范围覆盖反射值（指南：缺省才用 AcceptableValueRange）
        if (it.Max != null) ii.Max = it.Max;
        if (ii.T == "sel" && ii.Opts == null && e.SettingType == typeof(string)) ii.T = "str"; // Tier 0 同款
        if (it.Step != null) ii.St = it.Step;
        else if (ii.St == null && ii.Min != null) ii.St = ii.T == "int" ? 1 : 0.1; // tp 步进兜底同 Tier 0
        return ii;
    }

    /// <summary>徽标归一：指南 now/reload/cold（§六）→ 面板契约 now/reload/reboot（cold=重启后）；
    /// 缺省/未知 → cfg（诚实缺省，指南 §六）。</summary>
    private static string NormBadge(string b)
    {
        if (string.IsNullOrEmpty(b)) return "cfg";
        return b.Trim().ToLowerInvariant() switch
        {
            "now" => "now",
            "reload" => "reload",
            "cold" => "reboot",
            "cfg" => "cfg",
            _ => "cfg",
        };
    }

    /// <summary>拆 "Section:Key"（首个冒号；无冒号=空节全串为键——容忍裸键写法）。</summary>
    private static bool Split(string k, out string section, out string key)
    {
        section = ""; key = "";
        if (string.IsNullOrEmpty(k)) return false;
        var ix = k.IndexOf(':');
        if (ix < 0) { key = k; return true; }
        if (ix == 0 || ix == k.Length - 1) return false; // "Section:" / ":Key" 畸形 → 隐藏
        section = k.Substring(0, ix);
        key = k.Substring(ix + 1);
        return true;
    }

    private static string GetS(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetN(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static string Raw(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number || e.ValueKind == JsonValueKind.String ? e.GetRawText() : "缺失";

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}
