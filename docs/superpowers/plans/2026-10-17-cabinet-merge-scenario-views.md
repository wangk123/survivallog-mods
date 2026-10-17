# 储藏柜直达合并与场景视图 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CabinetTagFilter 合并进 CabinetEverywhere，实现按界面的固定场景视图（食物/材料/下拉自由筛选）、自绘游戏风格下拉框、按界面记忆，并把颜料桶纳入注入覆盖。

**Architecture:** 显示层统一（方案 A）——JS 只做 DOM 可见性，C# 侧 CabinetTabs 永远全量，自动取料零影响。合并后单工程：CabinetCatalog 单遍扫描产出全容器名单（柜子/冰箱/材料架+标签），直达注入与筛选共用；JS 从 C# verbatim 迁移到内嵌资源 `tagfilter.page.js`；场景视图表 cfg 驱动、C# 周期推送。

**Tech Stack:** BepInEx 6 IL2CPP + Il2CppInterop（net6.0）、Vuplex WebView ExecuteJavaScript（同源 DOM）、原生 JS（无框架）、Chrome DevTools Protocol（playwright-core，验证用）。

**Spec:** `docs/superpowers/specs/2026-10-17-cabinet-merge-scenario-views-design.md`（本计划从 spec 出发，执行者两份都读）

## Global Constraints

- **版本号由作者在 version.json 定，任何任务不得自行定号/改号**；csproj 里保持占位 `1.3.3`。
- 游戏/部署路径：游戏本体 `E:\SteamLibrary\steamapps\common\Survival Log`，mod dll 部署到其 `BepInEx\plugins\`；UI 源码明文在同游戏 `SurvivalLog_Data\StreamingAssets\WebUI\`（快照 `tools/webui_src/`，游戏目录永远为准）。
- **构建/部署铁律（v1.3.2 教训）**：构建必须看退出码（`build | tail && cp` 会吞编译失败）；JS 改动必须过 `node --check`；dll 部署三关 = 构建 0 错误 + node --check 通过 + 复制成功。游戏运行时 dll 被占用（先确认游戏退出再 cp）。
- 本工程**无单元测试框架**（IL2CPP interop 依赖游戏运行时）；每任务的验证 = `dotnet build` 退出码 + `node --check` + （需游戏的）CDP 断言/作者进档实测。标注【作者配合】的步骤需要作者开游戏进存档。
- JS 侧铁律（十五轮打磨，勿破坏）：MutationObserver 回调内**同步**套 display:none（禁防抖）；只截读不改下发的页签数组；observer 必须 `{childList, subtree, attributes, attributeFilter:['class']}`；伪造切换事件字段类型逐页照抄原生（trade=字符串 ownerId、bag=数字）；trade 一切柜号读元素 `data-owner` 属性。
- 日志前缀：合并后统一 `[CabinetEverywhere]`，筛选子系统消息用 `[CabinetEverywhere.筛选]`。
- 每 Task 结束 commit（信息用中文，格式 `feat:`/`refactor:`/`docs:`/`test:`）。
- ModShared 共享源码经 csproj `<Compile Include="../ModShared/...">` 链入，勿复制文件。

## 背景速览（执行者必读）

- **直达**（CabinetEverywhere）：`CraftSourceInjector` 向官方白名单 `ToolCabinetConfig.CraftSourceFurnitureIds` Add 柜子家具 ID（驱动工作台/交易/无人机站/粉碎机/温室 五系统原生页签+自动取料）；`TabInjector` 向烹饪/老鼠笼/酿酒的 Redux 状态 `BagTabs`（ObservableList）追加柜子页签（0.5s 轮询幂等补挂）。
- **筛选**（CabinetTagFilter）：`TagFilter.Tick` 每 5s 经 `ExecuteJavaScript` 注入 `TagFilterJs.Bootstrap`（运行于 Root 帧）+ `setData(json)` 喂数据；JS 包装各 iframe 的 postMessage 截读 `*_BagMsg` 记页签数组，按选中标签 DOM 隐藏不匹配容器页签 + 消息层扣发/伪造切换（A/D 步进弹回）。
- **数据契约 v2（现状）**：`setData({owners: {"<ownerId>": [tagId,...]}, lib: [{id,order,dim,name}]})`；选中态 localStorage `sltf_sel_v2` 全局单值。
- **detectC（页签栏识别，JS）**：按 DOM 特征返回 `{kind: tt|bag|trade|cart|cart0, list, sel, attr?, offset, bar, pos}`。**已实证：颜料桶（#bagTabsScroll）自动落 bag 分支、温室（#tabsScroll/.left-tab/cartTabs/activeTab）自动落 cart 分支——筛选机制层已天然覆盖两页**，无需新增 detectC 条目（spec §6 的"加页"仅剩验证）。
- **iframe id = pageId**（Root.html `frame.id = pageId` 实证），场景表键用页面 id：`Cooking / RatCage / BrewPanel / DyePanel / Compressor / ToolTable / Shredder / GreenhouseBuild / TradeUI / DroneHub`（Task 1 逐个核实）。

---

### Task 1: 静态取证包（Dye interop 类型 + 页面 id 清单）

**Files:**
- Create: `tools/dump_dye_types.ps1`（Cecil 脚本，参考 `tools/` 下既有 dump_*.ps1 的 Mono.Cecil 加载写法）
- Modify: 无源码改动

**Interfaces:**
- Produces: ① `WebUI_Dye`/`State_Web_Dye`/`Data_Web_Dye_BagTab` 的确切类型名与字段清单（Task 8 PollDye 用）；② 10 个页面的 iframe id 清单（Task 5 场景表键用）；③ `Config_FurnitureTag.Color` 字段类型（Task 5 lib JSON 用）。

- [ ] **Step 1: 写 Cecil 取证脚本**

```powershell
# tools/dump_dye_types.ps1 —— dump Dye 相关 interop 类型与字段（取证用，跑完即用）
# Mono.Cecil 加载方式照抄 tools/ 下任一 dump_*.ps1 的头部（LoadFile Mono.Cecil.dll + 读 HotUpdate.dll）
# 目标：打印以下类型的全字段（名/类型）：
#   WebUI_Dye、State_Web_Dye、Data_Web_Dye_BagTab、WebUI_Dye 对应的 State/Tab DTO
#   Config_FurnitureTag（重点 Color 字段类型：string / int / float？）
# 匹配规则：TypeName -like '*Dye*'（排除明显无关），另单独打印 Config_FurnitureTag
```

Run: `powershell -File tools/dump_dye_types.ps1`
Expected: 输出 `State_Web_Dye`（含 `BagTabs` 字段，类型 `ObservableList<Data_Web_Dye_BagTab>`）、`Data_Web_Dye_BagTab`（含 `Index/OwnerId/Name/Locked` 或等价字段——**以实际输出为准记录**，Task 8 按此写）。

- [ ] **Step 2: 核对页面 id 清单**

Run: `grep -n "newPages\|pageId" tools/webui_src/Root.html | head -30`，并在 Root.html 的页面注册表里找出 10 个页面的 id（Cooking/RatCage/BrewPanel/DyePanel/Compressor/ToolTable/Shredder/GreenhouseBuild/TradeUI/DroneHub）。若注册表 id 与 UI 文件夹名不一致，以注册表为准记录。
Expected: 10 个 id 明确记录（写入本任务 commit 信息或临时笔记，Task 5 的 scenario JSON 与 cfg 键直接引用）。

- [ ] **Step 3: 确认 Color 字段与 TAG_ICONS 键集**

`Config_FurnitureTag.Color` 类型记录（Step 1 输出）；同时 `grep -o "TAG_ICONS = {[^}]*" tools/webui_src/UI/BackpackUI/BackpackUI.html` 不可行（多行），改为 `sed -n '/const TAG_ICONS = {/,/^                };/p' tools/webui_src/UI/BackpackUI/BackpackUI.html` 拿到全部 iconKey 清单，记录 iconKey 集合（Task 7 静态兜底图标集用）。

- [ ] **Step 4: Commit**

```bash
git add tools/dump_dye_types.ps1
git commit -m "tools: Dye interop 类型与页面 id 取证脚本（合并前置）"
```

---

### Task 2: 合并骨架——TagFilter 工程并入 CabinetEverywhere（行为不变）

**Files:**
- Move: `SurvivalLog.CabinetTagFilter/ContainerScan.cs` → `SurvivalLog.CabinetEverywhere/ContainerScan.cs`
- Move: `SurvivalLog.CabinetTagFilter/TagFilter.cs` → `SurvivalLog.CabinetEverywhere/TagFilter.cs`
- Move: `SurvivalLog.CabinetTagFilter/TagFilterJs.cs` → `SurvivalLog.CabinetEverywhere/TagFilterJs.cs`
- Delete: `SurvivalLog.CabinetTagFilter/`（整个文件夹：csproj/bin/obj/DESIGN.md——DESIGN.md 历史先复制暂存到 `SurvivalLog.CabinetEverywhere/DESIGN-TagFilter-Archive.md`，Task 10 并入正式文档后删）
- Modify: `SurvivalLog.CabinetEverywhere/CabinetEverywherePlugin.cs`、`SurvivalLog.CabinetEverywhere/SurvivalLog.CabinetEverywhere.csproj`、`tools/publish.ps1`（$suiteMods 去 cabinettagfilter）、`tools/make_mods_release.ps1`（$mods 去 cabinettagfilter；cfg 归一表同步）

**Interfaces:**
- Produces（后续任务依赖）：`CabinetEverywherePlugin.FilterEnable : ConfigEntry<bool>`（原 TagFilter Enable，键改 `[Filter] Enable`）；`AccessTicker` 慢通道（5s）内调用 `ContainerScan.Refresh()` + `TagFilter.Tick()`；三个文件命名空间全部 `SurvivalLog.CabinetEverywhere`，`ContainerScan`/`TagFilter`/`TagFilterJs` 类名不变。

- [ ] **Step 1: 搬文件 + 改命名空间**

`git mv` 三个文件，把 `namespace SurvivalLog.CabinetTagFilter` 全部替换为 `namespace SurvivalLog.CabinetEverywhere`；文件内 `CabinetTagFilterPlugin.Log` 替换为 `CabinetEverywherePlugin.Log`，日志字符串前缀 `[CabinetTagFilter]` 替换为 `[CabinetEverywhere.筛选]`。

- [ ] **Step 2: plugin 合并 cfg 与 Ticker**

`CabinetEverywherePlugin.Load()` 增加：

```csharp
internal static ConfigEntry<bool> FilterEnable;
// Load() 内，现有 cfg 绑定之后：
FilterEnable = Config.Bind("Filter", "Enable", true,
    "标签筛选总开关：关闭后不注入筛选（场景视图也一并失效，容器页签全部显示）。");
FilterEnable.SettingChanged += (object s, System.EventArgs e) => { Rerun = true; };
```

`AccessTicker.Tick` 慢通道块（`if (slowDue)` 内、白名单注入之后）追加：

```csharp
// 4) 筛选：容器枚举 + JS 注入/喂标签数据（原独立 Ticker 的 5s 周期，幂等）
if (CabinetEverywherePlugin.FilterEnable.Value)
{
    ContainerScan.Refresh();
    TagFilter.Tick();
}
```

删除原 `CabinetTagFilterPlugin.cs`/`Ticker`（已随文件夹删除）。

- [ ] **Step 3: csproj 加 Vuplex 引用 + 删旧工程**

`SurvivalLog.CabinetEverywhere.csproj` 的 Reference 组追加（照抄原 TagFilter csproj 的写法）：

```xml
    <Reference Include="Vuplex.WebView">
      <HintPath>E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\interop\Vuplex.WebView.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

`git rm -r SurvivalLog.CabinetTagFilter/`（先 `cp SurvivalLog.CabinetTagFilter/DESIGN.md SurvivalLog.CabinetEverywhere/DESIGN-TagFilter-Archive.md`）。

- [ ] **Step 4: 发布脚本最小同步（保持脚本可用；完整登记在 Task 10）**

`tools/publish.ps1` 的 `$suiteMods` 删 `cabinettagfilter` 项；`tools/make_mods_release.ps1` 的 `$mods` 表删 cabinettagfilter 行（cfg 归一清单里对应行一并删）。installer.iss 等 Task 10 再动（此期间不发版即可）。

- [ ] **Step 5: 构建 + 部署 + 验证（行为不变）**

```bash
dotnet build SurvivalLog.CabinetEverywhere -v q; echo "EXIT=$?"
```
Expected: `EXIT=0`、0 错误 0 警告（新代码）。确认游戏已退出后：
```bash
rm "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\SurvivalLog.CabinetTagFilter.dll"
cp SurvivalLog.CabinetEverywhere/bin/Debug/net6.0/SurvivalLog.CabinetEverywhere.dll "E:\SteamLibrary\steamapps\common\Survival Log\BepInEx\plugins\"
```
【作者配合】进档开工作台/烹饪：柜子页签照常 + 下拉筛选照常（选标签能隐藏）+ A/D 切页签正常。`BepInEx\LogOutput.log` 出现 `[CabinetEverywhere.筛选] 家中容器 N 个` 行。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: CabinetTagFilter 并入 CabinetEverywhere（行为不变，双 Ticker 合一）"
```

---

### Task 3: 扫描合一——ContainerScan 并入 CabinetCatalog

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/CabinetCatalog.cs`（RefreshHomeInstances 扩展为统一容器扫描）
- Delete: `SurvivalLog.CabinetEverywhere/ContainerScan.cs`
- Modify: `SurvivalLog.CabinetEverywhere/TagFilter.cs`（`ContainerScan.Home` → `CabinetCatalog.AllContainers`）

**Interfaces:**
- Produces: `CabinetCatalog.ContainerInst { long OwnerId; int ConfigId; string Name; char Kind; int[] TagIds; bool IsMaterialRack }`——`Kind`: `'c'`柜子 / `'f'`冰箱 / `'r'`材料架；`CabinetCatalog.AllContainers : IReadOnlyList<ContainerInst>`（筛选用，全容器）；`CabinetCatalog.HomeInstances`（**语义不变**：直达注入用柜子子集 = `Kind=='c'`，与旧行为逐字节等价——仍由 `ConfigIds` 名单门槛决定）。

- [ ] **Step 1: 统一扫描实现**

`CabinetCatalog.RefreshHomeInstances` 单遍循环改造：廉价门槛从 `ConfigIds.Contains(cfgId)` 放宽为「配置查包 `bagId>0` 且不在 `ExcludeBagIds`/NonStorage 名单」（`ContainerScan.NonStorageBagIds` 常量并入 `CabinetCatalog`，与 cfg `ExcludeBagIds` 默认名单**同源同维护**的注释保留）；对每个通过者构造 `ContainerInst`：

```csharp
bool coldChain = isElec && elecType == 1 && cold > 0f;   // 与原生 CollectFridges 同判定
char kind = rack ? 'r' : (coldChain ? 'f' : 'c');         // rack=MaterialRackComponent 存在（原 FillTags 的 HasComponent 逻辑并入）
inst.Kind = kind;
inst.TagIds = ReadTagIds(furn);                            // 原 ContainerScan.FillTags 的标签读取，方法搬入
// 直达柜子子集判定（保持旧语义）：kind=='c' && ConfigIds.Contains(cfgId) 才进 _homeInstances
```

性能顺序保持：TryCast → 查包/名单（廉价）→ 主图严格相等短路 → 非主图 IsHomeMap。`DumpFurniture` 诊断逻辑不动（仍走原全量段）。镜像补全（BagCache）继续只对 `_homeInstances`（柜子子集）做——行为不变。
**注意**：合并后每 5s 只扫一遍（原两个 mod 各扫一遍），`AllContainers` 与 `HomeInstances` 从同一次遍历产出。

- [ ] **Step 2: TagFilter 数据源切换**

`TagFilter.cs`：`using` 无需变（同命名空间）；`foreach (var c in ContainerScan.Home)` → `foreach (var c in CabinetCatalog.AllContainers)`；`AppendOwner(sb, c, materialTagId)` 签名改收 `CabinetCatalog.ContainerInst`（伪标签逻辑原样）。`ContainerScan.cs` 删除。

- [ ] **Step 3: 构建 + 验证**

`dotnet build SurvivalLog.CabinetEverywhere -v q; echo "EXIT=$?"` → 0 错误；部署三关同 Task 2 Step 5。【作者配合】进档验证同 Task 2（重点：冰箱页签在烹饪界面照常可筛——扫描合一后冰箱仍在名单里；日志容器计数 ≥ 旧值，因多了冰箱/材料架）。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor: 容器扫描合一——CabinetCatalog 单遍产出全容器名单（柜/冰箱/材料架+标签），直达/筛选共用"
```

---

### Task 4: JS 内嵌资源化——tagfilter.page.js

**Files:**
- Create: `SurvivalLog.CabinetEverywhere/tagfilter.page.js`（内容 = `TagFilterJs.Bootstrap` 字符串逐字节搬运）
- Modify: `SurvivalLog.CabinetEverywhere/SurvivalLog.CabinetEverywhere.csproj`（EmbeddedResource）
- Modify: `SurvivalLog.CabinetEverywhere/TagFilter.cs`（读资源注入）
- Delete: `SurvivalLog.CabinetEverywhere/TagFilterJs.cs`

**Interfaces:**
- Produces: 内嵌资源逻辑名 `SurvivalLog.CabinetEverywhere.tagfilter.page.js`；`TagFilter.BootstrapJs : string`（缓存属性，读资源一次）。

- [ ] **Step 1: 抽取 JS 到文件**

从 `TagFilterJs.cs` 的 `Bootstrap` 常量（`@"..."` verbatim）提取正文（去掉 C# 包装与 `";` 结尾），写入 `tagfilter.page.js`。内容**一字不改**（本轮只搬家不重构；半角双引号在独立文件里已无 C# 语义风险，但保持原样最小 diff）。

- [ ] **Step 2: csproj 资源声明 + node 校验**

```xml
  <ItemGroup>
    <EmbeddedResource Include="tagfilter.page.js" LogicalName="SurvivalLog.CabinetEverywhere.tagfilter.page.js" />
  </ItemGroup>
```

```bash
node --check SurvivalLog.CabinetEverywhere/tagfilter.page.js; echo "EXIT=$?"
```
Expected: `EXIT=0`（语法合法）。

- [ ] **Step 3: TagFilter.cs 读资源**

```csharp
private static string _bootstrap;
internal static string BootstrapJs => _bootstrap ??= ReadBootstrap();
private static string ReadBootstrap()
{
    var asm = System.Reflection.Assembly.GetExecutingAssembly();
    using var s = asm.GetManifestResourceStream("SurvivalLog.CabinetEverywhere.tagfilter.page.js");
    if (s == null) { WarnOnce("res", "内嵌资源 tagfilter.page.js 缺失"); return ""; }
    using var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
    return r.ReadToEnd();
}
// Tick() 内：web.ExecuteJavaScript(TagFilterJs.Bootstrap) → web.ExecuteJavaScript(BootstrapJs)
```

- [ ] **Step 4: 构建 + 部署 + 验证（行为不变）**

构建退出码 0 + `node --check` 0 + 部署。【作者配合】进档：筛选照常工作（下拉在、选择生效、A/D 正常）。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor: 筛选 JS 迁移到内嵌资源 tagfilter.page.js（node --check 可校验，verbatim 引号坑根治）"
```

---

### Task 5: 场景视图 cfg + 数据契约 v3

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/CabinetEverywherePlugin.cs`（[Scenario] cfg）
- Modify: `SurvivalLog.CabinetEverywhere/TagFilter.cs`（BuildDataJson v3）
- Modify: `SurvivalLog.CabinetEverywhere/tagfilter.page.js`（setData 收 v3；本任务 JS 只收数据，行为仍 v2 等价——视图判定在 Task 6）

**Interfaces:**
- Produces（JS 侧可见契约 v3）:

```json
{
  "owners": { "<ownerId>": { "t": [tagId,...], "kind": "c|f|r" } },
  "lib":    [ { "id":1, "order":1, "dim":0, "name":"材料", "icon":"material", "color":"..." } ],
  "scenario": { "Cooking":"food", "RatCage":"food", "BrewPanel":"food", "DyePanel":"food", "Compressor":"food",
                "ToolTable":"material", "Shredder":"material", "GreenhouseBuild":"material",
                "TradeUI":"browser", "DroneHub":"browser" }
}
```
- cfg 键（页面 id 用 Task 1 核实后的清单）：`[Scenario] View_Cooking` 等 10 键，enum `ScenarioView { Food, Material, Browser }`，默认值按上表。

- [ ] **Step 1: cfg 定义**

```csharp
public enum ScenarioView { Food, Material, Browser }
internal static ConfigEntry<ScenarioView> ViewCooking, ViewRatCage, ViewBrew, ViewDye, ViewCompressor,
                                          ViewToolTable, ViewShredder, ViewGreenhouse, ViewTrade, ViewDroneHub;
// Load() 内：
ViewCooking   = Config.Bind("Scenario", "View_Cooking",    ScenarioView.Food,    "烹饪界面视图：Food=食物标签柜+全部冰箱；Material=材料/工具/未标签柜；Browser=下拉自由筛选。");
// ... 10 键同款，默认值按 Interfaces 表；SettingChanged += Rerun 复位
```

- [ ] **Step 2: BuildDataJson v3**

`AppendOwner` 改为对象形式：`{"t":[ids],"kind":"c|f|r"}`（`c.Kind` 直出 char；伪标签材料架追加逻辑保留在 `t` 数组内）。lib 条目追加 `"icon":"<IconKey>"` 与 `"color":"<Color 原样字符串化>"`（Color 类型按 Task 1 取证：string 直出 / 数值则 `Convert.ToString`，JS 不解析只透传）。追加 `scenario` 段：

```csharp
sb.Append(",\"scenario\":{");
sb.Append("\"Cooking\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewCooking)).Append("\",");
// ... 10 键；ViewStr: Food→"food" / Material→"material" / Browser→"browser"
sb.Append("}");
```

- [ ] **Step 3: JS setData 兼容收 v3（行为暂不变）**

`tagfilter.page.js`：`S.owners` 值从数组改对象后，`matchesSel/shouldHide` 的旧读法会失效——本任务同步最小改造：新增 `S.kind = {}`、`S.scenario = {}`、`S.matId/S.toolId`（从 lib 按 `icon==='material'/'tool'` 求）；内部把 `S.owners[k]` 统一改读 `ownerOf(k) = {t:[],kind:'c'}` 归一化函数（v3 数据下 `matchesSel(ownerOf(k).t)` 与 v2 行为等价：`sel=foodId` 时 Dim1-4 匹配、否则精确命中、材料架伪标签在 `t` 内自然生效）。**不动视图/下拉/记忆**（Task 6 来）。

- [ ] **Step 4: 构建 + node --check + 部署 + 验证**

【作者配合】进档：v2 行为完全等价（筛选/A/D 照旧），`Player.log` 无 JS 报错（有 iframe console 转发；必要时 DevTools Console 看 `__SLTF__.state()`）。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: 场景视图 cfg（Scenario.View_*）+ 筛选数据契约 v3（kind/icon/color/scenario）"
```

---

### Task 6: JS 固定视图 + 按界面记忆 +「其他」选项

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/tagfilter.page.js`

**Interfaces:**
- Consumes: Task 5 的 v3 数据。
- Produces: `frameMode(st) → 'food'|'material'|'browser'`；`pageSel(st)`/`setSel(st, v)`（v 为 tagId 数字或 `'other'` 或 0）；localStorage `sltf_sel_v3`（按 pageId 的对象）；固定视图页无下拉、`browser` 页保留下拉（本任务仍是旧样式 select，样式 Task 7 重做）。

- [ ] **Step 1: 记忆层改造**

```js
var LS_KEY = 'sltf_sel_v3';           // v2 全局值废弃，不迁移
S.sel = {};                            // { pageId: sel }
try { S.sel = JSON.parse(localStorage.getItem(LS_KEY) || '{}') || {}; } catch (e) {}
function pageKey(st) { return st.el && st.el.id ? st.el.id : ('f' + S.frames.indexOf(st)); }
function pageSel(st) { return S.sel[pageKey(st)] || 0; }
function frameMode(st) { return S.scenario[pageKey(st)] || 'browser'; }
function setSel(st, v) {
  S.sel[pageKey(st)] = v || 0;
  try { localStorage.setItem(LS_KEY, JSON.stringify(S.sel)); } catch (e) {}
  st.lastActiveIdx = null;
  applyFrame(st);
}
```

- [ ] **Step 2: 视图谓词（替换 shouldHide/matchesSel 语义）**

```js
function matchesFood(t) {           // 原 matchesSel 的 foodId 分支抽出
  for (var i = 0; i < t.length; i++) { var d = S.dim[t[i]] || 0; if (d >= 1 && d <= 4) return true; }
  return false;
}
function viewAllows(st, o) {        // o = {t:[], kind:'c'|'f'|'r'}
  var m = frameMode(st);
  if (m === 'browser') {
    var sel = pageSel(st);
    if (!sel) return true;
    if (sel === 'other') return o.t.length === 0 && o.kind !== 'r';   // 「其他」=未打标签容器
    if (sel === S.foodId) return matchesFood(o.t);
    return o.t.indexOf(sel) >= 0 || (o.kind === 'r' && sel === S.matId); // 材料架伪标签
  }
  if (m === 'food') {
    if (o.kind === 'f') return true;                    // 全部冰箱一律显示
    return matchesFood(o.t);                            // 柜子/材料架按食物域（材料架必然不中→隐藏）
  }
  // material：材料+工具+未标签柜；不含冰箱；材料架显
  if (o.kind === 'r') return true;
  if (o.kind === 'f') return false;
  return o.t.length === 0 || o.t.indexOf(S.matId) >= 0 || o.t.indexOf(S.toolId) >= 0
      || (S.matFood && matchesFood(o.t));               // S.matFood=K2 取证后若材料组需含食物则 true（默认 false，cfg 见 Task 9）
}
function shouldHide(k, st) {
  var o = S.owners[k];
  return o !== undefined && !viewAllows(st, o);
}
```

调用面同步：`applyFrame(st, c)`/`tryHold(st,...)` 里 `shouldHide(String(...))` 全部补第二参 `st`；`readActive` 不变。

- [ ] **Step 3: 下拉开关 +「其他」选项**

`refreshDD`：`frameMode(st) !== 'browser'` 时若有 `st.dd` 则移除并 return（固定视图页无任何 UI）。`options()` 末尾追加：`out.push({ id: 'other', name: '其他' })`（排在「全部柜子」之后、标签之前或按现有顺序约定——放「全部柜子」后第二个）。`change` 事件值解析：`dd.value === 'other' ? 'other' : parseInt(dd.value,10) || 0`。`setSel` 调用点改为 `setSel(st, v)`（原全局 `setSel(v)` 循环全部 frames 的行为删除——每页独立）。

- [ ] **Step 4: 首开默认与换筛选**

固定视图页打开即套视图（applyFrame 走 viewAllows，无需 sel）；browser 页 `pageSel` 默认 0=全部。

- [ ] **Step 5: 构建/校验/部署/验证**

`node --check` + 构建退出码 + 部署。【作者配合】进档逐页验证：烹饪/老鼠笼/酿酒/压制机=只见食物柜+冰箱；工作台/粉碎机/温室=材料/工具/未标签柜、无冰箱；交易/调度机队=下拉在、「其他」可选（只出未标签柜）、选择后关开界面记忆保持、两界面记忆互不影响。

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: 场景固定视图（食物/材料）+ 按界面记忆 sltf_sel_v3 + 「其他」选项"
```

---

### Task 7: 自绘下拉框（位置/样式/图标）

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/tagfilter.page.js`

**Interfaces:**
- Consumes: v3 lib 的 `icon`（iconKey）与 `color`；BackpackUI 帧 `TAG_ICONS`（同源提取）。
- Produces: `.sltf-dd-btn`（button 触发器）+ `.sltf-dd-pop`（body 级 popup，position:fixed 防裁切）；位置=页签行末、滚动区外（`c.list` 的父容器 append 尾）。

- [ ] **Step 1: TAG_ICONS 同源提取 + 静态兜底**

```js
var TAG_ICONS_FALLBACK = { /* 从 tools/webui_src/UI/BackpackUI/BackpackUI.html 的 TAG_ICONS 复制
   food/misc/material/tool 等 Task 1 Step 3 记录的键集，SVG 字符串原样 */ };
function tagIcon(pageDoc, key) {
  try {
    var bp = pageDoc.defaultView;                       // 页面自身 window
    // BackpackUI 帧另取：从 Root 找 id='BackpackUI' 的 iframe（未打开时为 null→走兜底）
    var f = document.querySelector('#iframe-container iframe#BackpackUI');
    var icons = f && f.contentWindow ? f.contentWindow.eval('TAG_ICONS') : null;
    if (icons && icons[key]) return icons[key];
  } catch (e) {}
  return TAG_ICONS_FALLBACK[key] || TAG_ICONS_FALLBACK.misc;
}
```
（BackpackUI 多数时候未打开→实际多走兜底；官方图标库更新时 diff 快照同步兜底副本，注释写明。）

- [ ] **Step 2: 触发器 + popup 构建（替换 refreshDD 的 select 实现）**

```js
function refreshDD(st, c) {
  // ... 前置同旧（mode!=='browser' 移除并 return；旧的 .sltf-dd 清理改清 .sltf-dd-btn/.sltf-dd-pop）
  var dd = st.dd;                                       // 现在是 <button class="sltf-dd-btn" type="button">
  if (!dd || !dd.isConnected || dd.ownerDocument !== st.doc) {
    dd = st.doc.createElement('button');
    dd.type = 'button'; dd.className = 'sltf-dd-btn'; dd.title = '按标签筛选容器页签';
    dd.style.cssText = 'height:26px;min-width:96px;max-width:150px;margin:2px 6px 2px 4px;padding:0 22px 0 8px;'
      + 'font-size:12px;flex:0 0 auto;align-self:center;position:relative;cursor:pointer;'
      + 'color:#e8dcc8;background:rgba(28,24,18,0.96);border:1px solid rgba(255,193,7,0.35);border-radius:5px;'
      + 'display:flex;align-items:center;gap:5px;overflow:hidden;white-space:nowrap;'
      // ▾ 箭头与 hover 态：克隆 DyePanel .dd-btn::after / :hover（tools/webui_src 快照，逐值抄）
      + '';
    dd.addEventListener('click', function (e) { e.stopPropagation(); togglePop(st, c); });
    dd.innerHTML = '<span class="sltf-dd-ico" style="display:flex;width:14px;height:14px;flex:0 0 auto">'
      + '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 5h18v2H3zm0 6h18v2H3zm0 6h18v2H3z"/></svg></span>'
      + '<span class="sltf-dd-txt" style="min-width:0;overflow:hidden;text-overflow:ellipsis"></span>';
    // 位置：滚动区外行末——挂到 c.list 的父容器（行容器）尾部
    var row = (c.list.parentElement) || c.bar;
    row.appendChild(dd);
    st.dd = dd;
  }
  // 选中态文案+图标
  var sel = pageSel(st), lib = selById(sel);
  dd.querySelector('.sltf-dd-txt').textContent = sel ? (lib ? lib.name : '其他') : '全部柜子';
  dd.querySelector('.sltf-dd-ico').innerHTML = sel && lib ? tagIcon(st.doc, lib.icon) : TAG_ICONS_FALLBACK.misc;
}
```
popup：`togglePop` 在 `st.doc.body` 下建/删 `.sltf-dd-pop`（position:fixed，坐标=按钮 getBoundingClientRect，下方溢出则向上弹出）；每行 `<button class="sltf-dd-opt" data-interactive>`=图标(svg, color=lib.color)+名称+选中标记，click→`setSel(st, id)`+关 popup；`st.doc` 任意 mousedown 关闭；**popup 打开期间行数多时 max-height+滚动**。

- [ ] **Step 3: 事件与重建兼容**

TradeUI 是 Vue 页会整条重渲染摘按钮——沿用观察器 `isConnected` 自动重插（已有机制对 `st.dd` 生效）；popup 挂 body + 每帧唯一（`st.pop`，schedule 时 `!isConnected` 清引用）。`select` 元素时代的 `change` 监听代码删除。

- [ ] **Step 4: 校验/部署/验证**

`node --check` + 构建 + 部署。【作者配合】交易/调度机队：下拉在页签行末不挤压页签、随页签多不滚动出视野；弹出列表有图标+官方色；选择即时生效；点击穿透正常（能点开、能选项、点外部关闭）；固定视图页确认无任何残留 UI。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: 自绘游戏风格下拉框——行末滚动区外、官方图标/配色、body 级 popup 防裁切"
```

---

### Task 8: 颜料桶注入（PollDye）

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/TabInjector.cs`
- Modify: `SurvivalLog.CabinetEverywhere/CabinetEverywherePlugin.cs`（EnableDye cfg）

**Interfaces:**
- Consumes: Task 1 取证的 `WebUI_Dye`/`State_Web_Dye`/`Data_Web_Dye_BagTab` 确切字段（下述代码按 PollCooking 同型假设写出，**执行时以取证输出为准逐名核对**）。
- Produces: 颜料桶界面柜子页签注入（bag 族，筛选自动生效）。

- [ ] **Step 1: cfg**

```csharp
internal static ConfigEntry<bool> EnableDye;
EnableDye = Config.Bind("Access", "EnableDye", true, "颜料桶界面注入柜子页签。");
EnableDye.SettingChanged += reload;  // 并入现有 reload 委托链
// AccessTicker：TabInjector.PollAll() 前无额外改动（PollAll 内部判断）
```

- [ ] **Step 2: PollDye（照抄 PollCooking 换类型）**

```csharp
private static readonly Il2CppSystem.Type DyeType = Il2CppInterop.Runtime.Il2CppType.From(typeof(WebUI_Dye));
private static void PollDye(GameCore.HotUpdate.ReduxUI.ReduxUISystem rus)
{
    var s = Session("dye");
    bool active = SafeActive(rus, DyeType);
    if (!active) { s.WasActive = false; return; }
    if (!s.WasActive) { s.WasActive = true; s.Injected.Clear(); }
    var st = rus.GetStateByUI(DyeType)?.TryCast<State_Web_Dye>();
    var tabs = st?.BagTabs;
    if (tabs == null || tabs.Count == 0) return;
    var have = CollectOwners(tabs, t => t?.OwnerId?.Value ?? 0);
    var added = AddMissing("颜料桶", s, have, tabs.Count, (ownerId, name, index) =>
    {
        var tab = new Data_Web_Dye_BagTab();          // 字段名按 Task 1 取证核对（Index/OwnerId/Name/Locked 或等价）
        tab.Index = index;
        tab.OwnerId = new R3.ReactiveProperty<long>(ownerId);
        tab.Name = new R3.ReactiveProperty<string>(name);
        tab.Locked = new R3.ReactiveProperty<bool>(false);
        tabs.Add(tab);
    });
    if (added > 0) Repush(rus, DyeType);
}
```
`PollAll` 增加 `if (CabinetEverywherePlugin.EnableDye.Value) PollDye(rus);`。

- [ ] **Step 3: 构建 + 部署 + 验证**

【作者配合】放一个颜料桶，开颜料桶界面：出现柜子页签、可切换、默认只见食物柜+冰箱（bag 族筛选自动生效）；无标签柜子被藏（食物视图）符合预期。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: 颜料桶界面柜子页签注入（PollDye，bag 族筛选自动覆盖）"
```

---

### Task 9: CDP 断言脚本 + 运行时取证（K2/K3/K4）

**Files:**
- Create: `tools/cabinet_views_cdp.js`（node + playwright-core，依赖路径同 `tools/devtools_cdp_test.js`：`C:\temp\baidu_up\node_modules`）
- Modify: `SurvivalLog.CabinetEverywhere/CabinetEverywherePlugin.cs`（K2 结论落地：若材料组需含食物，加 cfg `Scenario.MaterialIncludeFood`，默认按取证结论）

**Interfaces:**
- Consumes: ViewportProbe DevTools 开关（`[DevTools] Enabled=true`，9222 端口，仅开发机）。
- Produces: 逐页断言脚本（退出码非 0=断言失败）；K2/K3/K4 结论记录（commit 信息+DESIGN.md Task 10 落档）。

- [ ] **Step 1: 写断言脚本**

```js
// tools/cabinet_views_cdp.js —— 逐界面断言场景视图（需游戏已开、作者已进档、界面已打开）
// 模式：node tools/cabinet_views_cdp.js <pageId>            → 断言该页可见页签集合
//      node tools/cabinet_views_cdp.js --dd TradeUI         → 断言下拉存在/位置（行末、滚动区外）
//      node tools/cabinet_views_cdp.js --mem TradeUI <tagId> → 记忆断言（选→重开→选中保持）
// 实现骨架：connectOverCDP('http://localhost:9222') → 枚举 frame（pageId 匹配 iframe id）
//   → evaluate 读 detectC 同款选择器集合：
//   可见柜 tab 数 = querySelectorAll('.cabinet-tab/.bag-tab/.left-tab').filter(el=>el.style.display!=='none' && 不含 pin/drawer)
//   期望值来源：常量表（与 spec §3 场景视图表一一对应，按测试存档柜子打标情况硬编码期望数）
//   断言：固定页 querySelector('.sltf-dd-btn')===null；browser 页 !==null 且其 parentElement 非滚动容器
```
（具体期望数字按作者测试存档的柜子打标实况填——脚本里做成顶部的 EXPECT 常量块。）

- [ ] **Step 2: K2/K3/K4 取证【作者配合】**

- K2：开工作台翻配方（重点：是否有配方消耗食物域材料）。手段：DevTools Console 里看 `ToolTable` 帧 recipe 数据 / 或 ActionProbe dump 配方表。结论二选一：材料组维持 `材料+工具+未标签`（无食物配方）或加 cfg `MaterialIncludeFood=true` 并 JS `S.matFood=true` 路径（Task 6 Step 2 已留钩子）。
- K3：开口粮压制机，记录页签构成（原生清单里有无冰箱页签）→ 核对食物视图「全部冰箱」语义与实际呈现。
- K4：交易/调度机队翻页签确认无域限制（维持 browser 默认的前提成立）。

- [ ] **Step 3: 跑断言 + 落地结论**

`node tools/cabinet_views_cdp.js <每页>` 全绿；K2 结论若有 cfg 化改动，构建+部署+复测后进 commit。

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test: CDP 场景视图断言脚本 + K2/K3/K4 运行时取证结论"
```

---

### Task 10: 白盒穷举 + 文档 + 发布登记

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/DESIGN.md`（合并重写：吸收 `DESIGN-TagFilter-Archive.md` 全部技术取证与演进史为附录，新增场景视图/合并架构/取证结论章节）
- Delete: `SurvivalLog.CabinetEverywhere/DESIGN-TagFilter-Archive.md`（并入后删）
- Modify: `installer/installer.iss`（InstallDelete 增 `SurvivalLog.CabinetTagFilter.dll` 与 `com.local.survivallog.cabinettagfilter.cfg`；cabinettagfilter 组件移除、cabineteverywhere 组件描述改为"直达+智能视图+筛选"）
- Modify: `installer/install_notes.tpl.txt`、`tools/test_installer.ps1`、`CHANGELOG.md`、`README.md`（mod 数 8→7，两处表/矩阵同步）
- Modify: `tools/make_mods_release.ps1`（cfg 归一清单并入 Filter/Scenario 键）

**Interfaces:**
- Consumes: 前面全部任务的最终行为。
- Produces: 发版就绪状态（版本号除外）。

- [ ] **Step 1: 白盒穷举表（十七轮教训）**

从 `tools/webui_src` 逐页 grep `registerHandlers`/`BagMsg`/active 字段，做 10 页 × {页签字段, active 字段, 切换事件, 族别} 对照表，核对本 mod 拦截链（wrapRelay 判定的 hasActive 字段清单 `bagOwnerId/bagOwnerKey/activeLeftTab/activeTab`）**每页每字段都覆盖**；表落 DESIGN.md 附录。发现缺口→先补 JS 再继续。

- [ ] **Step 2: DESIGN.md 合并重写**

新结构：功能定位（含场景视图表）/实现原理（直达+筛选合一、数据契约 v3、场景视图、自绘下拉、PollDye）/配置项全表（Access+Filter+Scenario+Debug）/排障与取证史（两份旧文档的实录全部保留为附录）/演进要点（本次条目：合并、场景视图、颜料桶、自绘下拉、v3 记忆）。

- [ ] **Step 3: 发布登记全套**

installer.iss 四处（Types 描述/Components/Files/InstallDelete——InstallDelete 键加两行：`{app}\BepInEx\plugins\SurvivalLog.CabinetTagFilter.dll` 与 `{app}\BepInEx\config\com.local.survivallog.cabinettagfilter.cfg`）；install_notes 三处；test_installer.ps1（矩阵删 tagfilter 行、absent check 加"旧 tagfilter dll 不存在"）；CHANGELOG（合并+场景视图+颜料桶+UI 重做条目，**版本号留待作者定**写 `{version}` 占位并在交付说明里注明）；README 表更新。

- [ ] **Step 4: 安装器本地验证**

`tools/build_installer.ps1` + `tools/test_installer.ps1` 全绿（旧 dll 清理路径覆盖验证）。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "docs: DESIGN 合并重写 + 白盒穷举表；发布登记全套（InstallDelete 旧 tagfilter dll/cfg）"
```

---

### Task 11: 版本号与发版【作者拍板后执行】

**Files:**
- Modify: `version.json`（**作者定号**）、`CHANGELOG.md`（占位替换）

- [ ] **Step 1: 作者定版本号 → 写入 version.json → `tools/publish.ps1` 一键流水线 → 网盘/安装器分发按既定流程。**

- [ ] **Step 2: 发版后回归：干净环境装安装器 → 进档全页过一遍（Task 9 断言脚本复跑）。**

---

## 自审记录

- **Spec 覆盖**：spec §3 场景视图表→Task 5/6；§4 架构（合并/扫描合一/page.js/数据流）→Task 2/3/4/5；§5 UI→Task 6/7；§6 覆盖扩展→Task 1（detectC 天然匹配的实证）+Task 8；§7 取证 K1→Task 1、K2/K3/K4→Task 9、K5→Task 7 Step 2（行末锚点实现即验证）；§8 测试三层→各任务验证步+Task 9+Task 10 Step 1；§9 迁移发布→Task 2 Step 4/Task 10/Task 11；§10 边界→无对应任务（by design）。
- **占位符**：无 TBD；Task 8 代码注明"以 Task 1 取证为准逐名核对"（取证任务产出的就是确定值，非占位）。
- **类型一致性**：`CabinetCatalog.ContainerInst.Kind:char` 与 JS `"c|f|r"` 对应；`setSel(st,v)` 在 Task 6 定义、Task 7 调用；`S.matFood` 钩子 Task 6 定义、Task 9 落地。
- **一处 spec 偏差（有意）**：spec §4.2 说 domain 标志由 C# 预计算——实现改为 C# 只推 `kind`，域谓词在 JS 从 lib(dim/icon) 推导（lib 数据已在 JS 侧，避免 dim 逻辑双份维护）；契约形状不变。
