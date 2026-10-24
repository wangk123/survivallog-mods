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
/// 职责：快照（BuildSnapshot）/ 写（TrySet，联动 writeKeys 等值）/ 启停（SetModEnabled，
/// 任务 6 落本插件 cfg [Disabled] 名单，任务 13 接各 mod General.Enabled）/ 消息分发
/// （MC_OPEN→__mcRender 快照；MC_SET→TrySet→__mcEvent MC_SET_OK；MC_TOGGLE_MOD→回新快照）
/// / Save 防抖（首写 SaveOnConfigSet=false，3s 节流 + MC_CLOSE 立即落盘后恢复原值）。
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

    /// <summary>全量快照：{self:{ver}, mods:[ModInfo], tp:[]}（tp 任务 14 填）。</summary>
    internal static Snapshot BuildSnapshot()
    {
        var snap = new Snapshot { Self = new SelfInfo { Ver = ModCenterPlugin.Version } };
        foreach (var mm in Meta.Mods)
        {
            var cfg = CfgOf(mm.Guid);
            var mi = new ModInfo
            {
                Guid = mm.Guid, Name = mm.Name, En = mm.En, Desc = mm.Desc,
                // 插件不在链上（dll 级缺失）视为停用；在场则以 [Disabled] 名单取反
                Enabled = cfg != null && !(Marker(mm.Guid)?.Value ?? true)
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
        return snap;
    }

    /// <summary>
    /// 写一项：guid 可用完整 GUID 或短名（en，大小写不敏感，如 'RobotSpeed'）；
    /// key 可为主键、任一 writeKeys 或组合展示键（联动项）。类型按 entry.SettingType 转换
    /// （float/int/bool/string/enum），联动项 writeKeys 逐键等值写；写前标脏（关该文件自动落盘）。
    /// </summary>
    internal static bool TrySet(string guidOrName, string section, string key, string raw)
    {
        var mm = Resolve(guidOrName);
        if (mm == null) { Log("set 拒绝：未知插件 " + guidOrName); return false; }
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

    /// <summary>启停：任务 6 落本插件 cfg [Disabled]&lt;guid&gt; 名单（true=停用）；任务 13 起改写各 mod General.Enabled。</summary>
    internal static void SetModEnabled(string guidOrName, bool enabled)
    {
        var mm = Resolve(guidOrName);
        if (mm == null) { Log("toggle 拒绝：未知插件 " + guidOrName); return; }
        var mk = Marker(mm.Guid);
        var self = ModCenterPlugin.Instance;
        if (self == null) return;
        MarkDirty(self.Config);
        mk.Value = !enabled;
        Log($"toggle {mm.En} → {(enabled ? "启用" : "停用")}（名单标记 {mm.Guid}={!enabled}）");
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
            // 范围/白名单反射合入（元数据不写死为准；AcceptableValueRange/List 是 closed generic，走 GetType 反射）
            var av = e1.Description != null ? e1.Description.AcceptableValues : null;
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
            if (ii.Opts == null && e1.SettingType.IsEnum)
                ii.Opts = Enum.GetNames(e1.SettingType).ToList();
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
        ii.Note = it.Note ?? (it.Linked ? LinkedNote(cfg, it) : null);
        return ii;
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

    private static string TypeName(Type t)
    {
        if (t == typeof(float) || t == typeof(double)) return "float";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
            return "int";
        if (t == typeof(bool)) return "bool";
        return "sel"; // enum / string / 其他 → 下拉
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

    private static ConfigEntryBase Entry(ConfigFile cfg, string section, string key)
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

    /// <summary>启停名单 entry（懒 Bind，幂等；写在本插件 cfg [Disabled] 节，key=插件 GUID）。</summary>
    private static ConfigEntry<bool> Marker(string guid)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) return null;
        return self.Config.Bind("Disabled", guid, false,
            "ModCenter 停用名单（true=面板已停用该 mod）。任务 13 起改写各 mod General.Enabled，此前仅面板状态源。");
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
                var type = GetStr(el, "type");
                switch (type)
                {
                    case "MC_OPEN":
                        PushRender(BuildSnapshot().ToJson());
                        break;
                    case "MC_CLOSE": // 面板关闭：立即落盘全部脏 cfg（防抖收口）
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
        }
        catch (Exception e) { Log("warn 消息解析异常: " + e.Message); }
    }

    private static void HandleSet(string guid, string section, string key, JsonElement el)
    {
        var mm = Resolve(guid);
        var it = mm != null ? FindItem(mm, section, key) : null;
        var raw = el.TryGetProperty("value", out var v) ? RawOf(v) : null;
        var ok = TrySet(guid, section, key, raw);
        if (ok)
            PushEvent("{\"type\":\"MC_SET_OK\",\"k\":" + JsonW.Str(it.K) + ",\"badge\":" + JsonW.Str(it.Badge) + "}");
        else
            PushEvent("{\"type\":\"MC_SET_FAIL\",\"k\":" + JsonW.Str(it != null ? it.K : section + "." + key)
                + ",\"err\":\"write rejected（详见 LogOutput）\"}");
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

    /// <summary>写证据文件（RobotSpeed 移速倍率 entry 值 + Config_RobotAttribute 内存表快照，表读复用 BridgeProbe）。</summary>
    private static void WriteEvidence(string tag)
    {
        try
        {
            var cfg = CfgOf("com.local.survivallog.robotspeed");
            var e = cfg != null ? Entry(cfg, "机器人提速", "移速倍率") : null;
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] tag={tag} entry={(e != null ? System.Convert.ToDouble(e.BoxedValue, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture) : "?")}"
                + " table=" + BridgeProbe.Table();
            System.IO.File.AppendAllText(EvidencePath, line + "\n");
            Log("evidence: " + line);
        }
        catch (Exception ex) { Log("warn evidence 失败: " + ex.Message); }
    }

    private static void Log(string msg) => ModCenterPlugin.Log(msg);
}
