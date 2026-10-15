# 柜子标签筛选 CabinetTagFilter — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（csproj/BepInPlugin 内为占位号，发版以 version.json 为准）；机制与实证结论长期有效。

## 1. 功能定位

在带容器页签的界面（工作台/烹饪/老鼠笼/酿酒/无人机交易/无人机站/粉碎机/压缩机，共 8 个）的
页签栏注入"按标签筛选"下拉框：选中标签后**实时只显示带该标签的容器页签**（背包页签不受影响）。

- 标签用游戏官方的"柜子标签规则"（背包界面给柜子设置的同一套，机器人按标签归置共用）。
- **独立 mod，不依赖其他 mod**：容器页签来自游戏原生（冰箱/工具柜）或储藏柜直达（CabinetEverywhere）
  注入的柜子——本 mod 只做显示层筛选，两者可各自单独安装。
- 不改存档、不改游戏文件、不改任何下发给界面的数据，删 dll 即还原。

## 2. 用户可见行为（2026-10-14 第三轮定版）

- 下拉选项 = **全部柜子** + 完整标签库：食物维度汇总为**一个"食物"**（Dim 4 代表，命中任何
  Dim 1-4 食物标签——柜子只挂"肉类"这类细分标签也能被"食物"筛出），物资维度（Dim 0：材料/工具/
  燃料/药品/日用品/书刊/电器/特殊/杂项等）逐个列出，按官方 Order 排序。
  （第二版曾按"柜子实际用到的标签"裁剪选项，空标签存档里只剩材料架伪标签一项，困惑，弃用。）
- 选中态存 localStorage（`sltf_sel_v2`），界面间共享、重启保持。
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
  背包先后实测中招）。
  弹回 click 前先把 active 类挪到目标上（预高亮）：弹回要走官方链触发第二次 BagMsg 整条
  重建，两次重建间有一帧，预高亮消除页签栏"无高亮"闪帧。
- 观察器降噪（2026-10-15）：body 级 MutationObserver 回调按 records 过滤——只有页签栏
  （list/bar）子树真正被触碰才 applyFrame/refreshDD；物品网格、tooltip 等高频变更直接跳过
  （切页签卡顿的主要放大器）。**必须同时观察 class 属性**（attributeFilter 只放 class、排除
  style——display 改动不自触发）：官方 `renderCabinetTabs` 有 key 短路（ToolTable.html），
  A/D 切页签时 cabinetTabs 内容不变、页面不重建页签 DOM，只由 `syncTabActiveClass` 切
  active 类；childList-only 过滤会漏掉这唯一的变化，弹回失去触发点，玩家停在看不见页签的
  被藏柜子上（2026-10-15 实测回归）。防闪烁铁律不受影响：BagMsg 重建页签 DOM 的 records
  必然命中过滤（target=list），仍在绘制前同步套隐藏。

## 3. 官方标签数据模型（Cecil 取证，2026-10-14）

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

## 4. 容器枚举（ContainerScan）

- 单遍直扫 `AgentManager.agentList`：`TryCast<Furniture>` → 配置查包（`BagId>0`，柜子/冰箱/置物架/
  材料架/床头柜/载具全收）→ 家地图判定（主图严格相等短路，非主图走 `IsHomeMap` 家地图组判定，
  与储藏柜直达同一实证结论：地下室是家地图组另一张图）。
- **宁全勿缺**：筛选语义="不是带该标签的容器就藏页签"，没有页签的容器进了名单也无害；
  反之缺了（如冰箱）会出现"常驻藏不掉"（第二轮实测教训）。不使用储藏柜直达的黑名单——
  那份名单只决定"注入哪些柜子页签"，与筛选无关。
- 性能顺序（储藏柜直达同款）：廉价过滤（TryCast+查包）→ 主图短路 → 仅非主图走 IsHomeMap（重）。
- 5s 周期幂等重扫；每容器读 `TagIds` + 材料架标记。

## 5. UI 注入通道（本套件首次打通"往游戏 UI 加东西"）

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

## 6. 为什么是 DOM 隐藏而不是数据过滤（关键决策）

- 切页签语义两派：RatCage/Cooking/Brew/DroneHub/TradeUI 发 `SWITCH_BAG {ownerId}`；
  **ToolTable/Shredder/Compressor 发 `TAB_SWITCH {tab: 数组下标}`（C#/JS 共享下标）**。
  过滤下发给 JS 的 tab 数组会让索引制三件套**切错柜子**；C# 侧删状态列表则工作台自动取料
  （GetAllLinkedOwnerIds 遍历 CabinetTabs，属储藏柜直达/官方链路）跟着丢柜子。
- 故 JS **只截读不修改**：包装 postMessage 记住页签数组（ownerId 顺序表），按位置映射 DOM；
  隐藏规则 `display:none` 只作用于"在容器名单(owner map)里且不匹配选中标签"的页签按钮——
  背包等不在名单里的 ownerId 永不隐藏；下标/事件/自动取料全部原样，零错位风险。
- 位置映射兜底：DOM 候选数=数组长度→偏移 0；=长度-1→偏移 1（pin 背包分离）；对不上则**宁可不藏**。
  无人机交易的 tab 自带 `data-owner` 属性，直接按属性匹配不走位置映射。
- 页签栏选择器（各页 DOM 实证）：ToolTable=`#leftTabsScroll .cabinet-tab`；烹饪/老鼠笼/酿酒/
  无人机站=`#bagTabsScroll .bag-tab`；交易=`.bag-tabs-scroll .bag-tab[data-owner]`；
  粉碎机=`#tabsScroll .left-tab`；压缩机=`#tabs .left-tab`。

## 7. 下拉框与防闪烁

- 下拉框插入各页页签栏首位；`select` 本就在官方 clickThrough 交互同步清单（webui-core.js），
  矩形自动上报 Unity，点击穿透原生生效。压缩机 `#tabs` 重建时清 innerHTML、交易页 Vue 重渲染
  可能摘除——观察器自动重插（isConnected 检查）。
- **防闪烁铁律**：页面每次 BagMsg 整条重建页签 DOM，任何"防抖后处理"都会让被藏页签闪现
  （80ms 防抖=闪烁根因，实测）；MutationObserver 回调是**微任务、绘制前执行**——必须回调内
  **同步**套 display:none。只改 display（attribute）不会再触发 childList 观察器，无循环。

## 8. 代码结构

| 文件 | 职责 |
|---|---|
| `CabinetTagFilterPlugin.cs` | 入口：cfg（Filter.Enable）、5s Ticker、WelcomeTip |
| `ContainerScan.cs` | 容器枚举：全带包家具（含冰箱）→ ownerId/标签/材料架标记 |
| `TagFilter.cs` | WebView 注入通道：幂等引导 + 标签数据 JSON + 同源探针 |
| `TagFilterJs.cs` | JS 引导：截读页签数据 + DOM 隐藏 + 下拉框（全量选项/食物汇总） |
| `ModShared\BagTableRw.cs` | 共享源码（ConfigManager/FurnDict，编译进本 dll） |

配置项（com.local.survivallog.cabinettagfilter.cfg）：`Filter.Enable`（默认 true）。

## 9. 已知限制

- mod 注入前界面已收到首份 BagMsg 的场景：页签数据要等下一次推送（切页签/操作即触发）才开始隐藏。
- 标签变更（玩家在背包界面改标签）最迟 5s 内同步到筛选（轮询周期）。
- 选中一个没有任何容器匹配的标签时会藏掉全部容器页签（只剩背包）——全量选项语义下的预期行为。
- 同源不成立（探针非 OK）时功能整体静默失效，需换 C# 状态层方案另议。

## 10. 演进要点

- 2026-10-14：诞生（自 CabinetEverywhere 拆分为独立 mod）。三轮迭代：①首版（DOM 隐藏+下拉）；
  ②冰箱纳入筛选/防闪烁/材料架伪标签；③下拉选项全量化（不再按使用裁剪）+ 食物维度（Dim1-4）
  汇总为一个"食物"选项 + 拆分独立。
- 2026-10-15：修复"按 A/D 切页签来回跳跃/无法移动"——根因是 mod 把官方步进（不感知隐藏）
  切到的被藏柜子盲拽回第一个可见页签，与步进互相拉扯；改为方向感知弹回（见第 2 节）。
- 2026-10-15（二轮）：①修复"A 键到不了工作台抽屉"——第一个可见柜子左侧的被藏柜子形成屏障，
  弹回只能在柜子区里打转；向左穿出被藏区时改点 edges（抽屉→背包，官方 tab 1→0 序）。
  ②弹回预高亮消除两次 BagMsg 重建间的页签栏闪帧；③观察器按 records 过滤降噪（见第 2 节）。
- 2026-10-15（三轮）：修二轮③引入的回归"能移到看不见页签的被藏柜子"——官方
  renderCabinetTabs 有 key 短路，切页签只 toggle active 类不重建 DOM；childList-only 过滤
  把弹回的唯一触发点滤没了。observer 补 attributes+attributeFilter(class)（见第 2 节）。
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

## 11. 防盗署名气泡（2026-10-14，套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入；套件规范见 docs\00 规则 4，publish.ps1 强制校验）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。
- 7+1 个套件 mod 全部调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建 DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后全部套件 mod 需重编。
