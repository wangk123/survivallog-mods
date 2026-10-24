# SurvivalLog.RoleUnlock — 角色专属解锁（设计文档）

## 1. 功能

- **藤蔓全角色解锁**：任何角色（不只女大学生）收获作物时都可能"发现稀有种子"拿到藤蔓种子，
  并可种植藤蔓、安装到家具、获得藤蔓护盾防御效果。
- **防御配方全角色互通**：`Config_DefenseLv.unlock_recipes_P1~P4` 四列合并成并集——
  燃烧瓶等角色专属防御配方对全部角色变为**可解锁**（各自的防御等级门槛照旧，不是白送）。
- **生存规划互通**（2026-10-24）：46 条功能类专属规划全角色可刷可买（连带大学生 6 条烹饪配方、
  燃烧瓶强化、老主顾互通）；家具位类规划黑名单排除；**已购不清退**——还原只影响后续抽取。
- **花瓶配方互通**（2026-10-24）：花瓶包裹配方 325 等级化解锁，制作等级 1 全角色可学；
  P2 已获者不受影响。
- 纯内存表编辑、零 Harmony、幂等。藤蔓/花种/防御配方有 cfg 开关（关=还原官方值）；
  生存规划互通/花瓶配方无开关（mod 在即生效，删 dll 即回官方）；已购规划不清退（还原只影响后续抽取）。

## 2. 机制实证（2026-10-21 反汇编，dump=tools/il2cppdumper/ 根目录当日产物）

### 2.1 角色体系

- 当前角色 = `ConfigManager.GetCurrentPlayerSelectId()`（静态，返回 1~4 = P1~P4）。
- 通用判定 `ConfigManager.IsRoleAllowed(List<int>, int)`：空列表=不限，非空=白名单。
- `Config_ProductionList` / `Config_Item` / `Config_Furniture` 均**无**角色字段；
  角色限定分散在 Config_Talent/Config_Event 等的 `PlayerSelectId` 字段、DefenseLv 分列、
  GlobalSetting 白名单、以及 IvyPlanManager 的代码级比对。

### 2.2 藤蔓线（女大学生专属的原生实现）

- `Config_GlobalSetting["Plant_RareSeedDiscover"]` 四合一条目：
  `Params1(int 0x18)`=藤蔓种子物品ID（≤0 时随机挑已解锁种子）、`Params2(float 0x1C)`=发现概率、
  **`Params3(string 0x20)`=角色白名单**（';' 分隔，空=不限）。
- 三处消费全部只读 Params3 做"空=放行"判定（空值路径均已反汇编验证落点）：
  - `IvyPlanManager.IsIvyRoleAllowed()`（RVA 0x2702F80；空→return true @0x27030B6）——
    供 TowerCombatManager 咬人/波次结算、TowerWaveManager.EvaluateReadiness（护盾战斗）用；
  - `IvyPlanManager.IsInstallTarget(Furniture)`（0x2702CA0；空→跳过角色比对 @0x2702E30）；
  - `AE_HarvestPlant.TryDiscoverRareSeed`（经 RollYield 0x30943C0 调用）：收获时
    ①Params2>0 ②防御门槛 `Plant_RareSeedDiscover_DefenseLv`.Params1≤当前防御等级
    ③Params3 白名单含当前角色 → 概率=Params2×(1+天赋 Buff/AE_RareSeedDiscoverBonus)（封顶 1.0）
    掷中 → 收获物 +1 颗 Params1 种子（空→跳过角色门 @0x30963AC）。
  - 辅助：`AE_HarvestPlant.IsRoleInAllowList(value, role)`（0x3093720）=同款语义的列表版；
    `GetRoleExclusiveSeedPlantIds(role)`（0x3092870）用同一 Params3 决定哪些植物计入角色专属种子。
- **红线：白名单只能清空，不能改成 "1;2" 多值**——IsIvyRoleAllowed 的内联解析器逐字符
  解析数字、遇非数字即停（不识别 ';'），多值会静默变成"只放行解析出的首个数字"。
- 种植/安装家具本身（`Config_Furniture.PlantFurnitureID(0xF0)>0`）无角色字段，全角色可持有。

### 2.3 防御配方线（打工仔专属燃烧瓶的原生实现）

- 配方可做性总闸 `ToolTableManager.GetProductionCraftLockReason`（0x28098E0）：
  !IsUseable→隐藏 / 染色配方→走染色线 / 防御装置→防御等级线 / 解锁列表含→解锁 /
  `GetDefenseRequiredLevelForRecipe(id)`>0→防御等级线 / craft_level>0→等级线 / 否则 NotLearned。
- `ConfigManager.GetDefenseUnlockRecipes(cfg)`（0x2F37880）按当前角色 switch 取
  DefenseLv 行的 `unlock_recipes_P1(0x80)/P2(0x88)/P3(0x90)/P4(0x98)`；
  `GetDefenseRequiredLevelForRecipe`（0x2F37610）内部也走它——错角色配方查到的需求等级=0
  →落入 NotLearned → 工作台不可见。展示侧（ProficiencyManager.BuildRecipeUnlockList）
  与执行侧（ToolTableManager.TryCreateItem）都走这条链，数据层打通即双侧生效。
- "防御配方"界定 `IsTowerDeviceProduction`（0x2F49D30）：首产品 Config_Item.TargetFurnitureID
  →Config_Furniture.SlotType==10。燃烧瓶在哪个角色列/哪个等级行属表数据（装机后从
  ListOnReady 日志核对，见 §5）。

### 2.4 生存规划线（2026-10-24 反汇编+表统计，表=gamekb/2026-10-23_26157732）

- 生存规划 = Type2 天赋池：`SurvivalPlanningComponent` 按 talentId 从 `Config_Talent` 取规划，
  RogueSelect 信念点三选一抽取；UI 专属印章 = `PlayerSelectId` 非空（空列表不盖印）。
- Type2 专属共 123 条 = 家具位 77 + 功能 46（2026-10-23 表统计）。
- 空列表 = `IsRoleAllowed` 全放行（§2.1 通用判定）→ 清空 `PlayerSelectId` 即全角色可刷可买；
  家具位类规划绑定各角色自家地图，跨角色必排除（按名称/描述关键词黑名单，见 §8）。

### 2.6 防御配方列勘误（2026-10-24 汇编+表定案）

- 本版（build 26157732）防御配方数据已挪回**通用列** `unlock_recipes`(0x78)：Lv1[102 维修零件] Lv2[404 劣质/405 普通/406 烈性燃烧瓶] Lv3[402 电网] Lv4[403 机械电锯] Lv5[407 钛合金大门/408 防弹窗]；`unlock_recipes_P1~P4` 四列**全空**——10-23 游戏更新官方已自行统一全角色防御配方。
- `GetDefenseRequiredLevelForRecipe`(0x2F655D0) 先查通用列再查角色列（两者任一 Contains 即计入），`GetDefenseUnlockRecipes`(0x2F65840) 仍按角色取 P 列——机制未变、数据挪了。
- 推论：本 mod 的 ShareDefenseRecipes（P1~P4 并集）在本版**空转无害**（空并集），日志表现为 "DefenseLv N 并集 []" 属正常；若未来版本恢复分列则自动重新生效。§5 验证清单中"燃烧瓶出现在并集"一项在本版不适用（原生即全角色）。

### 2.5 花瓶配方 325（2026-10-24 表实证）

- 原生 `Config_ProductionList[325].craft_level=0`：不走等级解锁，纯 P2 剧情事件发放——
  与 434/435 并列三条"真独占"配方（零解锁途径）；作者定案只做 325（434/435 不动）。
- 锁判定链（§2.3 GetProductionCraftLockReason）里"解锁列表含→解锁"优先于等级线，故 P2
  已获者不受改表影响；改为 craft_level/show_level=1/1 后全角色制作等级 1 可学（对齐工具柜包裹 389 档位）。

## 3. 实现选型

- **为什么纯表编辑而不是 Harmony**：GetDefenseUnlockRecipes 返回 `List<int>`（il2cpp 泛型），
  Harmony 补丁参数含 il2cpp 泛型类型会 IL Compile Error 静默失败（既有实证）；且藤蔓侧
  清一个字符串即全链路生效，无补丁必要。
- **防御配方的表编辑等价性**：把每行 P1~P4 都写成四列并集 → GetDefenseUnlockRecipes 取哪列
  都是全量；GetDefenseRequiredLevelForRecipe 逐行扫"本角色列含该配方"→返回首个命中的行
  （即该配方在全体角色列中的最低防御等级）→解锁节奏与原生同构。
- 藤蔓侧：Params3→""；防御门槛默认不动（cfg 可清零）；花种白名单默认不动（cfg 可开）。
- 轮询幂等（同套件模式）：5 秒一轮，目标态由配置+进程内首读原值推导；读档/切场景表重载
  后下一轮自动补写；关配置自动还原官方值。快照不落盘、不写存档。

## 4. 配置

| 项 | 默认 | 说明 |
|---|---|---|
| UnlockVine | true | 清空藤蔓角色白名单 |
| SkipDefenseGate | false | 清零藤蔓种子防御等级门槛 |
| UnlockFlowerSeeds | false | 观赏花种子同样全角色 |
| ShareDefenseRecipes | true | DefenseLv P1~P4 并集互通 |
| ListOnReady | true | 首轮列原始白名单与各行并集 |
| Verbose | false | 每轮明细 |

注：生存规划互通 / 花瓶配方互通两项新功能**无 cfg 开关**（作者定案默认生效，删 dll 即还原官方；
生存规划已购不清退，还原只影响后续抽取）。

cfg 文件名 `com.local.survivallog.roleunlock.cfg`（Guid `com.local.survivallog.roleunlock`）。

## 5. 待装机验证清单（作者实测）

1. 日志 `GS Plant_RareSeedDiscover.Params3 藤蔓白名单 "X" -> ""`——X 即原白名单
   （女大学生的角色编号实值，顺带定 P1~P4 与四个角色的对照）。
2. 日志 `DefenseLv N P1~P4 -> 并集 [...]` 各行——确认燃烧瓶配方 ID 出现在并集、
   以及它原生只在打工仔那列（首跑日志带原始四列，一眼可核）。
3. 女大学生档：工作台能看到/解锁燃烧瓶（防御等级够的行）；收获作物能掉藤蔓种子（原生本来就能，回归验证）。
4. 打工仔档：收获作物能发现藤蔓种子；种下后家具菜单出现藤蔓相关入口（Ivy_BtnName），
   安装后守尸潮护盾生效（弹性降难日志里"燃烧瓶藤蔓"项>0）。
5. 关闭 ShareDefenseRecipes / UnlockVine 后重进游戏，工作台与收获行为回到原生。
6. 首跑日志两行：`Talent 互通：功能类已清空 46 条 / 家具位黑名单保留 77 条（基线 46/77 吻合）`
   与 `Recipe 325 花瓶包裹 craft_level X->1 show_level Y->1（原值已快照）`——
   前者不符时带 ⚠（疑似游戏更新改表，请核对上方清单）。
7. 打工仔档：中期规划池（RogueSelect 信念点三选一）可刷出并购买大学生功能天赋（烹饪配方等）。
8. 制作等级 1 的工作台出现花瓶包裹（可学可做）；大学生（P2）档已获花瓶配方不受影响。
9. 家具位类规划全程未出现在其他角色规划池。

## 6. 防盗署名气泡（套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。
- 套件全部 mod 调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建 DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后全部 mod 需重编。

## 7. 文件

- `RoleUnlockPlugin.cs`：插件主体（cfg + 轮询 Ticker + RoleTable 表改写），单文件。
- 依赖：ModShared（BagTable.Cm() ConfigManager 取实例 + WelcomeTip），随 csproj 源码并入编译。

## 8. 迭代维护清单

- **2026-10-24 新增：生存规划互通 + 花瓶配方 325 等级化解锁**（`RoleTable.ApplyTalents` / `ApplyVaseRecipe`，
  均无 cfg 开关）。
  - 依赖登记（`tools/gamekb/moddeps.json` RoleUnlock.configFields 新增两组）：`_Config_Talent_Dict`
    （`PlayerSelectId` / `Name_Local` / `Dec_Local`）与 `_Config_ProductionList_Dict`
    （`craft_level` / `show_level`）——游戏更新后跑 modcompat_check 重点看这两组是否仍命中。
  - **"已购不清退"语义**：清空 PlayerSelectId 只影响规划池的后续抽取；已购/已学规划为存档侧数据，
    不回读表——关 mod / 删 dll 后已购项保留，仅新抽取回官方行为。
  - 基线 46/77 来源：2026-10-23 表（`gamekb/2026-10-23_26157732`）统计——功能类专属 46 条、
    家具位黑名单 77 条。不符时插件日志带 ⚠ **只报警不阻断**（防游戏更新改表误判），需人工核对
    ListOnReady 清单后同步更新 `RoleUnlockPlugin.cs` 的 `ExpectShareTalents` / `ExpectKeepSlotTalents`。
  - 家具位黑名单关键词（`SlotTalentKeywords`，匹配 Name_Local / Dec_Local，命中即跳过不清）：
    `位·`、`扩建`、`腾出一处`、`置物位`——宽匹配设计对游戏更新新增的家具位规划自动免疫。
  - 花瓶 325 原值快照于进程内首读（`_origVaseCraft`/`_origVaseShow`），同表重载后下一轮自动补写；不改 434/435。
