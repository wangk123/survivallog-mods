# 动作提速 QuickAction — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。

## 1. 功能定位

按游戏官方动作分类（`Config_Action.ActionType`）统一改写动作耗时（默认 500ms），
五个分类各一个开关。改的是内存配置表，不碰存档、无 Harmony 补丁、热改即时生效、0=恢复原值。
游戏更新新增动作**自动归类覆盖**（分类、守卫、引用判定全部运行时实时计算，无需更新 mod）。

## 2. 实现原理（原生机制实证：Il2CppDumper dump + Iced 反汇编 + 运行时探针 EffectDump）

- **动作耗时 = `Config_Action.During`**（float，游戏秒，偏移 0x48），进度条按**游戏时钟**递减。
- **时钟换算**：探索/家场景 `Config_Chapter.TimeScale = 120`。目标毫秒 → During：`ms ÷ 1000 × 120`
  （500ms → 60；原值 1800 = 15 真实秒）。
- 改写点：`ConfigManager._Config_Action_Dict`（内存表直写，轮询幂等）。
- **官方分类枚举** `GameCore.HotUpdate.ActionType`（interop Cecil 实证）：
  Food=1, Rest=2, Entertainment=3, Exercise=4, Social=5, Treatment=6, Move=7(未用),
  Other=8, Free=9(未用), RepairFurniture=10；表值 0 = 枚举无名的默认桶（家具功能类动作）。
  语义是"动作满足角色哪种生存需求"，Other=8 是官方杂类（真交互与内部行为混居）。
- **收益结算三段式**：动作经 `EffectConfigID` 挂 `Config_Effect`（Start 开始瞬间 / Interval
  动作期间周期结算 / End 结束瞬间）。**Interval 周期 = ItemAttrInterval（每 N 游戏秒结算一次其余
  Interval 字段值），总收益与动作时长成正比**——例：瑜伽每 600 秒 士气+2/体力-1.67。
- 天赋：仅「眼明手快」影响动作耗时（`PlayAction` → `GetTalentEffectRatio` +
  `AttributeComponent.GetTotalValue_Float` 读 buff 属性，-30%），与本 mod 同向叠加；
  游戏内部缩放下限 `MIN_ACTION_DURING_RATIO=0.1`（mod 直改表不经过，不受影响）。
- **动作间隔** `ActionInterval = 200` 游戏秒用户明确**不改**。

## 3. 分类与守卫（v2.0.0 核心）

### 五个分类开关（[分类修改]，v2.1 起默认值统一 500；旧 cfg 一次性迁移 DefaultsV21）

| 分类（cfg 键） | ActionType | 内容 | 可改条数（v2.0.0 实测） | 默认 |
|---|---|---|---|---|
| 物品使用Ms | 1 Food ∪ 6 Treatment | 吃/喝/品尝/吞咽/享受/嚼/畅饮、吃药 | ~2750 | 500 |
| 家具功能Ms | 0（默认桶） | 拆封包裹/种植/烹饪/升级大门/改装/救援建造 | ~900 | 500 |
| 房屋维护Ms | 10 RepairFurniture | 布置/安装/移动/拆除陷阱、安装家具、出门 | ~78 | 500 |
| 杂项交互Ms | 8 Other ∩ 玩家可触发 | 搜查/撬锁/开锁/翻找/开关电器/拾取/家务 | ~135 | 500 |
| 娱乐锻炼Ms | 3 Entertainment ∪ 4 Exercise | 看书/听音乐/按摩/运动/洗澡/空调吹风 | ~173 | 500 |

> v2.0 曾把家具功能/娱乐锻炼默认 0（当时未逐条实证收益结算）。v2.1 已实证四类代表动作
> 均无周期结算收益（看书 100003xxx 效果全 0；空调吹风 1915=End 型 士气+5/健康+5；
> 上厕所 9075 效果全 0；浇水 1615 效果全 0），周期守卫仍在运行时兜底，故统一默认开。
> 睡觉 Rest=2 与社交 Social=5 是禁改类，**不提供任何开关**（防误设置）。

### 三道运行时守卫（顺序即 Apply 内判定顺序）

1. **周期结算守卫**：`EffectConfigID → _Config_Effect_Dict`，任一 Item*/Buff* Interval 字段非零
   ⇒ **永不改动**（带自愈：若值已被历史会话改错则恢复原值）。
   实测黑名单 74 条：修理/加固本体系 10（修理设施/修补房门/修复电力/窗户/加固房门·窗户等）、
   生吃冷冻食品 32、跳舞×4+掌上游戏机、锻炼/持续发电/塑钢哑铃 3、社交类全部 5（观察窗外/听收音机/
   冥想/写日记/瑜伽）、Other 内部 18。
   *注意：同族不同动作效果不同——高级加固房门(124)/修补房门(9128)/强化加固×2 无周期效果，照常提速。*
2. **禁改类**：Rest=2（睡觉 During 兼任时间推进）、Social=5（全部周期结算）。
3. **引用轴**（Other 专用）：`_Config_FurnitureFunc_Dict.ActionIds`（家具按钮，241 个动作）
   ∪ `_Config_Item_Dict.UseAction`（物品使用，3857 个）= 玩家可触发集；Other 只有命中才改。
   情绪播报（"好饿！"）、呕吐/腹泻等强制状态、系统动作不被引用 ⇒ 不动。
4. **During<=0** 哨兵值（-1/0 的特殊语义动作）跳过；**只减不增**（原值快于目标不放大）。

### 配置优先级（高 → 低）

1. `Overrides`（`ID:毫秒`；`ID:0` = 恢复原值）——v2.1 起判定先于分类，可命中
   引用轴漏掉的动作（家具本体 BT 硬编码交互，如上厕所 9075：效果全 0 无周期收益，
   但不被 FurnitureFunc/Item 任何表引用，cfg 写 `9075:500` 即可提速）
2. 五分类开关（0=该类恢复原值）

### 家具本体交互链（v2.1 实证补充）

原生家具（马桶/绿植/床等 `Config_Furniture`）的 `ActionId` 字段**全部=2**（"打开目标"，
During=1 的通用开场动作），具体交互动作由 BT 节点硬编码（`Battle.Logic.E_Action_Furniture_Bar_*`
系列，如 E_Action_Furniture_Bar_Toilet→9075），不经过 FurnitureFunc 按钮表——
**引用轴结构性覆盖不到这批动作**。对策=内置 `BtInteractIds` 名单（归入杂项交互，
全部经 EffectDump 实证无周期收益）+ Overrides 点名兜底。运行时监视入口（ActionProbe v0.8）：
`BaseSingleton<BattleLogicWorld>.Instance._ActionManager.AgentActionSourceDict[agentId].CurrentAction.ActionId`，
AgentAction 另有 MatchedFuncId（>0=走家具功能表）可区分两类来源。

**ActionWatch 运行时实测结论（2026-10-09，农活链全破译）**：
- 浇绿植真实动作=**102"浇点水…"（Type=3 娱乐锻炼桶）**，非 1615"浇水"（那是种植区浇灌）；
  v2.1 开桶后实测 0.5s 生效。效果 End 型（饱食+3/士气-5），提速无损失。
- 种植链：收获 1617/翻土 1618（被引用，已提速）→ 播种 1610（D-1 哨兵，瞬时标记）→
  **正在种植 1611/9130（主体动作 7.5 秒，BT 硬编码无引用）→ 已入 BtInteractIds 名单**。
- **改不了的一类**：手工制作 39 / 整理物品 3 / 酿造 9070 等 D=-1 哨兵动作——实际时长
  不走 During（实测手工 14.7s/整理 13.6s，时长疑似由配方/物品数驱动），During 无杠杆。

### 幂等机制

`OrigDuring` 字典按 actionId 记首次原始值；所有计算/恢复以原值为基准。
**启动门**：Action/Effect/FurnitureFunc/Item 四张表全部非空才开始扫描——只等 Action 表会让
守卫集合为空导致守卫失效（v2.0.0 首版实测踩过）。

### 安装引导（installer 联动）

安装器 mod_quick 下 5 个子组件勾选 → `CurStepChanged` 写 ASCII 引导文件
`BepInEx\config\quickaction.boot.ini`（itemMs/furnMs/maintMs/miscMs/funMs）→
mod `ReadBoot()` 读后即删，仅作 Bind 默认值；**已有 cfg 键永远优先**（重装不覆盖玩家调好的数值）。

## 4. 代码结构

| 文件 | 职责 |
|---|---|
| `QuickActionPlugin.cs` | 入口：ReadBoot 默认值 → 五分类 Bind + Overrides；cfg 遗留清理（[动作·*]/[整类设置]）；注入 Ticker |
| `QuickTicker`（同文件） | 5 秒轮询：BuildIntervalEffects + BuildReferenced + Apply() 全表扫描；ActionList.txt / QuickActionTrace.txt |

（v1.x 的 ActionCatalog.cs 已删除，77 逐项被分类收编。）

## 5. 生成文件（BepInEx 根目录）

| 文件 | 说明 |
|---|---|
| ActionList.txt | 全表（ID\|名称\|**Type**\|原 During\|真实秒），按耗时降序；检测到 v1 旧格式（无 Type 列）自动重写 |
| QuickActionTrace.txt | 首次生效：各分类改写数 + 守卫跳过数（周期/禁改/内部/瞬时）+ 明细 |

v2.0.0 实测（全开）：物品×2746 家具×733 维护×69 杂项×67 娱乐×167；
守卫 周期74/禁改15/内部23/瞬时163。

## 6. 与原生 / 其他 mod 的交互

- 天赋眼明手快同向叠加（§2），不冲突。
- 与 4 个扩容 mod 完全正交（不同配置表）。
- 改表不落存档：卸载后所有动作耗时回到原版。

## 7. 已知限制

- Scale=120 硬编码常量；游戏大更新后体感异常改 `QuickActionPlugin.Scale` 重编译。
- cfg 条目注释只在键**首次创建**时写入：改注释必须删 cfg 重建。
- 引用轴不含 Think 表（实测 Think 引用与 Other 无交集，不影响判定）。

## 8. 演进要点（详细见根 CHANGELOG.md）

- v2.1.0（2026-10-09，随套件 v1.0.5 发版；插件版本独立于套件号）：五分类默认统一 500 + 旧 cfg 一次性迁移（[迁移] DefaultsV21，
  仅把仍是旧默认 0 的家具功能/娱乐锻炼抬到 500）；Overrides 提前到分类判定前（可命中无引用
  的 BT 硬编码动作）；安装器 qa_furn/qa_fun 移入 recommended 默认勾选；实证补录四动作
  效果结算（看书/上厕所/浇水效果全 0、吹风 End 型）+ 家具本体交互链结构（见 §3）。
- v2.0.0（2026-10-07）：分类引擎重构（ActionType 五分类 + 周期结算守卫 + 引用轴 + 安装器勾选）；
  修复 v1.x 按动词匹配漏 78 条（品尝/享受/吞咽/嚼/畅饮）与误提速周期结算动作（修理体系/生吃冷冻）的问题。
- v1.3.x：77 逐项 + 4 整类（动词前缀匹配）形态；v1.0 起为逐项扩容。
- 套件 v1.0.0 起：版本随 version.json 统一。

## 9. 迭代维护清单（改本 mod 时同步更新）

- 分类/守卫规则变化 → §3 并附实证依据（EffectDump/反汇编）；
- 换算/天赋结论变化 → §2；
- 配置形态变化 → §3 + tools\make_quickaction_release.ps1（预置 cfg 直接生成）+ installer.iss 勾选；
- 每次发版在根 CHANGELOG.md 补一节。
