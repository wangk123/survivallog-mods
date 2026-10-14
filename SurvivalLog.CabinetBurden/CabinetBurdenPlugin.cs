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
/// 储物家具（橱柜/床头柜/储物柜/置物架/冰箱/箱子等）负重上限 ×BurdenMult（默认 ×1000，
/// 等于实际解除"格子没满却放不进"的限制）。
///
/// 机制（2026-10-11 反汇编实证，见 DESIGN.md）：
/// Config_Furniture 无负重字段；家具负重唯一来源 = Config_Bag.Burden，
/// BagComponent.OnInit 时快照；放入总闸 ItemManager.TryAddItemToBag → CanAddWeight
/// 用 GetMaxBurden = Round((Burden+ExtraBurden)×(1+天赋比例)+固定加成) 判定。
/// 天赋/增益项按 ownerId 查 AE 管线，家具恒为 0 → 改基础值即完全主导。
/// 快照语义：改表后需重新读档才对已存在容器生效（新放家具立即生效）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetBurdenPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetburden";
    public const string Name = "CabinetBurden";
    // 跟随套件 version.json，发版时与套件同步（版本号由作者定义）
    public const string Version = "1.3.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<int> BurdenMult;
    internal static ConfigEntry<string> IncludeBagIds;
    internal static ConfigEntry<string> ExcludeBagIds;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> Verbose;
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        BurdenMult = Config.Bind("Burden", "BurdenMult", 1000,
            "储物家具负重上限倍数。1000=实际解除限制；1=还原原生；0 或负数=停用改写（改回 1 后重进游戏还原）。");
        IncludeBagIds = Config.Bind("Burden", "IncludeBagIds", "",
            "追加的 BagId（逗号分隔）。默认不含：工作台抽屉 5010/5012/5013（扩容 mod 曾明确撤销）、购物车 3、轿车 4；要解放就加数字。");
        ExcludeBagIds = Config.Bind("Burden", "ExcludeBagIds", "3,4",
            "排除的 BagId（优先级最高）。默认排除 3(购物车)/4(轿车)——载具包走背包装备体系。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "首次生效时在日志列出全部受管储物包与负重改写（核对名单用）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮输出明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        BurdenMult.SettingChanged += reload;
        IncludeBagIds.SettingChanged += reload;
        ExcludeBagIds.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BurdenTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<BurdenTicker>();
        var go = new GameObject("CabinetBurden.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<BurdenTicker>();
        Log.LogInfo($"[CabinetBurden] loaded：储物家具负重 ×{BurdenMult.Value}（默认排除购物车 3、轿车 4）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }
}

/// <summary>
/// 名单发现 + 负重改写（幂等：进程内首读值记为原值，重复执行永远按 原值×倍数）。
/// 故意不复用 BagTable.RewriteBurden：需要"全量列名单"日志与 0/1 停用语义，且不动共享源
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
    /// 对名单内 Config_Bag 改写 Burden。返回 (managed, changed, lines)；
    /// managed=-1 表示未就绪。listAll=true 时逐条输出（首跑核对名单用）。
    /// </summary>
    internal static (int, int, List<string>) Rewrite(HashSet<int> targets, int mult, bool verbose, bool listAll)
    {
        var lines = new List<string>();
        var cm = BagTable.Cm();
        var dict = BagTable.BagDict(cm);
        if (dict == null || dict.Count == 0) return (-1, 0, lines);

        // 防溢出上限：GetMaxBurden 内 (Burden+Extra)×(1+比例) 走 float 后取整回 int
        const int cap = 500_000_000;
        int managed = 0, changed = 0;
        foreach (var pair in dict)
        {
            if (!targets.Contains(pair.Key)) continue;
            var bag = pair.Value;
            if (bag == null) continue;

            int raw = 0;
            try { raw = bag.Burden; } catch { }
            if (raw <= 0) continue; // 无负重语义的包不动

            managed++;
            Orig.TryAdd(pair.Key, raw);
            int want = (int)Math.Min((long)Orig[pair.Key] * mult, cap);

            if (raw != want)
            {
                bag.Burden = want;
                changed++;
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) 负重 {Orig[pair.Key]} -> {want}");
            }
            else if (verbose || listAll)
                lines.Add($"BAG {pair.Key}({BagTable.Name(bag)}) 负重 {raw}（原 {Orig[pair.Key]}，×{mult}）");
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

        var mult = CabinetBurdenPlugin.BurdenMult.Value;
        var targets = BurdenRewriter.DiscoverTargets();
        if (targets == null) { CabinetBurdenPlugin.Rerun = true; return; } // 配置表未就绪

        if (mult <= 0)
        {
            // 停用：不写表。改回正数后自动恢复改写
            CabinetBurdenPlugin.Rerun = false;
            if (!_announced)
            {
                _announced = true;
                CabinetBurdenPlugin.Log.LogInfo("[CabinetBurden] BurdenMult<=0，已停用改写。");
            }
            return;
        }

        var listAll = CabinetBurdenPlugin.ListOnReady.Value && !_announced;
        var (managed, changed, lines) = BurdenRewriter.Rewrite(
            targets, mult, CabinetBurdenPlugin.Verbose.Value, listAll);
        if (managed < 0) { CabinetBurdenPlugin.Rerun = true; return; }
        CabinetBurdenPlugin.Rerun = false;

        if (lines.Count > 0)
            foreach (var l in lines) CabinetBurdenPlugin.Log.LogInfo("[CabinetBurden] " + l);

        if (!_announced && managed > 0)
        {
            _announced = true;
            CabinetBurdenPlugin.Log.LogInfo(
                $"[CabinetBurden] 生效：{managed} 个储物包负重 ×{mult}（家具名单自动发现；Burden=0 的包（冰箱/置物架/工具柜等）原生无负重语义，自动跳过。默认排除购物车 3、轿车 4）。");
        }
    }
}
