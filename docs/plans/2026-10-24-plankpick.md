# SurvivalLog.PlanPick 自选生存规划 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在每日生存规划选择页（RogueSelect）注入"自选规划"弹层（全 465 项、二级分类、锁定原因、消耗×2），购买完整复用官方链路。

**Architecture:** C# BepInEx 插件两件套——巡检 Ticker 通过 Root WebView 往 RogueSelect iframe 注入 page.js 并推送 C# 侧算好的 JSON 数据（JS 零推导）；Harmony prefix/finalizer 钩 `OnBuySurvivalPlan` 对"非今日候选"购买临时把 `LvUpCost` ×倍率、finalizer 必恢复。零新增存档字段，删 dll 即还原。

**Tech Stack:** BepInEx IL2CPP (net6.0)、HarmonyLib、Vuplex WebView ExecuteJavaScript、vanilla JS（内嵌资源）、Node+playwright-core 浏览器夹具。

**Spec:** `SurvivalLog.PlanPick/DESIGN.md`（v5 定稿，含全部机制实证 RVA 与作者口径）。执行本计划前必读其 §1-§5。

## Global Constraints（仓库硬规则，每个任务隐含遵守）

- 版本号作者定：`PlanPickPlugin.Version` 常量写死为**读取时** `version.json` 的 `suite` 值（不自行编造）。
- 构建判退出码：`"sdk/dotnet/dotnet.exe" build SurvivalLog.PlanPick -c Release > build.log 2>&1; echo $?`，**禁止**管道 tail。
- 提交外科手术式：`git add <明确文件清单>`，禁 `git add -A` / `git add .`；工作树有作者脏文件，只 add 本计划产物。
- 复杂 JS 片段写 .js 文件执行，不 inline 进 bash；ps1 一律 ASCII-only。
- 不进 publish.ps1 白名单/安装器（发版另定）；探针红线无关。
- 改挂钩点必须同步 `tools/gamekb/moddeps.json`（Task 6 内完成）。
- UI 代码禁在 JS 里写半角双引号字符串字面量于 bash heredoc（全部走 Write 工具写文件，无此问题）。
- 游戏 exe 在跑时不得杀进程/动游戏目录文件（部署步骤带 tasklist 守卫）。

## 已实证的运行时访问路径（全部照抄，勿另行探索）

| 数据 | 路径 |
|---|---|
| 配置表 | `SurvivalLog.ModShared.BagTable.Cm()` → `cm._Config_Talent_Dict` 等（`_Config_RandomGroup_Dict`/`_Config_DailyRandom_Dict`/`_Config_ConditionSet_Dict`/`_Config_PlayerSelect_Dict` 同款，interop 直访，先例 `ModShared/BagTableRw.cs:35`） |
| 当前角色 | `GameCore.HotUpdate.ConfigManager.GetCurrentPlayerSelectId()`（静态，1~4） |
| 角色名 | `cm._Config_PlayerSelect_Dict[id].Name_Local`（P1打工仔/P2大学生/P3仓库管理员/P4退伍军人） |
| 条件判定 | `BaseSingleton<BattleLogicWorld>.Instance._ConditionSetManager.CheckConditionSet(condSetId)`（public，RVA 0x2833A30 购买链同款） |
| 今日候选/天数 | `BaseSingleton<ReduxUISystem>.Instance.reduxStoreLayer.dataTree.TryGetValue(Il2CppType.From(typeof(State_UI_RogueSelect)), out raw)` → `raw.TryCast<State_UI_RogueSelect>().CachedSurvivalPlanIds / Day`（先例 `SurvivalLog.CabinetEverywhere/CabinetCatalog.cs:274-281`） |
| 已激活 | 同上取 `State_Data_Talent` → `SurvivalPlanningIds` |
| 生存点 | 同上取 `State_Web_RogueSelect` → `OwnPoints.Value`（ReactiveProperty\<int\>） |
| WebView | `BaseSingleton<ReduxUISystem>.IsInstanceCreated` → `.Instance.GetWebUILayer().canvasWebViewPrefab.WebView`（先例 `SurvivalLog.CabinetExpand/ViewportFit.cs:102-114`）；**ExecuteJavaScript 在 Root 上下文执行**，RogueSelect 是 Root 内同源 iframe（`document.getElementById('RogueSelect')`，见 `tools/webui_src/Root.html:224`） |
| 购买事件 | iframe 内 `window.parent.postMessage({type:'CLICK_BUY', data:{id}, sourcePageId:'RogueSelect'}, '*')` → Root 转 protocol-3 → vuplex → `Ac_RogueSelect_WebClickBuy` → 全局通知 15003 → `OnBuySurvivalPlan`（全链路已实证，见 DESIGN §2.3） |
| il2cpp 类型注册 | `ClassInjector.RegisterTypeInIl2Cpp<T>()` + `Object.DontDestroyOnLoad`（先例 RoleUnlockPlugin.cs:74-78） |

**il2cpp List\<int\> 遍历**：`Il2CppSystem.Collections.Generic.List<int>` 用 `for (int i=0;i<list.Count;i++) list[i]`（不要 foreach/LINQ）。

**JSON 序列化**：不引第三方库。手写最小转义（`"`→`\"`、`\`→`\\`、换行→`\n`、控制字符<0x20→`\uXXXX`）+ StringBuilder 拼装。数据全部来自表（无用户输入），转义是防御性的。

---

### Task 1: 工程骨架与插件空壳

**Files:**
- Create: `SurvivalLog.PlanPick/SurvivalLog.PlanPick.csproj`
- Create: `SurvivalLog.PlanPick/PlanPickPlugin.cs`
- Create: `SurvivalLog.PlanPick/page.js`（空占位，Task 3 填充）

**Interfaces:**
- Produces: `PlanPickPlugin`（Guid=`com.local.survivallog.planpick`）、静态 `Log`、cfg 项 `CostMultiplier`(float,默认2)/`ShowLocked`(bool,true)/`ShowUnpooled`(bool,true)/`ListOnReady`(bool,true)/`Verbose`(bool,false)、`PlanTicker` MonoBehaviour 壳（Task 4/5 填充逻辑）

- [ ] **Step 1: 写 csproj**——照抄 `SurvivalLog.RoleUnlock/SurvivalLog.RoleUnlock.csproj` 全部引用（含 ModShared 两行 Compile Include、BepInEx core 三引用、HotUpdate/UnityEngine.CoreModule interop），追加 0Harmony 引用（照抄 `SurvivalLog.CabinetExpand/SurvivalLog.CabinetExpand.csproj:23-25`）；AssemblyName/RootNamespace 换 `SurvivalLog.PlanPick`；Version 读 `version.json` suite 值填入；追加 `<EmbeddedResource Include="page.js" />`。

- [ ] **Step 2: 写插件壳** PlanPickPlugin.cs：

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.PlanPick;

/// <summary>自选生存规划：RogueSelect 页注入全量规划列表，自选购买消耗翻倍（官方购买链复用）。设计见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class PlanPickPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.planpick";
    public const string Name = "PlanPick";
    public const string Version = "<version.json suite 值>";

    internal static ManualLogSource Log;
    internal static ConfigEntry<float> CostMultiplier;   // 自选倍率（1=原价；<1 按 1 处理）
    internal static ConfigEntry<bool> ShowLocked;        // 未解锁项展示（置灰+原因）
    internal static ConfigEntry<bool> ShowUnpooled;      // 不入池规划（图纸/无人机）展示
    internal static ConfigEntry<bool> ListOnReady;       // 首轮列统计
    internal static ConfigEntry<bool> Verbose;

    public override void Load()
    {
        Log = base.Log;
        CostMultiplier = Config.Bind("Cost", "CostMultiplier", 2f,
            "自选规划（不在今日候选名单）的生存点消耗倍率。默认 2=翻倍；1=免代价。今日候选内购买始终原价。");
        ShowLocked = Config.Bind("List", "ShowLocked", true, "列表展示未解锁规划（置灰+点击弹原因）。");
        ShowUnpooled = Config.Bind("List", "ShowUnpooled", true, "列表展示从不进入抽取池的规划（机器人图纸/拾荒无人机）。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true, "首次数据就绪时在日志列统计（分类计数/今日候选）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "每轮推送输出明细。");

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PlanTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<PlanTicker>();
        var go = new GameObject("PlanPick.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PlanTicker>();
        Log.LogInfo("[PlanPick] loaded：自选规划入口待注入（倍率 x" + CostMultiplier.Value + "）");
        WelcomeTip.Install(Log);
    }
}

/// <summary>2s 巡检：WebView 就绪→注入 page.js→推数据。Task 4/5 填充 Update 逻辑。</summary>
internal sealed class PlanTicker : MonoBehaviour
{
    public PlanTicker(IntPtr ptr) : base(ptr) { }
    public PlanTicker() { }
    private float _check;
    private void Update() { _check += Time.deltaTime; if (_check < 2f) return; _check = 0; }
}
```

- [ ] **Step 3: 建 page.js 占位**——内容一行注释 `/* Task 3 填充 */`。

- [ ] **Step 4: 构建**——`"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.PlanPick -c Release > build.log 2>&1; echo $?`，期望退出码 0。

- [ ] **Step 5: Commit**

```bash
git add SurvivalLog.PlanPick/SurvivalLog.PlanPick.csproj SurvivalLog.PlanPick/PlanPickPlugin.cs SurvivalLog.PlanPick/page.js
git commit -m "feat(plankpick): 工程骨架+插件壳（cfg/WelcomeTip/Ticker占位）"
```

---

### Task 2: PlanCatalog 静态目录（表 → 465 条规划元数据）

**Files:**
- Create: `SurvivalLog.PlanPick/PlanCatalog.cs`
- Modify: `SurvivalLog.PlanPick/PlanPickPlugin.cs`（Ticker 调用点，Task 5 再接）

**Interfaces:**
- Consumes: `BagTable.Cm()`、`cm._Config_*_Dict`
- Produces: `PlanCatalog.Build(ConfigManager cm)` → `List<PlanEntry>`；`PlanEntry { int Id; string Name, Desc, Icon; int Cost; bool IsReget; string Cat; string Sub; int? UnlockDay; List<int> PrereqIds; List<string> SpecialDesc; List<int> RoleIds; }`；`static List<string> BuildLog`（ListOnReady 用）
- 语义与 `prototype/gen_data.js` 完全一致（它是唯一真源对照）

- [ ] **Step 1: 写 PlanCatalog.cs**——移植 gen_data.js 逻辑（读 `prototype/gen_data.js` 对照）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SurvivalLog.PlanPick;

internal sealed class PlanEntry
{
    public int Id; public string Name = "", Desc = "", Icon = "", Cat = "function", Sub; // Sub=null→独立
    public int Cost; public bool IsReget; public int? UnlockDay;                 // null=不入池
    public List<int> PrereqIds = new(); public List<string> SpecialDesc = new(); public List<int> RoleIds = new();
}

internal static class PlanCatalog
{
    internal static List<PlanEntry> Plans;          // 表就绪后构建一次
    internal static readonly List<string> BuildLog = new();

    internal static bool Build(GameCore.HotUpdate.ConfigManager cm)
    {
        if (Plans != null) return true;
        var talentDict = cm._Config_Talent_Dict; var groupDict = cm._Config_RandomGroup_Dict;
        var dailyDict = cm._Config_DailyRandom_Dict; var condDict = cm._Config_ConditionSet_Dict;
        if (talentDict == null || groupDict == null || dailyDict == null || condDict == null) return false;
        try { BuildCore(talentDict, groupDict, dailyDict, condDict); return true; }
        catch (Exception e) { BuildLog.Add("构建异常: " + e.Message); return false; }
    }

    private static void BuildCore(
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_Talent> talentDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_RandomGroup> groupDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_DailyRandom> dailyDict,
        Il2CppSystem.Collections.Generic.Dictionary<int, GameCore.HotUpdate.Config_ConditionSet> condDict)
    {
        // ---- 1) 名单：Type==2 && Available==1（审计定稿，与官方可抽集合一致）----
        var rows = new List<GameCore.HotUpdate.Config_Talent>();
        foreach (var kv in talentDict) { var r = kv.Value; if (r.Type == 2 && r.Available == 1) rows.Add(r); }

        // ---- 2) 池最早出现日：DailyRandom.RandomPlanID ∪ SpecifiedPlanID，组→日取首见 ----
        var poolFirstDay = new Dictionary<int, int>();
        foreach (var kv in dailyDict)
        {
            var r = kv.Value; int day = r.Day;
            foreach (var g in r.RandomPlanID) if (!poolFirstDay.ContainsKey(g)) poolFirstDay[g] = day;
            foreach (var g in r.SpecifiedPlanID) if (!poolFirstDay.ContainsKey(g)) poolFirstDay[g] = day;
        }
        var planDay = new Dictionary<int, int>();
        foreach (var kv in groupDict)
        {
            var g = kv.Value; if (!poolFirstDay.TryGetValue(g.ID, out var d)) continue;
            foreach (var id in g.IdList)
                if (!planDay.TryGetValue(id, out var cur) || d < cur) planDay[id] = d;
        }

        // ---- 3) 分类（与 gen_data.js 同序）：物资→扩建→属性→功能 ----
        var materialIds = new HashSet<int>();
        foreach (var kv in groupDict) { var g = kv.Value; if (g.ID == 1005 || g.ID == 1009) foreach (var id in g.IdList) materialIds.Add(id); }
        var expandPool = new HashSet<int>();
        foreach (var kv in groupDict) { var g = kv.Value; if (g.ID == 9122) foreach (var id in g.IdList) expandPool.Add(id); }
        string CatOf(GameCore.HotUpdate.Config_Talent r) =>
            materialIds.Contains(r.ID) ? "material"
            : (r.Name_Local ?? "").StartsWith("扩建·") || expandPool.Contains(r.ID) ? "expand"
            : (r.ID >= 30000 && r.ID < 31000) ? "attribute" : "function";

        // ---- 4) 二级：前置链系列（LCP 剥尾 Lv/·）；扩建取区域段；专属家具位 ----
        var condById = new Dictionary<int, GameCore.HotUpdate.Config_ConditionSet>();
        foreach (var kv in condDict) condById[kv.Key] = kv.Value;
        var t2all = new HashSet<int>();
        foreach (var kv in talentDict) if (kv.Value.Type == 2) t2all.Add(kv.Key);
        var parent = new Dictionary<int, int>();   // 仅 PlanLvGate 单前置链
        foreach (var r in rows)
        {
            if (r.ConditionSetId == 0 || !condById.TryGetValue(r.ConditionSetId, out var c)) continue;
            var tn = c.TalentNeed; if (tn == null || tn.Count != 1) continue;
            if (t2all.Contains(tn[0]) && (c.Source ?? "").StartsWith("PlanLvGate")) parent[r.ID] = tn[0];
        }
        int RootOf(int id) { var seen = new HashSet<int>(); while (parent.TryGetValue(id, out var p) && seen.Add(id)) id = p; return id; }
        var chains = new Dictionary<int, List<GameCore.HotUpdate.Config_Talent>>();
        foreach (var r in rows) { var k = RootOf(r.ID); if (!chains.TryGetValue(k, out var l)) chains[k] = l = new(); l.Add(r); }
        var series = new Dictionary<int, string>();
        foreach (var ch in chains.Values)
        {
            if (ch.Count < 2) continue;
            var lcp = LcpNames(ch);
            lcp = System.Text.RegularExpressions.Regex.Replace(lcp, @"\s*Lv[\s·]*$", "");
            lcp = System.Text.RegularExpressions.Regex.Replace(lcp, @"[\s·]+$", "");
            if (lcp.Length < 2) lcp = ch[0].Name_Local.Replace(" Lv1", "");
            foreach (var r in ch) series[r.ID] = lcp;
        }

        // ---- 5) 输出 ----
        Plans = new List<PlanEntry>();
        foreach (var r in rows)
        {
            var e = new PlanEntry { Id = r.ID, Name = r.Name_Local ?? "", Desc = r.Dec_Local ?? "",
                Icon = r.WebIcon ?? "", Cost = r.LvUpCost, IsReget = r.IsReget, Cat = CatOf(r) };
            e.UnlockDay = planDay.TryGetValue(r.ID, out var ud) ? ud : (int?)null;
            if (r.ConditionSetId != 0 && condById.TryGetValue(r.ConditionSetId, out var c))
            {
                var tn = c.TalentNeed;
                if (tn != null) for (int i = 0; i < tn.Count; i++) e.PrereqIds.Add(tn[i]);
                if (c.SurvivalToDay > 0) e.SpecialDesc.Add("生存至第" + c.SurvivalToDay + "天");
                if (c.NeedFurnitureIds != null && c.NeedFurnitureIds.Count > 0) e.SpecialDesc.Add("需持有特定家具");
                if (c.EventTag != null && c.EventTag.Count > 0) e.SpecialDesc.Add("需解锁特定地点/事件");
                if (c.DefenseLevel > 0) e.SpecialDesc.Add("需防御等级" + c.DefenseLevel);
                if (c.NeedTrapObtainable != null && c.NeedTrapObtainable.Count > 0) e.SpecialDesc.Add("需可制作特定防御装置");
            }
            var ps = r.PlayerSelectId;
            if (ps != null) for (int i = 0; i < ps.Count; i++) e.RoleIds.Add(ps[i]);
            // 二级
            if (e.Cat == "expand")
            {
                if ((r.Name_Local ?? "").StartsWith("扩建·"))
                {
                    var seg = r.Name_Local.Split('·').Length > 1 ? r.Name_Local.Split('·')[1] : "";
                    e.Sub = System.Text.RegularExpressions.Regex.Replace(seg, @"(大型位|中型位|小型位|桌面位)$", "");
                    if (e.Sub.Length == 0) e.Sub = seg;
                }
                else if (e.RoleIds.Count > 0) e.Sub = "专属家具位";
            }
            if (e.Sub == null) e.Sub = series.TryGetValue(r.ID, out var s) ? s : null;
            Plans.Add(e);
        }
        var byCat = Plans.GroupBy(p => p.Cat);
        foreach (var g in byCat) BuildLog.Add(g.Key + " " + g.Count() + " 项");
        BuildLog.Add("不入池 " + Plans.Count(p => p.UnlockDay == null) + " 项");
    }

    private static string LcpNames(List<GameCore.HotUpdate.Config_Talent> ch)
    {
        var s = ch[0].Name_Local ?? "";
        foreach (var r in ch) { var n = r.Name_Local ?? ""; int i = 0; while (i < s.Length && i < n.Length && s[i] == n[i]) i++; s = s.Substring(0, i); }
        return s;
    }
}
```

注意：`foreach (var kv in dict)` 对 il2cpp Dictionary 的 KeyValuePair 枚举在 interop 下可用（BagTableRw 同款字典只做索引访问；若枚举编译报错，退化为 `dict.Keys` foreach + `dict[key]`，两种写法都合法）。

- [ ] **Step 2: 构建**（同 Task 1 命令），期望 0。若 il2cpp 集合枚举编译失败，按注释退化改写后重建。

- [ ] **Step 3: Commit**

```bash
git add SurvivalLog.PlanPick/PlanCatalog.cs
git commit -m "feat(plankpick): PlanCatalog 静态目录（分类/系列/解锁日/前置/特殊条件/角色白名单）"
```

---

### Task 3: page.js 弹层 UI + 浏览器夹具（TDD）

**Files:**
- Create: `SurvivalLog.PlanPick/page.js`（内嵌资源主体）
- Create: `SurvivalLog.PlanPick/tests/fixture.html`
- Create: `SurvivalLog.PlanPick/tests/run_tests.js`
- Create: `SurvivalLog.PlanPick/tests/.gitignore`（`golden.json` 由脚本生成，不入库）

**Interfaces:**
- Consumes: `window.__PlanPickBoot(payload)` —— payload 结构见 Step 1 规范（C# Task 4 产出同构 JSON）
- Produces: `window.__PlanPickBoot`（幂等安装 + 数据更新，可反复调用）；UI 行为与 `prototype/index.html` v5 完全一致（唯一真源）

- [ ] **Step 1: 写 fixture.html**——最小宿主页：引入 `../page.js`，body 内放官方页骨架最小复刻（`.panel > .content > .footer > .footer-right` 含 `<button class="primary-btn">离 开</button>`），再 `fetch('golden.json')` 调 `__PlanPickBoot`。golden.json 由 run_tests.js 从 `prototype/plan_data.json`（再跑 `prototype/gen_data.js` 生成）转换：

```js
// run_tests.js 顶部的转换（在临时 http 服务里动态完成，不落盘）：
// golden = { plans: <plan_data.json 的 plans>, categories: <同>, today: <3 个样例 id>,
//            points: 150, mult: 2, day: 20, role: 0, roleNames: {1:'打工仔',2:'大学生',3:'仓库管理员',4:'退伍军人'} }
```

- [ ] **Step 2: 写 run_tests.js 失败断言**——移植 `prototype/_verify.js` 全部断言（入口按钮在 footer-right、官方区无锁定卡、tab 计数、二级 chips 换行无横向溢出、"更多"展开、状态筛选联动回写计数、0 计数 chip 隐藏、锁定原因（含"仅限「打工仔」的专属规划"官方角色名）、确认弹窗无提示句、×2 数学、扣点模拟（page.js 本地乐观更新点数）、天数滑杆改由 fixture 调 `__PlanPickBoot` 重推新 day 断言解锁数变化）。页面顶部不再有原型调试条——用 fixture 提供 `window.__fixtureSet({day,points,role,mult})` 转调 `__PlanPickBoot` 合并推送。

- [ ] **Step 3: 跑红**——`node tests/run_tests.js`，期望 FAIL（page.js 为空占位，`__PlanPickBoot` 未定义）。

- [ ] **Step 4: 写 page.js**——从 `prototype/index.html` 移植（该文件是 UI 唯一真源，v5 定稿）：
  - 全部 `.pp-*` CSS 原样内联成 `<style>` 注入（含换行展开、下拉配色、64px 图标、toast/confirm/why 样式）；CSS 变量（`--card-bg` 等） RogueSelect 页自带，直接用。
  - DOM：入口按钮插入页脚 `.footer-right` **首位**（`insertBefore`，"离开"按钮之前）；弹层/确认/why/toast 挂 body。
  - `__PlanPickBoot(payload)`：幂等（已装则只更新数据）；重算 lockState/lockReason（判定函数从原型 `evalLock` 移植，但**直接使用 payload 中 C# 已算好的 lockState/lockReason**——JS 不推导条件，仅按 payload 渲染；点数对比（too-expensive）是唯一 JS 侧算的展示逻辑，用 payload.points）。
  - 购买点击：确认弹窗（原价/×N/余额三行，无提示句）→ 确认后 `window.parent.postMessage({type:'CLICK_BUY', data:{id: <number>}, sourcePageId:'RogueSelect'}, '*')`；本地乐观置 bought、`payload.points -= pay`，等 C# 下一轮推送校正。
  - 图标：`payload.plans[].icon` 为官方 `WebIcon` 原值（`../../Res/...`），页内同源直接可用；夹具环境由 run_tests.js 的 http 服务把 `/Res/*` 映射到 `gamekb/<新版>/webui/`（照抄 `prototype/serve.js`）。
  - 锁定原因、今日徽章、不入池标签、可重购标签、二级 chips（含 `__all`/`__solo` 语义）、状态筛选联动计数、排序、搜索、分块渲染（CHUNK=120 + 加载更多）——逐项对照原型实现，行为一致。

- [ ] **Step 5: 跑绿**——`node tests/run_tests.js`，期望全部断言 PASS 且无 console error。

- [ ] **Step 6: Commit**

```bash
git add SurvivalLog.PlanPick/page.js SurvivalLog.PlanPick/tests/fixture.html SurvivalLog.PlanPick/tests/run_tests.js SurvivalLog.PlanPick/tests/.gitignore
git commit -m "feat(plankpick): page.js 弹层（v5 原型移植）+ 浏览器回归夹具全绿"
```

---

### Task 4: 运行时状态读取 + payload 组装

**Files:**
- Create: `SurvivalLog.PlanPick/PlanState.cs`

**Interfaces:**
- Consumes: Task 2 `PlanCatalog.Plans`；本文件头"已实证访问路径"表
- Produces: `PlanState.TryBuildPayload(out string json, out List<string> logLines)` —— json 即 Task 3 的 payload 规范；`PlanState.IsTodayCandidate(int id)`（Task 6 消费）；`PlanState.CurrentDay()`

- [ ] **Step 1: 写 PlanState.cs**：

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivalLog.PlanPick;

internal static class PlanState
{
    private static HashSet<int> _today;      // 今日候选（含刷新后）
    private static int _day = -1;

    internal static int CurrentDay() { ReadStates(); return _day; }
    internal static bool IsTodayCandidate(int id) { ReadStates(); return _today != null && _today.Contains(id); }

    /// <summary>读 Redux 状态树。任何一步失败保持旧值（下一轮重试）。</summary>
    private static void ReadStates()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;
            if (dataTree == null) return;

            GameCore.HotUpdate.ReduxUI.BaseReduxState raw;
            dataTree.TryGetValue(Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_UI_RogueSelect)), out raw);
            var ui = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_UI_RogueSelect>();
            if (ui != null)
            {
                _day = ui.Day;
                var cached = ui.CachedSurvivalPlanIds;     // il2cpp List<int>
                if (cached != null)
                {
                    var set = new HashSet<int>();
                    for (int i = 0; i < cached.Count; i++) set.Add(cached[i]);
                    _today = set;
                }
            }
        }
        catch { /* 保持旧值 */ }
    }

    /// <summary>组装 payload JSON。返回 false=依赖未就绪（不推送）。</summary>
    internal static bool TryBuildPayload(out string json, out List<string> logs)
    {
        json = null; logs = new List<string>();
        var cm = SurvivalLog.ModShared.BagTable.Cm();
        if (cm == null || !PlanCatalog.Build(cm)) return false;
        ReadStates();
        if (_day < 0) return false;

        int role = 1; try { role = GameCore.HotUpdate.ConfigManager.GetCurrentPlayerSelectId(); } catch { }
        int points = 0;
        HashSet<int> activated = null;
        try
        {
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;
            GameCore.HotUpdate.ReduxUI.BaseReduxState raw;
            dataTree.TryGetValue(Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Web_RogueSelect)), out raw);
            var web = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Web_RogueSelect>();
            if (web != null && web.OwnPoints != null) points = web.OwnPoints.Value;
            dataTree.TryGetValue(Il2CppInterop.Runtime.Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Talent)), out raw);
            var talent = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Talent>();
            if (talent?.SurvivalPlanningIds != null)
            {
                activated = new HashSet<int>();
                var sp = talent.SurvivalPlanningIds;
                for (int i = 0; i < sp.Count; i++) activated.Add(sp[i]);
            }
        }
        catch { }
        if (activated == null) return false;

        // 条件管理器（特殊条件判定）
        GameCore.HotUpdate.Battle.Logic.ConditionSetManager csm = null;
        try { csm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance?._ConditionSetManager; } catch { }

        // 角色名表
        var roleNames = new Dictionary<int, string>();
        try { foreach (var kv in cm._Config_PlayerSelect_Dict) roleNames[kv.Key] = kv.Value.Name_Local ?? ("角色" + kv.Key); } catch { }

        var mult = Math.Max(1f, PlanPickPlugin.CostMultiplier.Value);
        var showLocked = PlanPickPlugin.ShowLocked.Value; var showUnpooled = PlanPickPlugin.ShowUnpooled.Value;

        var sb = new StringBuilder(64 * 1024);
        sb.Append("{\"plans\":[");
        int n = 0, shown = 0;
        foreach (var p in PlanCatalog.Plans)
        {
            n++;
            if (p.UnlockDay == null && !showUnpooled) continue;
            // lockState: bought/locked/ok（点数是否够 JS 侧用 points 判，不在此算）
            string state; var reasons = new List<string>();
            if (activated.Contains(p.Id)) state = "bought";
            else
            {
                if (p.UnlockDay != null && _day < p.UnlockDay.Value) reasons.Add("第 " + p.UnlockDay.Value + " 天解锁（当前第 " + _day + " 天）");
                foreach (var pre in p.PrereqIds) if (!activated.Contains(pre))
                    {
                        var preName = PlanCatalog.Plans.Find(x => x.Id == pre)?.Name ?? ("#" + pre);
                        reasons.Add("需先激活「" + preName + "」");
                    }
                if (p.RoleIds.Count > 0 && !p.RoleIds.Contains(role))
                    reasons.Add("仅限「" + JoinRoleNames(p.RoleIds, roleNames) + "」的专属规划");
                if (p.SpecialDesc.Count > 0)
                {
                    if (csm != null)
                    {
                        bool ok = false;
                        try { } finally { }
                        // 特殊条件走官方判定：需要 ConditionSetId——PlanEntry 未带，改为从 cm 复查
                        // （见 Step 2：给 PlanEntry 补 ConditionSetId 字段）
                        ok = CheckSpecial(cm, csm, p);
                        if (!ok) reasons.AddRange(p.SpecialDesc);
                    }
                    else reasons.AddRange(p.SpecialDesc);
                }
                state = reasons.Count > 0 ? (showLocked ? "locked" : "skip") : "ok";
            }
            if (state == "skip") continue;
            if (n > 1 && shown > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(p.Id)
              .Append(",\"name\":\"").Append(J(p.Name)).Append("\",\"desc\":\"").Append(J(p.Desc)).Append('"')
              .Append(",\"icon\":\"").Append(J(p.Icon)).Append("\",\"cost\":").Append(p.Cost)
              .Append(",\"isReget\":").Append(p.IsReget ? "true" : "false")
              .Append(",\"cat\":\"").Append(p.Cat).Append("\",\"sub\":").Append(p.Sub == null ? "null" : "\"" + J(p.Sub) + "\"")
              .Append(",\"unlockDay\":").Append(p.UnlockDay == null ? "null" : p.UnlockDay.Value.ToString())
              .Append(",\"today\":").Append(_today != null && _today.Contains(p.Id) ? "true" : "false")
              .Append(",\"exclusive\":").Append(p.RoleIds.Count > 0 ? "true" : "false")
              .Append(",\"state\":\"").Append(state).Append("\",\"reasons\":[");
            for (int i = 0; i < reasons.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(J(reasons[i])).Append('"'); }
            sb.Append("]}");
            shown++;
        }
        sb.Append("],\"categories\":[{\"key\":\"attribute\",\"name\":\"属性成长\"},{\"key\":\"function\",\"name\":\"功能强化\"},")
          .Append("{\"key\":\"expand\",\"name\":\"家具扩建\"},{\"key\":\"material\",\"name\":\"物资贸易\"}]")
          .Append(",\"today\":").Append(_today != null ? J(string.Join(",", _today)) : "\"\"")
          .Append(",\"points\":").Append(points).Append(",\"mult\":").Append(mult.ToString("0.##"))
          .Append(",\"day\":").Append(_day).Append(",\"role\":").Append(role)
          .Append(",\"roleNames\":{");
        int rn = 0; foreach (var kv in roleNames) { if (rn++ > 0) sb.Append(','); sb.Append(kv.Key).Append(":\"").Append(J(kv.Value)).Append('"'); }
        sb.Append("}}");
        json = sb.ToString();
        logs.Add("规划 " + shown + "/" + n + " 项（今日候选 " + (_today == null ? 0 : _today.Count) + "，第 " + _day + " 天，点数 " + points + "，倍率 x" + mult.ToString("0.##") + "）");
        return shown > 0;
    }

    private static bool CheckSpecial(GameCore.HotUpdate.ConfigManager cm,
        GameCore.HotUpdate.Battle.Logic.ConditionSetManager csm, PlanEntry p)
    {
        try
        {
            var cfg = cm.Get_Config_Talent(p.Id);
            if (cfg == null || cfg.ConditionSetId == 0) return true;   // 无条件视为满足
            return csm.CheckConditionSet(cfg.ConditionSetId);
        }
        catch { return false; }
    }

    private static string JoinRoleNames(List<int> ids, Dictionary<int, string> names)
    {
        var parts = new List<string>();
        foreach (var id in ids) parts.Add(names.TryGetValue(id, out var n) ? n : ("角色" + id));
        return string.Join(" / ", parts);
    }

    private static string J(string s) { /* 最小 JSON 转义 */ 
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 2: 补 PlanEntry.ConditionSetId**——Task 2 的 `PlanEntry` 增 `public int ConditionSetId;` 并在构建时赋值 `r.ConditionSetId`（`CheckSpecial` 复查可改为直接用 `p.ConditionSetId`，删除上面代码中"从 cm 复查"的中间注释与调用，直接 `return csm.CheckConditionSet(p.ConditionSetId)`；`p.ConditionSetId==0` 时返回 true）。同时把 Step 1 代码里 `try { } finally { }` 占位段清理为最终形态。

- [ ] **Step 3: 构建**，期望 0。

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.PlanPick/PlanState.cs SurvivalLog.PlanPick/PlanCatalog.cs
git commit -m "feat(plankpick): 运行时状态读取（今日候选/天数/已激活/点数/角色）+ payload 组装"
```

---

### Task 5: 注入器（Root 巡检 → iframe 注入 → 推送）

**Files:**
- Create: `SurvivalLog.PlanPick/Injector.cs`
- Modify: `SurvivalLog.PlanPick/PlanPickPlugin.cs`（Ticker.Update 接入 Injector.Tick()）

**Interfaces:**
- Consumes: `ViewportFit.GetWebView` 同款路径、`PlanState.TryBuildPayload`、内嵌资源 `page.js`
- Produces: `Injector.Tick()`（每 2s 由 PlanTicker 调用，幂等）

- [ ] **Step 1: 写 Injector.cs**：

```csharp
using System;
using System.Reflection;

namespace SurvivalLog.PlanPick;

internal static class Injector
{
    private static string _pageJs;          // 内嵌资源缓存
    private static bool _pageJsWarned;
    private static bool _announced;
    private static string _lastPayloadSig;  // day+points+候选数+已激活数 签名，变化才推

    internal static void Tick()
    {
        var web = GetWebView();
        if (web == null) { _announced = false; return; }
        if (_pageJs == null && !_pageJsWarned)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SurvivalLog.PlanPick.page.js");
                if (stream == null) { _pageJsWarned = true; PlanPickPlugin.Log.LogWarning("[PlanPick] 内嵌资源 page.js 缺失"); return; }
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                _pageJs = reader.ReadToEnd();
            }
            catch (Exception e) { _pageJsWarned = true; PlanPickPlugin.Log.LogWarning("[PlanPick] 读取 page.js 失败: " + e.Message); return; }
        }

        // Root 上下文：找 RogueSelect iframe，注入 page.js（每帧一次幂等）+ 推 payload
        var js =
            "(function(){var f=document.getElementById('RogueSelect');if(!f||f.style.display==='none'||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;w.__pp=" + JsonOfString(_pageJs) + ";w.eval(w.__pp);return 1;})()";
        object alive = null;
        try { alive = EvalAsync(web, js); } catch (Exception e) { if (!_announced) PlanPickPlugin.Log.LogWarning("[PlanPick] 注入异常: " + e.Message); }

        if (!PlanState.TryBuildPayload(out var json, out var logs)) return;
        foreach (var l in logs) { } // ListOnReady 首轮在下方统一打
        try
        {
            var push = "(function(){var f=document.getElementById('RogueSelect');if(!f||!f.contentWindow||!f.contentWindow.__PlanPickBoot)return 0;" +
                       "f.contentWindow.__PlanPickBoot(" + json + ");return 1;})()";
            EvalAsync(web, push);
        }
        catch (Exception e) { if (!_announced) PlanPickPlugin.Log.LogWarning("[PlanPick] 推送异常: " + e.Message); }

        if (!_announced)
        {
            _announced = true;
            foreach (var l in PlanCatalog.BuildLog) PlanPickPlugin.Log.LogInfo("[PlanPick] 目录: " + l);
            foreach (var l in logs) PlanPickPlugin.Log.LogInfo("[PlanPick] " + l);
        }
    }

    private static object EvalAsync(Vuplex.WebView.IWebView web, string js)
    {
        // ExecuteJavaScript(script, callback) —— callback 可空；套件先例 ViewportFit 直接调用
        web.ExecuteJavaScript(js, null);
        return 1;
    }

    private static Vuplex.WebView.IWebView GetWebView()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return null;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            return rus?.GetWebUILayer()?.canvasWebViewPrefab?.WebView;
        }
        catch { return null; }
    }

    /// <summary>把整段 page.js 作为 JSON 字符串字面量嵌进注入脚本（复用 PlanState.J 同款转义）。</summary>
    private static string JsonOfString(string s) => "\"" + PlanState.JsEscapePublic(s) + "\"";
}
```

注意三点（执行时落实）：
1. `PlanState.J` 改为 `internal static string JsEscapePublic(string s)`（Task 4 的私有方法公开化改名，Task 4/5 两文件同 commit 前先改签名再编译）。
2. `web.ExecuteJavaScript(js, callback)` 的确切重载以 interop 程序集为准：先试 `ExecuteJavaScript(string, Action<string>)` 传 `null`；编译不过则用单参重载（ViewportFit 只用单参，保底方案）。
3. 每轮都 `w.eval(w.__pp)` 代价大——page.js 内部必须幂等（`__PlanPickInstall` 标记已装则跳过安装只更新数据）；且注入脚本把 `__pp` 赋值改为 `if(!w.__ppInstalled)` 才 eval（page.js 安装成功后自置 `w.__ppInstalled=true`）。按此把上面的 js 字符串改为条件安装版本。

- [ ] **Step 2: Ticker 接线**——`PlanTicker.Update` 的 2s 分支改为调用 `Injector.Tick()`。

- [ ] **Step 3: 构建**，期望 0。跑 Task 3 夹具确认 page.js 幂等安装（fixture 反复调 `__PlanPickBoot` 不重复插 DOM——在 run_tests.js 加断言：连调两次后 `.pp-overlay` 仍只有一个）。

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.PlanPick/Injector.cs SurvivalLog.PlanPick/PlanPickPlugin.cs SurvivalLog.PlanPick/tests/run_tests.js
git commit -m "feat(plankpick): Root 巡检注入器（iframe 幂等安装 + payload 推送）"
```

---

### Task 6: Harmony 翻倍补丁 + moddeps 登记

**Files:**
- Create: `SurvivalLog.PlanPick/CostPatch.cs`
- Modify: `SurvivalLog.PlanPick/PlanPickPlugin.cs`（Load 里 `var h = new HarmonyLib.Harmony(Guid); h.PatchAll();`）
- Modify: `tools/gamekb/moddeps.json`

**Interfaces:**
- Consumes: `PlanState.IsTodayCandidate`、`BagTable.Cm()._Config_Talent_Dict`/`Get_Config_Talent`
- Produces: `OnBuySurvivalPlan` prefix/finalizer（运行时自证日志 `[PlanPick][Cost]`）

- [ ] **Step 1: 写 CostPatch.cs**：

```csharp
using System;
using System.Reflection;

namespace SurvivalLog.PlanPick;

/// <summary>
/// 自选消耗翻倍：prefix 判"不在今日候选"→临时把该行 LvUpCost ×倍率（内存表编辑，RoleUnlock 同款）；
/// finalizer 无条件恢复（异常路径也还原）。官方候选内购买零干预（原生零接触承诺，DESIGN §3）。
/// </summary>
[HarmonyLib.HarmonyPatch]
internal static class CostPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.SurvivalResultsManager), "OnBuySurvivalPlan");

    private static int _boostedId;
    private static int _origCost;

    private static void Prefix(int talentId)
    {
        _boostedId = 0;
        try
        {
            var mult = Math.Max(1f, PlanPickPlugin.CostMultiplier.Value);
            if (mult <= 1f) return;                       // 无倍率零干预
            if (PlanState.IsTodayCandidate(talentId)) return;   // 今日候选：官方原价路径
            var cm = SurvivalLog.ModShared.BagTable.Cm();
            var cfg = cm?.Get_Config_Talent(talentId);
            if (cfg == null) return;
            _origCost = cfg.LvUpCost;
            cfg.LvUpCost = (int)Math.Round(_origCost * mult);
            _boostedId = talentId;
            PlanPickPlugin.Log.LogInfo("[PlanPick][Cost] 自选 " + talentId + " 原价 " + _origCost + " -> " + cfg.LvUpCost);
        }
        catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick][Cost] prefix 异常: " + e.Message); }
    }

    private static void Finalizer()
    {
        if (_boostedId == 0) return;
        try
        {
            var cfg = SurvivalLog.ModShared.BagTable.Cm()?.Get_Config_Talent(_boostedId);
            if (cfg != null) { cfg.LvUpCost = _origCost; }
            PlanPickPlugin.Log.LogInfo("[PlanPick][Cost] 已恢复 " + _boostedId + " 原价 " + _origCost);
        }
        catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick][Cost] finalizer 异常: " + e.Message); }
        _boostedId = 0;
    }
}
```

- [ ] **Step 2: Load 接线**——`PlanPickPlugin.Load()` 在 Ticker 创建前加：

```csharp
var harmony = new HarmonyLib.Harmony(Guid);
harmony.PatchAll();
```

- [ ] **Step 3: moddeps.json 登记**——在 `tools/gamekb/moddeps.json` 增加 PlanPick 条目（照现有条目结构，先读文件看格式再写）：Harmony 目标 `GameCore.HotUpdate.Battle.Logic.SurvivalResultsManager.OnBuySurvivalPlan(int)`；表依赖 `Config_Talent.{Type,Available,LvUpCost,WebIcon,ConditionSetId,PlayerSelectId,Name_Local,Dec_Local,IsReget}`、`Config_RandomGroup.{GroupType,IdList}`、`Config_DailyRandom.{Day,RandomPlanID,SpecifiedPlanID}`、`Config_ConditionSet.{TalentNeed,SurvivalToDay,NeedFurnitureIds,EventTag,DefenseLevel,NeedTrapObtainable,Source}`、`Config_PlayerSelect.Name_Local`；状态依赖 `State_UI_RogueSelect.CachedSurvivalPlanIds/Day`、`State_Data_Talent.SurvivalPlanningIds`、`State_Web_RogueSelect.OwnPoints`、`BattleLogicWorld._ConditionSetManager`、`ReduxUISystem.GetWebUILayer`；页面 `UI/RogueSelect`；通知 15003 语义依赖。

- [ ] **Step 4: 构建全绿**——同命令；再跑 `node tests/run_tests.js` 复绿（确保 Task 5 改动没破坏夹具）。

- [ ] **Step 5: modcompat 核对**——`node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732`，确认 PlanPick 新条目无红灯（exit 0）。

- [ ] **Step 6: Commit**

```bash
git add SurvivalLog.PlanPick/CostPatch.cs SurvivalLog.PlanPick/PlanPickPlugin.cs tools/gamekb/moddeps.json
git commit -m "feat(plankpick): 自选消耗翻倍（Harmony prefix/finalizer + 内存表临时编辑）+ moddeps 登记"
```

---

### Task 7: 收尾（DESIGN 同步 + 部署说明 + 总验）

**Files:**
- Modify: `SurvivalLog.PlanPick/DESIGN.md`
- Create: `SurvivalLog.PlanPick/VERIFY.md`（装机验证步骤，给作者）

- [ ] **Step 1: DESIGN.md 同步**——§11 迭代清单追加 `2026-10-24 实装（按 docs/plans/2026-10-24-plankpick.md）`；§4 修订一句：分类/系列改**运行期从表一次性构建**（等价冻结、游戏更新零维护），生成脚本保留为对照真源。

- [ ] **Step 2: 写 VERIFY.md**——装机验证清单（作者执行）：① 启动游戏进档，过一天触发 RogueSelect；② 看日志 `[PlanPick] loaded` / 目录统计 / 推送行；③ 弹层四 tab + 二级筛选 + 锁定原因（角色名真名）；④ 今日候选内买一张=原价（`[PlanPick][Cost]` 无日志）；⑤ 自选买一张=×2 且有 prefix/恢复两条日志、点数扣对、重开规划页"已激活"；⑥ 钉选中的候选被自选购买后钉释放；⑦ 图纸（不入池）可买；⑧ 关 cfg ShowLocked 后未解锁项消失；⑨ 卸载 dll 还原（无存档残留）。

- [ ] **Step 3: 最终构建 + 夹具全绿 + 退出码留档**（build.log 尾部 exit 0、run_tests.js 全 PASS 截进 commit message 或 VERIFY.md）。

- [ ] **Step 4: Commit**

```bash
git add SurvivalLog.PlanPick/DESIGN.md SurvivalLog.PlanPick/VERIFY.md
git commit -m "docs(plankpick): 实装收尾——DESIGN 迭代清单 + 装机验证清单"
```

- [ ] **Step 5: 本机部署（可选，作者在场时）**——确认 SurvivalLog.exe 不在跑（`tasklist` 原始输出核对，GBK 控制台勿用 grep）后：`cp SurvivalLog.PlanPick/bin/Release/SurvivalLog.PlanPick.dll "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/"`。**不进 publish.ps1/安装器**（发版待作者定号）。

---

## Self-Review（已执行）

1. **Spec 覆盖**：DESIGN §1 功能（入口/全展示/分类/锁定原因/翻倍/官方链复用）→ Task 3/4/5/6；§3 零接触承诺 → Task 6 prefix 判定；§4 分类 → Task 2；§5 配置 → Task 1；§6 验证清单 → Task 7 VERIFY.md；§10 文件结构 → 全部任务。无缺口。
2. **占位扫描**：Task 4 Step 1 段内含一处带注释的中间态代码（`try{}finally{}` + "见 Step 2"），Step 2 明确删除/替换——这是刻意的两步走指令而非未完成占位；其余无 TBD。
3. **类型一致性**：`PlanEntry.Sub`（string，null=独立）在 Task 2 定义、Task 4 序列化用 `null`/字符串 ✓；`PlanState.J` → Task 5 改名 `JsEscapePublic` 有显式指令 ✓；`__PlanPickBoot` payload 字段在 Task 3 Step 1 规范与 Task 4 序列化逐字段对应（plans[]/categories/today/points/mult/day/role/roleNames + 每项 state/reasons 由 C# 给）——Task 3 夹具 golden 转换须补 `state/reasons` 字段（由 day=20/role=0/无已激活推导，转换脚本内联实现，与原型 evalLock 同语义）✓。
