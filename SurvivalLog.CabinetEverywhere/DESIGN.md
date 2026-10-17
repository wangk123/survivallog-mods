# 储藏柜直达 + 标签筛选/场景视图 CabinetEverywhere — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。
> 2026-10-17 合并重写：原独立 mod `SurvivalLog.CabinetTagFilter` 并回本 mod，其设计文档
> （DESIGN-TagFilter-Archive.md）全部技术取证与演进史保留在**附录 C**，原文件已删除。

## 1. 功能定位

让普通储物类家具（储物柜/橱柜/金属柜/收纳架/保险箱等）出现在制作类界面的容器页签里，
并按界面场景自动收窄页签视野（或手动按标签筛选）：

- **直达**：工作台 / 无人机交易 / 无人机站 / 粉碎机 / 温室 = 柜子页签 + **制作自动取料**
  （官方白名单路径）；烹饪 / 老鼠笼 / 酿酒桶 / 颜料桶 = 柜子页签（状态层注入路径）。
- **场景视图**：每个界面可配置固定视图（食物/材料）或浏览器式下拉自由筛选（见下表）。
- **标签筛选**：标签用游戏官方的"柜子标签规则"（背包界面给柜子设置的同一套，机器人按
  标签归置共用）；A/D 步进切页签自动跳过被藏柜子（十七+轮防闪烁/防卡死工程，附录 C）。

不改存档、不改游戏文件，删 dll 即还原。**发电机加油界面原生没有容器页签系统（只有背包），
不在本 mod 范围**（要支持等于新造 UI 功能，另立项）。

### 场景视图表（cfg [Scenario]，默认值按 2026-10-17 spec）

| 界面 | 页面 id（实测 iframe id） | 族别 | 默认视图 |
|---|---|---|---|
| 烹饪 | Cooking | bag | Food |
| 老鼠笼 | RatCage | bag | Food |
| 酿酒桶 | BrewPanel | bag | Food |
| 颜料桶 | DyePanel | bag | Food |
| 口粮压制机 | Compressor | cart0 | Food |
| 工作台 | ToolTable | tt | Material |
| 粉碎机 | Shredder | cart | Material |
| 温室工程台 | GreenhouseBuild | cart | Material |
| 无人机交易 | Trade（不是 TradeUI） | trade | Browser |
| 调度机队（无人机站） | DroneHub | bag | Browser |

视图语义（JS `viewAllows`）：

- **Food**：全部冰箱一律显示（无论有无标签）；柜子按食物域命中（任一标签 Dim 1-4）显示；
  材料架必然不中→隐藏。
- **Material**：材料 + 工具 + 未打标签的柜子显示；材料架显示；**冰箱隐藏**。
  材料组当前实现 = 材料+工具+未标签（不含冰箱、不含食物域柜子）。**语义以 K2 取证结论
  为准**（工作台配方是否消耗食物域材料，作者节点取证中）；若 K2 结论需含食物，后续版本
  再加 cfg 开关（当前无此键，勿在文档/说明里写"已支持"）。
- **Browser**：下拉自由筛选，选项 = 全部柜子 / 其他（未打标签容器）/ 食物（Dim 1-4 汇总）/
  物资维度（Dim 0）逐标签按官方 Order；材料架补"材料"伪标签参与筛选。选中态按界面记忆
  （localStorage `sltf_sel_v3`，键 = iframe 元素 id），重启保持。

固定视图页无任何 UI 注入（无下拉/触发器）；Browser 页注入自绘下拉（§2.5）。
筛选总开关 `[Filter] Enable` 关闭后不注入筛选（场景视图一并失效，页签全部显示）。

## 2. 实现原理

### 2.1 官方"制作材料源"白名单（直达的核心杠杆，2026-10-10 反汇编实证）

- `ToolCabinetConfig.CraftSourceFurnitureIds`：静态 `HashSet<int>`，cctor 硬编码 **{70006 工具柜}**；
  `IsCraftSource(configId)` = 集合命中。
- E8 扫描全部调用方共 5 个：`AE_OpenHandMade.Run`（工作台）、`ItemManager.CollectTradeContainers`
  （无人机交易，经 OnTradeContainerTabsRequest）、`AE_OpenDroneHubPanel.CollectContainers`
  （无人机站，冷链∪白名单）、`AE_OpenShredderPanel.Run`（粉碎机）、`GreenhouseProgress.CollectMaterialSources`（温室）。
- **工作台自动取料链**：`RA_ToolTable_RecipeClick → ApplyFillRecipe → SelectItemsForRecipeLinked
  → GetLinkedOwnersInFillOrder → GetAllLinkedOwnerIds`，后者反汇编实证读 State_Web_ToolTable 的
  **PlayerBagOwnerId(0x30) + DrawerOwnerId(0x88) + CabinetTabs(0x90 列表遍历)**，取完
  `AnnounceTookFromOtherTabs` 提示。→ 柜子进 CabinetTabs 即原生参与自动补料，零额外改动。
- mod 做法：启动后（5s 幂等）向该 HashSet `Add` 柜子家具 ConfigId。interop 静态私有字段直访
  （首访即触发 cctor），不依赖版本敏感地址。

### 2.2 烹饪类界面（内联冷链过滤，不走白名单→状态层注入）

- `AE_OpenCookingPanel.Run` 内联过滤 `IsElectrical && ElectricalType==1 && ColdRate>0`（只认冷链
  电器）；老鼠笼 `AE_OpenRatCagePanel.CollectFridges`、酿酒 `AE_OpenBrewPanel.CollectFridges`、
  颜料桶同款（PollDye，2026-10-17 加入；interop 取证 `WebUI_Dye` / `State_Web_Dye.BagTabs` /
  `Data_Web_Dye_BagTab` 四字段 Index/OwnerId/Name/Locked，类型与烹饪族同构）。
- 切换路径各界面同构且通用：`ApplySwitchBag(ownerId) → State_Extensions_Item.GetBagData(ownerId)`，
  无类型过滤，tab 的 Locked 标志控制解锁。
- mod 做法：**状态层页签注入**——`ReduxUISystem`（BaseSingleton）→ `IsUIActive(Il2CppType)` +
  `GetStateByUI(Type)→State_Web_XXX`（非泛型，绕开 interop 泛型坑）→ 向 `BagTabs`
  （ObservableList）追加 `Data_Web_XXX_BagTab`（构造 R3.ReactiveProperty 填 OwnerId/Name/Locked=false）。
  0.5s 轮询幂等：界面打开期间发现柜子 ownerId 缺页签就补挂（打开/刷新重建后自动恢复）。
  注入后必须 `WebUIVm.MarkAllMsgDirty() + OnMsgChange()` 重推（脏标记制，附录 B §B4）。

### 2.3 柜子名单与统一容器扫描（CabinetCatalog，单遍两名单）

- 配置层：扫 `Config_Furniture_Dict`，默认收录**全部带储物包**的家具（不按 FurnitureType
  分类——实测 Type=0/3 里也有大量真容器），仅排除非储物包 + 冷链电器 + 用户 Include/Exclude。
  家具粒度默认排除（与纸箱/自制工具台共用包，按包排会误伤纸箱，只能按家具 ID）：自制工具台
  313/80033/80164、载具 1024-1028/42033、**染料桶 66196（BagId 1010）、材料堆 330/384/389/9126
  （BagId 2003，仓库管理员专属四个变体）**——后两者 2026-10-17 作者实测日志实锤混进工作台/
  粉碎机的柜子名单（功能容器/角色专属），追加排除。
- 运行时：**单遍直扫 `AgentManager.agentList`**（5s 周期，原直达/筛选两个扫描合一），
  同一次遍历产出两份名单：
  - `AllContainers`——家中全部储物容器（柜/冰箱/材料架 + 标签 TagIds + Kind），**筛选用**；
  - `HomeInstances`——其中柜子子集（kind=='c' 且配置名单命中），**直达注入用**（语义与合并前一致）。
- Kind 三值：'c'=柜子（直达注入子集）/ 'f'=冰箱（冷链电器，走原生冰箱页签）/ 'r'=材料架
  （MaterialRackComponent 存在即材料架，不能打标签只能放材料）。
- 非储物功能包双保险排除（cfg ExcludeBagIds + 代码内 NonStorageBagIds 常量）：功能容器页签
  （如无人机**中控台 BagId 1009**，家具 9080——2026-10-17 作者实测发现 1008 无人机货舱在名单
  而 1009 漏网，被注入烹饪/酿酒等界面，已补；两包同在名单）误收进筛选名单会被当柜子藏掉，
  A/D 切过去即被弹回＝卡死闪一下（2026-10-15 五轮实测，附录 C）。
- 性能顺序：TryCast → 配置查包/名单（廉价，滤掉 ~90% 家具）→ 主图严格相等短路 → 仅非主图
  走 IsHomeMap（家地图组判定，重）→ 通过者才读类别/标签（组件字典/电器属性最贵，放最后）。
- Redux 包镜像补全：新摆放家具的包要重读档才进 `State_Data_Item.BagCache`（GetBagData 直接
  索引，缺失即抛→页签点切换静默失败）；每轮为缺失柜子补 `BagCache[owner]=bagConfigId + 空
  OwnerCache`，与读档后状态一致（附录 B §B4 根因 3）。

### 2.4 筛选通道与数据契约 v3（TagFilter + tagfilter.page.js）

- 通道：`WebUILayer.canvasWebViewPrefab.WebView`（Vuplex IWebView）`ExecuteJavaScript` 注入
  引导脚本（内嵌资源 `tagfilter.page.js`，每 5s 幂等重注，WebView 崩溃重建后自动重挂）+ 每周期
  直推标签数据 JSON（体量 ~2KB，免维护注入先后时序）。Root 与 iframe 同源（实测验证成立，
  探针留痕：日志 `同源探针: OK/XORIGIN`）。
- **数据契约 v3**（`window.__SLTF__.setData(json)`）：
  - `owners`：ownerId → `{t:[tagId...], kind:'c'|'f'|'r'}`（v2 为纯数组，读取统一走 `ownerOf`
    兜底旧形状）；家中全部容器含空标签；材料架补"材料"伪标签保留在 t 内。
  - `lib`：标签库（id/order/dim/name + **v3 追加 icon/color 原样字符串透出**，JS 不解析只透传
    给自绘下拉）。Dim 语义（BackpackUI.js 实证）：4=全部食物 / 1=做到哪一步 / 2=什么食物 /
    3=状态 / 0=物资。
  - `scenario`：页面 id → 'food'|'material'|'browser'（cfg [Scenario] 直出，cfg 改动重推即换视图）。
- **记忆 v3**：`localStorage.sltf_sel_v3` = `{页面id: 选择}`，按界面独立记忆（v2 全局单值废弃、
  不迁移）；选择 = 标签 id 数字 | 'other' | 0（全部柜子）。
- JS 侧行为全貌（截读消息/隐藏 DOM/**扣发+伪造切换**/回声丢弃/冻结/自绘下拉）见附录 C
  （原 TagFilter 设计文档实录，含十七+轮防闪烁演进）；**当前不再是"只隐藏 DOM 不发事件"**——
  A/D 步进到被藏柜子时 JS 会以页面同款事件伪造切换（官方链，见附录 C 演进七轮起）。

### 2.5 自绘下拉（2026-10-17 定稿：BetterTrade 同构——包裹器+行内插入+纯 CSS 锚定+静态样式表）

> 十轮作者实测反馈迭代出的终态。教训链（症状→根因→为什么终态这样设计）：
> 页签行末被挤→改绝对定位；视口角落跑出面板→改列容器锚定；尺寸两页不一致→k 反推；
> 点开列表尺寸跳→样式表被无 k 调用按 kk=1 整表重写（采样实锤 sheet 字体 12.26→7.76）；
> 首帧闪小→按钮未渲染时 k 回退 body zoom=1（交易页缩放在祖先层）；改测一次写死→首帧量错
> 成永久错误；offsetTop 定位→行内坐标恒≈0 误判"放不下"向下开出屏；包裹器被误设 static→
> 弹层失去锚点飘出屏+被 #trade overflow 裁切隐身（CDP 实锤 pop y=-108）。
> **结论：JS 算坐标/运行时算样式这条路整体是错的**（每条换算路径都是新 bug 源）。终态与
> BetterTrade 自身同构：它怎么做我们怎么做，浏览器布局引擎负责一切几何。

- **包裹器（.sltf-dd-wrap）**：按钮外套一个 `position:relative;display:inline-flex` 的 div
  （BetterTrade .bt-view 根同构）——**relative 是弹层的锚定块，行内分支永不许改 static**
  （改 static 弹层即失去锚点，作者实测隐身根因）；无锚行兜底分支用 absolute（同为 positioned，
  弹层锚定同样成立）。Vue 重渲染摘除由 isConnected/ownerDocument 重建分支兜底。
- **触发器落位（placeDD）**：**行内插入**（零坐标计算）——交易页插在 `.sort-btn`（BetterTrade
  自己的稳定锚，它每次重绘 insertBefore 到 sort-btn 紧左边、够不到我们右侧）之后；调度机队插进
  负重条行 `#bagCap`（官方 flex 行实证）最左；锚行必须 flex（守卫），无锚行=col 绝对左下兜底。
  外来 mod 按钮卸载→行自动收拢（"自动居左"免费获得）。旁按钮晚注入的时序由 0.5/1.5/3s 延迟
  重放三轮覆盖。排除自家弹层元素参与锚扫描（弹层选项行在列底带会被误当邻居——"点一下跳位"根因）。
- **样式（ensureDDStyle，静态一次写成永不重写）**：**逐字克隆 BetterTrade 下拉库 CSS**（其 dll
  内嵌 page.js 提取的 .PFX-btn/:hover/.PFX-active/.PFX-menu/.PFX-on，token：fg #fff、accent
  #fbb034、menu-bg #1d1e22）——两页同款（作者核心诉求）。计量值按**静态查表 k**（DD_K_TABLE：
  Trade 0.62/DroneHub 0.845，作者机实测）归一：css=视觉值÷k（视觉值=BT css×DD_VIS 0.647）；
  **按钮/选项行显式 height**（内容撑高在缩放层下有舍入怪癖，回归测试实测差 6.6px，显式高后
  两页 0.0px 差）。样式表带 `__sltfK` 只写一次——**任何路径不再重写**（重写=尺寸跳变之源）；
  页面重载随文档重建。选中某标签→按钮 `.on` 金色激活态（其排序激活同语义）。内联 cssText 只留
  布局（内联外观压死样式表 :hover，复审 R1 旧坑）。
- **弹出层（togglePop）**：挂**包裹器**内，`position:absolute; left:0; bottom:calc(100% + 6px)`
  ——**纯 CSS 锚定零 JS 坐标**（BetterTrade .PFX-menu 同款）；右缘溢出翻转 right:0（rect 只做
  视觉空间比较，无换算）。弹层自身尺寸（min 104/max 150/max-height 190≈10 行+滚动）按表 k 归一。
  选项行 `<button>`+data-interactive；官方图标（TAG_ICONS 同源提取+33 键静态兜底）+官方色
  currentColor+选中 ✓。任意 mousedown（capture）关闭。
- **回归测试（tools/dd_regression_harness.html + dd_regression_test.js，本地 Chromium 无游戏
  依赖）**：仿两页缩放结构（.pa zoom .845/.pb zoom .62）+overflow:hidden 祖先，12 项断言：
  弹层贴身（gap 3-10px）/水平对齐/不出视口/不遮按钮/包裹器 relative/两页按钮视觉等高（≤1.2px）/
  激活不连坐外来按钮。**本控件任何改动必须先过这套**（规则同 docs\00：构建 0 错误+node --check）。

#### 2.5.1 页面双缩放空间（2026-10-17 实证，后续所有 UI 注入任务的硬知识）

- **双空间问题**：rect（getBoundingClientRect）是缩放后**视觉像素**，element.style 写的是缩放前
  **css 布局像素**，同一坐标两空间差 1/缩放系数。**两页缩放挂的层级不同**：调度机队=body 带
  CSS `zoom`（≈0.845，getComputedStyle(body).zoom 测得到）；交易页=祖先层 #app 链缩放（≈0.62、
  body zoom=1）——只读 body zoom 的官方 showFitTip 公式在交易页静默失效。
- **测量路线全部失败**（勿再走）：按钮自比值 k（未渲染时回退 body zoom=1→首帧闪小+祖先层页
  恒错）；列容器比值 k（测一次写死→首帧量错成永久错误）；rect÷k 定位（长距离换算放大表值误差
  →遮盖/飘远/出屏三症状）；offsetTop 定位（行内坐标恒≈0 误判放不下）。**终态两招**：
  ①计量类 css 用**静态查表 k** 生成一次（表值=作者机实测，改分辨率/游戏改版更新 DD_K_TABLE）；
  ②几何锚定用**纯 CSS**（包裹器+bottom:calc()），浏览器布局引擎原生处理缩放，无任何换算路径。
- 规则：凡「rect 读出→写进 style」的换算=潜在 bug 源，优先找 CSS 原生锚定替代；确需换算的
  场景（如右溢出判断）只做 rect↔rect 视觉空间比较。

### 2.6 代码结构

| 文件 | 职责 |
|---|---|
| `CabinetEverywherePlugin.cs` | 入口：配置定义（Access/Filter/Scenario/Debug 四区）、AccessTicker（慢通道 5s：名单重扫+白名单+统一扫描+筛选；快通道 0.5s：页签注入轮询） |
| `CabinetCatalog.cs` | 统一容器扫描：单遍产出 AllContainers（筛选）+ HomeInstances（直达）+ 包镜像补全 + DumpFurniture 诊断 |
| `CraftSourceInjector.cs` | 白名单注入（幂等 Add + 日志自证） |
| `TabInjector.cs` | 烹饪/老鼠笼/酿酒/颜料桶 页签注入（会话防重、缺则补挂、Repush 重推） |
| `TagFilter.cs` | WebView 注入通道：幂等引导 + 标签数据 JSON v3 + scenario 段 + 同源探针 |
| `tagfilter.page.js` | 内嵌资源 JS：截读页签数据、DOM 隐藏、消息层扣发+伪造切换、回声丢弃、场景视图判定、自绘下拉 |
| `ModShared\BagTableRw.cs` | 共享源码（ConfigManager/FurnDict/ParseIds/FurnName，编译进本 dll） |

**JS 改动纪律**：`tagfilter.page.js` 是内嵌资源（verbatim 字符串坑根治，node --check 可校验）；
改完必须 `node --check` + 重编 dll + 部署复测（A/D 步进/到头/边界落点/交易页卡死是回归高危区，
见附录 C 演进史）。

## 3. 配置项（com.local.survivallog.cabineteverywhere.cfg）

| 分区.键 | 默认 | 说明 |
|---|---|---|
| Access.EnableCraftSource | true | 工作台/无人机交易/站/粉碎机/温室 的页签+自动取料 |
| Access.EnableCooking / EnableRatCage / EnableBrew / EnableDye | true | 烹饪/老鼠笼/酿酒桶/颜料桶 页签注入 |
| Access.IncludeFurnitureIds | 空 | 追加家具 ConfigId（强制加入） |
| Access.ExcludeFurnitureIds | 313,80033,80164,1024-1028,42033,66196,330/384/389/9126 | 排除家具（自制工具台+载具系+染料桶+材料堆：后两者=功能容器/角色专属，与纸箱共用包只能按家具排除） |
| Access.ExcludeBagIds | 电器/机器槽/围栏/吊篮/门窗/床/垃圾桶等 23 项（含无人机货舱 1008/中控台 1009） | 排除整包系（非储物功能包） |
| Filter.Enable | true | 标签筛选总开关：关闭后不注入筛选（场景视图一并失效，页签全部显示） |
| Scenario.View_Cooking / View_RatCage / View_BrewPanel / View_DyePanel / View_Compressor | Food | 烹饪/老鼠笼/酿酒桶/颜料桶/口粮压制机 视图 |
| Scenario.View_ToolTable / View_Shredder / View_GreenhouseBuild | Material | 工作台/粉碎机/温室工程台 视图 |
| Scenario.View_Trade / View_DroneHub | Browser | 无人机交易/调度机队 视图 |
| Debug.Verbose | false | 每条明细 |
| Debug.ListOnReady | true | 启动日志列全名单（核对用） |
| Debug.DumpFurniture | false | 全量家具明细诊断（owner/map/楼层/判定，热改约 5s 生效） |

视图枚举：Food=食物标签柜+全部冰箱；Material=材料/工具/未标签柜（不含冰箱）；
Browser=下拉自由筛选。发版打包时 make_mods_release.ps1 会把 Include/ExcludeFurnitureIds 归一
为空、ExcludeBagIds 保留默认、Filter.Enable 归一 true、Scenario.View_* 归一发布默认、
Debug 全关（防本机实验值带进发布包）。

**cfg 首跑固化坑**：BepInEx 不用代码新默认覆盖已生成的 cfg——改默认名单后要同步改用户侧
cfg 文件（置物架排除/炉具排除/电器排除三次演进都靠手工同步）。
**旧独立筛选 mod 用户**：曾装过 `com.local.survivallog.cabinettagfilter.cfg` 且 Enable=false 的，
合并后筛选开关以本 cfg `[Filter] Enable`（默认 true）为准，旧 cfg 随安装器清理（InstallDelete）。

## 4. 与原生 / 其他 mod 的交互

- 与 4 个扩容 mod 零冲突（一个管格子数量，本 mod 管出现在哪些界面/可见性）。
- 温室/粉碎机连带认柜子（白名单 5 消费方共用，无法只开部分界面——属增益）。
- 柜子页签遵守原生规则：无负重语义；保鲜看家具冷链属性（柜子非冷链，食物放柜子不保鲜——原生行为）。
- 筛选是显示层：不改下发给界面的 tab 数组/下标（索引制界面切错柜子的根因，附录 C §C6），
  自动取料链不受影响。
- interop 依据：`ToolCabinetConfig.CraftSourceFurnitureIds` 静态属性直访（先例
  ConfigManager._bagSizeSet）；`Il2CppType.From()` 转换 UI 类型参数；`R3.ReactiveProperty<T>`
  构造可 new；`agentComponentDictionary` 按 Type 索引直达组件（无泛型/枚举坑）。

## 5. 已知限制

- 烹饪"一键填装"是否遍历全部页签未逐指令实证（工作台已实锤）；实测以切换到柜子页签再点菜谱为保底用法。
- 家具 Type=0 的场景固定储物物（9120 金属柜/9168 抽屉柜等）默认不含（不可搬运场景摆设）。
- 页签为运行时注入，烹饪类界面每次重开会重建（0.5s 内自动补回，肉眼无感）。
- mod 注入前界面已收到首份 BagMsg 的场景：页签数据要等下一次推送才开始隐藏（轮询周期 5s 内）。
- 标签变更（背包界面改标签）最迟 5s 内同步到筛选。
- Browser 页选中一个没有任何容器匹配的标签时会藏掉全部容器页签（只剩背包）——全量选项语义下的预期行为。
- 同源不成立（探针非 OK）时筛选整体静默失效，需换 C# 状态层方案另议。
- 版本鲁棒性：白名单/状态层路径均不依赖硬编码地址；官方 WebUI 页面 DOM/字段变化会命中
  detectC 兜底"宁可不藏也不错藏"（附录 A 穷举表是对照基线）。

## 6. 演进要点

- v1.1.0（2026-10-10）：直达首发。白名单注入 + 烹饪/老鼠笼/酿酒页签注入；37 条柜族名单（启动日志自证）。
- v1.1.2（2026-10-11）：修复"新柜子不生效"四连根因（附录 B §B4）；名单放宽为全容器默认收录；
  扫描性能优化；DumpFurniture 诊断；排除清单定型。
- v1.3.0（2026-10-14）：标签筛选拆分为独立 mod CabinetTagFilter（本轮已并回，历史见附录 C）。
- 2026-10-17（本次，版本号待作者定）：
  - **合并**：CabinetTagFilter 并回本 mod（双 Ticker 合一、容器扫描合一为 CabinetCatalog 单遍
    两名单、JS 迁内嵌资源 tagfilter.page.js）；安装器移除独立组件、InstallDelete 清理旧
    dll/cfg（发布登记见 CHANGELOG/安装器脚本）。
  - **场景视图**：cfg [Scenario] View_*（10 页）+ 数据契约 v3（owners 对象化/lib 带图标色/
    scenario 段）+ 固定视图判定 viewAllows（food/material/browser）。
  - **记忆 v3**：sltf_sel_v3 按界面独立记忆（iframe id 为键）+「其他」（未打标签容器）选项。
  - **颜料桶**：PollDye 页签注入（颜料桶界面柜子页签，bag 族筛选自动覆盖）。
  - **自绘下拉（终态）**：BetterTrade 同构——包裹器（relative，弹层 CSS 锚点）+ 行内插入
    （交易页 .sort-btn 右侧稳定锚/调度机队负重条行最左）+ 纯 CSS 弹层锚定
    bottom:calc(100%+6px) + 静态样式表（BT 下拉库逐字克隆，k 查表归一+显式高度，一次写成
    永不重写）+ 本地回归测试 12 断言（tools/dd_regression_*，无游戏依赖，改动必过）。
    十轮反馈排障史（症状→根因→教训）见 §2.5 引语与 §2.5.1。
  - **取证工具**：tools/cabinet_views_cdp.js（CDP 只读断言：计数/下拉/记忆三模式）+ K2/K3/K4
    取证操作手册（作者节点）。
  - **白盒穷举**：10 页 × 页签字段/active 字段/切换事件/族别 对照表落附录 A，拦截链覆盖核对闭环。
  - **作者实测反馈修复（fix-wave-2）**：染料桶 66196、材料堆 330/384/389/9126（与纸箱共用包
    只能按家具排除）与无人机中控台 BagId 1009 补进排除名单——cfg 默认值/游戏侧 cfg/
    NonStorageBagIds/本文档四处同步（§2.3、§3）。
  - **触发器/弹层十轮排障（fix-wave-2..9，2026-10-17）**：症状链与各轮根因已沉淀进
    §2.5 引语（跳变=无k调用整表重写、首帧闪小=未渲染回退body zoom、offsetTop 行内坐标
    误判、包裹器 static 失锚隐身等），终态方案与回归测试见 §2.5；双缩放空间硬知识 §2.5.1。

## 7. 防盗署名气泡（2026-10-14，套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列
  `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替）；管理器不可用时退
  `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。每次进档一次，回主菜单再进再弹。
- 套件全部 mod 调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建
  DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后全部套件 mod 需重编。

---

## 附录 A：白盒穷举表（2026-10-17，T10；十七+轮教训的对照基线）

**方法**：直读游戏目录 `WebUI\UI\*\*.html`（`E:\SteamLibrary\...\StreamingAssets\WebUI\`），
全库 grep 页签数组字段（cabinetTabs/bagTabs/cartTabs/bagTabsJson）命中**恰好 10 页**；逐页读
registerHandlers 段与 active 字段读取处填表；对照 `tagfilter.page.js` 拦截链：
wrapRelay 的 hasActive 判定字段清单 **{bagOwnerId, bagOwnerKey, activeLeftTab, activeTab}**、
tryHold/readActive 各族 active 读取、forgeSwitch 各族事件签名、detectC 族别判定。

| 页面（html） | 消息类型 | 页签数组字段 | active 字段 | 切换事件+负载（页面原生） | 族别 detectC | hasActive 覆盖 | forgeSwitch 签名一致 |
|---|---|---|---|---|---|---|---|
| ToolTable 工作台 | WebUI_ToolTable_BagMsg | cabinetTabs | activeLeftTab（0=背包/1=抽屉/≥2=柜子下标+2；另带 bagOwnerId=背包，非 active） | TAB_SWITCH {tab:int} | tt：#leftTabsScroll/.cabinet-tab，offset 0；pin 背包=栏外 .left-tab-pinned；抽屉 #tab-drawer 在 scroll 内但无 .cabinet-tab 类（不在 cands） | activeLeftTab ✓（bagOwnerId 亦在清单） | tt：tab=j+offset+2 ✓；edge：#tab-drawer→1、其余（tab-bag）→0 ✓ |
| Cooking 烹饪 | WebUI_Cooking_BagMsg | bagTabs | bagOwnerId | SWITCH_BAG {ownerId:int} | bag：#bagTabsScroll/.bag-tab，pin=tabs[0]@#bagTabPinned（.bag-tab-pinned-slot） | bagOwnerId ✓ | bag：parseInt(arr[j+1]) 数字 ✓；edge pin=arr[0] ✓ |
| RatCage 老鼠笼 | WebUI_RatCage_BagMsg | bagTabs | bagOwnerId | SWITCH_BAG {ownerId:int} | bag（同上） | ✓ | ✓ |
| BrewPanel 酿酒桶 | WebUI_Brew_BagMsg | bagTabs | bagOwnerId | SWITCH_BAG {ownerId:int} | bag（同上） | ✓ | ✓ |
| DyePanel 颜料桶 | WebUI_Dye_BagMsg | bagTabs | bagOwnerId | SWITCH_BAG {ownerId:int} | bag（同上；2026-10-17 PollDye 注入自动覆盖） | ✓ | ✓ |
| DroneHub 调度机队 | WebUI_DroneHub_BagMsg | bagTabs | bagOwnerId | SWITCH_BAG {ownerId:int} | bag（**#bagTabsScroll 先于 trade 判定**——容器 class 与 TradeUI 同名 .bag-tabs-scroll，先判 trade 会误读 bagOwnerKey 全部静默放行，十八轮 misB 实锤） | ✓ | ✓ |
| TradeUI 无人机交易 | WebUI_Trade_BagMsg | bagTabsJson（JSON 字符串，条目 .owner） | bagOwnerKey | SWITCH_BAG {ownerId:String(t.owner)} | trade：.bag-tabs-scroll .bag-tab[data-owner]（attr 制，Vue 页） | bagOwnerKey ✓ | trade：String(data-owner) **attr 直读，禁位置映射**（十一轮错位→消息风暴教训）✓；edge pin 自带 data-owner ✓ |
| Shredder 粉碎机 | WebUI_Shredder_BagMsg | cartTabs | activeTab | TAB_SWITCH {tab:int} | cart：#tabsScroll/.left-tab，pin=tabs[0]@#tabPinned（.left-tab-pinned） | activeTab ✓ | cart：tab=j+offset（pin 分离时=1）✓；edge pin→tab 0 ✓ |
| GreenhouseBuild 温室工程台 | WebUI_GreenhouseBuild_BagMsg | cartTabs | activeTab | TAB_SWITCH {tab:int} | cart（结构与粉碎机同：#tabPinned+#tabsScroll） | activeTab ✓ | ✓ |
| Compressor 口粮压制机 | WebUI_Compressor_BagMsg | cartTabs | activeTab | TAB_SWITCH {tab:int}（locked 页签 disabled 不绑点击） | cart0：#tabs/.left-tab，offset 0，**无 pin 分离**（全部页签同容器） | activeTab ✓ | cart0：tab=j ✓；locked 页签 disabled 被 isTabBtn/nearestVisible 排除，不会成为弹回目标 ✓ |

**覆盖结论**：10 页的 active 字段全部 ∈ hasActive 清单 {bagOwnerId/bagOwnerKey/activeLeftTab/activeTab}；
各族切换事件负载与 forgeSwitch 逐页同型（数字/字符串类型差异已按族处理：bag=数字、trade=字符串）；
extractTabs 的数组字段读取顺序（bagTabsJson → cabinetTabs → bagTabs → cartTabs）覆盖全部四种字段名；
detectC 五分支（tt/bag/trade/cart/cart0）恰好覆盖 10 页 DOM 且判定顺序正确（bag 先于 trade）。
**未发现缺口。**

**排除页核对**（grep 佐证，防漏网）：BackpackUI 的 TabsJson=背包标签编辑数据（非容器页签切换）；
MaterialRack/AidDepot/FeedingUI 等只收 bagOwnerId（单包视图）无页签数组；全库无第 11 个页签数组字段页。

**维护约定**：游戏更新后若某页改字段/改 DOM，对照本表即可定位缺口（某页 active 字段不在
hasActive 清单=轻量消息漏扣=闪；事件签名不符=伪造失效/错位）。改 JS 前先更新本表。

## 附录 B：原 CabinetEverywhere 设计实录（2026-10-10/11 取证与排障，合并前文档原文保留）

### B1. 柜子名单自动发现（2026-10-10）

- 扫 `ConfigManager._Config_Furniture_Dict`：`BagId>0 && FurnitureType==2(Storage)` 且非冷链电器
  （与原生 CollectFridges 同判定），套用户 Include/Exclude。
- 默认排除 BagId：110000/110001/110008（置物架系——工作台已有原生物料架左栏，110001 与工具柜
  共用包且工具柜已在官方白名单）、5001/5002（燃料炉/电烤箱，烹饪电器）。
- 实测 37 条入选（箱子/大储物柜/收纳架/储物柜/橱柜/金属柜 380/储物柜 383/工具柜 406/普通储物柜
  435/保险箱/800xx 探索图储物柜等）。注意：FurnitureType 并非"储藏"唯一权威（9120 金属柜等场景
  固定物是 Type=0），但可放置的家柜族全在 Type=2 内；Type=0 的床头柜/纸箱/衣柜默认不含，要可加
  IncludeFurnitureIds。
- 家中实例枚举：单遍直扫 `AgentManager.agentList`（与原生 GetFurnituresWithBag 同一数据源）。
  tab 按实例逐个出（多柜多 tab，与冰箱同语义）。
- **置物架语义**：小型(bag 1001)/中型(110001)/大型(110000)/超大型(110008)全系列默认收录
  （用户明确要求）；110001 与工具柜 70006 共用包不影响（tab 按家具实例走）。
- **配置名单两轮演进**：默认收录从"Type==2 储藏类"放宽为"**全部带储物包**"，仅排除非储物：
  厨房电器（5001-5006/5011）、机器功能槽（5007-5009/5014 老鼠笼/5015 粉碎机/5017 口粮机/5020 暖房台/
  115004 酿酒桶）、围栏 3001、吊篮 1050、门 1060、窗 1014、床 10001、垃圾桶 10004、无人机 1008；
  载具与自制工具台(313)与纸箱共用包，按家具 ID 排除（1024-1028/42033/313）。
  注意 FurnitureType 不可靠（9xxx 场景固定储物柜是 Type=0，衣柜/抽屉柜/床头柜也是 Type=0）。

### B2. 性能（2026-10-11 排障副产物）

直扫循环的判定顺序=性能——先 `ConfigIds.Contains(cfgId)`（廉价，滤掉 ~90% 家具）→ 主图严格
相等短路 → 仅非主图柜子走 `IsHomeMap`（内部含主角查询+两次配置查询，较重）。曾因对所有家具
逐个调 IsHomeMap 造成周期性卡顿（用户感知为"制作完成卡几秒"）。

### B3. 诊断工具

`DumpFurniture=true`（cfg 热改约 5s 生效）→ 全量列出 agentList 家具
（owner/map/IsHomeMap/floor/locked/入选判定）；`FlushDump` 有双保险（世界已建 + 数据非空），
避免加载屏空消费标志（曾踩坑）。

### B4. 排障实录（2026-10-11，"新柜子不生效"四连根因）

用户报告"新制作的柜子只在部分界面生效"，逐层排查出**四个独立根因**，全部已修：

1. **家地图组判定**（核心）：家的地下室是**另一张地图 ID**（与主图同属"家地图组"）。原生
   `GetFurnituresWithBag(mapId, true,…)` 的 b1 分支走 `AgentManager.IsHomeMap(furnMap)`（含同组判定）；
   mod 直扫若用严格 `furnMap == GetHomeMapId()` 会**误杀地下室全部家具**（症状：工作台能看到、
   注入式界面看不到——原生枚举认、mod 枚举不认）。修法：`fm == mapId || am.IsHomeMap(fm)` 短路。
   注意：GetFurnituresWithBag 的另两个 bool（b2=ConfigManager 判定 / b3=[agent+0x76] 过滤）未解码，
   b1 分支已覆盖主图+同组。
2. **WebUI 推送是脏标记制**：`ObservableList.Add` 后界面不会自动收到 bagTabs——必须经
   `WebUIVm.MarkAllMsgDirty() + OnMsgChange()`（`GetWebUILayer().GetWebUI(type, out vm)` 取 VM）。
   `ReduxUISystem.TriggerStateSubscribe` 是 C# 侧订阅通道，**推不到 JS**（实测无效，勿再用）。
3. **Redux 包镜像**：`GetBagData` 对 `State_Data_Item.BagCache` 直接索引（缺失即抛）——新摆放家具
   的包要重读档才登记。mod 每轮补全缺失条目（BagCache[owner]=BagId + 空 OwnerCache，与读档后
   状态一致）。实测用户场景中镜像已在（未触发补全），保留为兜底。
4. **配置名单两轮演进**：见 B1 末段（默认名单从储藏类放宽为全带包）。

## 附录 C：原 CabinetTagFilter 设计实录（2026-10-14~17，合并前文档全文保留 + 演进史补全）

> 原 mod `SurvivalLog.CabinetTagFilter`（v1.3.0 拆分独立 → 2026-10-17 并回本 mod）的设计文档
> 原文。其中"独立 mod/可单独安装"类表述为历史状态（合并后失效）；代码结构表对应合并前文件名。

### C1. 功能定位（原文）

在带容器页签的界面（工作台/烹饪/老鼠笼/酿酒/无人机交易/无人机站/粉碎机/压缩机，共 8 个——
2026-10-17 后为 10 个：并入颜料桶/温室）的页签栏注入"按标签筛选"下拉框：选中标签后**实时只
显示带该标签的容器页签**（背包页签不受影响）。

- 标签用游戏官方的"柜子标签规则"（背包界面给柜子设置的同一套，机器人按标签归置共用）。
- （历史表述）独立 mod，不依赖其他 mod：容器页签来自游戏原生（冰箱/工具柜）或储藏柜直达
  （CabinetEverywhere）注入的柜子——本 mod 只做显示层筛选，两者可各自单独安装。
- 不改存档、不改游戏文件、不改任何下发给界面的数据，删 dll 即还原。

### C2. 用户可见行为（2026-10-14 第三轮定版，原文）

- 下拉选项 = **全部柜子** + 完整标签库：食物维度汇总为**一个"食物"**（Dim 4 代表，命中任何
  Dim 1-4 食物标签——柜子只挂"肉类"这类细分标签也能被"食物"筛出），物资维度（Dim 0：材料/工具/
  燃料/药品/日用品/书刊/电器/特殊/杂项等）逐个列出，按官方 Order 排序。
  （第二版曾按"柜子实际用到的标签"裁剪选项，空标签存档里只剩材料架伪标签一项，困惑，弃用。）
- 选中态存 localStorage（v2 为 `sltf_sel_v2` 全局共享；2026-10-17 v3 起按界面记忆 `sltf_sel_v3`）。
- 冰箱页签一并参与筛选（无匹配标签即隐藏）；可打标签的冰箱按其标签走。
- 材料架不能打标签但只能放材料：自动补"材料"**伪标签**——筛"材料"时显示，筛其他标签时隐藏。
  从"材料"拆出的细分标签（防御耗材等）不含材料架（只认材料主标签）。
- 筛选后若当前打开的页签被藏掉，自动切到第一个可见页签。
- **A/D 快捷键切页签跳过被藏柜子**（2026-10-15 方向感知弹回）：官方 A/D 步进是 C# 侧
  `ContainerTabHotKey.SendSwitchTab → 各面板 RA_SwitchTabByKey` 的"下标±1"（反汇编实证，
  ToolTable=`ActiveLeftTab+dir`、越界不动不循环；Cooking 等 ownerId 制面板=bagTabs 数组取邻居），
  对 DOM 隐藏零感知——按 D 常落在被藏柜子上。mod 对比上帧 active 下标推断移动方向，
  点**同方向最近可见页签**（=替玩家跳过被藏柜子）；方向未知（首帧/玩家换筛选）才点第一个
  可见页签。盲拽回第一个可见曾是"按 A/D 来回跳跃/无法移动"缺陷的根因。
  向左穿出**整片**被藏柜子区（同方向无可见柜子）时，落点=**DOM 上紧邻柜子区左侧的最近
  可用页签按钮**（leftEdgeTab 运行时通用探测：先在页签容器内走左兄弟链——工作台抽屉
  `#tab-drawer`（官方 tab 1，无 .cabinet-tab 类、不在 cands 里）在这里；到头跳出容器后对
  左兄弟块取内部最右页签按钮——各页背包 pin 槽 `.left-tab-pinned`/`.bag-tab-pinned-slot`）。
  不做按页硬编码；否则被藏柜子屏障会挡死通往抽屉/背包的 A 键路径（工作台抽屉、无人机站
  背包先后实测中招）。弹回 click 前先把 active 类挪到目标上（预高亮）：弹回要走官方链触发
  第二次 BagMsg 整条重建，两次重建间有一帧，预高亮消除页签栏"无高亮"闪帧。
- 观察器降噪（2026-10-15）：body 级 MutationObserver 回调按 records 过滤——只有页签栏
  （list/bar）子树真正被触碰才 applyFrame/refreshDD；物品网格、tooltip 等高频变更直接跳过
  （切页签卡顿的主要放大器）。**必须同时观察 class 属性**（attributeFilter 只放 class、排除
  style——display 改动不自触发）：官方 `renderCabinetTabs` 有 key 短路（ToolTable.html），
  A/D 切页签时 cabinetTabs 内容不变、页面不重建页签 DOM，只由 `syncTabActiveClass` 切
  active 类；childList-only 过滤会漏掉这唯一的变化，弹回失去触发点，玩家停在看不见页签的
  被藏柜子上（2026-10-15 实测回归）。防闪烁铁律不受影响：BagMsg 重建页签 DOM 的 records
  必然命中过滤（target=list），仍在绘制前同步套隐藏。

### C3. 官方标签数据模型（Cecil 取证，2026-10-14，原文）

- 标签库：`Config_FurnitureTag`（ID/TagName/TagName_Local/IconKey/Color/SortPriority/**Dim**/Order/TagDesc），
  `ConfigManager._Config_FurnitureTag_Dict`（`Dictionary<int, Config_FurnitureTag>`，foreach 可用）。
  **Dim 语义**（BackpackUI.js 实证）：4=全部食物 / 1=做到哪一步 / 2=什么食物 / 3=状态 / 0=物资。
- 容器实例标签：`FurnitureTagComponent`（BaseComponent 子类，BaseAgent 组件）——**`TagIds : List<int>`**
  公开属性；存档走 `AgentSave.FurnitureTagIds`；`SetRule/GetRule/IsRuleEmpty` 为游戏自身读写口。
- 组件获取：`BaseAgent.agentComponentDictionary` 是 **`Dictionary<Il2CppSystem.Type, BaseEntity>`**，
  `dict[Il2CppType.From(typeof(FurnitureTagComponent))]` 索引直达后 TryCast——无泛型/枚举坑。
  （BaseEntity 无 GetComponent API；BaseComponent 反有 `ownerInstanceId` 可做 ownerId 映射，备用。）
- 材料架判定：同字典索引 `GameCore.HotUpdate.Battle.Logic.MaterialRackComponent`（存在即材料架）。
- "材料"标签 ID：标签库 IconKey=="material" 的那条（与游戏 JS 侧 TAG_NAME_FALLBACK 同键）。

### C4. 容器枚举（原 ContainerScan，2026-10-17 起并入 CabinetCatalog 单遍扫描）

- 单遍直扫 `AgentManager.agentList`：`TryCast<Furniture>` → 配置查包（`BagId>0`，柜子/冰箱/置物架/
  材料架/床头柜/载具全收）→ 家地图判定（主图严格相等短路，非主图走 `IsHomeMap` 家地图组判定，
  与储藏柜直达同一实证结论：地下室是家地图组另一张图）。
- **宁全勿缺**：筛选语义="不是带该标签的容器就藏页签"，没有页签的容器进了名单也无害；
  反之缺了（如冰箱）会出现"常驻藏不掉"（第二轮实测教训）。不使用储藏柜直达的黑名单——
  那份名单只决定"注入哪些柜子页签"，与筛选无关。（合并后注：非储物功能包除外——五轮教训，
  见 C10 演进五轮与 §2.3。）
- 性能顺序（储藏柜直达同款）：廉价过滤（TryCast+查包）→ 主图短路 → 仅非主图走 IsHomeMap（重）。
- 5s 周期幂等重扫；每容器读 `TagIds` + 材料架标记。

### C5. UI 注入通道（本套件首次打通"往游戏 UI 加东西"，原文）

- 游戏 UI = 一个 **Vuplex 内嵌 Chromium**（`SurvivalLog_Data\Plugins\x86_64\VuplexWebViewChromium`）
  装载 `WebUI\Root.html`，各界面页是 `#iframe-container` 下 iframe；C#→页面数据全部走 Root 的
  case-10 广播 `frame.contentWindow.postMessage({type, data:json字符串})`；页签数据都在 `*_BagMsg`
  （字段名四种：ToolTable=cabinetTabs，烹饪/老鼠笼/酿酒/无人机站=bagTabs，粉碎机/压缩机=cartTabs，
  无人机交易=bagTabsJson 字符串；条目含 ownerId）。
- **mod 入口**：`WebUILayer.canvasWebViewPrefab`（public 属性，CanvasWebViewPrefab）→ `.WebView`
  （IWebView）→ **`ExecuteJavaScript(js[, Action<string>])`**（接口直有，interop 干净）。
  每 5s 幂等重注引导脚本（`window.__SLTF__` 已存在即返回；WebView 崩溃恢复重建后自动重挂），
  数据每周期直推（体量 ~2KB，免维护注入时序）。
- **同源假设已被实测验证成立**（2026-10-14 首轮实测功能正常）：Root 与 iframe 同源，可直访
  contentDocument / 包装 contentWindow.postMessage。探针仍在（`同源探针: OK/XORIGIN` 日志），
  游戏更新后若变跨域会留痕。
- 回调委托：`Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(...)`
  （命名空间无 InteropTypes 后缀；不可用时仅少探针回读日志，功能不受影响）。

### C6. 为什么是 DOM 隐藏而不是数据过滤（关键决策，原文）

- 切页签语义两派：RatCage/Cooking/Brew/DroneHub/TradeUI 发 `SWITCH_BAG {ownerId}`；
  **ToolTable/Shredder/Compressor 发 `TAB_SWITCH {tab: 数组下标}`（C#/JS 共享下标）**。
  过滤下发给 JS 的 tab 数组会让索引制三件套**切错柜子**；C# 侧删状态列表则工作台自动取料
  （GetAllLinkedOwnerIds 遍历 CabinetTabs，属储藏柜直达/官方链路）跟着丢柜子。
- 故 JS **只截读不修改（下发数据）**：包装 postMessage 记住页签数组（ownerId 顺序表），按位置
  映射 DOM；隐藏规则 `display:none` 只作用于"在容器名单(owner map)里且不匹配选中标签"的页签
  按钮——背包等不在名单里的 ownerId 永不隐藏；下标/事件/自动取料全部原样，零错位风险。
  （2026-10-15 七轮起 JS 增补"扣发+伪造切换"：只扣 Root→iframe 的渲染消息、以页面同款事件
  伪造直达——仍不改任何下发的 tabs 数组/下标，数据层决策不变。）
- 位置映射兜底：DOM 候选数=数组长度→偏移 0；=长度-1→偏移 1（pin 背包分离）；对不上则**宁可不藏**。
  无人机交易的 tab 自带 `data-owner` 属性，直接按属性匹配不走位置映射。
- 页签栏选择器（各页 DOM 实证）：ToolTable=`#leftTabsScroll .cabinet-tab`；烹饪/老鼠笼/酿酒/
  无人机站=`#bagTabsScroll .bag-tab`；交易=`.bag-tabs-scroll .bag-tab[data-owner]`；
  粉碎机=`#tabsScroll .left-tab`；压缩机=`#tabs .left-tab`。（温室同粉碎机结构；颜料桶同 bag 族。）

### C7. 下拉框与防闪烁（原文 + 2026-10-17 自绘重做注）

- （v1 原生 select 时代）下拉框插入各页页签栏首位；`select` 本就在官方 clickThrough 交互同步
  清单（webui-core.js），矩形自动上报 Unity，点击穿透原生生效。压缩机 `#tabs` 重建时清
  innerHTML、交易页 Vue 重渲染可能摘除——观察器自动重插（isConnected 检查）。
  （2026-10-17 起为自绘下拉：触发器列容器内 absolute 贴左下 + body 级 popup，见 §2.5；
  clickThrough 走 button/[data-interactive] 通道。）
- **防闪烁铁律**：页面每次 BagMsg 整条重建页签 DOM，任何"防抖后处理"都会让被藏页签闪现
  （80ms 防抖=闪烁根因，实测）；MutationObserver 回调是**微任务、绘制前执行**——必须回调内
  **同步**套 display:none。只改 display（attribute）不会再触发 childList 观察器，无循环。

### C8. 代码结构（合并前原文，仅供对照）

| 文件 | 职责 |
|---|---|
| `CabinetTagFilterPlugin.cs` | 入口：cfg（Filter.Enable）、5s Ticker、WelcomeTip |
| `ContainerScan.cs` | 容器枚举：全带包家具（含冰箱）→ ownerId/标签/材料架标记 |
| `TagFilter.cs` | WebView 注入通道：幂等引导 + 标签数据 JSON + 同源探针 |
| `TagFilterJs.cs` | JS 引导：截读页签数据 + DOM 隐藏 + 下拉框（全量选项/食物汇总） |
| `ModShared\BagTableRw.cs` | 共享源码（ConfigManager/FurnDict，编译进本 dll） |

配置项（com.local.survivallog.cabinettagfilter.cfg）：`Filter.Enable`（默认 true）。

### C9. 已知限制（原文，合并后部分由 §5 取代）

- mod 注入前界面已收到首份 BagMsg 的场景：页签数据要等下一次推送（切页签/操作即触发）才开始隐藏。
- 标签变更（玩家在背包界面改标签）最迟 5s 内同步到筛选（轮询周期）。
- 选中一个没有任何容器匹配的标签时会藏掉全部容器页签（只剩背包）——全量选项语义下的预期行为。
- 同源不成立（探针非 OK）时功能整体静默失效，需换 C# 状态层方案另议。

### C10. 演进史（全部轮次，一条不丢；v1.3.0~v1.3.3 为独立 mod 时期）

- 2026-10-14：诞生（自 CabinetEverywhere 拆分为独立 mod）。三轮迭代：①首版（DOM 隐藏+下拉）；
  ②冰箱纳入筛选/防闪烁/材料架伪标签；③下拉选项全量化（不再按使用裁剪）+ 食物维度（Dim1-4）
  汇总为一个"食物"选项 + 拆分独立。
- 2026-10-15：修复"按 A/D 切页签来回跳跃/无法移动"——根因是 mod 把官方步进（不感知隐藏）
  切到的被藏柜子盲拽回第一个可见页签，与步进互相拉扯；改为方向感知弹回（见 C2）。
- 2026-10-15（二轮）：①修复"A 键到不了工作台抽屉"——第一个可见柜子左侧的被藏柜子形成屏障，
  弹回只能在柜子区里打转；向左穿出被藏区时改点 edges（抽屉→背包，官方 tab 1→0 序）。
  ②弹回预高亮消除两次 BagMsg 重建间的页签栏闪帧；③观察器按 records 过滤降噪（见 C2）。
- 2026-10-15（三轮）：修二轮③引入的回归"能移到看不见页签的被藏柜子"——官方
  renderCabinetTabs 有 key 短路，切页签只 toggle active 类不重建 DOM；childList-only 过滤
  把弹回的唯一触发点滤没了。observer 补 attributes+attributeFilter(class)（见 C2）。
- 2026-10-15（四轮）：A/D 到头弹回原地时冻结内容区一拍——第一次 BagMsg 已把被藏柜子的
  物品渲染进内容区，弹回的第二次 BagMsg 要等 Unity 往返，中间帧会闪现被藏柜子内容（实测
  "最右侧再按 D 闪下一个柜子"）。冻结让中间帧内容空白（页签栏预高亮在），玩家看到的
  是"按了没变化"=到头的正确反馈；下一批 DOM 变化（二次渲染）即解冻，250ms 超时兜底。
  仅作用于"弹回原地"（目标=上一个 active 可见柜）；正常跳过/edges 落点是真实切换不冻。
- 2026-10-15（五轮）：修复"无人机交易界面向左到中控台卡住闪一下"——中控台挂无人机
  货舱（BagId 1008）被"宁全勿缺"扫描误收进筛选名单、无匹配标签页签被藏，A 切过去即被
  弹回。ContainerScan 补非储物功能包黑名单（与储藏柜直达 ExcludeBagIds 默认名单同源：
  无人机/炉具/老鼠笼/粉碎机/酿酒桶/门窗床等），功能容器页签永不参与筛选。
- 2026-10-15（六轮）：修复"无人机站（调度机队）向左到不了背包"——同二轮抽屉屏障，但
  边界落点是 pin 背包（#bagTabPinned 静态分离，不在 cands）。把二轮的 ToolTable 硬编码
  edges 改为 leftEdgeTab 运行时通用探测（沿 DOM 向左找最近可用页签按钮，.left-tab/.bag-tab
  类判定），全部界面一次覆盖，不再按页补丁。
- 2026-10-15（七轮）：根治"A/D 到头闪一下"——四轮的冻结只是把闪内容变闪空白。改为
  **消息层扣发+伪造切换**：wrapRelay 截读时发现 BagMsg 的 active 字段落在被藏柜子上
  （各页字段实证：tt=activeLeftTab、bag=bagOwnerId、trade=bagOwnerKey、cart=activeTab）
  就**扣下不放行**（页面完全不动=零中间帧），复用 pickBounceTarget 算出弹回目标，以
  **页面同款事件**（TAB_SWITCH/SWITCH_BAG，sourcePageId=iframe id——Root 的
  resolveSourceId claimed 分支实证）从 Root 帧伪造发给 Unity 直达正确柜子，其回包正常
  放行；300ms 超时放行原消息=降级为 DOM 弹回（四/二轮逻辑保留为兜底）。伪造事件与玩家
  点击页签走完全相同的官方链。
- 2026-10-15（八轮）：交易页暂禁扣发止血（七轮卡死）+ dbg 调试事件流（JS 队列 + C# 每 5s
  回读打日志：HOLD/FORGE/RELEASE/DROP-stale）。
- 2026-10-15（九轮）：交易页被动实验（不扣消息只发伪造+记响应）——日志实锤**伪造完全生效**
  （回包 +32~52ms 到达且切到目标），同时定位七轮卡死根因：**回包消息只带 active 字段不带
  页签数组**（ReactiveProperty 独立推送），七轮的 pending 清理依赖 extractTabs 命中（要 tabs
  字段）→ 回包被无视 → 被扣旧消息 300ms 超时乱序放行 → 页面与 C# 状态错位（卡死）。
  实验版偶发"双高亮/无法移动"=实验特有的双通道竞争（弹回 click+伪造同时跑），恢复扣发后自愈。
- 2026-10-15（十轮）：修复 pending 清理——任何 *_BagMsg 类型消息（isBag 按 type 判定）
  都视为状态已前进即刻清扣发；交易页恢复扣发（零闪烁覆盖全部界面），实验代码移除。
- 2026-10-16（十一轮）：修复十轮后交易页仍卡死——forgeSwitch 的 trade 分支误用位置映射
  `arr[j]` 取柜号（数组首位是 pin 背包而 cands 不含 pin，恒错位 1）→ 伪造切到**错误柜子**，
  错柜也被藏则回包再命中拦截→循环伪造=消息风暴卡死。改直读目标元素 `data-owner` 属性
  （零错位）；trade 的 edge 落点（pin 背包按钮自带 data-owner）同修。教训：attr 制界面
  （trade）一切柜号必须读元素属性，位置映射只属于位置制（tt/bag/cart）。
- 2026-10-16（十二轮）：修复"无人机站（调度机队）到头闪烁"——bag 族伪造的 ownerId 发了
  **字符串**（从 trade 沿用），而 bag 族原生发**数字**（Cooking/DroneHub 页实证
  `UnitySendEvent('SWITCH_BAG', { ownerId: ownerId })` 数字直传），C# 端丢弃字符串解析
  → 伪造从未生效 → 被藏柜状态高频推送每条被扣、偶有撑到 300ms 超时放行=闪烁（dbg 日志
  HOLD→FORGE→HOLD 同柜循环实锤）。改 `parseInt(...,10)` 发数字（含 edge 的 pin arr[0]）。
  **铁律：伪造事件的字段类型必须逐页照抄原生 JS 的发送代码（trade=字符串、bag=数字），
  勿跨族沿用。**
- 2026-10-16（十三轮）：消灭到头的最后残闪——伪造生效后回包 active=当前柜（与画面一致），
  但 bag 族页面无 key 短路、收到消息就**整条重建**页签+网格 → 状态没变也白闪一帧。
  修法=**回声丢弃（skipEcho）**：到头弹回（bounced）的伪造目标即画面现状，窗口内同 active
  消息全部丢弃；画面纹丝不动、C# 已同步到位；真实切换（active 变化）不匹配即放行。
- 2026-10-16（十四轮）：skipEcho 改**窗口式**（250ms 内所有同 active 回声持续丢弃）——一次
  切换 C# 推多条消息（字段独立推送），只丢第一条的话第二条照样整条重建闪（SKIP-echo 命中
  后仍闪的实锤）。
- 2026-10-16（十五轮）：抓到间歇闪的最后一因——**A/D 步进只改 active，C# 只推 active-only
  轻量消息（不带页签数组）**，而扣发入口要求消息带 tabs 才判定 → 轻量消息全部漏过直放
  =渲染被藏柜+DOM 弹回=闪，且 dbg 零记录（时带 tabs 时不带的整条/轻量消息交替，解释
  闪的间歇性）。修法：消息没带 tabs 时用内存缓存 st.tabs 补判，轻量消息同样走扣发。
- 2026-10-16（v1.3.2 发布事故修复，v1.3.3）：①v1.3.2 发版清理调试代码时误删 6 个核心函数
  （页签数据解析/筛选判定等）→ 安装后进游戏存档列表为空——全部恢复，发版流程加函数完整性
  对账；②"首次打开调度机队筛选下拉出现两个"——界面首次加载的初始化竞态（重载会留下旧
  初始化流程插出重复下拉），界面 load 时取消还在轮询的旧 init 链 + 插入前做文档内唯一性
  清理（querySelectorAll('.sltf-dd-btn, .sltf-dd-pop') 先清再插）。
- 2026-10-16（十七轮）：**hasActive 按内容判定**——存在 type 不含 BagMsg、但 data 里带
  『当前柜子』字段的轻量消息（漏过扣发直接渲染被藏柜=玩家看到被过滤柜子闪现，隐身模式
  日志实锤：闪的时刻 HOLD/PROBE 全无记录）。凡携带 bagOwnerId/bagOwnerKey/activeLeftTab/
  activeTab 任一字段的消息一律纳入扣发判定（即附录 A 对照的 hasActive 清单）。同时**冻结
  内容区扩展到全部弹回场景**（此前仅 bounced）：消息层拦截失效时 DOM 弹回是唯一防线，
  冻结把『闪被藏柜内容』降级为『空白一拍回到原柜』。
- 2026-10-16（十八轮）：**detectC 族别判定顺序**——DroneHub/Cooking/RatCage/Brew 的页签
  容器有 id=bagTabsScroll（bag 族，字段 bagOwnerId），必须**先于** trade 分支判定：两者的
  容器 class 同名（bag-tabs-scroll），若 trade 先判会把 DroneHub 误判成 trade、读错字段名
  （bagOwnerKey）全部静默放行=漏网闪（misB 日志实锤 frame=DroneHub type=WebUI_DroneHub_BagMsg）。
  TradeUI 无此 id 落回 trade。
- 2026-10-17（合并系列）：CabinetTagFilter 并回 CabinetEverywhere（双 Ticker 合一、容器扫描
  合一）；JS 迁内嵌资源 tagfilter.page.js（verbatim 引号坑根治）；数据契约 v3 + 场景视图 +
  自绘下拉 + 颜料桶 PollDye + 按界面记忆（正文 §2、§6）。
