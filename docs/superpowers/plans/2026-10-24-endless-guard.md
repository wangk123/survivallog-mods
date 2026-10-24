# EndlessGuard（无尽守卫）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `SurvivalLog.EndlessGuard` mod：拦无尽沦陷 + 旧档恢复 + 剥倒计时 tag + 注入 11 条拾遗扩展规划（内容扩池 + 官方探索物资难度数量层）。

**Architecture:** BepInEx IL2CPP 插件（net6.0，Harmony + 内存表编辑，零存档写入）。四个功能块：FallMapPoint prefix 拦沦陷；Initialize postfix 恢复旧档；Ticker 轮询注入 Config_Talent/Config_ConditionSet 表 + 洞察卡注册表活引用追加 + RandomGroup 篮子/降档；RollExploreLootCopies postfix 数量层。

**Tech Stack:** C# net6.0 / BepInEx.Unity.IL2CPP / HarmonyLib / Il2CppInterop（游戏 interop 程序集直连）；离线校验用 Node.js（读 gamekb 表快照）。

**Spec:** `SurvivalLog.EndlessGuard/DESIGN.md`（权威设计文档，实现前通读；本计划所有数值与机制结论出自该文档 §2.5/§5/§6/§7/§8）。

## Global Constraints（每个任务隐含遵守）

- **构建判退出码**：`"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.EndlessGuard -c Release > build.log 2>&1; echo $?` 必须回显 0；**禁止管道 tail**（AGENTS 硬规则 8）。
- **git 提交外科手术式**：只 `git add <明确文件清单>`，**禁止 `git add -A` / `git add .`**（硬规则 2）；本 mod 不碰作者已脏文件。
- **版本号不自定**：csproj Version 写 `1.3.8`（跟随套件当前 version.json），不增不改（硬规则 1）。
- **不进发布白名单**：不改 publish.ps1 / installer.iss（作者发话前）。
- **部署前查游戏进程**：`tasklist //FI "IMAGENAME eq SurvivalLog.exe"`（看原始输出，GBK 控制台 grep 不可靠）——游戏在跑就中止任务择时重试（硬规则 3）。
- **验证用自证文件**：日志写 `BepInEx/EndlessGuard_trace.txt`，不依赖 LogOutput.log（硬规则 9）。
- **游戏启动方式**：`powershell Start-Process 'steam://rungameid/4164790'`；表就绪约主菜单后 15-25 秒（硬规则 4）。
- **数量层红线**：不得新增任何私有刷新曲线（锚点/抽取数/周期/每柜数量），丰富度只走 `RollExploreLootCopies` 公式（DESIGN §6）。
- **存档零写入**：所有改动都是内存态；不写任何存档字段、不写文件到存档目录（DESIGN §2.6）。
- 所有 C# 源码文件 UTF-8；注释密度与套件现有 mod 一致（机制注释带实证依据）。

## 关键类型与 API（已全部实证，直接照用；实现者勿再发明访问路径）

| 用途 | 访问方式（本仓库已验证模式） |
|---|---|
| ConfigManager | `SurvivalLog.ModShared.BagTable.Cm()`（返回 null=未就绪） |
| 表字典 | `cm._Config_Talent_Dict` / `cm._Config_ConditionSet_Dict` / `cm._Config_RandomGroup_Dict`（`Il2CppSystem.Collections.Generic.Dictionary<int, ...>`） |
| 已购规划名单 | Redux：`BaseSingleton<ReduxUISystem>.Instance.reduxStoreLayer.dataTree.TryGetValue(Il2CppType.From(typeof(State_Data_Talent)), out raw)` → `raw.TryCast<State_Data_Talent>().SurvivalPlanningIds`（PlanPick PlanState.cs:73-84 同款） |
| 当前图 ID | `BaseSingleton<BattleLogicWorld>.Instance._ExploreManager.MapConfigId`（interop 暴露 internal/private） |
| TagManager | `BaseSingleton<BattleLogicWorld>.Instance._TagManager` |
| GameCounter | 静态类：`GameCore.HotUpdate.Battle.Logic.GameCounter.GetCounter(key)` / `SetCounter(key, value)` |
| 洞察注册表活引用 | `ExploreDiscoveryTools.GetInsightCards(mapId)` 返回**注册表内的活 List\<int\>**（0x2826350 反汇编证：EnsureRegistry 后直接返回字典值，非副本）——`.Add()` 即改注册表 |
| 营地判定 | `EndlessManager.IsCampMapPoint(int)` 公共静态 |
| Harmony 挂私有方法 | `[HarmonyPatch]` + `static MethodBase TargetMethod() => AccessTools.Method(typeof(T), "方法名")`（PlanPick CostPatch 同款） |

---

### Task 1: 数据单源 JSON + 离线校验脚本（红→绿）

**Files:**
- Create: `SurvivalLog.EndlessGuard/scavenge_plans.json`（数据单源，后续作为 EmbeddedResource）
- Create: `tools/endlessguard_check.js`（离线校验：对 gamekb 快照验证数据合法性）

**Interfaces:**
- Produces: `scavenge_plans.json` 结构（后续 Task 5/6/7 按此解析）：
```json
{
  "plans": [
    {"name":"超市拾遗·六","talentId":31606,"condId":48201,"day":100,"prereq":31605,"cost":30,"mapId":1004,"level":6},
    {"name":"医院踏勘·三","talentId":31623,"condId":48202,"day":120,"prereq":31622,"cost":32,"mapId":1005,"level":3},
    {"name":"学校摸排·三","talentId":31633,"condId":48203,"day":140,"prereq":31632,"cost":32,"mapId":1002,"level":3},
    {"name":"超市拾遗·七","talentId":31607,"condId":48204,"day":160,"prereq":31606,"cost":38,"mapId":1004,"level":7},
    {"name":"医院踏勘·四","talentId":31624,"condId":48205,"day":180,"prereq":31623,"cost":44,"mapId":1005,"level":4},
    {"name":"学校摸排·四","talentId":31634,"condId":48206,"day":200,"prereq":31633,"cost":44,"mapId":1002,"level":4},
    {"name":"超市拾遗·八","talentId":31608,"condId":48207,"day":220,"prereq":31607,"cost":46,"mapId":1004,"level":8},
    {"name":"医院踏勘·五","talentId":31625,"condId":48208,"day":240,"prereq":31624,"cost":56,"mapId":1005,"level":5},
    {"name":"学校摸排·五","talentId":31635,"condId":48209,"day":260,"prereq":31634,"cost":56,"mapId":1002,"level":5},
    {"name":"超市拾遗·九","talentId":31609,"condId":48210,"day":280,"prereq":31608,"cost":54,"mapId":1004,"level":9},
    {"name":"超市拾遗·十","talentId":31610,"condId":48211,"day":300,"prereq":31609,"cost":62,"mapId":1004,"level":10}
  ],
  "baskets": [
    {"mapId":1004,"level":6,"poolId":8006,"items":[{"name":"精制碘盐","copies":2},{"name":"白砂糖","copies":2},{"name":"黑胡椒粒","copies":2},{"name":"纯牛奶","copies":2},{"name":"车达芝士片","copies":2},{"name":"饼干","copies":2},{"name":"糖","copies":2}]},
    {"mapId":1004,"level":7,"poolId":8006,"items":[{"name":"德式大肉肠","copies":2},{"name":"速食味噌汤","copies":2},{"name":"咖啡","copies":2},{"name":"草莓","copies":2},{"name":"西瓜","copies":2}]},
    {"mapId":1004,"level":8,"poolId":8006,"items":[{"name":"金枪鱼鱼段","copies":2},{"name":"小猫鱼","copies":2},{"name":"水果运输箱","copies":1}]},
    {"mapId":1004,"level":9,"poolId":8006,"items":[{"name":"半扇猪肋排","copies":1},{"name":"完整牛里脊","copies":1},{"name":"零食大礼包","copies":1}]},
    {"mapId":1004,"level":10,"poolId":8006,"items":[{"name":"整只全羊","copies":1},{"name":"巨型火鸡","copies":1},{"name":"完整猪后腿","copies":1}]},
    {"mapId":1005,"level":3,"poolId":8024,"items":[{"name":"维生素","copies":2},{"name":"康复新液","copies":2},{"name":"家庭急救手册","copies":1}]},
    {"mapId":1005,"level":4,"poolId":8024,"items":[{"name":"治疗药","copies":2},{"name":"强效消毒喷雾","copies":2},{"name":"叮当罐子","copies":1},{"name":"荧光棒","copies":2}]},
    {"mapId":1005,"level":5,"poolId":8024,"items":[{"name":"吗啡注射液","copies":1},{"name":"整箱功能饮料","copies":1},{"name":"高浓度消毒喷雾","copies":2},{"name":"照明弹","copies":1}]},
    {"mapId":1002,"level":3,"poolId":8014,"items":[{"name":"草莓","copies":2},{"name":"西瓜","copies":2},{"name":"卷烟","copies":2}]},
    {"mapId":1002,"level":4,"poolId":8014,"items":[{"name":"白玉菇种子","copies":2},{"name":"甜菜种子","copies":2},{"name":"韭菜种子","copies":2}]},
    {"mapId":1002,"level":5,"poolId":8014,"items":[{"name":"松茸","copies":2},{"name":"灵芝","copies":2}]}
  ],
  "demotions": [
    {"poolId":8009,"itemId":20310,"fromLevel":12,"toLevel":6,"note":"万能加固件·纸箱"},
    {"poolId":8010,"itemId":20320,"fromLevel":13,"toLevel":8,"note":"开锁器·货车"},
    {"poolId":8009,"itemId":20330,"fromLevel":14,"toLevel":9,"note":"撬棍·纸箱"},
    {"poolId":8010,"itemId":20330,"fromLevel":15,"toLevel":10,"note":"撬棍·货车"},
    {"poolId":8024,"itemId":11008,"fromLevel":6,"toLevel":3,"note":"幸存者的应急药品·药品柜"},
    {"poolId":8026,"itemId":2404,"fromLevel":7,"toLevel":5,"note":"兴奋剂·实验器皿"},
    {"poolId":8018,"itemId":15013,"fromLevel":6,"toLevel":4,"note":"松茸种子·实验室"},
    {"poolId":8018,"itemId":15014,"fromLevel":8,"toLevel":5,"note":"灵芝种子·实验室"},
    {"poolId":8017,"itemId":2308,"fromLevel":8,"toLevel":5,"note":"冷冻整只火鸡·食堂"}
  ],
  "nativeUnlocks": [
    {"poolId":8006,"itemId":2145,"level":7},{"poolId":8006,"itemId":2163,"level":7},{"poolId":8006,"itemId":2121,"level":9},
    {"poolId":8010,"itemId":20310,"level":10},{"poolId":8025,"itemId":20320,"level":4},
    {"poolId":8014,"itemId":9032,"level":3},{"poolId":8014,"itemId":2121,"level":5},
    {"poolId":8017,"itemId":2151,"level":3},{"poolId":8017,"itemId":2201,"level":5},
    {"poolId":8018,"itemId":20330,"level":5},{"poolId":8023,"itemId":9030,"level":4}
  ]
}
```

- [ ] **Step 1: 写校验脚本（先红）**

创建 `tools/endlessguard_check.js`，内容完整如下：

```js
// EndlessGuard 数据单源离线校验：node endlessguard_check.js
// 校验 scavenge_plans.json 对 gamekb 快照的合法性；全绿 exit 0，任何一条红 exit 1。
const fs = require('fs');
const path = require('path');
const ROOT = path.join(__dirname, '..');
const KB = fs.readdirSync(path.join(ROOT, 'gamekb')).filter(d => /^\d{4}-\d{2}-\d{2}_/.test(d)).sort().pop();
const T = f => JSON.parse(fs.readFileSync(path.join(ROOT, 'gamekb', KB, 'tables', f), 'utf8')).data;
const data = JSON.parse(fs.readFileSync(path.join(ROOT, 'SurvivalLog.EndlessGuard', 'scavenge_plans.json'), 'utf8'));
let bad = 0;
const fail = m => { console.error('[FAIL]', m); bad++; };
const ok = m => console.log('[ok]', m);

// 1) plans：11 条、ID/条件集在 gamekb 空闲、链一致、日/价单调
const talentIds = new Set(T('Config_Talent.json').map(r => r._ID_k__BackingField));
const condIds = new Set(T('Config_ConditionSet.json').map(r => r.ID ?? r._ID_k__BackingField));
if (data.plans.length !== 11) fail('plans 应为 11 条，实际 ' + data.plans.length);
for (const p of data.plans) {
  if (talentIds.has(p.talentId)) fail(`talentId ${p.talentId} 已被占用`);
  if (condIds.has(p.condId)) fail(`condId ${p.condId} 已被占用`);
}
const byMap = {};
for (const p of data.plans) (byMap[p.mapId] = byMap[p.mapId] || []).push(p);
for (const [m, list] of Object.entries(byMap)) {
  list.sort((a, b) => a.level - b.level);
  for (let i = 1; i < list.length; i++) {
    if (list[i].day <= list[i - 1].day) fail(`图${m} ${list[i].name} 解锁日未递增`);
    if (list[i].cost <= list[i - 1].cost) fail(`图${m} ${list[i].name} 消耗未递增`);
    if (list[i].prereq !== list[i - 1].talentId) fail(`图${m} ${list[i].name} 前置链断裂`);
  }
}
const first = { 1004: 31605, 1005: 31622, 1002: 31632 };
for (const [m, f] of Object.entries(first)) {
  const l = byMap[m][0];
  if (l.prereq !== f) fail(`图${m} 首条前置应为 ${f}，实际 ${l.prereq}`);
}
if (!bad) ok('plans：11 条，ID 全空闲，链/日/价全单调');

// 2) baskets：名字在 Config_Item 唯一命中、不在对应图现有池
const items = T('Config_Item.json');
const byName = new Map();
for (const r of items) {
  const n = r._ItemName_Local_k__BackingField;
  if (n == null) continue;
  if (!byName.has(n)) byName.set(n, []); byName.get(n).push(r._ID_k__BackingField);
}
const rg = new Map(T('Config_RandomGroup.json').map(r => [r._ID_k__BackingField, r]));
const mapPools = { 1004: [8001,8002,8003,8004,8005,8006,8007,8008,8009,8010], 1005: [8024,8025,8026,8215,8216,8217,8218,8219,8220], 1002: [8012,8013,8014,8015,8016,8017,8018,8019,8020,8023,8221,8222,8223,8224,8225,8226] };
const poolItems = {};
for (const [m, ids] of Object.entries(mapPools)) {
  const s = new Set();
  for (const gid of ids) for (const i of (rg.get(gid)?._IdList_k__BackingField || [])) s.add(i);
  poolItems[m] = s;
}
const resolvedIds = [];
for (const b of data.baskets) {
  if (!mapPools[b.mapId].includes(b.poolId)) fail(`basket 图${b.mapId} 池 ${b.poolId} 不属于该图`);
  for (const it of b.items) {
    const hits = byName.get(it.name);
    if (!hits || hits.length === 0) { fail(`物品名未命中：${it.name}`); continue; }
    if (hits.length > 1) { fail(`物品名多义（${hits.join(',')}）：${it.name}`); continue; }
    const id = hits[0];
    if (poolItems[b.mapId].has(id)) fail(`物品已在图${b.mapId}池中：${it.name}(${id})`);
    it.itemId = id; resolvedIds.push(`${it.name}=${id}`);
  }
}
if (!bad) ok('baskets：' + resolvedIds.length + ' 项全部唯一命中且不在现有池');

// 3) demotions：池里确实有该物品且原门槛等于 fromLevel
for (const d of data.demotions) {
  const g = rg.get(d.poolId);
  const idx = (g?._IdList_k__BackingField || []).indexOf(d.itemId);
  if (idx < 0) { fail(`降档目标不在池 ${d.poolId}：item ${d.itemId}`); continue; }
  const lv = (g._DiscoverLvList_k__BackingField || [])[idx];
  if (lv !== d.fromLevel) fail(`池 ${d.poolId} item ${d.itemId} 实际门槛 Lv${lv}，声明 fromLevel=${d.fromLevel}`);
  if (d.toLevel >= d.fromLevel) fail(`降档必须降：${d.note}`);
}
if (!bad) ok('demotions：9 项原门槛全部核对一致');

// 4) nativeUnlocks：池内物品在该等级门槛存在（防表版本漂移）
for (const n of data.nativeUnlocks) {
  const g = rg.get(n.poolId);
  const idx = (g?._IdList_k__BackingField || []).indexOf(n.itemId);
  if (idx < 0) { fail(`原生解锁项不在池 ${n.poolId}：item ${n.itemId}`); continue; }
  const lv = (g._DiscoverLvList_k__BackingField || [])[idx];
  if (lv !== n.level) fail(`原生项 ${n.poolId}:${n.itemId} 实际 Lv${lv}，声明 ${n.level}`);
}
if (!bad) ok('nativeUnlocks：11 项原生门槛核对一致');

// 5) 每级覆盖检查：11 张卡每张至少 1 项解锁物（篮子+降档+原生 并集）
const unlockAt = new Map();
const bump = (mapId, level) => unlockAt.set(mapId + ':' + level, (unlockAt.get(mapId + ':' + level) || 0) + 1);
for (const b of data.baskets) bump(b.mapId, b.level);
for (const d of data.demotions) {
  const p = data.plans.find(x => x.mapId === (rg.get(d.poolId) ? mapOfPool(d.poolId) : 0) && x.level === d.toLevel);
  if (p) bump(p.mapId, p.level);
}
for (const n of data.nativeUnlocks) bump(mapOfPool(n.poolId), n.level);
function mapOfPool(pid) {
  for (const [m, ids] of Object.entries(mapPools)) if (ids.includes(Number(pid))) return Number(m);
  return 0;
}
for (const p of data.plans) {
  const c = unlockAt.get(p.mapId + ':' + p.level) || 0;
  if (c === 0) fail(`${p.name}（Lv${p.level}）无任何解锁物（空档级）`);
}
if (!bad) ok('每张卡至少 1 项解锁物，无空档');

if (bad) { console.error(`共 ${bad} 条失败`); process.exit(1); }
console.log('ALL GREEN');
```

- [ ] **Step 2: 跑脚本确认红**

Run: `cd E:/Game/Mod/mod_stack && node tools/endlessguard_check.js`
Expected: FAIL（`scavenge_plans.json` 不存在，读取抛错）→ exit 1。

- [ ] **Step 3: 写数据单源文件**

创建 `SurvivalLog.EndlessGuard/scavenge_plans.json`，内容 = 本任务 Interfaces 一节给出的完整 JSON（原样照抄，不改任何数值）。

- [ ] **Step 4: 跑脚本确认绿**

Run: `cd E:/Game/Mod/mod_stack && node tools/endlessguard_check.js`
Expected: 5 行 `[ok]` + `ALL GREEN`，exit 0。
（若红：物品名未命中/多义 → 用 `node -e "const j=require('./gamekb/2026-10-23_26157732/tables/Config_Item.json');for(const r of j.data)if(/名字片段/.test(r._ItemName_Local_k__BackingField||''))console.log(r._ID_k__BackingField,r._ItemName_Local_k__BackingField)"` 查准确名后改 JSON 的 name 字段重跑；**禁止改物品 ID 之外的数值**。）

- [ ] **Step 5: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/scavenge_plans.json tools/endlessguard_check.js
git commit -m "feat(EndlessGuard): 数据单源 scavenge_plans.json + gamekb 离线校验脚本（11 条目/篮子/降档/原生门槛全绿）"
```

---

### Task 2: 工程骨架 + 插件入口（可编译空壳 + 启动自证）

**Files:**
- Create: `SurvivalLog.EndlessGuard/SurvivalLog.EndlessGuard.csproj`
- Create: `SurvivalLog.EndlessGuard/EndlessGuardPlugin.cs`

**Interfaces:**
- Consumes: ModShared（`WelcomeTip.cs`；BagTable.Cm() 暂不用但 csproj 先链入）
- Produces: `EndlessGuardPlugin`（BepInPlugin Guid=`com.local.survivallog.endlessguard`）；静态成员 `Log`、cfg 五键 `PreventNewFalls/RestoreFallenPoints/StripPendingFallTags/ExtendScavengePlans/RichnessStep`、`internal static void Trace(string line)`（全 mod 共用自证文件写入器）；Ticker 类（2s 轮询，后续任务挂工作）。

- [ ] **Step 1: 写 csproj**

`SurvivalLog.EndlessGuard.csproj`：以 `SurvivalLog.RoleUnlock/SurvivalLog.RoleUnlock.csproj` 为底本，改 `AssemblyName`/`RootNamespace` 为 `SurvivalLog.EndlessGuard`，`Version` 保持 `1.3.8`，ItemGroup 增加两行：

```xml
    <Compile Include="../ModShared/BagTableRw.cs" />
    <Compile Include="../ModShared/WelcomeTip.cs" />
    <EmbeddedResource Include="scavenge_plans.json" />
```

并在 Reference 组补 Harmony（路径同 PlanPick csproj 第 67-69 行）：

```xml
    <Reference Include="0Harmony">
      <HintPath>E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/core/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

- [ ] **Step 2: 写插件入口**

`EndlessGuardPlugin.cs`：

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.EndlessGuard;

/// <summary>无尽守卫：拦沦陷/旧档恢复/剥倒计时tag/生存规划扩展。机制与实证见 DESIGN.md。</summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class EndlessGuardPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.endlessguard";
    public const string Name = "EndlessGuard";
    public const string Version = "1.3.8"; // 跟随套件 version.json（作者定号）

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> PreventNewFalls;
    internal static ConfigEntry<bool> RestoreFallenPoints;
    internal static ConfigEntry<bool> StripPendingFallTags;
    internal static ConfigEntry<bool> ExtendScavengePlans;
    internal static ConfigEntry<float> RichnessStep;

    public override void Load()
    {
        Log = base.Log;
        PreventNewFalls = Config.Bind("Fall", "PreventNewFalls", true, "拦截全部新沦陷（三模式通吃）。");
        RestoreFallenPoints = Config.Bind("Fall", "RestoreFallenPoints", true, "读档后恢复已沦陷的交易点/探险图（营地除外，恢复例程幂等）。");
        StripPendingFallTags = Config.Bind("Fall", "StripPendingFallTags", true, "剥掉沦陷倒计时 tag（前缀运行时读，读不到自动降级跳过）。");
        ExtendScavengePlans = Config.Bind("Plans", "ExtendScavengePlans", true, "注入 11 条拾遗扩展规划（超市·六~十/医院·三~五/学校·三~五，D100-D300）+ 洞察注册表 + 篮子/降档。");
        RichnessStep = Config.Bind("Plans", "RichnessStep", 0.3f, "数量层每张已购新卡的期望份数加成（官方探索物资难度值之上叠加；0=纯官方难度）。");

        var harmony = new HarmonyLib.Harmony(Guid);
        // 各补丁类由后续功能块自带 [HarmonyPatch]，统一在此 PatchAll
        harmony.PatchAll(typeof(EndlessGuardPlugin).Assembly);

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<GuardTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<GuardTicker>();
        var go = new GameObject("EndlessGuard.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GuardTicker>();

        Trace("loaded：cfg=" + PreventNewFalls.Value + "," + RestoreFallenPoints.Value + "," + StripPendingFallTags.Value + "," + ExtendScavengePlans.Value + ",step=" + RichnessStep.Value);
        Log.LogInfo("[EndlessGuard] loaded（拦沦陷/恢复/剥tag/规划扩展）");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    /// <summary>自证文件（硬规则9：不依赖 LogOutput.log）。每行带时间戳。</summary>
    internal static void Trace(string line)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Paths.BepInExRootPath, "EndlessGuard_trace.txt"), $"[{DateTime.Now:HH:mm:ss}] {line}\n"); }
        catch { }
    }
}

/// <summary>2s 轮询：表注入与注册表追加在 Cm() 就绪后执行（早于 PlanPick 目录构建=首次开规划页）。</summary>
internal sealed class GuardTicker : MonoBehaviour
{
    internal static volatile bool PlansInjected;
    private float _acc;
    public GuardTicker(IntPtr p) : base(p) { }
    private void Update()
    {
        _acc += UnityEngine.Time.unscaledDeltaTime;
        if (_acc < 2f) return;
        _acc = 0f;
        try { ScavengePlans.Tick(); } catch (Exception e) { EndlessGuardPlugin.Trace("ticker ScavengePlans: " + e.Message); }
    }
}
```

注意：`ScavengePlans.Tick()` 本任务先建**空实现占位**（后续 Task 5 充实），避免编译错：

```csharp
namespace SurvivalLog.EndlessGuard;

internal static class ScavengePlans
{
    internal static void Tick() { }
}
```

（单独放 `ScavengePlans.cs`，Task 5 会重写此文件。）

- [ ] **Step 3: 构建判退出码**

Run: `cd E:/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.EndlessGuard -c Release > SurvivalLog.EndlessGuard/build.log 2>&1; echo $?`
Expected: `0`（非 0 → 读 build.log 修到 0，不许带病前进）。

- [ ] **Step 4: 装机冒烟（先查游戏没在跑）**

Run: `tasklist //FI "IMAGENAME eq SurvivalLog.exe"`（原始输出无该进程名才继续；在跑则中止本步择时重试）。
```bash
cp "E:/Game/Mod/mod_stack/SurvivalLog.EndlessGuard/bin/Release/net6.0/SurvivalLog.EndlessGuard.dll" "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/" && echo deployed
powershell Start-Process 'steam://rungameid/4164790'
sleep 40
cat "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/EndlessGuard_trace.txt"
```
Expected: trace 文件含 `[..:..:..] loaded：cfg=True,...` 行。
收尾：任务结束前关游戏 `taskkill //IM SurvivalLog.exe //F`，`sleep 3`，再 `tasklist` 确认死透（硬规则 3）。

- [ ] **Step 5: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/SurvivalLog.EndlessGuard.csproj SurvivalLog.EndlessGuard/EndlessGuardPlugin.cs SurvivalLog.EndlessGuard/ScavengePlans.cs
git commit -m "feat(EndlessGuard): 工程骨架+插件入口+Ticker+trace自证（装机冒烟通过）"
```

---

### Task 3: 功能1 FallBlocker（拦沦陷）

**Files:**
- Create: `SurvivalLog.EndlessGuard/FallBlocker.cs`

**Interfaces:**
- Consumes: `EndlessGuardPlugin.Trace/PreventNewFalls`
- Produces: Harmony 补丁（自动被 Task 2 的 PatchAll 挂上）；静态计数 `FallBlocker.BlockedCount`（供集成验证读取）。

- [ ] **Step 1: 写补丁**

```csharp
using System.Reflection;
using HarmonyLib;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 拦沦陷：EndlessManager.FallMapPoint 是全游戏唯一沦陷执行点（唯一调用方 ApplyFallenMapPoints，
/// 反汇编 0x264CD90/0x2649140，DESIGN §2.1）。prefix 恒 false = "未新沦陷"跳过分支（test al,al; je 实证），
/// 无横幅/遗产事件/封锁写入。参数 (GameSaveData, TagManager, int, int, ref string, bool, bool) 全非泛型，interop 兼容。
/// </summary>
[HarmonyPatch]
internal static class FallBlocker
{
    internal static int BlockedCount;

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(GameCore.HotUpdate.Battle.Logic.EndlessManager), "FallMapPoint");

    private static bool Prefix()
    {
        if (!EndlessGuardPlugin.PreventNewFalls.Value) return true; // 开关关=官方原行为
        BlockedCount++;
        if (BlockedCount == 1 || BlockedCount % 10 == 0)
            EndlessGuardPlugin.Trace("FallBlocker：已拦截 " + BlockedCount + " 次沦陷（累计）");
        return false;
    }
}
```

- [ ] **Step 2: 构建 + 装机验挂载**

Run（同 Task 2 Step 3 构建，退出码 0）→ 部署 dll → 启动游戏到主菜单 → 查 trace。
Expected: 无异常；BepInEx 控制台/LogOutput.log 若可见应有 Harmony 挂载记录；trace 无新增报错行。Harmony 挂载失败的静默症状 = 无 BlockedCount 输出——真实验证留给 Task 8 的 GM 触发（探针兜底：本步只证"游戏带补丁正常启动"）。

- [ ] **Step 3: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/FallBlocker.cs
git commit -m "feat(EndlessGuard): 功能1 拦沦陷——FallMapPoint prefix 恒 false（唯一咽喉实证）"
```

---

### Task 4: 功能2+3 FallRestorer（旧档恢复 + 剥 tag）

**Files:**
- Create: `SurvivalLog.EndlessGuard/FallRestorer.cs`

**Interfaces:**
- Consumes: `EndlessGuardPlugin.Trace/RestoreFallenPoints/StripPendingFallTags`；`BagTable.Cm()`（读 Config_MapPoint 判交易类）；`GameCore.HotUpdate.Battle.Logic.GameCounter.SetCounter`（静态）；`EndlessManager.IsCampMapPoint(int)`（公共静态）。
- Produces: Harmony 补丁（挂 `EndlessManager.Initialize(GameSaveData, GameMode)` postfix）；幂等（每次读档重跑）。

- [ ] **Step 1: 写恢复器**

```csharp
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 旧档恢复+剥倒计时tag：postfix 挂 EndlessManager.Initialize（读档补账 ApplyFallenMapPoints 之后跑，
/// DESIGN §2.3）。四步全走原版 API（存档格式层零风险论证见 DESIGN §2.6）：
/// ①FallenMapPointIds 名单剔除非营地 ②fall_tier counter 清零 ③剥全局交易封印tag ④剥缓刑tag。
/// 全程 try/catch + trace，幂等（半途异常下次读档跑齐）。
/// </summary>
[HarmonyPatch]
internal static class FallRestorer
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EndlessManager), "Initialize");

    private static void Postfix(EndlessManager __instance, GameCore.HotUpdate.GameSaveData __0)
    {
        try
        {
            if (!EndlessGuardPlugin.RestoreFallenPoints.Value) return;
            if (__instance == null || __0 == null) return;
            if (__instance.IsActive != true) return;   // 非无尽档空跑（IsActive 公共属性）
            var save = __0;
            var list = save.FallenMapPointIds;
            if (list == null || list.Count == 0) { EndlessGuardPlugin.Trace("restore：名单为空，无事可做"); return; }

            var restored = new List<int>();
            for (int i = list.Count - 1; i >= 0; i--)   // 倒序 Remove 安全
            {
                var id = list[i];
                bool isCamp = false;
                try { isCamp = EndlessManager.IsCampMapPoint(id); } catch { }
                if (isCamp) continue;                    // 营地永不恢复（DESIGN §6 红线）
                list.RemoveAt(i);
                GameCounter.SetCounter("endless.fall_tier_" + id, 0);
                restored.Add(id);
            }

            // ③全局交易封印：只要恢复了任意点就剥（幂等：HasTag 防呆）
            var tm = BaseSingleton<BattleLogicWorld>.IsInstanceCreated
                ? BaseSingleton<BattleLogicWorld>.Instance?._TagManager : null;
            if (tm != null && restored.Count > 0 && tm.HasTag("EndlessAllTradePointsFallen"))
            {
                var tags = new Il2CppSystem.Collections.Generic.List<string>();
                tags.Add("EndlessAllTradePointsFallen");
                tm.RemoveTags(tags);
            }

            // ④剥缓刑tag（前缀运行时读私有静态字段，读不到降级）
            int stripped = 0;
            if (EndlessGuardPlugin.StripPendingFallTags.Value && tm != null)
            {
                var prefix = ReadDeferredFallPrefix();
                if (prefix != null)
                {
                    var keys = tm.GetTagKeys();
                    var doomed = new Il2CppSystem.Collections.Generic.List<string>();
                    for (int i = 0; i < keys.Count; i++)
                        if (keys[i] != null && keys[i].StartsWith(prefix)) doomed.Add(keys[i]);
                    if (doomed.Count > 0) { tm.RemoveTags(doomed); stripped = doomed.Count; }
                }
            }

            EndlessGuardPlugin.Trace($"restore：恢复 {restored.Count} 点 [{string.Join(",", restored)}]，剥缓刑tag {stripped} 条");
        }
        catch (System.Exception e)
        {
            EndlessGuardPlugin.Trace("restore 异常（下次读档重试）: " + e.Message);
        }
    }

    /// <summary>TAG_DEFERRED_FALL_PREFIX 私有静态字段运行时读（不硬编码，抗表更新；读失败返回 null 降级）。</summary>
    private static string ReadDeferredFallPrefix()
    {
        try
        {
            var f = typeof(EndlessManager).GetField("TAG_DEFERRED_FALL_PREFIX",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return f?.GetValue(null) as string;
        }
        catch { return null; }
    }
}
```

实现者注意两点（勿改）：
- `save.FallenMapPointIds` 是 interop 属性（原生字段偏移 0xE0 的 List\<int\>），`RemoveAt` 即改存档对象树；
- `TAG_DEFERRED_FALL_PREFIX` 若 interop 反射拿不到 il2cpp 静态字段（返回 null），**降级跳过就是正确行为**，不要改成硬编码字符串。

- [ ] **Step 2: 构建**

Run: 同 Task 2 Step 3。Expected: 退出码 0。

- [ ] **Step 3: 装机验证（用测试档）**

1. 复制无尽测试档：`cp -r "C:/Users/wangk/AppData/LocalLow/LLS/SLGame/Saves" "C:/Users/wangk/AppData/LocalLow/LLS/SLGame/Saves_eg_test_backup"`（留底）。
2. 部署 dll → 启动游戏 → 进该无尽档一次（读到主场景）→ 退游戏 → 查 trace。
Expected: trace 出现 `restore：恢复 N 点 [...]，剥缓刑tag M 条`（N=该档已沦陷的非营地点数；N=0 也算通过——档本身没沦陷点）。
3. `tasklist` 确认游戏退出后再做任何文件操作。

- [ ] **Step 4: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/FallRestorer.cs
git commit -m "feat(EndlessGuard): 功能2+3 旧档恢复四步清账+剥tag（Initialize postfix，幂等）"
```

---

### Task 5: 功能4a 表注入 + 洞察注册表追加

**Files:**
- Rewrite: `SurvivalLog.EndlessGuard/ScavengePlans.cs`（替换 Task 2 的空占位）
- Read: `SurvivalLog.EndlessGuard/scavenge_plans.json`（EmbeddedResource）

**Interfaces:**
- Consumes: `BagTable.Cm()`；`ExploreDiscoveryTools.GetInsightCards(int)`（活 List 引用）；数据 JSON（Task 1 结构）。
- Produces: `ScavengePlans.Tick()`（Ticker 调）；`ScavengePlans.OwnedNewCount(int mapId)`（Task 7 数量层消费——读 Redux 已购名单 ∩ 该图新卡 ID，返回 0..5）；`ScavengePlans.Plans`（`List<PlanRow>`，PlanRow 字段同 JSON）。
- Produces（对内）: `ScavengePlans.ApplyContent()`（Task 6 将实现，本任务留 `internal static void ApplyContent() { }` 空占位并在 Tick 末尾调用——Task 6 重写它）。

- [ ] **Step 1: 写实现**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime;
using SurvivalLog.ModShared;

namespace SurvivalLog.EndlessGuard;

internal sealed class PlanRow
{
    public string Name; public int TalentId, CondId, Day, Prereq, Cost, MapId, Level;
}

/// <summary>
/// 生存规划扩展：读档期把 11 条 Talent/ConditionSet 行注进内存表 + 新卡 ID 追加进洞察注册表活引用。
/// 时机：Ticker 每 2s 检查 Cm() 就绪即注（远早于 PlanPick 目录构建=首次开规划页，DESIGN §5）。
/// 幂等：dict.ContainsKey 防重注；注册表 Contains 防重加。表是进程内存态，不写任何存档（DESIGN §2.6）。
/// </summary>
internal static class ScavengePlans
{
    internal static List<PlanRow> Plans = new();
    private static bool _loaded;
    private static bool _injected;
    internal static int InjectedCount;   // 自证

    internal static void Tick()
    {
        if (!EndlessGuardPlugin.ExtendScavengePlans.Value) return;
        if (!_loaded) { LoadJson(); if (!_loaded) return; }
        var cm = BagTable.Cm();
        if (cm == null) return;                 // 表未就绪，下轮再试
        if (!_injected) { Inject(cm); _injected = true; ApplyContent(); }
        AppendRegistry();                       // 注册表可能被 EnsureRegistry 重载覆盖：每轮补（Contains 幂等，开销可忽略）
    }

    private static void LoadJson()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SurvivalLog.EndlessGuard.scavenge_plans.json");
            if (s == null) { EndlessGuardPlugin.Trace("plans：内嵌资源缺失（csproj 检查 EmbeddedResource）"); return; }
            // 零外部依赖：System.Text.Json（net6 自带，套件不引 Newtonsoft）
            using var doc = System.Text.Json.JsonDocument.Parse(new StreamReader(s).ReadToEnd());
            foreach (var p in doc.RootElement.GetProperty("plans").EnumerateArray())
            {
                Plans.Add(new PlanRow
                {
                    Name = p.GetProperty("name").GetString(),
                    TalentId = p.GetProperty("talentId").GetInt32(),
                    CondId = p.GetProperty("condId").GetInt32(),
                    Day = p.GetProperty("day").GetInt32(),
                    Prereq = p.GetProperty("prereq").GetInt32(),
                    Cost = p.GetProperty("cost").GetInt32(),
                    MapId = p.GetProperty("mapId").GetInt32(),
                    Level = p.GetProperty("level").GetInt32()
                });
            }
            _loaded = true;
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("plans LoadJson: " + e.Message); }
    }

    private static void Inject(GameCore.HotUpdate.ConfigManager cm)
    {
        try
        {
            var tDict = cm._Config_Talent_Dict;
            var cDict = cm._Config_ConditionSet_Dict;
            var icon = tDict.TryGetValue(31601, out var proto) ? (proto.Icon, proto.WebIcon) : ("", "");
            foreach (var p in Plans)
            {
                if (tDict.ContainsKey(p.TalentId)) continue;
                var t = new GameCore.HotUpdate.Config_Talent();
                t.ID = p.TalentId; t.TalentID = p.TalentId; t.Lv = 1; t.Available = 1;
                t.DemoTwoMode = 0; t.EndlessMode = 0; t.IsReget = false;
                t.Name = "Talent_Name_" + p.TalentId; t.Name_Local = p.Name;
                t.Dec = "Talent_Dec_" + p.TalentId; t.Dec_Local = "搜索能力增强，可以发现更多被忽略的物资（发现等级+" + p.Level + "档）";
                t.Type = 2; t.ShowType = 1; t.Icon = icon.Item1; t.WebIcon = icon.Item2;
                t.LvUpCost = p.Cost; t.BuffID = 0; t.BuffRefresh = 0;
                t.ConditionSetId = p.CondId; t.PlayerSelectId = null;
                tDict[p.TalentId] = t;

                if (cDict.ContainsKey(p.CondId)) continue;
                var c = NewCondSet(p.CondId);
                c.Source = "PlanLvGate_" + p.TalentId;    // PlanPick 系列推导靠此前缀（DESIGN §5 要素4）
                c.TalentNeed.Add(p.Prereq);
                c.SurvivalToDay = p.Day;                  // 卡面"生存至第N天"（PlanCatalog.cs:112）
                cDict[p.CondId] = c;
                InjectedCount++;
            }
            EndlessGuardPlugin.Trace("plans 注入 " + InjectedCount + "/11 行（icon=" + icon.Item2 + "）");
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("plans Inject: " + e.Message); }
    }

    /// <summary>ConditionSet 全列表字段必须非 null（官方消费方直接 .Count，null 即 NRE）。照 gamekb 列清单逐个建空表。</summary>
    private static GameCore.HotUpdate.Config_ConditionSet NewCondSet(int id)
    {
        var c = new GameCore.HotUpdate.Config_ConditionSet();
        c.ID = id;
        c.EventTag = new Il2CppSystem.Collections.Generic.List<int>();
        c.EventNoTag = new Il2CppSystem.Collections.Generic.List<int>();
        c.AttriKey = new Il2CppSystem.Collections.Generic.List<int>();
        c.Compare = new Il2CppSystem.Collections.Generic.List<int>();
        c.Number = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapAttriKey = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapCompare = new Il2CppSystem.Collections.Generic.List<int>();
        c.GapNumber = new Il2CppSystem.Collections.Generic.List<int>();
        c.ItemNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.BagRequire = new Il2CppSystem.Collections.Generic.List<int>();
        c.BagSatietyFurnitureIDs = new Il2CppSystem.Collections.Generic.List<int>();
        c.BuffTag = new Il2CppSystem.Collections.Generic.List<int>();
        c.EventTagOr = new Il2CppSystem.Collections.Generic.List<int>();
        c.BuffNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.SymbolNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.SymbolNoNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.TalentNeed = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureType = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureIds = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedFurnitureObtainable = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedTrapObtainable = new Il2CppSystem.Collections.Generic.List<int>();
        c.NeedItemCategory = 0; c.CounterKey = "";
        c.SurvivalToDay = 0; c.OpenTimeStart = 0; c.OpenTimeEnd = 0;
        c.SelfModelState = ""; c.SelfModelStateNot = "";
        return c;
    }

    /// <summary>注册表活引用追加：GetInsightCards 返回字典内的 List（0x2826350 实证非副本），Add 即生效。
    /// 追加后 GetDiscoveryLv 自动数到新卡（HasActivated 循环），官方追加波（AppendDiscoveryWave）下轮进图自动上货。</summary>
    private static void AppendRegistry()
    {
        try
        {
            foreach (var g in Plans.GroupBy(p => p.MapId))
            {
                var cards = GameCore.HotUpdate.Battle.Logic.ExploreDiscoveryTools.GetInsightCards(g.Key);
                if (cards == null) continue;
                foreach (var p in g)
                    if (!cards.Contains(p.TalentId)) { cards.Add(p.TalentId); EndlessGuardPlugin.Trace("registry：图" + g.Key + " +卡 " + p.TalentId); }
            }
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("registry: " + e.Message); }
    }

    /// <summary>该图已购新卡数（数量层 Task 7 消费）。Redux 已购名单 PlanPick 同款读取；读不到返回 0（fail-safe 无加成）。</summary>
    internal static int OwnedNewCount(int mapId)
    {
        try
        {
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var dataTree = rus?.reduxStoreLayer?.dataTree;
            if (dataTree == null) return 0;
            dataTree.TryGetValue(Il2CppType.From(typeof(GameCore.HotUpdate.ReduxUI.State_Data_Talent)), out var raw);
            var st = raw?.TryCast<GameCore.HotUpdate.ReduxUI.State_Data_Talent>();
            var ids = st?.SurvivalPlanningIds;
            if (ids == null) return 0;
            var set = new HashSet<int>();
            for (int i = 0; i < ids.Count; i++) set.Add(ids[i]);
            var n = 0; foreach (var p in Plans) if (p.MapId == mapId && set.Contains(p.TalentId)) n++;
            return n;
        }
        catch { return 0; }
    }

    // Task 6 实现（篮子+降档）；本任务空占位
    internal static void ApplyContent() { }
}
```

实现者注意：
- `t.PlayerSelectId = null` 若 interop 不接受 null 赋值，改为 `new Il2CppSystem.Collections.Generic.List<int>()`（空表=无角色限制，官方语义）。
- Inject 里 `t.Type = 2` 等属性名以 interop 实际为准（`Type`/`ShowType`/`IsReget` 都是 interop 属性）；构建报错时用 `mdump`（`tools/mdump`，命令见其 README/头注释）对照 Config_Talent 的属性名修正，**不改数值语义**。

- [ ] **Step 2: 构建 + 装机验证**

1. 构建退出码 0。
2. 部署 → 启动 → 进任意档 → trace 应有 `plans 注入 11/11 行` 与 `registry：图1004 +卡 31606` 等 11 行。
3. （有 PlanPick 时）开规划自选页：应能看到"超市拾遗·六"新卡、锁定原因"生存至第100天"。无 PlanPick 则跳过本小步。

- [ ] **Step 3: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/ScavengePlans.cs
git commit -m "feat(EndlessGuard): 功能4a 表注入11行+洞察注册表活引用追加（GetInsightCards实锚）"
```

---

### Task 6: 功能4b 内容层落表（篮子 + 降档）

**Files:**
- Modify: `SurvivalLog.EndlessGuard/ScavengePlans.cs`（重写 `ApplyContent()` 空占位；JSON 里的 `baskets`/`demotions` 段本任务开始消费）

**Interfaces:**
- Consumes: `BagTable.Cm()` 的 `_Config_RandomGroup_Dict`；JSON baskets（Step 1 会把 `name` 解析为 `itemId`，**运行时不需要再按名字查**——见下）；JSON demotions。
- Produces: `ApplyContent()`（Tick 在注入后调用一次）。

**关键决策（实现者照做）**：篮子物品的**显式 `itemId` 字段已在 Task 1 落地**（2026-10-24 裁决：同名物品表内存在，名字通道不可靠，itemId 机制从本任务提前）——本任务**直接消费** JSON 里现成的 `itemId`，不做任何名字查找。Task 1 的数据裁决同步记录：超市·六篮子 纯牛奶→面包(2502)、车达芝士片→意面(2501)（原两件已在超市池 8006@Lv0，违反去重）；超市·七 咖啡→首个核验通过的唯一饮品。具体：

- [ ] **Step 1: 确认数据就绪（itemId 已在 Task 1 落地，本步只核对）**

Run: `cd E:/Game/Mod/mod_stack && node tools/endlessguard_check.js; echo $?` → ALL GREEN + exit 0（itemId 字段与校验逻辑已在 Task 1 完成——2026-10-24 裁决前移）。历史参考（原 Step 1 的 ID 解析命令，仅供溯源，不再执行）：

```bash
node -e "
const j=require('./gamekb/2026-10-23_26157732/tables/Config_Item.json');
const names=['精制碘盐','白砂糖','黑胡椒粒','纯牛奶','车达芝士片','饼干','糖','德式大肉肠','速食味噌汤','咖啡','草莓','西瓜','金枪鱼鱼段','小猫鱼','水果运输箱','半扇猪肋排','完整牛里脊','零食大礼包','整只全羊','巨型火鸡','完整猪后腿','维生素','康复新液','家庭急救手册','治疗药','强效消毒喷雾','叮当罐子','荧光棒','吗啡注射液','整箱功能饮料','高浓度消毒喷雾','照明弹','卷烟','白玉菇种子','甜菜种子','韭菜种子','松茸','灵芝'];
for(const n of names){const hits=j.data.filter(r=>r._ItemName_Local_k__BackingField===n);console.log(n, hits.length===1?hits[0]._ID_k__BackingField:'AMBIGUOUS('+hits.length+')');}"
```

把输出的 ID 写进 JSON 每个 basket item 的 `"itemId": <ID>` 字段；同时给 `tools/endlessguard_check.js` 的 baskets 校验加一行（在 `it.itemId = id;` 之前）：

```js
    if (it.itemId !== undefined && it.itemId !== hits[0]) fail(`itemId 与名字不符：${it.name} json=${it.itemId} 实际=${hits[0]}`);
```

重跑 check：`node tools/endlessguard_check.js` → ALL GREEN。

- [ ] **Step 2: 实现 ApplyContent()**

替换空占位为（幂等三重防护：篮子只追加一次/降档记原值按原值推导/池缺失跳过并 trace）：

```csharp
    private static bool _contentDone;
    private static readonly Dictionary<long, int> OrigGates = new();   // poolId*100000+itemId -> 原门槛

    internal static void ApplyContent()
    {
        try
        {
            var cm = BagTable.Cm();
            var rgDict = cm?._Config_RandomGroup_Dict;
            if (rgDict == null) return;

            // 降档：记进程内首读原值，永远按 原值=fromLevel 的声明校验后写 toLevel（RoleUnlock 幂等模式）
            foreach (var d in Demotions)
            {
                if (!rgDict.TryGetValue(d.PoolId, out var g)) { EndlessGuardPlugin.Trace("content：缺池 " + d.PoolId); continue; }
                var idx = IndexOf(g.IdList, d.ItemId);
                if (idx < 0) { EndlessGuardPlugin.Trace("content：池" + d.PoolId + " 缺物品 " + d.ItemId + "（表漂移？跳过）"); continue; }
                var key = d.PoolId * 100000L + d.ItemId;
                if (!OrigGates.TryGetValue(key, out var orig)) { orig = g.DiscoverLvList[idx]; OrigGates[key] = orig; }
                if (orig != d.FromLevel) { EndlessGuardPlugin.Trace($"content：{d.Note} 原门槛 Lv{orig}≠声明 Lv{d.FromLevel}，跳过"); continue; }
                if (g.DiscoverLvList[idx] != d.ToLevel) g.DiscoverLvList[idx] = d.ToLevel;
            }

            // 篮子：一次追加（_contentDone 防重复；池追加后 IdList/CountList/DiscoverLvList 三表同步）
            if (!_contentDone)
            {
                int added = 0;
                foreach (var b in Baskets)
                {
                    if (!rgDict.TryGetValue(b.PoolId, out var g)) { EndlessGuardPlugin.Trace("content：缺池 " + b.PoolId); continue; }
                    foreach (var it in b.Items)
                    {
                        if (IndexOf(g.IdList, it.ItemId) >= 0) continue;   // 去重（check 脚本已离线保证）
                        g.IdList.Add(it.ItemId);
                        g.CountList.Add(it.Copies);
                        g.DiscoverLvList.Add(b.Level);
                        added++;
                    }
                }
                _contentDone = true;
                EndlessGuardPlugin.Trace("content：篮子追加 " + added + " 项 + 降档 " + Demotions.Count + " 项已对齐");
            }
        }
        catch (Exception e) { EndlessGuardPlugin.Trace("content: " + e.Message); }
    }

    private static int IndexOf(Il2CppSystem.Collections.Generic.List<int> list, int v)
    {
        if (list == null) return -1;
        for (int i = 0; i < list.Count; i++) if (list[i] == v) return i;
        return -1;
    }
```

配套：`LoadJson()` 里补解析 `baskets`/`demotions` 两个数组到 `internal static List<BasketRow> Baskets` / `List<DemoteRow> Demotions`（类定义：`BasketRow{MapId,Level,PoolId,List<BasketItem> Items}`、`BasketItem{ItemId,Copies}`、`DemoteRow{PoolId,ItemId,FromLevel,ToLevel,Note}`；解析代码照抄 plans 段的 `JsonDocument` 用法）。

- [ ] **Step 3: 构建 + 离线校验 + 装机**

1. `node tools/endlessguard_check.js` → ALL GREEN（itemId 校验生效）。
2. 构建退出码 0 → 部署 → 进档 → trace 有 `content：篮子追加 N 项 + 降档 9 项已对齐`（N = 篮子物品总数）。

- [ ] **Step 4: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/ScavengePlans.cs SurvivalLog.EndlessGuard/scavenge_plans.json tools/endlessguard_check.js
git commit -m "feat(EndlessGuard): 功能4b 内容层——篮子扩池+官方封印货降档（幂等+原值校验）"
```

---

### Task 7: 数量层 LootRichnessPatch（官方难度通道叠加）

**Files:**
- Create: `SurvivalLog.EndlessGuard/LootRichnessPatch.cs`

**Interfaces:**
- Consumes: `ScavengePlans.OwnedNewCount(int mapId)`（Task 5）；`EndlessGuardPlugin.RichnessStep`；当前图 `_ExploreManager.MapConfigId`。
- Produces: Harmony postfix（自动 PatchAll 挂载）。

**语义红线（写进代码注释）**：官方 `RollExploreLootCopies` 返回 0 = 难度档判定"该件不出现"（稀少/极少档）——**不救回**，加成只作用于"已出现"的件（`__result > 0` 才加），尊重玩家建档时的难度选择（DESIGN §2.5.4）。

- [ ] **Step 1: 写补丁**

```csharp
using System.Reflection;
using HarmonyLib;
using GameCore.HotUpdate.Battle.Logic;

namespace SurvivalLog.EndlessGuard;

/// <summary>
/// 数量层（DESIGN §2.5.4 v5 定稿）：官方探索物资难度值 + RichnessStep×该图已购新卡数。
/// 挂官方 RollExploreLootCopies（地面/柜子/追加波三路自动全生效），零私有刷新曲线。
/// __result==0（难度跳过）不救回；非三图不加成。
/// </summary>
[HarmonyPatch]
internal static class LootRichnessPatch
{
    private static readonly System.Random Rng = new();

    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(DifficultyTools), "RollExploreLootCopies");

    private static void Postfix(ref int __result)
    {
        try
        {
            var step = EndlessGuardPlugin.RichnessStep.Value;
            if (step <= 0f || __result <= 0) return;      // 难度跳过的件不救回
            var map = CurMap();
            if (map != 1002 && map != 1004 && map != 1005) return;
            var owned = ScavengePlans.OwnedNewCount(map);
            if (owned <= 0) return;
            var bonus = step * owned;
            var whole = (int)bonus;
            if (Rng.NextDouble() < bonus - whole) whole++;
            if (whole > 0) __result += whole;
        }
        catch { /* fail-safe：任何异常=本件不加成，官方结果原样 */ }
    }

    private static int CurMap()
    {
        if (!BaseSingleton<BattleLogicWorld>.IsInstanceCreated) return 0;
        var em = BaseSingleton<BattleLogicWorld>.Instance?._ExploreManager;
        return em == null ? 0 : em.MapConfigId;
    }
}
```

- [ ] **Step 2: 构建 + 装机**

构建退出码 0 → 部署 → 进档 → 进超市图搜一轮柜子 → trace 无异常（数量层行为验证归 Task 8 清单第 7 步）。

- [ ] **Step 3: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add SurvivalLog.EndlessGuard/LootRichnessPatch.cs
git commit -m "feat(EndlessGuard): 数量层v5——RollExploreLootCopies postfix 官方难度通道叠加（难度跳过不救回）"
```

---

### Task 8: moddeps 同步 + DESIGN 迭代记录 + 集成装机验证

**Files:**
- Modify: `tools/gamekb/moddeps.json`（新增 EndlessGuard 段）
- Modify: `SurvivalLog.EndlessGuard/DESIGN.md`（§9 迭代记录补一行实现落地）

**Interfaces:**
- Consumes: 全部前序任务产物（dll 已含四功能块）。

- [ ] **Step 1: moddeps.json 加段**

在 JSON 顶层对象里追加（schema 照 StackLimit999 段：type/method/params）：

```json
  "EndlessGuard": {
    "harmony": [
      { "type": "GameCore.HotUpdate.Battle.Logic.EndlessManager", "method": "FallMapPoint" },
      { "type": "GameCore.HotUpdate.Battle.Logic.EndlessManager", "method": "Initialize" },
      { "type": "GameCore.HotUpdate.Battle.Logic.DifficultyTools", "method": "RollExploreLootCopies" }
    ],
    "tables": [
      "Config_Talent._Config_Talent_Dict",
      "Config_ConditionSet._Config_ConditionSet_Dict",
      "Config_RandomGroup._Config_RandomGroup_Dict"
    ],
    "apis": [
      "ExploreDiscoveryTools.GetInsightCards",
      "ExploreDiscoveryTools.GetDiscoveryLv",
      "EndlessManager.IsCampMapPoint",
      "GameCounter.SetCounter",
      "TagManager.GetTagKeys/RemoveTags/HasTag",
      "ReduxUI.State_Data_Talent.SurvivalPlanningIds",
      "BattleLogicWorld._ExploreManager.MapConfigId"
    ]
  }
```

（若现有文件无 `tables`/`apis` 键先例，保持顶层 schema 兼容：对照文件内既有键，harmony 段必加，tables/apis 若无先例则并入注释性字段或按现有最大信息量写法——以 `node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732` 跑绿为准。）

- [ ] **Step 2: modcompat 核对**

Run: `cd E:/Game/Mod/mod_stack && node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732; echo $?`
Expected: 全绿 exit 0（红=挂钩点签名漂移，回到对应任务修）。

- [ ] **Step 3: 集成装机验证清单（DESIGN §7 逐项）**

按顺序执行并每项记录 pass/fail 到 trace（用测试档，先 `tasklist` 确认游戏未运行）：
1. 启动进无尽测试档 → trace：`loaded` / `plans 注入 11/11` / `registry +卡`×11 / `content：篮子追加...` / `restore：...` 全出现；
2. 地图界面：沦陷点恢复（名字不再"已沦陷"）、无人机交易入口在、倒计时消失；
3. 连续跨天（游玩或 GM 推进 tier）：trace `FallBlocker：已拦截 N 次` 增长且地图无新沦陷；
4. PlanPick 自选页：11 张新卡在"超市拾遗/医院踏勘/学校摸排"系列下、锁定原因正确、买一张（D100+ 档）扣点成功；
5. 买卡后进对应图：柜子里出现该级新解锁物品（官方追加波+注册表生效铁证）；
6. 买满超市·十后进超市数一轮总件数：对比买卡前 ≈2.5 倍量级（标准档，允许体感浮动）；
7. 卸载：删 dll → 重进测试档 → 无异常（孤儿 ID 容错验证，DESIGN §6 发布闸门）。
任一 fail → 停下取证报作者，不带病收尾。

- [ ] **Step 4: DESIGN.md 迭代记录**

§9 迭代维护清单下补一行（日期用当天）：

```
- 2026-10-24 实现落地：四功能块按本设计实施，装机验证清单见 plans/2026-10-24-endless-guard.md Task 8；moddeps 已同步。
```

- [ ] **Step 5: 提交**

```bash
cd E:/Game/Mod/mod_stack
git add tools/gamekb/moddeps.json SurvivalLog.EndlessGuard/DESIGN.md
git commit -m "feat(EndlessGuard): moddeps 同步+DESIGN 迭代记录+集成装机验证（四功能块收官）"
```

---

## 自审记录（writing-plans Self-Review）

1. **Spec 覆盖**：DESIGN §2.2→Task 3；§2.3/2.4→Task 4；§2.5.2→Task 1+5；§2.5.3→Task 1+6；§2.5.4→Task 7；§2.5.5 注册表→Task 5；§5 PlanPick 兼容→Task 5 注入时机+Source 前缀+验证步骤；§7 验证方案→Task 8 清单；§8 moddeps→Task 8。§6 红线（营地排除/不进官方池/无私有曲线/孤儿ID验证）分别落在 Task 4 代码、Task 5 注释、Task 7 注释、Task 8 第 7 项。
2. **占位符扫描**：Task 2/5 的"空占位"是显式的两阶段实现策略（先骨架后充实），非 TBD；全部代码块完整可编译语义。
3. **类型一致性**：`ScavengePlans.Tick/OwnedNewCount/ApplyContent/Plans` 在 Task 2（占位）、5（实现）、6（重写 ApplyContent）、7（消费 OwnedNewCount）间签名一致；`EndlessGuardPlugin.Trace` 五处消费一致。
