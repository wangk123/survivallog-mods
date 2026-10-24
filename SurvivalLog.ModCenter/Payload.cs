using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 面板数据契约（任务 6 定稿，任务 7 UI 逐字消费，签名不得偏差）：
/// Snapshot = {self:{ver}, mods:[ModInfo], tp:[ModInfo]}（tp 第三方通用页，任务 14 填，本任务空数组占位）；
/// ModInfo = {guid,name,en,desc,enabled,groups:[{title,items:[Item]}]}，ovr 类 mod（动作加速）另挂 ovr:[{id,ms}]；
/// Item = {k,l,t,v,min?,max?,st?,b,note?,opts?}——与原型 v4 MODS 数组同名同义
/// （docs/superpowers/specs/2026-10-24-modcenter-prototype.html）。
/// 序列化手写 StringBuilder（AOT 零反射；套件 JSON 纪律同 EndlessGuard「零外部依赖」，
/// 入向解析用 JsonDocument——EndlessGuard ScavengePlans.cs 同款实绩 API）。
/// </summary>
public sealed class Snapshot
{
    public SelfInfo Self;
    public List<ModInfo> Mods = new();
    public List<ModInfo> Tp = new(); // 第三方 Tier 0/1（任务 14）

    public string ToJson()
    {
        var sb = new StringBuilder(8192);
        sb.Append("{\"self\":{\"ver\":").Append(JsonW.Str(Self?.Ver)).Append("},\"mods\":[");
        for (var i = 0; i < Mods.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Mods[i].Write(sb);
        }
        sb.Append("],\"tp\":[");
        for (var i = 0; i < Tp.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Tp[i].Write(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

public sealed class SelfInfo
{
    public string Ver; // ModCenter 版本（跟 suite version.json，作者定号）
}

public sealed class ModInfo
{
    public string Guid;  // BepInEx 插件 GUID（面板回传 MC_SET/MC_TOGGLE_MOD 用它路由）
    public string Name;  // 面板显示名（中文）
    public string En;    // 内部名（原型 id 的对应物，兼容短名路由）
    public string Desc;  // 一句话功能简介
    public bool Enabled; // 启停状态（任务 6 = ModCenter cfg [Disabled] 名单取反；任务 13 接各 mod General.Enabled）
    public List<GroupInfo> Groups = new();
    public List<OvrInfo> Ovr; // 仅动作加速挂（其他.Overrides 解析结果）；null 不输出该字段

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"guid\":").Append(JsonW.Str(Guid))
          .Append(",\"name\":").Append(JsonW.Str(Name))
          .Append(",\"en\":").Append(JsonW.Str(En))
          .Append(",\"desc\":").Append(JsonW.Str(Desc))
          .Append(",\"enabled\":").Append(Enabled ? "true" : "false")
          .Append(",\"groups\":[");
        for (var i = 0; i < Groups.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Groups[i].Write(sb);
        }
        sb.Append(']');
        if (Ovr != null)
        {
            sb.Append(",\"ovr\":[");
            for (var i = 0; i < Ovr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Ovr[i].Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"ms\":").Append(Ovr[i].Ms.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
        }
        sb.Append('}');
    }
}

public sealed class GroupInfo
{
    public string Title; // 分组标题（显示用；≠cfg 节名）
    public List<ItemInfo> Items = new();

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"title\":").Append(JsonW.Str(Title)).Append(",\"items\":[");
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            Items[i].Write(sb);
        }
        sb.Append("]}");
    }
}

public sealed class ItemInfo
{
    public string K;     // cfg 地址展示串（联动项 = "节.键A + 键B"，展示用；写路由以 Meta.WriteKeys 为准）
    public string L;     // 面板中文名
    public string T;     // float / int / bool / sel
    public object V;     // 当前值（快照时从 ConfigEntry.BoxedValue 读；entry 缺失用 Meta 兜底）
    public double? Min;  // 范围：优先反射 AcceptableValueRange，缺失用 Meta 兜底（bool/sel 为 null 不输出）
    public double? Max;
    public double? St;  // 滑条步进：AcceptableValue 不携带步进，永远 Meta 兜底
    public string B;     // 徽标：now(⚡≤5s) / reload(📁读档后) / reboot(🔄重启后) / cfg(⚙)
    public string Note;  // 补充说明（联动项含动态"当前 宽w×高h"）
    public List<string> Opts; // sel 选项（enum 名/白名单值/Meta 兜底）

    internal void Write(StringBuilder sb)
    {
        sb.Append("{\"k\":").Append(JsonW.Str(K))
          .Append(",\"l\":").Append(JsonW.Str(L))
          .Append(",\"t\":").Append(JsonW.Str(T))
          .Append(",\"v\":").Append(WriteValue());
        if (Min != null) sb.Append(",\"min\":").Append(JsonW.Num(Min.Value));
        if (Max != null) sb.Append(",\"max\":").Append(JsonW.Num(Max.Value));
        if (St != null) sb.Append(",\"st\":").Append(JsonW.Num(St.Value));
        sb.Append(",\"b\":").Append(JsonW.Str(B));
        if (Note != null) sb.Append(",\"note\":").Append(JsonW.Str(Note));
        if (Opts != null && Opts.Count > 0)
        {
            sb.Append(",\"opts\":[");
            for (var i = 0; i < Opts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonW.Str(Opts[i]));
            }
            sb.Append(']');
        }
        sb.Append('}');
    }

    private string WriteValue()
    {
        if (V == null) return "null";
        switch (T)
        {
            case "bool":
                return (V is bool b) && b ? "true" : "false";
            case "float":
            case "int":
                return JsonW.Num(System.Convert.ToDouble(V, CultureInfo.InvariantCulture));
            default: // sel（string/enum）
                return JsonW.Str(System.Convert.ToString(V, CultureInfo.InvariantCulture));
        }
    }
}

public sealed class OvrInfo
{
    public int Id; // 动作 ID
    public int Ms; // 毫秒
}

/// <summary>手写 JSON 片段工具（转义 + 不变文化数字）。</summary>
internal static class JsonW
{
    internal static string Str(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    internal static string Num(double? d) =>
        d == null ? "" : d.Value.ToString("0.###", CultureInfo.InvariantCulture);
}
