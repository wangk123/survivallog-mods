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

## 11. 防盗署名气泡（2026-10-14，套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入；套件规范见 docs\00 规则 4，publish.ps1 强制校验）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。
- 7+1 个套件 mod 全部调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建 DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后全部套件 mod 需重编。
