# 机器人提速 RobotSpeed — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。
> 验证于 gamekb：`2026-10-23_26157732`（游戏 buildid 26157732 / RuntimeAssets 1.1.19611，实机全绿）

## 1. 功能定位

三机器人（修理 FX / 整理 HK / 种植 GR）统一提速，全部走内存配置表直写：
无 Harmony、不碰存档、删 dll 即还原。默认全部翻倍（除动作系数为开关语义），每项独立倍率可调。

覆盖：动作变慢系数清零（对齐手速）、移速、充电速率、续航、每趟耗电、
机器人专属动作时长、货舱格数。

**边界（明确不做）**：不碰与玩家共用的动作行（修理 9121/9128/9129、农业 1613–1624 归 QuickAction）；
不碰 -1 哨兵行（回座 9101 等）；不动全局 NPC 思考间隔；不做修理退避补丁（TickRepairWake 2→60s 退避保持官方）；
**不做插槽与模块相关的任何修改**（插槽数量、插槽扩展模块、代装/补发——见 §2.4，课题封存待独立新 mod）。

## 2. 实现原理（原生机制实证，2026-10-12/13 反汇编 + 10-13 热更版重验）

### 2.1 动作时长公式（机器人慢的第一根因）

`ActionComponent.PlayAction` 反汇编实证：

```
EffectiveDuring = Config_Action.During × max(0.1, 1 + WorkSpeedRatio) ÷ (1 + Σ工速模块)
```

- `Config_RobotAttribute.WorkSpeedRatio` 是官方内置的"机器人比人慢"加成（=1 即每个动作 2 倍时长）；
  mod 置 0 ⇒ 动作时长对齐手动；工速模块的加速除数 `(1+Σ)` 继续叠加，**模块收益不吞**。
- 机器人专属动作行（During 单位游戏秒，TimeScale=120 → 300=2.5 真实秒）：
  9098 去取 / 9099 去放 / 9100 放下 / 9130 正在种植(900) / 9131 去取(种植)。
  mod 按 `原值 ÷ ActionTimeMult` 改写，下限 36 游戏秒（0.3 真实秒，防动画/音效爆闪），只减不增。
- 回座 9101 During=-1 是"走到为止"哨兵，绝不在改写名单。

### 2.2 倍率语义：只改基础值，模块加成天然保留

`RecalcNumericModules` 反汇编实证的叠加链：

| 属性 | mod 改的表字段 | 模块加成（自动叠加） |
|---|---|---|
| 动作速度 | WorkSpeedRatio | 工速模块 → 除数 (1+Σ) |
| 移速 | MoveSpeed | 移速模块 → AttributeComponent 独立属性增量（撤旧加新） |
| 充电 | ChargeTripsPerHour | 快充模块 → ×(1+Σ) |
| 续航 | BatteryTrips | 电池模块 → ×(1+Σ)（BatteryMax 公式） |

内部字段 `_workSpeedRatioCfg(0x88)/_chargeTripsPerHourCfg(0x84)` 每次模块重算都会被
`base 快照 × 模块` 覆盖——**mod 只改配置表行，绝不碰机器人内部字段**。

`RobotAttrs` 读取守卫：BatteryTrips / TripCost / ChargeTripsPerHour 有 `>0` 门槛
（≤0 回退硬编码默认值），故 TripCost 减半下限夹 1；WorkSpeedRatio / MoveSpeed 裸透传无守卫。

### 2.3 货舱

容量 = `Config_Bag` 行 `Size`（List&lt;int&gt;，[0]=宽 [1]=高；官方"以 Bag 表为准"）。
`RobotComponent.CargoExpandBagIds` 静态字典：**键 = 基础货舱 bagId，值 = 装扩舱模块后换用的扩展档 bagId**。
mod 把**键和值两排行的宽度同倍放大**（基础档 4x2→8x2、扩展档同 ÷放大），
"装模块换更大货舱"的官方递进保持，装模块不会覆盖 mod 效果（v1.2.0 测试轮反馈修复）。
改后置 `ConfigManager._bagSizeSet = null`（尺寸白名单懒重建，与 BackpackExpand 同机制）。

### 2.4 插槽与模块：明确不做（作者定版 2026-10-13 终版）

**本 mod 不做插槽与模块相关的任何修改**。多轮反汇编+装机实测的完整结论（供日后独立新 mod 复用）：

- 插槽总上限 = 2 原始 + N 扩展；N = `Config_GlobalSetting["RobotModuleExpandSlots"].Params1`
  的快照（OnInit→ResetModuleState→_expandSlotCount→列表长度；**装/卸模块从不改变长度**）。
- 扩展槽开放门控 = 硬编码"装着 SlotExpand(11) 模块"（`IsModuleSlotOpen` 的 `_moduleTypeMask & 0x800`），
  **配置层面绕不过**；模块 Value 对槽数无作用；模块物品与配置行同 ID 约定
  （TryGiveModuleToLeadingRole 参数=物品 ID，误传配置 ID 即失败）。
- 读档校验"扩展槽有模块但没钥匙"仅记日志不动数据；"超出扩展数"=不载入+日志，均无删写。
- 四方案实测记录：①数量设置改写（生效但无钥匙不可见）②补发物品（发放接口 ID 语义坑，失败）
  ③GM 代装（可行但作者否决强制安装）④门控位强写（可行、零数据风险，但官方在每次装卸模块时
  重算该位→面板瞬间显示关闭需轮询补开+脏推送刷新，观感"卸载模块导致失效"被作者否决）。

**"之前不生效"的完整原因链（三轮排障定论）**：
① 第一版只改设置：改写成功（日志 SLOTS 2→4），但玩家没装钥匙模块 → 门控关闭 → 扩展槽不可见；
② 第二版补发物品失败：`TryGiveModuleToLeadingRole` 参数是**物品 ID**（内部 Get_Config_Item 校验），
  误传模块配置表 ID → 物品行查不到 → 返回 false；
③ 结论：设置改写本身一直是对的，缺的只是钥匙。

### 2.5 在场机器人直通（ApplyLiveRobots）

`CargoExpandBagIds` 静态字典实测只登记了 HK 整理（1061 基础 / 1063 扩展）——**WX-5 维修、
TB-3 种植的货舱是别的 Bag 行，字典查不到**（第二轮反馈"修理机器人货舱没变"根因）。
每轮轻扫在场机器人读 `_cargoBaseBagId` 逐一放大对应 Bag 行（Config_Bag.Size 实时直读，
改完即生效）。新制作/新摆放的机器人下一轮 5 秒轮询自动覆盖。

### 2.6 生效时机

mod 在 BepInEx 启动后注入 Ticker 轮询（5 秒），`ConfigManager` 就绪即改表；
机器人 `OnInit` 在读档时从表取值 ⇒ **重启游戏自动生效**。配置表可能随切场景/读档整体重载回原值，
轮询 5 秒内按原值缓存自动恢复（幂等，与 QuickAction/BagExpand 同模式）。
修改 cfg 即时热重跑（SettingChanged → Rerun），在场机器人的内部快照字段仍需重新读档才刷新。

## 3. 代码结构

| 文件 | 职责 |
|---|---|
| `RobotSpeedPlugin.cs` | 入口：cfg 定义、Ticker 注入；`RobotTicker` 轮询幂等改写四张表 + trace 自证 |

单文件 mod（不进 ModShared——机器人表工具暂无跨 mod 复用需求）。

## 4. 配置项（BepInEx\config\com.local.survivallog.robotspeed.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| General.Enabled | true | 总开关（ModCenter 启停位，2026-10-24）：false=按原值缓存还原全部已改表值并停写；下次启动不创建 Ticker（零日志零改写） |
| 动作速度对齐手动 | true | WorkSpeedRatio→0；false=恢复原值 |
| 移速倍率 | 2 | 机型基础移速 ×N |
| 充电速率倍率 | 2 | ChargeTripsPerHour ×N（秒/趟=3600÷速率，游戏小时制） |
| 续航倍率 | 2 | BatteryTrips ×N |
| 耗电降低倍率 | 2 | TripCost ÷N（下限 1） |
| 动作时长倍率 | 2 | 9098/9099/9100/9130/9131 During ÷N（下限 36 游戏秒，只减不增） |
| 货舱格数倍率 | 2 | 基础货舱行**和扩舱模块的扩展档行**宽度都 ×N（递进关系保持）；WX-5/TB-3 走在场直扫兜底 |

全部 1/false = 不改或恢复原值。首跑自动生成完整中文注释 cfg（改注释需删 cfg 重建——老坑）。

## 5. 已知边界与风险

- **General.Enabled 启停门（2026-10-24，ModCenter 任务 9）**：Bind 最前，`Load()` 全部 Bind
  完成后 false 即早退（不创建 Ticker、零日志——表值本会话从未改过）；运行中被置 false 时
  Ticker 首行 gate 触发 `RestoreAll()`：按八组原值缓存回写机型属性五字段/机器人动作行/
  货舱行宽高后停写（表未就绪下轮重试）。边界：已加载机器人的内部快照字段（充电 cfg/电量
  上限等）不回滚，模块重算或下次读档自然回官方值——与"删 dll 即还原"同边界。
- **QuickAction 互不冲突**：本 mod 只写机器人专属动作行（type 8 中五条）与机器人属性表，
  QuickAction 按分类写玩家动作行。**例外：9130 正在种植与玩家共用**（v1.2.0 误判专属），
  QuickAction（杂项交互 500ms）与本项目（÷2）曾每 5 秒互相覆盖、日志各刷 200+ 条——
  2026-10-17 已加**让位守卫**：当前值 ≤ 目标值时不再写回（谁更快听谁的），刷屏随打架一并消失。
  另注：QuickAction 首扫慢于本项目 0.5 秒快扫，其 OrigDuring[9130] 记到的是本项目 ÷2 后的 450
  （原值污染，仅影响其"恢复原值"目标回到 450 而非官方 900，无实际危害）。
  **还原让位（2026-10-24，ModCenter 任务 13）**：RestoreAll 对"当前值 > 本缓存原值"的动作行
  不回写（只减不增世界里首见最大值=官方原值，本缓存亦可能记到对方更低的中间值）——与
  QuickAction 同款守卫对称，双 mod 同关终值确定为官方 900（修复前按各自缓存先后回写、终值不定）。
- 模块越多每趟耗电越高（官方设定），本 mod 的耗电÷2 默认恰好对冲。
- 游戏大更新若改字段名/表结构，Ticker 的 try-catch 会逐项降级并在 LogOutput 留 WARN，
  不会硬崩；RobotSpeedTrace.txt（BepInEx 根目录）留有首次生效的改写明细自证。

## 6. 验证记录

- 2026-10-24 ModCenter 任务 13：面板 MC_TOGGLE_MOD 直写 General.Enabled 触发运行中 RestoreAll
  热还原实证（主菜单，`tools/_mc_fixture.js toggle`，两轮全绿）：关 → ≤5s MoveSpeed 4.8→2.4
  （×3 行）、9098/9099/9100/9131→300/300/60/300、货舱 1061/1063→4x2/4x3（9130 保持 60=QA
  接管）；重开逐值回 4.8/150/8x2（×2 轮）。9130 双 mod 同关终值=关序决定：QA 先关 + 本项目
  后关 = 900 官方确定（还原让位守卫生效，见 §5）；本项目先关 + QA 后关 = 450（QA 污染缓存
  最后落笔；其 apply 自愈已改只升不降不再压本项目还原值，但共管行竞速语义未变）。
  证据全文 .superpowers/sdd/2026-10-24-modcenter/task-13-report.md。
- 2026-10-24 20:33–20:44 ModCenter 任务 9（General.Enabled 启停门）四轮主菜单实测（ConfigDumpProbe
  全表 dump + trace 自证，未读档未碰存档）：全 on（MoveSpeed 4.8/WSR 0/动作÷2/货舱 8x2）→
  全 off（全部回官方基线：MoveSpeed 2.4、WSR 2/3/0、9098=300、货舱 4x2，三 mod 零 trace 零日志）→
  混合（RS off + BP/QA on：RS 字段全基线、BP/QA 全生效，独立性实证）→ RS 恢复 on（回到 4.8/0/÷2）。
  运行中关闭触发的 RestoreAll 分支待 ModCenter 任务 13 接 MC_SET 写 General.Enabled 后可实证。
- 2026-10-13 21:49 玩家实测（v1.2.0 测试轮）：属性表/动作行/货舱基础行/插槽设置行全部改写成功
  （机型原值：HK-1 整理 WorkSpeedRatio=2、TB-3 种植=3、WX-5 维修=0 本就无此加成；9130 官方热更已
  从 900 砍到 60 游戏秒）。发现两问题并当日修复：
  ① 装扩舱模块会切到未改写的扩展档行盖掉 mod 效果 → 改为键值两排行同倍放大；
  ② 插槽设置改成功但面板仍 2 槽 → 门控=装着 SlotExpand 模块（`_moduleTypeMask & 0x800`），
  默认补发该模块物品。修复版待玩家复测。

## 7. 防盗署名气泡（2026-10-14，套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。
- 7 个套件 mod 全部调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建 DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后 7 个 mod 需全部重编。
