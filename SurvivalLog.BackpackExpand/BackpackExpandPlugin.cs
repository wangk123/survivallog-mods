using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.BackpackExpand;

/// <summary>
/// 背包扩容（独立 mod；2026-10-25 倍率制改数值制，作者定稿：全局三键，配置面极简）。
/// [Expand] Width/Height/Burden 三个明确数值统一应用到全部 9 个可穿背包：
/// - Width：全部背包统一宽度；Height：基础高度——初始包/野外登山包为该值，天赋链（加行三连
///   31931-31933）每升一级自动 +1 行（游戏原生加行机制照常）；Burden：统一负重（读档后体现）。
/// - 任一键 ≤0 = 该维度恢复官方原值。
/// 名单（Config_Player.Bag + BagGridBagIdsByInitialBag 实证）：四角色初始包 1（普通）/8（仓管）+
/// 天赋升级目标 5,6,7/9,10,11 + 中途获得的野外登山包 2。
/// General.Enabled 总开关（ModCenter 启停位）：false=全部回写官方原值并停写；下次启动不加载。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class BackpackExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.backpackexpand";
    public const string Name = "BackpackExpand";
    public const string Version = "1.3.8"; // 跟随套件 version.json（版本号由作者定义）

    internal static ManualLogSource Log;
    internal static volatile bool Rerun = true;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> Width, Height, Burden;          // [Expand] 三数值键
    internal static ConfigEntry<int> MaxWidth, MaxHeight;            // [Global] 防呆上限

    /// <summary>默认名单：初始包 1,8 + 天赋加行升级目标 5,6,7/9,10,11 + 野外登山包 2（全覆盖）。</summary>
    internal static readonly int[] DefaultIds = { 1, 2, 5, 6, 7, 8, 9, 10, 11 };

    public override void Load()
    {
        Log = base.Log;
        // General.Enabled 放最前 Bind（保证 cfg 键已生成后才可能早退）
        Enabled = Config.Bind("General", "Enabled", true, "总开关：false=全部恢复官方原值并停写；下次启动不加载。");
        Width = Config.Bind("Expand", "Width", 11,
            "背包宽度（全部背包统一到此列数）。0=恢复官方原值。默认 11（作者口径，原生最大 10）。");
        Height = Config.Bind("Expand", "Height", 10,
            "背包基础高度（初始背包与野外登山包的行数；天赋『背包加一行』每升一级自动再 +1 行，照常生效）。0=恢复官方原值。");
        Burden = Config.Bind("Expand", "Burden", 48000,
            "负重上限（全部背包统一，基础值；天赋比例/固定加成照常独立叠加）。0=恢复官方原值。改后需重新读档生效。");
        MaxWidth = Config.Bind("Global", "MaxWidth", 16,
            "宽度上限（Width 超出即夹到此值；0=不封顶自担风险）。背包页双栏布局，过宽会挤压右侧栏；16=防呆值。");
        MaxHeight = Config.Bind("Global", "MaxHeight", 18,
            "高度上限（Height+天赋行数超出即夹到此值；0=不封顶）。18=防呆值。");

        // SettingChanged 在 ConfigEntry<T> 上（ConfigEntryBase 无事件，本机 BepInEx 实证），逐条订阅；
        // lambda 显式形参类型（本机 Roslyn 推断形参会 CS0656）
        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        Enabled.SettingChanged += reload;
        Width.SettingChanged += reload;
        Height.SettingChanged += reload;
        Burden.SettingChanged += reload;
        MaxWidth.SettingChanged += reload;
        MaxHeight.SettingChanged += reload;

        MigrateLegacyCfg(Config); // 老倍率 cfg 一次性迁移（纯文件维护，Enabled=false 也执行）

        if (!Enabled.Value) return; // General.Enabled=false：不启 Ticker、零日志（表值本会话从未改过）

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BackpackTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<BackpackTicker>();
        var go = new GameObject("BackpackExpand.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BackpackTicker>();
        Log.LogInfo($"[BackpackExpand] loaded（数值制：宽 {Width.Value} 高 {Height.Value}+天赋行 负重 {Burden.Value}）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    /// <summary>老倍率 cfg 一次性迁移：读旧 [Expand] WidthMult/HeightMult/BurdenMult，按普通链头原生
    /// 基准（8x7/9600）换算成新三键赋给 entry（体验连续；缺键按旧默认 1.5/1.5/2），随后从文件删掉
    /// 旧倍率键行与任何 [Bag.*] 节（数值制试点期残留清理；旧键无消费者会被 BepInEx orphan 永久保留）。
    /// 作者本机 1.4/1.4/5 换算值恰等于新默认 11/10/48000。</summary>
    private static void MigrateLegacyCfg(BepInEx.Configuration.ConfigFile cfg)
    {
        try
        {
            var path = cfg.ConfigFilePath;
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            float wm = 0f, hm = 0f; int bm = 0; bool bagSeen = false;
            string sec = "";
            foreach (var raw in lines)
            {
                var sm = Regex.Match(raw, @"^\s*\[\s*([^\]]+?)\s*]");
                if (sm.Success)
                {
                    sec = sm.Groups[1].Value.Trim();
                    if (sec.StartsWith("Bag.", StringComparison.OrdinalIgnoreCase)) bagSeen = true;
                    continue;
                }
                if (!sec.Equals("Expand", StringComparison.OrdinalIgnoreCase)) continue;
                var t = raw.TrimStart();
                if (t.Length == 0 || t[0] == '#' || t[0] == ';') continue;
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var key = t.Substring(0, eq).Trim();
                var val = t.Substring(eq + 1).Trim().Trim('"');
                if (key.Equals("WidthMult", StringComparison.OrdinalIgnoreCase))
                    float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out wm);
                else if (key.Equals("HeightMult", StringComparison.OrdinalIgnoreCase))
                    float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out hm);
                else if (key.Equals("BurdenMult", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var bv))
                    bm = bv;
            }

            var hasLegacy = wm > 0 || hm > 0 || bm > 0;
            if (!hasLegacy && !bagSeen) return; // 全新安装/已迁移：不动文件

            if (hasLegacy)
            {
                // 换算口径 = 旧版 WantSize/RewriteBurden：AwayFromZero 取整、倍数下限 1
                if (wm > 0) Width.Value = (int)Math.Round(8 * wm, MidpointRounding.AwayFromZero);
                if (hm > 0) Height.Value = (int)Math.Round(7 * hm, MidpointRounding.AwayFromZero);
                if (bm > 0) Burden.Value = 9600 * Math.Max(1, bm);
            }

            // 旧键清理（两跳实测定案 2026-10-25：①只重写文件没用——Bind 读入的 orphan 在任意一次
            // Config.Save 时原样写回；②ConfigFile.Remove 只删绑定项、删不掉 orphan）。正解=反射清
            // _orphanedEntries 字典（BepInEx.Core 私有字段，BepInEx 5/6 同名；仓库反射惯用法同
            // ModCenter cfg 桥）后 Save，旧键才真正从文件消失。
            foreach (var (secName, keyName) in new[]
            {
                ("Expand", "WidthMult"), ("Expand", "HeightMult"), ("Expand", "BurdenMult"),
                ("Expand", "ExtraIds"), ("Expand", "ExcludeIds"), ("Debug", "Verbose"),
            })
            {
                var def = new BepInEx.Configuration.ConfigDefinition(secName, keyName);
                cfg.Remove(def); // 兜底：万一被 Bind 过
                // 实证（反射列字段 2026-10-25）：本机 BepInEx 6 BE 的 orphan 存 <OrphanedEntries>
                // k__BackingField（非 public 属性，与 5.x 的 _orphanedEntries 不同名），反射清之
                try
                {
                    var f = typeof(BepInEx.Configuration.ConfigFile).GetField("<OrphanedEntries>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f?.GetValue(cfg) is System.Collections.IDictionary orphans) orphans.Remove(def);
                }
                catch { }
            }

            cfg.Save(); // 迁移值立即落盘（防下次启动文件无旧键→迁移不再触发→回默认值）
            if (hasLegacy)
                Log.LogInfo($"[BackpackExpand] 旧倍率 cfg 已迁移为数值键（宽×{wm} 高×{hm} 负重×{bm} → "
                    + $"{Width.Value}/{Height.Value}/{Burden.Value}，体验连续）；旧键已删除。");
        }
        catch (Exception e) { Log.LogWarning("[BackpackExpand] 旧 cfg 迁移失败（可手动改三键）: " + e.Message); }
    }
}

internal sealed class BackpackTicker : MonoBehaviour
{
    public BackpackTicker(IntPtr ptr) : base(ptr) { }
    public BackpackTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // General.Enabled 总开关：false=把本会话改写过的全部包回写官方原值并停写；表未就绪下轮重试。
        // 边界：负重是读档快照，已穿背包的负重值需读一次档才回官方值。
        if (!BackpackExpandPlugin.Enabled.Value)
        {
            if (Engine.Applied)
            {
                var (rScanned, _) = Engine.RestoreAll();
                if (rScanned >= 0)
                {
                    Engine.Applied = false;
                    BackpackExpandPlugin.Log.LogInfo(
                        "[BackpackExpand] General.Enabled=false：已还原官方原值并停写。已穿背包的负重读一次档后回官方值。");
                }
            }
            return;
        }

        // 持续轮询幂等改写：配置表在切场景/读档时可能整体重载（对象重建、回到原值），周期性按目标值
        // 重写则始终有效（旧 mod 验证过的模式）。配置改动（SettingChanged→Rerun）即时落地。
        _check += Time.deltaTime;
        var due = BackpackExpandPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = new HashSet<int>(BackpackExpandPlugin.DefaultIds);
        var listAll = !_announced; // 首单全列（原 ListOnReady 键已删，行为常开：装机自证靠 trace）
        var (scanned, lines) = Engine.Rewrite(targets, false, listAll);
        if (scanned < 0) { BackpackExpandPlugin.Rerun = true; return; } // 配置表未就绪，立即重试

        BackpackExpandPlugin.Rerun = false;

        if (lines.Count > 0)
            foreach (var l in lines) BackpackExpandPlugin.Log.LogInfo("[BackpackExpand] " + l);

        if (!_announced)
        {
            _announced = true;
            var summary = $"生效：{scanned} 条配置受管（宽 {BackpackExpandPlugin.Width.Value} 高 {BackpackExpandPlugin.Height.Value}+天赋行 "
                        + $"负重 {BackpackExpandPlugin.Burden.Value}；上限 {BackpackExpandPlugin.MaxWidth.Value}x{BackpackExpandPlugin.MaxHeight.Value}）。"
                        + "负重需重新读档体现。";
            BackpackExpandPlugin.Log.LogInfo("[BackpackExpand] " + summary);
            Engine.WriteTrace(summary + "\r\n\r\n" + string.Join("\r\n", lines));
        }
    }
}

/// <summary>数值制改写引擎（绝对值幂等；单写者=本 mod，共享层 BagTable 仅用访问原语）。
/// 目标值：Width/Height/Burden 三键（≤0=恢复原生）× 天赋级数（Height 每级 +1），夹全局上限。</summary>
internal static class Engine
{
    private static readonly Dictionary<int, int> NativeW = new();
    private static readonly Dictionary<int, int> NativeH = new();
    private static readonly Dictionary<int, int> NativeB = new();

    /// <summary>本会话是否已按配置落笔（General.Enabled 关闭时据此触发一次性还原）。</summary>
    internal static bool Applied;

    /// <summary>天赋链级数（BagGridBagIdsByInitialBag 实证：初始 1→[5,6,7]、8→[9,10,11]，触发器=加行
    /// 三连天赋逐级换链上"同宽+1 行"的包；2 无链=0 级）。Height 按此表逐级 +1，天赋照常生效。</summary>
    private static readonly Dictionary<int, int> ChainLevel = new()
    {
        { 1, 0 }, { 5, 1 }, { 6, 2 }, { 7, 3 },
        { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 },
        { 2, 0 },
    };

    /// <summary>目标值：键 ≤0 = 该维度恢复原生；Height 按链级数 +1（原生加行机制）；宽高夹上限。</summary>
    private static (int w, int h, int b) Resolve(int id, int nw, int nh, int nb)
    {
        ChainLevel.TryGetValue(id, out var lv);
        int w = BackpackExpandPlugin.Width.Value > 0 ? BackpackExpandPlugin.Width.Value : nw;
        int h = BackpackExpandPlugin.Height.Value > 0 ? BackpackExpandPlugin.Height.Value + lv : nh;
        int b = BackpackExpandPlugin.Burden.Value > 0 ? BackpackExpandPlugin.Burden.Value : nb;
        if (BackpackExpandPlugin.MaxWidth.Value > 0) w = Math.Min(w, BackpackExpandPlugin.MaxWidth.Value);
        if (BackpackExpandPlugin.MaxHeight.Value > 0) h = Math.Min(h, BackpackExpandPlugin.MaxHeight.Value);
        return (w, h, b);
    }

    /// <summary>逐包改写到目标值。restoreNative=true 时全部回写原生值（Enabled=false 还原路径）。
    /// 返回 (scanned, lines)；scanned=-1 表配置表未就绪。verbose 输出未变更包的前后值。</summary>
    internal static (int, List<string>) Rewrite(HashSet<int> targets, bool restoreNative, bool verbose)
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = cm == null ? null : BagTable.BagDict(cm);
        if (dict == null || dict.Count == 0) return (-1, lines);

        int scanned = 0, changed = 0;
        foreach (var pair in dict)
        {
            if (!targets.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;
            scanned++;

            Il2CppSystem.Collections.Generic.List<int> size = null;
            try { size = bag.Size; } catch { }
            int w0 = 0, h0 = 0, b0 = 0;
            if (size != null && size.Count >= 2) { w0 = size[0]; h0 = size[1]; }
            try { b0 = bag.Burden; } catch { }

            // 会话首见即原生（首条扫描时 Size/Burden 可能单边读取失败：分字段独立缓存）
            if (w0 > 0 && h0 > 0) { NativeW.TryAdd(pair.Key, w0); NativeH.TryAdd(pair.Key, h0); }
            if (b0 > 0) NativeB.TryAdd(pair.Key, b0);
            if (!NativeW.TryGetValue(pair.Key, out var nw) || !NativeH.TryGetValue(pair.Key, out var nh)) continue;
            if (!NativeB.TryGetValue(pair.Key, out var nb)) nb = b0 > 0 ? b0 : 0;

            var (tw, th, tb) = restoreNative ? (nw, nh, nb) : Resolve(pair.Key, nw, nh, nb);

            var name = BagTable.Name(bag);
            if ((w0 != tw || h0 != th) && size != null && size.Count >= 2)
            {
                size[0] = tw;
                size[1] = th;
                changed++;
                lines.Add($"BAG {pair.Key}({name}) {w0}x{h0} -> {tw}x{th}");
            }
            else if (verbose)
                lines.Add($"BAG {pair.Key}({name}) 已是 {w0}x{h0}（原生 {nw}x{nh}）");

            if (tb > 0 && b0 != tb)
            {
                bag.Burden = tb;
                changed++;
                lines.Add($"BAG {pair.Key}({name}) 负重 {b0} -> {tb}（原生 {nb}）");
            }
            else if (verbose && tb > 0)
                lines.Add($"BAG {pair.Key}({name}) 负重已是 {b0}（原生 {nb}）");
        }

        if (changed > 0)
        {
            Applied = true;
            BagTable.InvalidateBagSizeIndex(cm, lines);
        }
        return (scanned, lines);
    }

    /// <summary>还原路径：把本会话改写过（进过原生缓存）的全部包回写原生值。</summary>
    internal static (int, List<string>) RestoreAll()
    {
        var ids = new HashSet<int>(NativeW.Keys);
        ids.UnionWith(NativeH.Keys);
        ids.UnionWith(NativeB.Keys);
        if (ids.Count == 0) return (0, new List<string>());
        var result = Rewrite(ids, true, false);
        if (result.Item1 >= 0) Applied = false;
        return result;
    }

    /// <summary>自证 trace 文件（日志强杀不落盘，实机验证一律看它）。路径 BepInEx\BackpackExpand_trace.txt。</summary>
    internal static void WriteTrace(string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "BackpackExpand_trace.txt"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + content,
                new UTF8Encoding(false));
        }
        catch { }
    }
}
