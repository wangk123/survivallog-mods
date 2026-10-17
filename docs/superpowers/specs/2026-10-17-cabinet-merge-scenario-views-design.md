# 储藏柜直达 + 柜子标签筛选：合并与场景视图 —— 设计文档

> 2026-10-17 头脑风暴定稿（方案 A：显示层统一 + 固定场景视图）。
> 实施时本文档结论落入 `SurvivalLog.CabinetEverywhere/DESIGN.md`（套件文档规则），
> 本文件保留为该次重构的决策记录。**版本号由作者在 version.json 定义，本文不预先指定。**

## 1. 背景与四议题

两个既有 mod 强耦合：储藏柜直达（CabinetEverywhere，注入柜子页签）与柜子标签筛选
（CabinetTagFilter，WebUI JS 显示层筛选）。直达无筛选=柜子页签过多（实测 24 柜+背包）；
筛选无直达=几乎无柜可筛。本次四议题：

1. **场景化排除**：不同界面确定性不需要的柜子不再出现；
2. **下拉菜单位置与样式**：不再挤压页签栏，观感对齐游戏官方风格；
3. **筛选记忆**：按界面类型各自记忆，废弃全局单值；
4. **两 mod 合并**：沿用「储藏柜直达」身份（dll 名/GUID/安装器组件不变），
   吸收 CabinetTagFilter，旧 `SurvivalLog.CabinetTagFilter.dll` 由安装器 InstallDelete 清理。

## 2. 核心决策记录（均已作者拍板）

| 决策点 | 结论 |
|---|---|
| 排除发生层 | **方案 A：显示层统一**。JS 只做可见性，C# 侧 CabinetTabs 永远全量——工作台自动取料、扣发/伪造链零影响（保住 15 轮打磨的"只截读不修改"铁律） |
| 场景排除形态 | **固定视图**：多数界面无下拉框、按场景规则直接决定可见集合；仅交易/调度机队保留下拉自由筛选 |
| 未打标签柜 | 归入「其他」类：下拉组有「其他」选项可单独筛出；固定视图组按组规则处理 |
| 颜料桶/温室缺口 | 一并补：颜料桶进直达注入+筛选；温室进筛选（直达已在） |
| 冰箱 | 食物组固定视图=全部冰箱一律显示（无论有无标签）；下拉组维持现有"冰箱按其标签参与筛选"语义；材料组不含冰箱（白名单界面现状本就无冰箱，规则显式化+未来防护） |
| 合并身份 | 沿用 CabinetEverywhere；cfg 合并为一份（新增 `[Scenario]` 分区）；CabinetTagFilter 文件夹删除，其 DESIGN.md 历史附录并入 |
| JS 载体 | C# verbatim 字符串 → DLL 内嵌资源 `tagfilter.page.js`（N 网三件套模式；`node --check` 可独立校验，半角引号坑根治） |

## 3. 场景视图表（定稿）

全部 10 个带容器页签的界面（`WebUI/UI` 源码枚举实证）：

| 组 | 界面（页面名） | 可见集合 | UI |
|---|---|---|---|
| 食物组 | 烹饪 Cooking、老鼠笼 RatCage、酿酒 Brew、颜料桶 DyePanel、口粮压制机 Compressor | 食物标签柜（命中 Dim1-4 任一即"食物"，沿用汇总语义）+ **全部冰箱** | 无下拉 |
| 材料组 | 工作台 ToolTable、粉碎机 Shredder、温室 GreenhouseBuild | 材料（含材料架伪标签）+ 工具 + 未打标签柜；**不含冰箱** | 无下拉 |
| 下拉组 | 无人机交易 TradeUI、调度机队 DroneHub | 全部容器页签，按需筛选（含「其他」「全部柜子」） | 下拉菜单 |

- 配置逃生口：`Scenario.View.{界面}` = `food | material | browser`，默认按上表；高级用户可
  把任一界面改成 `browser`（恢复下拉自由筛选）。固定视图界面注入的下拉框一律不出现。
- 材料架（MaterialRackComponent）维持「材料」伪标签：材料组可见，食物组隐藏。
- 食物组语义注意：**存进未打标签柜子的食物在食物组界面不可见**（补救=给柜子打标签或
  走背包界面）；此为固定视图的既知取舍，作者已确认。

## 4. 架构设计

### 4.1 合并后代码结构（SurvivalLog.CabinetEverywhere）

| 文件 | 职责 |
|---|---|
| `CabinetEverywherePlugin.cs` | 入口：配置定义（原 cfg 键 + `[Scenario]`/`[Filter]` 分区）、AccessTicker（慢 5s：名单重扫+白名单；快 0.5s：页签轮询） |
| `CabinetCatalog.cs` | **扫描合一**：单遍直扫 agentList 产出全容器名单（柜子/冰箱/材料架 + TagIds + 材料架标记 + 未标签状态 + 食物/材料/工具域预计算标志），直达注入与筛选共用；周期扫描 2 份 → 1 份 |
| `CraftSourceInjector.cs` | 白名单注入（不变） |
| `TabInjector.cs` | 烹饪/老鼠笼/酿酒/**新增颜料桶**页签注入（State_Web_Dye，bag 族，同 Cooking 模式） |
| `TagFilter.cs` | WebView 注入通道（不变；JS 引导改为读内嵌资源） |
| `tagfilter.page.js` | 内嵌资源：截读页签数据 + DOM 隐藏 + 场景视图判定 + 下拉框（下拉组）+ A/D 扣发/伪造全链路（自 TagFilterJs.cs 迁移重写） |

- WelcomeTip：合并后单 dll，去重逻辑照旧。
- cfg 迁移：新键全部落在直达 cfg；旧 `com.local.survivallog.cabinettagfilter.cfg`
  残留无害（不再被加载）；用户自定义的 Include/Exclude 名单需按发版说明手动迁移。

### 4.2 数据流（每 5s 幂等推送，体量 ~2→3KB）

C# → Root 引导 JS（ExecuteJavaScript）：

```
containers: [{ ownerId, kind: cabinet|fridge|material-rack,
               tagIds: [...], untagged: bool,
               domain: { food, mat, tool }   // C# 预计算，供固定视图直判
             }]
scenario:  { pageId: 'food'|'material'|'browser' }   // cfg 驱动
taglib:    [ {id, name, iconKey, color, dim, order} ]  // 官方标签库（下拉组渲染用）
```

JS 侧每界面：`browser` → 现有筛选机制 + 记忆；`food|material` → 固定谓词直接套
display:none（与现有隐藏机制同一套 DOM 操作，无新机制）。

### 4.3 官方页 vs 整页自带（docs/06 手册 checkpoint）

本方案全部属于**增强官方页**（控制官方页签栏可见性、下拉框长在官方页签栏行内），
寄生官方 DOM 是题中之义，**不适用**手册"新 UI 类需求优先整页自带"路线。
若未来筛选演进为复杂独立筛选器，再评估整页自带（当前 YAGNI）。

## 5. UI 设计（下拉组）

- **位置**：页签栏行末、**滚动区外**（右箭头按钮之后/无箭头界面的滚动容器之后）——
  不挤压页签、不随页签滚动出视野。两界面 DOM 实施时逐个核实（TradeUI=Vue 页注意
  重渲染摘除，观察器自动重插已有机制照用）。
- **样式**：自绘 `<button>` 触发器 + 弹出列表；视觉克隆 DyePanel `dd-btn` 官方范式
  （样式自 `tools/webui_src` 快照精确复制）；选项 = 官方 SVG 图标（BackpackUI
  `TAG_ICONS` 同源提取 + 内置静态副本兜底）+ 官方标签色（Config_FurnitureTag.Color）
  + 名称；「全部柜子」「其他」配通用图标。
- **点击穿透**：触发器与选项均为 `button`（官方 clickThrough selector
  `.unity-interactive, button, input, select, a, [data-interactive]` 自动收录，实证）。
- **弹出层防裁切**：popup absolute 定位需防被 overflow:hidden 裁切，向上弹出/贴边
  兜底（实施时 DevTools 核实）。
- **记忆**：localStorage `sltf_sel_v3`，按界面 `{trade: tagId, dronehub: tagId}`，
  默认「全部柜子」；旧 `sltf_sel_v2` 全局值废弃不迁移。

## 6. 覆盖扩展（新界面接入）

| 界面 | 证据（tools/webui_src 快照直读） | 接入工作 |
|---|---|---|
| 颜料桶 DyePanel | bag 族：`#bagTabsScroll`、bagOwnerId、SWITCH_BAG（源码 953-958 行区域） | TabInjector +PollDye；筛选 detectC 加页 |
| 温室 GreenhouseBuild | **TAB_SWITCH 族**：`TAB_SWITCH {tab:i}` 数组下标制（源码 556 行） | 筛选 detectC 加页；扣发/伪造链按 tt/cart 族字段配 |

固定视图组两界面均无下拉，A/D 弹回边界（leftEdgeTab 通用探测）需覆盖新页 DOM。

## 7. 取证清单（实施第一步，源码→CDP→反汇编顺序，docs/06 手册）

| # | 待取证 | 手段 | 影响 |
|---|---|---|---|
| K1 | Dye 的 C# 收集逻辑（是否冷链同款）+ State_Web_Dye 字段名/BagTabs 结构 | interop Cecil + 源码；必要时 Iced | PollDye 实现 |
| K2 | 工作台/粉碎机/温室的配方/准入是否涉及**食物域**材料 | CDP 实测 + 配方表（ActionProbe） | 材料组是否补「食物」 |
| K3 | 口粮压制机原生清单里冰箱的存在性 | CDP 实测开界面看页签 | 食物组冰箱语义核对 |
| K4 | 交易/调度机队确认无域限制（维持 browser 默认的前提） | CDP 实测 | 下拉组默认值 |
| K5 | TradeUI/DroneHub 页签栏行 DOM（滚动区外插入点） | 源码 + DevTools Elements | 下拉框位置 |

已闭环：温室=TAB_SWITCH 族；颜料桶=bag 族（§6）。

## 8. 测试策略（三层，docs/06 手册）

1. **白盒穷举**：10 个带页签界面的消息 handler 清单对照拦截覆盖面（十七轮教训），
   兼查其余 bag 族页面不被误伤；`tagfilter.page.js` 过 `node --check`；构建 0 错误
   + 部署三关（退出码判断）。
2. **CDP 自动断言**（`tools/devtools_cdp_test.js` 模式）：逐界面断言——可见页签集合
   =场景视图表期望；固定组无下拉框、下拉组有且位置正确；记忆持久化（选标签→关开
   界面→断言）；A/D 步进落点（含被藏区边界）；图标/配色渲染。
3. **人工验收**：作者最终过一遍；重点回归——**固定视图=默认即有隐藏**，A/D 扣发/
   伪造链从"手动筛选后才暴露"变为常态暴露；工作台自动取料不受影响（隐藏柜子仍
   参与取料）；Dye/温室左边界探测。

## 9. 迁移与发布

- 安装器：InstallDelete 增 `SurvivalLog.CabinetTagFilter.dll`（机制已核验，组件级
  反勾选不遗留）；组件描述更新（直达=直达+筛选）。
- 发版登记全套：publish.ps1 $suiteMods（7 个）、make_mods_release.ps1、installer.iss
  四处、install_notes.tpl.txt、test_installer.ps1、CHANGELOG、README、
  CabinetEverywhere/DESIGN.md 重写（TagFilter DESIGN 历史并入附录）。
- 版本号：**作者在 version.json 定号**（硬规则）；csproj 维持占位。
- 宣传口径：合并后为"储藏柜直达"新版本（内置智能视图+自由筛选），不另起新名。

## 10. 不做什么（scope 边界）

- 不做 C# 状态层排除（方案 B 已评估否决：自动取料丢柜+重验全链路风险）。
- 不做独立筛选面板（整页自带路线，见 §4.3）。
- 不做界面覆盖面再扩展（发电机等无页签系统界面维持另立项口径）。
- 不做旧 cfg 自动迁移读取。
- 下拉组不新增按柜名搜索/多选等高级筛选（YAGNI，有需求再议）。
