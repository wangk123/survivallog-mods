# RoleUnlock 生存规划互通 + 花瓶配方 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 SurvivalLog.RoleUnlock mod 追加两个纯表编辑功能——①46 条功能类角色专属生存规划（天赋）全角色互通；②花瓶包裹配方 325 等级化解锁（不碰剧情事件）。

**Architecture:** 沿用现有 `RoleTable.Apply` 轮询幂等模式（5 秒一轮、首见快照原值、原地改写 il2cpp 表字段、表重载后自动补写）。零 Harmony、零存档写入、零事件触发。新功能**不新增 cfg 开关**（作者定案：默认生效，删 dll 即回官方）。

**Tech Stack:** C# / BepInEx.Unity.IL2CPP / Il2CppInterop（仓库自带 sdk/dotnet）。

**Spec:** 终版方案见会话定案（2026-10-24 作者确认）；机制实证记录在 `SurvivalLog.RoleUnlock/DESIGN.md`（本计划 Task 3 同步更新）。关键决策摘要：
- 46 条功能天赋互通 = 清 `Config_Talent.PlayerSelectId`（空列表 = `IsRoleAllowed` 全放行，已反汇编实证）
- 77 条家具位扩建天赋**必须排除**（绑定各角色地图；关键词黑名单：`位·`/`扩建`/`腾出一处`/`置物位`）
- 花瓶配方 325：`craft_level` 0→1、`show_level` 0→1（对齐工具柜包裹 389 的 1/1 档）；暖房工程台 434 / 中转仓 435 **不做**（作者定案）
- 连带自动生效（不写代码）：大学生 6 条烹饪配方、燃烧瓶强化 2 条、老主顾等随天赋互通

## Global Constraints

- **版本号不动**：`RoleUnlockPlugin.Version` 保持 `"1.3.8"`（版本号只由作者经 version.json 定义，硬规则 1）。
- **git 外科手术**：只 `git add` 明确列出的文件，**禁止 `git add -A` / `git add .`**（硬规则 2）。
- **工作树有作者未提交改动（v1.3.8 发版准备中）且 `SurvivalLog.RoleUnlock/` 整目录未入库**：本计划所有 commit 步骤**仅在作者明确同意后执行**；未获同意则跳过提交、保留工作树改动，并在任务收尾说明。
- **构建判退出码**（硬规则 8）：`"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.RoleUnlock -c Release > build.log 2>&1; echo $?`——禁止管道 `| tail`。本仓库 mod 无单测框架，每任务的"测试"= 构建退出码 + 静态核对；运行时验证集中在 Task 4。
- **游戏单实例红线**（硬规则 3）：部署前 `tasklist | findstr SurvivalLog` 看原始输出确认游戏未运行；游戏在跑则**中止部署**择时重试，绝不杀进程。
- **改表字段必须同步 `tools/gamekb/moddeps.json`**（硬规则 11，Task 3）。
- il2cpp 字段访问全部 try/catch 包裹（访问已卸载对象会抛）；字符串属性（`Name_Local`/`Dec_Local`）经 interop 隐式转 string，用前判空。
- 禁止触碰：藤蔓/花种/防御配方现有逻辑、现有 6 个 cfg 项、任何 Config_Event/剧情相关表。

---

### Task 1: 生存规划天赋互通（RoleTable 追加 Talent 步骤）

**Files:**
- Modify: `SurvivalLog.RoleUnlock/RoleUnlockPlugin.cs`

**Interfaces:**
- Consumes: 现有 `Snapshot(Il2CppSystem...List<int>) → List<int>`、`SyncList(list, target) → bool`、`RoleTable.Apply(cm, ...) → List<string>`（null=表未就绪）、`lines` 日志通道、`_announced`/ListOnReady 机制。
- Produces: `ApplyTalents(cm, verbose, listAll, lines) → bool`（false=Talent 表未就绪，调用方 return null 下轮重试）；静态字段 `OrigTalentRoles`、`SlotTalentKeywords`、基线常量。

- [ ] **Step 1: 在 RoleTable 类中追加字段与方法**

在 `RoleTable` 类内（`Apply` 方法之后、`ApplyAllowList` 之前）插入：

```csharp
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
```

- [ ] **Step 2: 接入 Apply 主流程**

在 `Apply` 方法内、`---- 2) DefenseLv ----` 整块结束之后、`return lines;` 之前插入：

```csharp
// ---- 2.5) Talent：专属生存规划互通（无开关，mod 在即生效）----
if (!ApplyTalents(cm, verbose, listAll, lines)) return null;
```

- [ ] **Step 3: 更新类头注释**

`RoleUnlockPlugin` 类顶 `<summary>` 追加一行：`/// 3) 生存规划互通：清功能类专属天赋 PlayerSelectId（家具位黑名单排除）+ 花瓶配方 325 等级化解锁。`（花瓶在 Task 2 落地，注释先行合并写。）

- [ ] **Step 4: 构建并判退出码**

```bash
cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.RoleUnlock -c Release > build.log 2>&1; echo $?
```
Expected: `0`；非 0 则读 build.log 修复后重跑（不许带管道）。

- [ ] **Step 5: 提交（须作者同意，见 Global Constraints）**

```bash
git add SurvivalLog.RoleUnlock/RoleUnlockPlugin.cs SurvivalLog.RoleUnlock/SurvivalLog.RoleUnlock.csproj
git commit -m "feat(RoleUnlock): 生存规划互通——46条功能类专属天赋清白名单（家具位关键词黑名单+基线自检）"
```

---

### Task 2: 花瓶配方 325 等级化解锁

**Files:**
- Modify: `SurvivalLog.RoleUnlock/RoleUnlockPlugin.cs`

**Interfaces:**
- Consumes: Task 1 的接入点（Apply 主流程）、`lines` 日志通道。
- Produces: `ApplyVaseRecipe(cm, verbose, listAll, lines) → bool`（false=表未就绪）。

- [ ] **Step 1: 追加方法**

在 `ApplyTalents` 之后插入：

```csharp
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
```

- [ ] **Step 2: 接入 Apply 主流程**

在 Task 1 插入的 Talent 调用之后、`return lines;` 之前插入：

```csharp
// ---- 2.6) ProductionList：花瓶配方 325（无开关）----
if (!ApplyVaseRecipe(cm, verbose, listAll, lines)) return null;
```

- [ ] **Step 3: 构建并判退出码**

同 Task 1 Step 4 命令。Expected: `0`。

- [ ] **Step 4: 提交（须作者同意）**

```bash
git add SurvivalLog.RoleUnlock/RoleUnlockPlugin.cs
git commit -m "feat(RoleUnlock): 花瓶包裹配方325等级化解锁（craft/show 0->1，纯表不碰剧情）"
```

---

### Task 3: moddeps.json + DESIGN.md 同步

**Files:**
- Modify: `tools/gamekb/moddeps.json`（RoleUnlock 条目，约 86 行起）
- Modify: `SurvivalLog.RoleUnlock/DESIGN.md`

**Interfaces:**
- Consumes: moddeps.json 现有 schema（`configFields: [{accessor, rowType, fields}]`）。
- Produces: 依赖登记完整（modcompat_check.js 会据此核对表字段在游戏更新后仍存在）。

- [ ] **Step 1: moddeps.json 补两个字段组**

把 `RoleUnlock.configFields` 从现有单条 DefenseLv 扩为：

```json
"configFields": [
  {
    "accessor": "_Config_DefenseLv_Dict",
    "rowType": "Config_DefenseLv",
    "fields": ["unlock_recipes_P1", "unlock_recipes_P2", "unlock_recipes_P3", "unlock_recipes_P4"]
  },
  {
    "accessor": "_Config_Talent_Dict",
    "rowType": "Config_Talent",
    "fields": ["PlayerSelectId", "Name_Local", "Dec_Local"]
  },
  {
    "accessor": "_Config_ProductionList_Dict",
    "rowType": "Config_ProductionList",
    "fields": ["craft_level", "show_level"]
  }
]
```

- [ ] **Step 2: modcompat 核对（改完登记立即验证 schema 无手误）**

```bash
cd /e/Game/Mod/mod_stack && node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732
```
Expected: 全绿 exit 0（RoleUnlock 三组 configFields 全部命中）。

- [ ] **Step 3: DESIGN.md 更新**

`SurvivalLog.RoleUnlock/DESIGN.md`：
1. §1 功能追加两条：**生存规划互通**（46 条功能类专属规划全角色可刷可买；家具位黑名单排除；连带大学生 6 条烹饪配方/燃烧瓶强化/老主顾互通；已购不清退——还原只影响后续抽取）；**花瓶配方互通**（325 等级化解锁，制作等级 1 全角色可学；P2 已获者不受影响）。
2. §2 追加实证小节：2.4 生存规划=Type2 天赋池（SurvivalPlanningComponent 按 talentId、RogueSelect 信念点三选一、UI 专属印章=PlayerSelectId 非空；Type2 专属 123 条=家具位 77+功能 46；空列表=IsRoleAllowed 全放行）；2.5 花瓶配方 325（原生 craft_level=0 纯 P2 事件发放 325/434/435 三条真独占，作者定案只做 325；锁判定链"解锁列表含→解锁"优先故 P2 不受影响）。
3. §4 配置表补注：两项新功能**无 cfg 开关**（作者定案默认生效，删 dll 即还原官方）。
4. §5 验证清单追加：首跑日志"Talent 互通：…基线 46/77 吻合"与"Recipe 325 …"两行；打工仔档中期规划池可刷出并购买大学生功能天赋；制作等级 1 工作台出现花瓶包裹；家具位类规划全程未出现在他角色池。
5. 文末新增 §8 迭代维护清单：2026-10-24 生存规划互通+花瓶配方（含"已购不清退"语义说明、基线 46/77 来源 2026-10-23 表、黑名单关键词清单）。

- [ ] **Step 4: 提交（须作者同意）**

```bash
git add tools/gamekb/moddeps.json SurvivalLog.RoleUnlock/DESIGN.md
git commit -m "docs(RoleUnlock): moddeps登记Talent/ProductionList依赖 + DESIGN补规划互通与花瓶配方实证"
```

---

### Task 4: 构建部署 + 实机验证清单

**Files:**
- Deploy: `SurvivalLog.RoleUnlock/bin/Release/SurvivalLog.RoleUnlock.dll` → `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/`
- 无源码改动。

**Interfaces:**
- Consumes: Task 1-3 的产物。
- Produces: 部署后的 dll + 作者实机验证记录（验证结论回填 DESIGN §5）。

- [ ] **Step 1: 确认游戏未运行（硬规则 3，看原始输出）**

```bash
tasklist | findstr SurvivalLog
```
Expected: 无输出（游戏未跑）。**有输出=作者正在玩：中止本任务，告知作者择时重试，绝不杀进程。**

- [ ] **Step 2: 重新构建并判退出码**

```bash
cd /e/Game/Mod/mod_stack && "E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.RoleUnlock -c Release > build.log 2>&1; echo $?
```
Expected: `0`。

- [ ] **Step 3: 部署 dll**

```bash
cp "E:/Game/Mod/mod_stack/SurvivalLog.RoleUnlock/bin/Release/SurvivalLog.RoleUnlock.dll" "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/"
```

- [ ] **Step 4: 作者实机验证清单（正常游玩启动，勿由助手拉起游戏）**

启动进打工仔档后核对（BepInEx 日志若未落盘，以行为实测为准，硬规则 9）：
1. 首跑日志出现 `Talent 互通：功能类已清空 46 条 / 家具位黑名单保留 77 条（基线 46/77 吻合）`；数字不符把上方清单发给助手。
2. 首跑日志出现 `Recipe 325 花瓶包裹 craft_level 0->1 show_level 0->1`。
3. 打到中期（约 D21+）规划商店刷出大学生功能天赋（如"御寒驯化"），购买后植物抗寒生效；**家具位类规划（"一楼中型位"等）不出现在打工仔池**。
4. 制作等级 ≥1 时工作台可见并制作"花瓶包裹"。
5. 回归：大学生档已购天赋、已解锁花瓶配方不受影响；藤蔓种子发现、防御配方互通（工作台燃烧瓶）无回归。

---

## Self-Review 记录

- **Spec 覆盖**：46 条互通（Task 1）、黑名单关键词四条（Task 1 Step 1 常量）、无 cfg 开关（Task 1/2 无 Bind 调用，Task 3 §4 注明）、花瓶 325 表编辑（Task 2）、434/435 不做（无对应任务 ✓）、moddeps 同步（Task 3）、已购不清退语义（Task 3 DESIGN 注明）——全覆盖。
- **占位符扫描**：所有代码块完整可编译；无 TBD/待补。
- **类型一致性**：`ApplyTalents`/`ApplyVaseRecipe` 命名在 Task 1/2 定义与接入点一致；`Snapshot`/`SyncList` 复用现有签名；`Name_Local`/`Dec_Local`/`PlayerSelectId`/`craft_level`/`show_level` 均与 dump.cs 属性名核对一致。
