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

namespace SurvivalLog.RoleUnlock;

/// <summary>
/// 角色专属解锁（独立 mod）：藤蔓种子 + 防御配方 全角色互通。
///
/// 机制（2026-10-21 反汇编实证，见 DESIGN.md）：
/// 1) 藤蔓线：Config_GlobalSetting["Plant_RareSeedDiscover"] 是四合一条目——
///    Params1=藤蔓种子物品ID / Params2=发现概率 / Params3=角色白名单（';'分隔，空=不限）。
///    IvyPlanManager.IsIvyRoleAllowed、IsInstallTarget、AE_HarvestPlant.TryDiscoverRareSeed
///    三处角色判定全部只读 Params3，且"空字符串=放行"是游戏内置约定（三处空值路径均已验证）。
///    本 mod 把 Params3 清空 → 任何角色收获作物都可能发现藤蔓种子，并可种植/安装/获得护盾。
///    注意：不能改成 "1;2" 多值——IsIvyRoleAllowed 的内联解析器遇非数字即停，会静默丢角色。
/// 2) 防御配方线（燃烧瓶等）：Config_DefenseLv.unlock_recipes_P1~P4 按角色分列，
///    ConfigManager.GetDefenseUnlockRecipes 按当前角色只取对应列（GetDefenseRequiredLevelForRecipe
///    也走它）→ 错角色配方永远查不到解锁等级=工作台不可见。
///    本 mod 把每行 P1~P4 四列互相合并成并集（列列相等）→ 取哪列都拿到全量，
///    解锁仍受各自防御等级门槛约束（不是白送，只是"可解锁"）。
/// 3) 生存规划互通：清功能类专属天赋 PlayerSelectId（家具位黑名单排除）+ 花瓶配方 325 等级化解锁。
///
/// 均为纯内存表编辑（零 Harmony——il2cpp 泛型 List 参数补丁会静默失败），
/// 幂等：进程内首读值记为原值，重复执行永远按原值推目标态；藤蔓/防御配方有 cfg 开关（关=还原），
/// 生存规划/花瓶配方无开关（mod 在即生效，删 dll 即回官方；已购规划不清退）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class RoleUnlockPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.roleunlock";
    public const string Name = "RoleUnlock";
    // 跟随套件 version.json，发版时与套件同步（版本号由作者定义）
    public const string Version = "1.3.8";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> UnlockVine;
    internal static ConfigEntry<bool> VineSkipDefenseGate;
    internal static ConfigEntry<bool> UnlockFlowerSeeds;
    internal static ConfigEntry<bool> ShareDefenseRecipes;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> Verbose;
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        UnlockVine = Config.Bind("Vine", "UnlockVine", true,
            "藤蔓全角色解锁：清空 Plant_RareSeedDiscover 的角色白名单。所有角色收获作物都可能发现藤蔓种子，并可种植/安装藤蔓。");
        VineSkipDefenseGate = Config.Bind("Vine", "SkipDefenseGate", false,
            "同时清零藤蔓种子的防御等级门槛（Plant_RareSeedDiscover_DefenseLv）。默认 false=保留门槛（发现种子需防御达标，与女大学生原生规则一致）。");
        UnlockFlowerSeeds = Config.Bind("Vine", "UnlockFlowerSeeds", false,
            "观赏花种子同样全角色解锁（清空 Plant_FlowerSeedDiscover 白名单）。默认 false=不动。");
        ShareDefenseRecipes = Config.Bind("Defense", "ShareDefenseRecipes", true,
            "防御配方全角色互通：Config_DefenseLv 的 unlock_recipes_P1~P4 四列合并成并集。燃烧瓶等角色专属配方对所有角色变为可解锁（等级门槛照旧）。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "首次生效时在日志列出原始白名单与各防御等级配方并集（核对数据用）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮输出明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        UnlockVine.SettingChanged += reload;
        VineSkipDefenseGate.SettingChanged += reload;
        UnlockFlowerSeeds.SettingChanged += reload;
        ShareDefenseRecipes.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RoleTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<RoleTicker>();
        var go = new GameObject("RoleUnlock.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<RoleTicker>();
        Log.LogInfo("[RoleUnlock] loaded：藤蔓种子全角色可发现 + 防御配方四角色互通 + 生存规划/花瓶配方互通（纯表编辑；藤蔓/防御配方可在配置关闭还原，规划/花瓶无开关、删 dll 即还原）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }
}

/// <summary>
/// 全部表改写。目标态由配置推导，原值取进程内首读；每轮把当前值同步到目标态
/// （防读档/切场景配置表重载覆盖，同其他套件 mod 的轮询幂等模式）。
/// </summary>
internal static class RoleTable
{
    private const string KeyRareSeed = "Plant_RareSeedDiscover";
    private const string KeyRareSeedGate = "Plant_RareSeedDiscover_DefenseLv";
    private const string KeyFlowerSeed = "Plant_FlowerSeedDiscover";

    // GlobalSetting 原值（null=尚未见过该条目）
    private static string _origRareAllow, _origFlowerAllow;
    private static int _origGate = int.MinValue;

    // DefenseLv 每行四列原值：rowKey(Lv) -> [P1,P2,P3,P4]
    private static readonly Dictionary<int, List<int>[]> OrigCols = new();

    /// <summary>一轮同步。返回 null=表未就绪；否则 (日志行列表)。</summary>
    internal static List<string> Apply(GameCore.HotUpdate.ConfigManager cm, bool unlockVine,
        bool skipGate, bool unlockFlower, bool shareRecipes, bool verbose, bool listAll)
    {
        var lines = new List<string>();

        // ---- 1) GlobalSetting：藤蔓 / 花种白名单 + 防御门槛 ----
        if (!ApplyAllowList(cm, KeyRareSeed, unlockVine, ref _origRareAllow, "藤蔓", verbose, lines) ||
            !ApplyAllowList(cm, KeyFlowerSeed, unlockFlower, ref _origFlowerAllow, "花种", verbose, lines))
            return null;

        var gate = cm.TryGet_Config_GlobalSetting(KeyRareSeedGate);
        if (gate != null)
        {
            int cur;
            try { cur = gate.Params1; } catch { cur = int.MinValue; }
            if (cur != int.MinValue)
            {
                if (_origGate == int.MinValue) _origGate = cur;
                int want = skipGate ? 0 : _origGate;
                if (cur != want)
                {
                    gate.Params1 = want;
                    lines.Add($"GS {KeyRareSeedGate}.Params1 防御门槛 {cur} -> {want}");
                }
                else if (verbose || listAll)
                    lines.Add($"GS {KeyRareSeedGate}.Params1 防御门槛 = {cur}（原 {_origGate}）");
            }
        }

        // ---- 2) DefenseLv：P1~P4 列并集 ----
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_DefenseLv> dict;
        try { dict = cm._Config_DefenseLv_Dict; } catch { dict = null; }
        if (dict == null || dict.Count == 0) return null;

        try
        {
            foreach (var kv in dict)
            {
                var row = kv.Value;
                if (row == null) continue;
                int lv;
                try { lv = row.Lv; } catch { continue; }

                var cols = new Il2CppSystem.Collections.Generic.List<int>[4];
                try
                {
                    cols[0] = row.unlock_recipes_P1;
                    cols[1] = row.unlock_recipes_P2;
                    cols[2] = row.unlock_recipes_P3;
                    cols[3] = row.unlock_recipes_P4;
                }
                catch { continue; }

                // 首见快照原值（托管侧，防自身写入污染）
                if (!OrigCols.TryGetValue(lv, out var orig))
                {
                    orig = new List<int>[4];
                    for (int i = 0; i < 4; i++) orig[i] = Snapshot(cols[i]);
                    OrigCols[lv] = orig;
                }

                if (shareRecipes)
                {
                    // 并集（保持等级顺序去重；0 是占位"无"不算配方）
                    var union = new List<int>();
                    foreach (var o in orig)
                        foreach (var id in o)
                            if (id != 0 && !union.Contains(id)) union.Add(id);

                    bool changed = false;
                    for (int i = 0; i < 4; i++)
                        if (SyncList(cols[i], union)) changed = true;

                    if (changed || verbose || listAll)
                    {
                        var joined = string.Join(",", union);
                        lines.Add(changed
                            ? $"DefenseLv {lv} P1~P4 -> 并集 [{joined}]（原 P1[{string.Join(",", orig[0])}] P2[{string.Join(",", orig[1])}] P3[{string.Join(",", orig[2])}] P4[{string.Join(",", orig[3])}]）"
                            : $"DefenseLv {lv} 并集 [{joined}]");
                    }
                }
                else
                {
                    // 还原官方分列
                    bool changed = false;
                    for (int i = 0; i < 4; i++)
                        if (SyncList(cols[i], orig[i])) changed = true;
                    if (changed) lines.Add($"DefenseLv {lv} P1~P4 还原官方分列");
                }
            }
        }
        catch { }

        // ---- 2.5) Talent：专属生存规划互通（无开关，mod 在即生效）----
        if (!ApplyTalents(cm, verbose, listAll, lines)) return null;

        // ---- 2.6) ProductionList：花瓶配方 325（无开关）----
        if (!ApplyVaseRecipe(cm, verbose, listAll, lines)) return null;

        return lines;
    }

    // ---- 3) Talent：专属生存规划互通（清 PlayerSelectId；家具位黑名单排除）----
    // 家具位扩建类规划绑定各角色自家地图，跨角色必排除；关键词对游戏更新新增的家具位自动免疫
    private static readonly string[] SlotTalentKeywords = { "位·", "扩建", "腾出一处", "置物位" };

    // Talent 行原白名单：ID -> 原值（进程内首见快照，幂等基准）
    private static readonly Dictionary<int, List<int>> OrigTalentRoles = new();

    // 首跑核对基线（2026-10-23 表数据；不符只报警不阻断，防游戏更新改表误判）
    private const int ExpectShareTalents = 46;
    private const int ExpectKeepSlotTalents = 77;

    /// <summary>清空功能类专属规划的角色白名单。返回 false=Talent 表未就绪。</summary>
    private static bool ApplyTalents(GameCore.HotUpdate.ConfigManager cm, bool verbose, bool listAll, List<string> lines)
    {
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Talent> dict;
        try { dict = cm._Config_Talent_Dict; } catch { dict = null; }
        if (dict == null || dict.Count == 0) return false;

        int kept = 0; bool changed = false;
        try
        {
            foreach (var kv in dict)
            {
                var row = kv.Value;
                if (row == null) continue;

                int id; string name, dec;
                Il2CppSystem.Collections.Generic.List<int> cur;
                try
                {
                    id = row.ID;
                    name = row.Name_Local ?? "";
                    dec = row.Dec_Local ?? "";
                    cur = row.PlayerSelectId;
                }
                catch { continue; }
                if (cur == null || cur.Count == 0) continue; // 本就全角色

                if (System.Array.Exists(SlotTalentKeywords, k => name.Contains(k) || dec.Contains(k)))
                {
                    kept++;
                    continue;
                }

                if (!OrigTalentRoles.TryGetValue(id, out var orig))
                {
                    orig = Snapshot(cur);
                    OrigTalentRoles[id] = orig;
                }
                if (SyncList(cur, new List<int>())) changed = true;
                if (changed && (listAll || verbose))
                    lines.Add($"Talent {id} {name} 白名单 [{string.Join(",", orig)}] -> 空");
            }
        }
        catch { }

        int cleared = OrigTalentRoles.Count; // 快照累计=历史清空总数（幂等，重复轮不重复计数）
        bool baselineOk = cleared == ExpectShareTalents && kept == ExpectKeepSlotTalents;
        if (changed || listAll || verbose)
            lines.Add($"Talent 互通：功能类已清空 {cleared} 条 / 家具位黑名单保留 {kept} 条" +
                      (baselineOk ? "（基线 46/77 吻合）" : "（⚠ 与基线 46/77 不符，疑似游戏更新改表，请核对上方清单）"));
        return true;
    }

    // ---- 4) ProductionList：花瓶包裹 325 等级化解锁 ----
    // 原生：craft_level=0 仅大学生剧情事件发放；改为 1/1 后全角色制作等级 1 可学（对齐工具柜包裹 389 档位）。
    // P2 档已获者走"解锁列表含→解锁"优先分支，不受影响。纯表编辑，零事件触发零存档写入。
    private const int VaseRecipeId = 325;
    private static int _origVaseCraft = int.MinValue, _origVaseShow = int.MinValue;

    private static bool ApplyVaseRecipe(GameCore.HotUpdate.ConfigManager cm, bool verbose, bool listAll, List<string> lines)
    {
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_ProductionList> dict;
        try { dict = cm._Config_ProductionList_Dict; } catch { dict = null; }
        if (dict == null || dict.Count == 0) return false;

        try
        {
            foreach (var kv in dict)
            {
                if (kv.Value == null || kv.Key != VaseRecipeId) continue;
                var row = kv.Value;

                int craft, show;
                try { craft = row.craft_level; show = row.show_level; } catch { return true; }
                if (_origVaseCraft == int.MinValue) { _origVaseCraft = craft; _origVaseShow = show; }

                if (craft != 1 || show != 1)
                {
                    row.craft_level = 1;
                    row.show_level = 1;
                    lines.Add($"Recipe {VaseRecipeId} 花瓶包裹 craft_level {craft}->1 show_level {show}->1（原值已快照）");
                }
                else if (verbose || listAll)
                    lines.Add($"Recipe {VaseRecipeId} 花瓶包裹 = 1/1（原 {_origVaseCraft}/{_origVaseShow}）");
                return true;
            }
        }
        catch { }
        return true; // 表就绪但无 325 行=游戏无此配方，不阻断其他功能
    }

    /// <summary>单条白名单同步：enabled=清空 Params3，disabled=还原原值。返回 false=表未就绪。</summary>
    private static bool ApplyAllowList(GameCore.HotUpdate.ConfigManager cm, string key, bool enabled,
        ref string orig, string label, bool verbose, List<string> lines)
    {
        var e = cm.TryGet_Config_GlobalSetting(key);
        if (e == null) return true; // 条目不存在=游戏没有此限制，不算未就绪

        string cur;
        try { cur = e.Params3; } catch { return true; }
        if (orig == null) orig = cur ?? "";

        string want = enabled ? "" : orig;
        if ((cur ?? "") != want)
        {
            e.Params3 = want;
            lines.Add($"GS {key}.Params3 {label}白名单 \"{cur}\" -> \"{want}\"");
        }
        else if (verbose)
            lines.Add($"GS {key}.Params3 {label}白名单 = \"{cur}\"（原 \"{orig}\"）");
        return true;
    }

    private static List<int> Snapshot(Il2CppSystem.Collections.Generic.List<int> list)
    {
        var r = new List<int>();
        if (list == null) return r;
        try { for (int i = 0; i < list.Count; i++) r.Add(list[i]); } catch { }
        return r;
    }

    /// <summary>把 il2cpp 列表内容同步成 target（多余的删、缺的按序补）。返回是否有改动。</summary>
    private static bool SyncList(Il2CppSystem.Collections.Generic.List<int> list, List<int> target)
    {
        if (list == null) return false;
        bool changed = false;
        try
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (!target.Contains(list[i])) { list.RemoveAt(i); changed = true; }
            foreach (var id in target)
                if (!list.Contains(id)) { list.Add(id); changed = true; }
        }
        catch { }
        return changed;
    }
}

internal sealed class RoleTicker : MonoBehaviour
{
    public RoleTicker(IntPtr ptr) : base(ptr) { }
    public RoleTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        _check += Time.deltaTime;
        var due = RoleUnlockPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var cm = BagTable.Cm();
        if (cm == null) { RoleUnlockPlugin.Rerun = true; return; }

        var listAll = RoleUnlockPlugin.ListOnReady.Value && !_announced;
        var lines = RoleTable.Apply(cm,
            RoleUnlockPlugin.UnlockVine.Value,
            RoleUnlockPlugin.VineSkipDefenseGate.Value,
            RoleUnlockPlugin.UnlockFlowerSeeds.Value,
            RoleUnlockPlugin.ShareDefenseRecipes.Value,
            RoleUnlockPlugin.Verbose.Value, listAll);
        if (lines == null) { RoleUnlockPlugin.Rerun = true; return; } // 配置表未就绪
        RoleUnlockPlugin.Rerun = false;

        foreach (var l in lines) RoleUnlockPlugin.Log.LogInfo("[RoleUnlock] " + l);

        if (!_announced)
        {
            _announced = true;
            var vine = RoleUnlockPlugin.UnlockVine.Value ? "开" : "关";
            var def = RoleUnlockPlugin.ShareDefenseRecipes.Value ? "开" : "关";
            RoleUnlockPlugin.Log.LogInfo(
                $"[RoleUnlock] 生效：藤蔓全角色解锁={vine}（防御门槛{(RoleUnlockPlugin.VineSkipDefenseGate.Value ? "已清零" : "保留")}），防御配方互通={def}。配置改动后自动重写/还原。");
        }
    }
}
