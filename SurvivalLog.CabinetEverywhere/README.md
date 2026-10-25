# 储藏柜直达 CabinetEverywhere（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

家里的柜子再多，做顿饭、修个东西还是得在背包和柜子之间来回倒腾。本 mod 让普通储物类家具（储物柜、橱柜、金属柜、收纳架、保险箱等）直接出现在各大加工与交易界面的容器页签里：工作台做东西能自动从柜子取料，烹饪、酿酒、无人机交易也都能直接翻柜子。默认开箱即用——家里所有带储物格的家具自动生效，烹饪类界面默认只看食物柜和冰箱，工作台默认只看材料柜，无人机交易/调度机队默认全部可见并可按标签下拉筛选。

## 二、功能清单

- **九大界面柜子直达**（默认全开，可按界面单独关）：工作台、无人机交易、调度机队（无人机站）、粉碎机、温室工程台出现柜子页签，制作/交易可直接从柜子取料（自动取料）；烹饪、老鼠笼、酿酒桶、颜料桶出现柜子页签。
- **场景视图**：10 个界面各自默认收窄视野——烹饪、老鼠笼、酿酒桶、颜料桶、口粮压制机默认只看"食物"标签柜 + 全部冰箱；工作台、粉碎机、温室工程台默认只看"材料/工具"标签柜；无人机交易、调度机队默认下拉自由筛选。每个界面可改为 Food / Material / Browser 三种视图。
- **官方标签体系**：筛选用的就是背包界面给柜子设置的那套官方标签（机器人归置共用同一套）；下拉选择按界面分别记忆，重启保持。
- **批量制作不卡**：工作台按当前视图收窄取料与扫描范围（所见即所取料范围），并对袋刷新做帧合并——柜子再多，批量制作也不因页签变多而卡顿。
- **默认排除功能容器**：自制工具台、载具（购物车/自行车/汽车等）、染料桶、材料堆、厨房电器、无人机货舱/中控台等非储物容器不会混进页签；追加/排除名单可在配置里自定义。
- **发电机加油界面不在范围**：那里原版只有背包、没有容器页签系统，本 mod 不覆盖（特此说明，管理预期）。

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原。
- 全部改动都在游戏运行内存里完成：柜子名单、界面页签、"制作材料源"名单都是启动后动态加入，关掉总开关即动态移除；游戏文件零改动。
- 保鲜遵循原生规则：柜子不是冷链电器，食物放柜子里不会保鲜（原版行为，本 mod 不改）。
- 与各扩容类 mod 互不冲突（它们管格子数量，本 mod 管柜子出现在哪些界面）。
- 已知限制：场景固定摆放、不可搬运的金属柜/抽屉柜默认不含；在背包界面改标签后，最迟 5 秒内同步到筛选；烹饪类界面重开时页签会重建（0.5 秒内自动补回，肉眼无感）。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.CabinetEverywhere.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载），游戏回到原版——制作类界面恢复只认背包/抽屉/冰箱/官方工具柜。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.cabineteverywhere.cfg`（游戏运行中改完保存，多数键约 5 秒内自动生效，无需重启；已打开的界面重开一次即恢复注入）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关。false=移除已注入的柜子页签并停止全部注入；下次启动不加载 |
| Access.EnableCraftSource | true | 工作台/无人机交易/无人机站/粉碎机/温室：柜子页签与制作自动取料 |
| Access.EnableCooking | true | 烹饪界面注入柜子页签 |
| Access.EnableRatCage | true | 老鼠笼界面注入柜子页签 |
| Access.EnableBrew | true | 酿酒桶界面注入柜子页签 |
| Access.EnableDye | true | 颜料桶界面注入柜子页签 |
| Access.IncludeFurnitureIds | （空） | 追加的家具 ConfigId（逗号分隔，强制加入） |
| Access.ExcludeFurnitureIds | `313,80033,80164,1024,1025,1026,1027,1028,42033,66196,330,384,389,9126` | 排除的家具 ConfigId（优先级最高）。默认排除自制工具台、载具类（购物袋/购物车/自行车/小汽车/SUV/旧SUV）、染料桶、材料堆等非储物或角色专属家具 |
| Access.ExcludeBagIds | `1008,1009,1014,1050,1060,10001,10004,3001,115004,5001,5002,5003,5004,5005,5006,5007,5008,5009,5011,5014,5015,5017,5020` | 排除的 BagId（整包系排除）。默认排除厨房电器、机器功能槽（老鼠笼/粉碎机/口粮压制机/暖房工程台等）、围栏、吊篮、无人机货舱/中控台、门窗、床、垃圾桶等非储物功能容器；其余带储物格家具默认全部生效 |
| Filter.Enable | true | 标签筛选总开关。关闭后不注入筛选（场景视图一并失效，容器页签全部显示） |
| Scenario.View_Cooking | Food | 烹饪界面视图（视图含义见表后说明） |
| Scenario.View_RatCage | Food | 老鼠笼界面视图 |
| Scenario.View_BrewPanel | Food | 酿酒桶界面视图 |
| Scenario.View_DyePanel | Food | 颜料桶界面视图 |
| Scenario.View_Compressor | Food | 口粮压制机界面视图 |
| Scenario.View_ToolTable | Material | 工作台界面视图 |
| Scenario.View_Shredder | Material | 粉碎机界面视图 |
| Scenario.View_GreenhouseBuild | Material | 温室工程台界面视图 |
| Scenario.View_Trade | Browser | 无人机交易界面视图 |
| Scenario.View_DroneHub | Browser | 调度机队（无人机站）界面视图 |
| Debug.Verbose | false | 输出每条明细日志 |
| Debug.ListOnReady | true | 启动时在日志列出发现的全部柜子名单（首次核对用，可关） |
| Debug.DumpFurniture | false | 排查"新柜子不生效"用：置 true 后（约 5 秒内生效）在日志列出家中全部带储物格家具明细，平时关 |

视图取值：**Food**=食物标签柜 + 全部冰箱；**Material**=材料/工具标签柜（不含冰箱与未打标签柜，通信设备等无标签容器要看请切 Browser 选「其他」）；**Browser**=下拉自由筛选。

## 六、English

**Cabinet Everywhere** — Every storage cabinet becomes a container tab in crafting and trade screens, with per-screen tag filtering.

**Features**
- Cabinet tabs in 9 screens: workbench / drone trade / drone hub / shredder / greenhouse (tabs + auto material pulling), and cooking / rat cage / brewing / dye (tabs). All on by default.
- Scenario views: cooking-family screens default to food-tagged cabinets + all fridges; workbench-family defaults to material/tool cabinets; trade screens default to a free tag filter dropdown. Each of the 10 screens is configurable (Food / Material / Browser).
- Uses the game's official cabinet tag system (same tags you set in the backpack screen); dropdown selection is remembered per screen.
- Workbench rescans and material pulling follow the current view, with frame-coalesced bag refreshes — no crafting lag even with many cabinets.
- Non-storage containers (crafting benches, vehicles, dye bucket, kitchen appliances, drone bays, etc.) are excluded by default; include/exclude lists are configurable.
- The generator refuel screen is out of scope (it has no container tab system in vanilla).

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; the game returns to vanilla (crafting screens only see backpack/drawer/fridge/official tool cabinet again).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.cabineteverywhere.cfg`. Most keys apply within ~5 seconds without restarting. Key knobs: per-screen on/off switches, per-screen Food/Material/Browser views, and include/exclude furniture/bag ID lists.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
