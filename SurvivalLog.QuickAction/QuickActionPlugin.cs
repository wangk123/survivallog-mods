using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.QuickAction;

/// <summary>
/// 动作提速（独立 mod 5）：把交互动作耗时改为固定毫秒数（默认 500ms）。
/// v1.3.0：54 个有区分度的动作逐项配置（分节 + 一行备注：名称/场景/原值），
/// 3 个无区分度的大家族用整类设置（翻阅笔记/包裹拆封/陷阱，运行时按名字匹配，
/// 游戏更新新增的同名动作自动覆盖）；配置瘦身：去掉 debug/换算等技术项。
///
/// 实证（Il2CppDumper dump + 探针运行时 dump，2026-10-04）：
/// - 动作耗时 = Config_Action.During（游戏秒），进度条按游戏时钟递减；
///   探索/家场景 TimeScale=120（1 真实秒=2 游戏分钟），1800 During=15 真实秒。
/// - 内存表直写：不碰存档、无 Harmony、热改即时生效；0=恢复原值。
/// - 天赋仅「眼明手快」影响动作耗时（开家具-30%，与本 mod 叠加、方向一致）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class QuickActionPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.quickaction";
    public const string Name = "QuickAction";
    public const string Version = "1.3.1";

    /// <summary>探索时钟倍速（Config_Chapter 实测 120；游戏更新后体感异常再改此处重编译）。</summary>
    internal const int Scale = 120;

    internal static new ManualLogSource Log;

    internal static ConfigEntry<int>[] ActionMs;
    internal static ConfigEntry<int> NoteMs;     // 翻阅笔记（整类）
    internal static ConfigEntry<int> ParcelMs;   // 打开/拆封包裹（整类）
    internal static ConfigEntry<int> TrapMs;     // 布置/安装陷阱（整类）
    internal static ConfigEntry<int> EatMs;      // 吃/生吃/喝（整类，约 2650 个食物动作）
    internal static ConfigEntry<string> Overrides;

    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;

        ActionMs = new ConfigEntry<int>[ActionCatalog.All.Length];
        for (int i = 0; i < ActionCatalog.All.Length; i++)
        {
            var e = ActionCatalog.All[i];
            ActionMs[i] = Config.Bind("动作·" + e.section, "A" + e.id, 500,
                $"{e.name}（ID {e.id}）· {e.use} · 原值 {e.orig:0} 游戏秒 ≈ {e.orig / 120f:0.#} 真实秒 · 0=恢复原值");
        }

        const string gsec = "整类设置";
        NoteMs = Config.Bind(gsec, "翻阅笔记Ms", 500,
            "全部「翻阅笔记」动作（34 个同名变体，书籍物品触发，原值 600~3600 游戏秒不等 ≈ 5~30 真实秒）。0=恢复原值。");
        ParcelMs = Config.Bind(gsec, "包裹拆封Ms", 500,
            "全部「打开 / 拆封 XX包裹·礼盒」动作（约 700 个：各类家具包裹/物资包，原值 900 或 1800 游戏秒 ≈ 7.5/15 真实秒）。0=恢复原值。");
        TrapMs = Config.Bind(gsec, "陷阱Ms", 500,
            "全部「布置陷阱 / 安装陷阱」动作（7 个，原值 300~2400 游戏秒 ≈ 2.5~20 真实秒）。0=恢复原值。");
        EatMs = Config.Bind(gsec, "进食Ms", 500,
            "全部「吃 / 生吃 / 喝 XX」动作（约 2650 个：每个食物一条，原值 600 或 1200 游戏秒 ≈ 5/10 真实秒）。\n整类只减不增：原本更快的（如吃药 0.2 秒）不会被放慢。0=恢复原值。");

        Overrides = Config.Bind("其他", "Overrides", "",
            "上面没列到的动作单独覆盖：ID:毫秒 逗号分隔（优先级最高）。\n" +
            "例：100009001:500（观察便签）。ID:0 = 恢复原值。完整名单看 BepInEx\\ActionList.txt\n" +
            "（首次启动自动生成；删掉该文件下次启动会再生成）。");

        Config.SettingChanged += (object s, BepInEx.Configuration.SettingChangedEventArgs e) => { Rerun = true; };

        StripLegacyAndDefaultComments();

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<QuickTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<QuickTicker>();
        var go = new GameObject("QuickAction.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<QuickTicker>();
        Log.LogInfo($"[QuickAction] loaded v{Version}：逐项 {ActionCatalog.All.Length} + 整类 3");
    }

    /// <summary>
    /// 清理 cfg：剔除 BepInEx 自动生成的「# Default value」行（与原值备注重复易误解），
    /// 并整体移除 v1.0~1.2 的遗留段（[动作配置] / [QuickAction] / [Debug]）。
    /// </summary>
    private void StripLegacyAndDefaultComments()
    {
        try
        {
            var p = Config.ConfigFilePath;
            if (!System.IO.File.Exists(p)) return;
            var lines = System.IO.File.ReadAllLines(p);
            var kept = new List<string>();
            bool skipping = false;
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (t.StartsWith("["))
                {
                    skipping = t == "[动作配置]" || t == "[QuickAction]" || t == "[Debug]";
                    if (skipping) continue;
                }
                if (!skipping && !l.StartsWith("# Default value", StringComparison.Ordinal))
                    kept.Add(l);
            }
            if (kept.Count != lines.Length)
                System.IO.File.WriteAllLines(p, kept, new UTF8Encoding(false));
        }
        catch (Exception e) { Log.LogWarning("[QuickAction] cfg 清理失败: " + e.Message); }
    }

    /// <summary>解析覆盖表 "ID:毫秒,..."。value&lt;=0 表示恢复原值。</summary>
    internal static Dictionary<int, int> ParseOverrides(string s)
    {
        var dict = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(s)) return dict;
        foreach (var part in s.Split(',', ';'))
        {
            var seg = part.Trim();
            if (seg.Length == 0) continue;
            var idx = seg.LastIndexOf(':');
            if (idx <= 0) continue;
            if (!int.TryParse(seg.Substring(0, idx), out var id)) continue;
            if (!int.TryParse(seg.Substring(idx + 1), out var ms)) continue;
            dict[id] = ms;
        }
        return dict;
    }
}

internal sealed class QuickTicker : MonoBehaviour
{
    public QuickTicker(IntPtr ptr) : base(ptr) { }
    public QuickTicker() { }

    private float _check;
    private bool _announced;
    private bool _listWritten;

    // key: actionId -> 原始 During（幂等：永远按原值判断/恢复）
    private static readonly Dictionary<int, float> OrigDuring = new();

    public void Update()
    {
        var due = QuickActionPlugin.Rerun || _check >= 5f;
        if (!due) { _check += Time.deltaTime; return; }
        _check = 0f;

        var cm = GetCm();
        if (cm == null) return;
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict = null;
        try { dict = cm._Config_Action_Dict; } catch { }
        if (dict == null || dict.Count == 0) return;

        if (!_listWritten && !System.IO.File.Exists(
                System.IO.Path.Combine(Paths.BepInExRootPath, "ActionList.txt")))
        {
            _listWritten = true;
            WriteActionList(dict);
        }

        var (scanned, changed, restored, groupHit, lines) = Apply(dict);
        QuickActionPlugin.Rerun = false;

        if (changed > 0 || restored > 0)
            foreach (var l in lines.Take(80)) QuickActionPlugin.Log.LogInfo("[QuickAction] " + l);

        if (!_announced)
        {
            _announced = true;
            QuickActionPlugin.Log.LogInfo(
                $"[QuickAction] 生效：逐项 {changed - groupHit.changed} 改写；整类 翻阅笔记×{groupHit.note} 包裹×{groupHit.parcel} 陷阱×{groupHit.trap} 进食×{groupHit.eat}；恢复 {restored}（表 {scanned} 条）。");
            try
            {
                var head = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] QuickAction {QuickActionPlugin.Version} 首次生效（表 {scanned} 条）：",
                    $"逐项改写 {changed - groupHit.changed}；整类：翻阅笔记×{groupHit.note}、包裹拆封×{groupHit.parcel}、陷阱×{groupHit.trap}、进食×{groupHit.eat}；恢复 {restored}",
                    "换算：目标毫秒 ÷1000 ×" + QuickActionPlugin.Scale + " = 游戏秒（During）",
                    "---- 改写明细（原值 -> 当前值）----"
                };
                System.IO.File.WriteAllLines(
                    System.IO.Path.Combine(Paths.BepInExRootPath, "QuickActionTrace.txt"),
                    head.Concat(lines));
            }
            catch { }
        }
    }

    private static GameCore.HotUpdate.ConfigManager GetCm()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return null; }
    }

    private static string NameOf(GameCore.HotUpdate.Config_Action a)
    {
        try { return a.Name_Local ?? a.Name ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>整类匹配：无区分度大家族按名字归类（游戏更新新增同名动作自动覆盖）。</summary>
    private static int GroupOf(GameCore.HotUpdate.Config_Action a)
    {
        var n = NameOf(a);
        if (n == "翻阅笔记") return 1;
        if (n.StartsWith("打开 ") || n.StartsWith("拆封 ")) return 2;
        if (n == "布置陷阱" || n == "安装陷阱") return 3;
        if (n.StartsWith("吃") || n.StartsWith("生吃") || n.StartsWith("喝")) return 4;
        return 0;
    }

    private static (int scanned, int changed, int restored, (int changed, int note, int parcel, int trap, int eat) groupHit, List<string> lines) Apply(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict)
    {
        var lines = new List<string>();
        var overrides = QuickActionPlugin.ParseOverrides(QuickActionPlugin.Overrides.Value);

        var perAction = new Dictionary<int, int>(ActionCatalog.All.Length);
        for (int i = 0; i < ActionCatalog.All.Length; i++)
            perAction[ActionCatalog.All[i].id] = QuickActionPlugin.ActionMs[i].Value;

        int noteMs = QuickActionPlugin.NoteMs.Value, parcelMs = QuickActionPlugin.ParcelMs.Value, trapMs = QuickActionPlugin.TrapMs.Value, eatMs = QuickActionPlugin.EatMs.Value;

        int scanned = 0, changed = 0, restored = 0;
        int gChanged = 0, gNote = 0, gParcel = 0, gTrap = 0, gEat = 0;
        try
        {
            foreach (var kv in dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                scanned++;

                float cur = 0f;
                try { cur = a.During; } catch { continue; }

                OrigDuring.TryAdd(kv.Key, cur);
                float orig = OrigDuring[kv.Key];

                float target = orig;
                string tag = null;
                if (overrides.TryGetValue(kv.Key, out var oms))
                {
                    target = oms <= 0 ? orig : oms / 1000f * QuickActionPlugin.Scale;
                    tag = "覆盖表";
                }
                else if (perAction.TryGetValue(kv.Key, out var ms))
                {
                    target = ms <= 0 ? orig : ms / 1000f * QuickActionPlugin.Scale;
                    tag = "逐项";
                }
                else
                {
                    var g = GroupOf(a);
                    var gms = g switch { 1 => noteMs, 2 => parcelMs, 3 => trapMs, 4 => eatMs, _ => 0 };
                    if (g > 0)
                    {
                        if (gms <= 0) target = orig;
                        else
                        {
                            // 整类只减不增：原本更快的动作（如吃药 0.2 秒）不会被放慢
                            target = Math.Min(orig, gms / 1000f * QuickActionPlugin.Scale);
                            if (Math.Abs(target - orig) <= 1e-6f) tag = null;
                            else tag = g == 1 ? "翻阅笔记" : g == 2 ? "包裹拆封" : g == 3 ? "陷阱" : "进食";
                        }
                    }
                }

                if (Math.Abs(cur - target) > 1e-6f)
                {
                    a.During = target;
                    bool isRestore = Math.Abs(target - orig) <= 1e-6f;
                    if (isRestore) restored++;
                    else
                    {
                        changed++;
                        if (tag == "翻阅笔记") { gChanged++; gNote++; }
                        else if (tag == "包裹拆封") { gChanged++; gParcel++; }
                        else if (tag == "陷阱") { gChanged++; gTrap++; }
                        else if (tag == "进食") { gChanged++; gEat++; }
                    }
                    if (lines.Count < 400)
                        lines.Add($"ACTION {kv.Key}({NameOf(a)}) During {cur:0.###} -> {target:0.###}" +
                                  (isRestore ? "（恢复原值）" : $"（{(target / QuickActionPlugin.Scale * 1000):0}ms·{(tag ?? "逐项")}）"));
                }
            }
        }
        catch (Exception e) { lines.Add("WARN 扫描中断: " + e.Message); }

        return (scanned, changed, restored, (gChanged, gNote, gParcel, gTrap, gEat), lines);
    }

    /// <summary>ActionList.txt：全表（按耗时降序），仅在文件不存在时生成一次。</summary>
    private static void WriteActionList(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Action> dict)
    {
        try
        {
            var path = System.IO.Path.Combine(Paths.BepInExRootPath, "ActionList.txt");
            using var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false));
            w.WriteLine("== Config_Action 全表（ID|名称|原始During游戏秒|真实秒按TimeScale=120）。删掉本文件下次启动会重新生成 ==");
            var rows = new List<(float d, string line)>();
            foreach (var kv in dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                float d = 0f;
                string n = "?";
                try { d = a.During; n = a.Name_Local ?? a.Name ?? "?"; } catch { }
                rows.Add((d, $"{kv.Key}|{n}|{d:0.###}|{d / 120f:0.##}"));
            }
            rows.Sort((x, y) => y.d.CompareTo(x.d));
            foreach (var r in rows) w.WriteLine(r.line);
            QuickActionPlugin.Log.LogInfo($"[QuickAction] ActionList.txt 已生成（{rows.Count} 条）");
        }
        catch (Exception e)
        {
            QuickActionPlugin.Log.LogWarning("[QuickAction] ActionList 写入失败: " + e.Message);
        }
    }
}
