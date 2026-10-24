using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetBurden;

/// <summary>
/// 柜子负重解放（独立 mod）。
/// 储物家具（橱柜/床头柜/储物柜/置物架/冰箱/箱子等）原生带负重的包直接清零 = 官方"无负重"语义
/// （2026-10-24 定案：去倍数方案，等同冰箱/置物架的原生待遇）。
///
/// 机制（2026-10-11 反汇编实证 + 2026-10-24 零值分支实证，见 DESIGN.md）：
/// Config_Furniture 无负重字段；家具负重唯一来源 = Config_Bag.Burden，
/// BagComponent.OnInit 时快照；放入总闸 ItemManager.TryAddItemToBag → CanAddWeight
/// 首步即"GetMaxBurden() <= 0 → return true"（≤0 恒可放入，冰箱/置物架原生走此分支）；
/// 柜子界面负重条与家具详情"负重上限"行均 v-if="max > 0"，零值时整行不渲染。
/// 快照语义：改表后需重新读档才对已存在容器生效（新放家具立即生效）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetBurdenPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetburden";
    public const string Name = "CabinetBurden";
    // 跟随套件 version.json，发版时与套件同步（版本号由作者定义）
    public const string Version = "1.3.8";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> IncludeBagIds;
    internal static ConfigEntry<string> ExcludeBagIds;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> Verbose;
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("Burden", "Enabled", true,
            "true=储物包负重清零（官方无负重语义，等同冰箱/置物架，柜子界面负重条整行隐藏）；false=停用并写回原生值。切换后需重进存档才对已存在容器生效。");
        IncludeBagIds = Config.Bind("Burden", "IncludeBagIds", "",
            "追加的 BagId（逗号分隔）。默认不含：工作台抽屉 5010/5012/5013（扩容 mod 曾明确撤销）、购物车 3、轿车 4；要解放就加数字。");
        ExcludeBagIds = Config.Bind("Burden", "ExcludeBagIds", "3,4",
            "排除的 BagId（优先级最高）。默认排除 3(购物车)/4(轿车)——载具包走背包装备体系。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "首次生效时在日志列出全部受管储物包与负重改写（核对名单用）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮输出明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        Enabled.SettingChanged += reload;
        IncludeBagIds.SettingChanged += reload;
        ExcludeBagIds.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BurdenTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<BurdenTicker>();
        var go = new GameObject("CabinetBurden.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BurdenTicker>();
        Log.LogInfo("[CabinetBurden] loaded：储物家具负重 → 无限制（Burden 清零；默认排除购物车 3、轿车 4）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }
}

/// <summary>
/// 名单发现 + 负重清零/还原（幂等：进程内首读值&gt;0 记为原值；生效=写 0，停用=写回原值）。
/// 原值记录先于一切跳过判断——否则写 0 后下一轮会被"raw&lt;=0 跳过"吞掉，还原路径失效
/// （旧倍数版即为此结构，改清零方案时必须重构，2026-10-24）。
/// 故意不复用 BagTable.RewriteBurden：需要"全量列名单"日志与 Enabled 停用语义，且不动共享源
/// （ModShared 改动需同步 4 份扩容文档，此处零侵入）。
/// </summary>
internal static class BurdenRewriter
{
    private static readonly Dictionary<int, int> Orig = new();

    /// <summary>
    /// 扫 Config_Furniture 收集全部带储物包的 BagId（不限 FurnitureType：
    /// Type=2 可放置储物家具 + Type=0 场景/原生固定柜如床头柜/纸箱/衣柜），套 Include/Exclude。
    /// 返回 null 表示配置表未就绪。
    /// </summary>
    internal static HashSet<int> DiscoverTargets()
    {
        var cm = BagTable.Cm();
        var dict = BagTable.FurnDict(cm);
        if (dict == null || dict.Count == 0) return null;

        var result = new HashSet<int>();
        foreach (var pair in dict)
        {
            var f = pair.Value;
            if (f == null) continue;
            int bagId = 0;
            try { bagId = f.BagId; } catch { continue; }
            if (bagId > 0) result.Add(bagId);
        }

        foreach (var id in BagTable.ParseIds(CabinetBurdenPlugin.IncludeBagIds.Value)) result.Add(id);
        foreach (var id in BagTable.ParseIds(CabinetBurdenPlugin.ExcludeBagIds.Value)) result.Remove(id);
        return result;
    }

    /// <summary>
    /// 对名单内 Config_Bag 清零/还原 Burden。返回 (managed, changed, lines)；
    /// managed=-1 表示未就绪。listAll=true 时逐条输出（首跑核对名单用）。
    /// </summary>
    internal static (int, int, List<string>) Rewrite(HashSet<int> targets, bool enabled, bool verbose, bool listAll)
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = BagTable.BagDict(cm);
        if (dict == null || dict.Count == 0) return (-1, 0, lines);

        int managed = 0, changed = 0;
        foreach (var pair in dict)
        {
            if (!targets.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;

            int raw = 0;
            try { raw = bag.Burden; } catch { continue; }

            // 只接管原生带负重的包；Burden=0 的包（冰箱/置物架等）原生即无限制，无需接管。
            // 首读值>0 才入册；入册后即使当前被写成 0 也永远按原值还原。
            if (!Orig.ContainsKey(pair.Key))
            {
                if (raw <= 0) continue;
                Orig[pair.Key] = raw;
            }

            managed++;
            int want = enabled ? 0 : Orig[pair.Key];
            if (raw != want)
            {
                bag.Burden = want;
                changed++;
                lines.Add(want == 0
                    ? $"BAG {pair.Key}({BagTable.Name(bag)}) 负重 {Orig[pair.Key]} -> 0（无限制）"
                    : $"BAG {pair.Key}({BagTable.Name(bag)}) 负重 还原为原生 {want}");
            }
            else if (verbose || listAll)
                lines.Add(want == 0
                    ? $"BAG {pair.Key}({BagTable.Name(bag)}) 负重 0（无限制，原 {Orig[pair.Key]}）"
                    : $"BAG {pair.Key}({BagTable.Name(bag)}) 负重 {raw}（原生）");
        }
        return (managed, changed, lines);
    }
}

internal sealed class BurdenTicker : MonoBehaviour
{
    public BurdenTicker(IntPtr ptr) : base(ptr) { }
    public BurdenTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 持续轮询幂等改写（同其他扩容 mod：防读档/切场景配置表重载覆盖）
        _check += Time.deltaTime;
        var due = CabinetBurdenPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = BurdenRewriter.DiscoverTargets();
        if (targets == null) { CabinetBurdenPlugin.Rerun = true; return; } // 配置表未就绪

        var listAll = CabinetBurdenPlugin.ListOnReady.Value && !_announced;
        var (managed, changed, lines) = BurdenRewriter.Rewrite(
            targets, CabinetBurdenPlugin.Enabled.Value, CabinetBurdenPlugin.Verbose.Value, listAll);
        if (managed < 0) { CabinetBurdenPlugin.Rerun = true; return; }
        CabinetBurdenPlugin.Rerun = false;

        if (lines.Count > 0)
            foreach (var l in lines) CabinetBurdenPlugin.Log.LogInfo("[CabinetBurden] " + l);

        if (!_announced && managed > 0)
        {
            _announced = true;
            if (CabinetBurdenPlugin.Enabled.Value)
                CabinetBurdenPlugin.Log.LogInfo(
                    $"[CabinetBurden] 生效：{managed} 个储物包负重清零 → 无限制（家具名单自动发现；Burden=0 的包（冰箱/置物架等）原生无负重语义，自动跳过。默认排除购物车 3、轿车 4）。柜子界面负重条将整行隐藏（与冰箱一致），需重进存档一次对已存在容器生效。");
            else
                CabinetBurdenPlugin.Log.LogInfo(
                    $"[CabinetBurden] 已停用：{managed} 个储物包负重保持/还原原生值（重进存档生效）。");
        }
    }
}
