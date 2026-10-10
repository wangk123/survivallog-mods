# 储藏柜直达 CabinetEverywhere — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。

## 1. 功能定位

让普通储物类家具（储物柜/橱柜/金属柜/收纳架/保险箱等）出现在制作类界面的容器页签里：

- 工作台 / 无人机交易 / 无人机站 / 粉碎机 / 温室：柜子页签 + **制作自动取料**（官方白名单路径）
- 烹饪 / 老鼠笼 / 酿酒桶：柜子页签（状态层注入路径）

不改存档、不改游戏文件，删 dll 即还原。**发电机加油界面原生没有容器页签系统（只有背包），
不在本 mod 范围**（要支持等于新造 UI 功能，另立项）。

## 2. 实现原理（2026-10-10 全程反汇编实证）

### 2.1 官方"制作材料源"白名单（本 mod 的核心杠杆）

- `ToolCabinetConfig.CraftSourceFurnitureIds`：静态 `HashSet<int>`，cctor 硬编码 **{70006 工具柜}**；
  `IsCraftSource(configId)` = 集合命中。
- E8 扫描全部调用方共 5 个：`AE_OpenHandMade.Run`（工作台）、`ItemManager.CollectTradeContainers`
  （无人机交易，经 OnTradeContainerTabsRequest）、`AE_OpenDroneHubPanel.CollectContainers`
  （无人机站，冷链∪白名单）、`AE_OpenShredderPanel.Run`（粉碎机）、`GreenhouseProgress.CollectMaterialSources`（温室）。
- **工作台自动取料链**（用户最关心的"制作时自动从柜子拿材料"）：
  `RA_ToolTable_RecipeClick → ApplyFillRecipe → SelectItemsForRecipeLinked → GetLinkedOwnersInFillOrder
  → GetAllLinkedOwnerIds`，后者反汇编实证读 State_Web_ToolTable 的 **PlayerBagOwnerId(0x30) +
  DrawerOwnerId(0x88) + CabinetTabs(0x90 列表遍历)**，取完 `AnnounceTookFromOtherTabs` 提示。
  → 柜子进 CabinetTabs 即原生参与自动补料，零额外改动。
- mod 做法：启动后（5s 幂等）向该 HashSet `Add` 柜子家具 ConfigId。interop 静态私有字段直访
  （首访即触发 cctor），不依赖版本敏感地址。

### 2.2 烹饪类界面（内联冷链过滤，不走白名单）

- `AE_OpenCookingPanel.Run` 内联过滤 `IsElectrical && ElectricalType==1 && ColdRate>0`（只认冷链
  电器）；老鼠笼 `AE_OpenRatCagePanel.CollectFridges`、酿酒 `AE_OpenBrewPanel.CollectFridges` 同款。
  （dump 里带 CabinetBags 的是压缩机 Ac_Compressor_Open，不是酿酒——曾误归，已更正。）
- 切换路径三界面同构且通用：`ApplySwitchBag(ownerId) → State_Extensions_Item.GetBagData(ownerId)`，
  无类型过滤，tab 的 Locked 标志控制解锁。
- mod 做法：**状态层页签注入**——`ReduxUISystem`（BaseSingleton）→ `IsUIActive(Il2CppType)` +
  `GetStateByUI(Type)→State_Web_XXX`（非泛型，绕开 interop 泛型坑）→ 向 `BagTabs`
  （ObservableList）追加 `Data_Web_XXX_BagTab`（构造 R3.ReactiveProperty 填 OwnerId/Name/Locked=false）。
  0.5s 轮询幂等：界面打开期间发现柜子 ownerId 缺页签就补挂（打开/刷新重建后自动恢复）。

### 2.3 柜子名单自动发现

- 扫 `ConfigManager._Config_Furniture_Dict`：`BagId>0 && FurnitureType==2(Storage)` 且
  非冷链电器（与原生 CollectFridges 同判定），套用户 Include/Exclude。
- 默认排除 BagId：110000/110001/110008（置物架系——工作台已有原生物料架左栏，110001 与工具柜
  共用包且工具柜已在官方白名单）、5001/5002（燃料炉/电烤箱，烹饪电器）。
- 实测 37 条入选（箱子/大储物柜/收纳架/储物柜/橱柜/金属柜 380/储物柜 383/工具柜 406/普通储物柜
  435/保险箱/800xx 探索图储物柜等）。注意：FurnitureType 并非"储藏"唯一权威（9120 金属柜等场景
  固定物是 Type=0），但可放置的家柜族全在 Type=2 内；Type=0 的床头柜/纸箱/衣柜默认不含，要可加
  IncludeFurnitureIds。
- 家中实例枚举：`BattleLogicWorld._AgentManager.GetHomeMapId() + GetFurnituresWithBag(mapId,true,false,false)`
  （与原生调用形态一致），跳过 `IsBagLocked` 实例；tab 按实例逐个出（多柜多 tab，与冰箱同语义）。

## 3. 代码结构

| 文件 | 职责 |
|---|---|
| `CabinetEverywherePlugin.cs` | 入口：配置定义、AccessTicker（慢通道 5s：名单重扫+白名单；快通道 0.5s：页签轮询） |
| `CabinetCatalog.cs` | 柜子发现：配置层名单（RebuildConfigIds/DescribeAll）+ 家中实例（RefreshHomeInstances） |
| `CraftSourceInjector.cs` | 白名单注入（幂等 Add + 日志自证） |
| `TabInjector.cs` | 烹饪/老鼠笼/酿酒页签注入（会话防重、缺则补挂） |
| `ModShared\BagTableRw.cs` | 共享源码（ConfigManager/FurnDict/ParseIds/FurnName，编译进本 dll） |

## 4. 配置项（com.local.survivallog.cabineteverywhere.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| EnableCraftSource | true | 工作台/无人机交易/站/粉碎机/温室 的页签+自动取料 |
| EnableCooking / EnableRatCage / EnableBrew | true | 三界面页签注入 |
| IncludeFurnitureIds | 空 | 追加家具 ConfigId（床头柜/纸箱/衣柜等要进页签就加这里） |
| ExcludeFurnitureIds | 空 | 排除家具 ConfigId（优先级最高） |
| ExcludeBagIds | 110000,110001,110008,5001,5002 | 排除整包系 |
| ListOnReady | true | 启动日志列全名单（核对用） |
| Verbose | false | 每条明细 |

发版打包时 make_mods_release.ps1 会把 Include/ExcludeFurnitureIds 归一为空、ExcludeBagIds 保留默认。

## 5. 与原生 / 其他 mod 的交互

- 与 4 个扩容 mod 零冲突（一个管格子数量，本 mod 管出现在哪些界面）。
- 温室/粉碎机连带认柜子（白名单 5 消费方共用，无法只开三个界面——属增益，已在文档说明）。
- 柜子页签遵守原生规则：无负重语义；保鲜看家具冷链属性（柜子非冷链，食物放柜子不保鲜——原生行为）。
- interop 依据：`ToolCabinetConfig.CraftSourceFurnitureIds` 静态属性直访（interop 暴露私有静态
  字段，先例 ConfigManager._bagSizeSet）；`Il2CppType.From()` 转换 UI 类型参数；
  `R3.ReactiveProperty<T>` 构造可 new（游戏全量使用该实例化）。

## 6. 已知限制

- 烹饪"一键填装"是否遍历全部页签未逐指令实证（工作台已实锤）；实测以切换到柜子页签再点菜谱
  为保底用法。若发现填装只认背包，属上游行为，非本 mod 缺陷。
- 家具 Type=0 的场景固定储物物（9120 金属柜/9168 抽屉柜等）默认不含（不可搬运场景摆设）。
- 页签为运行时注入，烹饪类界面每次重开会重建（0.5s 内自动补回，肉眼无感）。
- 版本鲁棒性：白名单/状态层路径均不依赖硬编码地址；仅"烹饪填装遍历范围"依赖上游行为。

## 7. 演进要点

- v1.1.0（2026-10-10）：首发。白名单注入 + 烹饪/老鼠笼/酿酒页签注入；37 条柜族名单（启动日志自证）。
