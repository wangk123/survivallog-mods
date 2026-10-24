# SurvivalLog.PlanPick — 自选生存规划（设计文档）

> 状态：**已实装**（2026-10-24，7 任务：构建/回归夹具/modcompat 三绿，待作者装机验证——步骤见 VERIFY.md）。
> 原型：`prototype/index.html`（`node prototype/serve.js` 后浏览器打开，见 §8）。

## 1. 功能

- 在每日生存规划选择页（RogueSelect，页面标题"生 存 规 划"）页脚增加 **自选规划** 入口按钮。
- 点击弹出生存规划**全量列表**（`Config_Talent.Type==2 && Available==1`，约 465 条）：
  - 按类型分类 tab；卡片含图标、名称、描述、消耗生存点（原价划线 + 自选实价）；
  - 角色专属规划沿用官方"专"徽章；
  - **全展示口径（作者定稿）**：解锁条件未满足的也展示——图标置灰、不可购买，点击弹**未解锁原因**；
  - 自选购买（不在今日候选名单的规划）消耗**翻倍**（倍率 cfg，默认 ×2）；今日候选内的购买走官方原价；
  - 购买后扣点、激活、UI 刷新、存档**全部走官方代码**——零新增存档字段，删 dll 即完全还原。

## 2. 机制实证（2026-10-23_26157732 dump + 表，RVA 均属该版）

### 2.1 页面与通道

- 选择页 = `UI/RogueSelect/RogueSelect.html`（Vue，`createWebUICore('RogueSelect')`，跑在 Root.html 的 iframe 里）。
- 数据下行：`WebUI_RogueSelect_Msg`（ownPoints / refreshCost / list：id,name,desc,icon,cost,locked,bought）。
- 事件上行：`sendMessage` = `window.parent.postMessage({type, data, sourcePageId}, '*')`；
  购买 = `type:'CLICK_BUY', data:{id}`（照抄 webui-core.js 实现，注入脚本无需 closure 访问）。

### 2.2 候选生成（SurvivalPlanningComponent）

- 每日 3 张（`PlanSlotCount=3`）。`Config_DailyRandom` 按天给"组×数量"；组 = `Config_RandomGroup` 阶段池。
- **解锁天数的根源 = 池切换**：9102@D1、9103@D7、9104@D13、9105@D20、9106@D24、9108@D33、9107@D49，
  特选池 9130/9131/9132/9133 @D22/26/34/49（Day 覆盖 1~200）。
- 抽取过滤 `IsValidTalentId`（0x301D2C0）：模式拦截（DemoTwo/Endless/纯无尽）+
  `ConditionSetManager.CheckConditionSet`（解锁条件）+ 配方已学排除 + 网格链排除。
- 钉选（Pin）：可钉 1 张保留到明天（`PinnedFreeSlotCount=2` 槽位语义），本 mod 是该机制的完全体。

### 2.3 购买链（关键：不校验候选名单）

- 点击 → Redux `Ac_RogueSelect_WebClickBuy` → `RA_OnClickBuy`（0x2C374B0）只是
  `EventDispatcherTools.NotifyGlobal<int>(15003, talentId)`（15003 = `EnumNotify_Talent.SurvivalPlanBuy`）。
- `SurvivalResultsManager.OnBuySurvivalPlan(int)`（0x2833A30，已逐指令反汇编）：
  1. 取 `Config_Talent` 行；`IsRowBlockedInDemoTwo` 拦截；
  2. cost = `LvUpCost`（行偏移 0x70），经 `LeadingRole.GetSettlementBuyDiscountRatio()` 折扣；
  3. `SurvivalPoint >= cost` 校验 → 扣点 → `RecordSurvivalPointCost` + `CheckSurvivalPointAchievement`；
  4. `SurvivalPlanningComponent.AddBuff(buff, talentId)` 激活；`BuffID>0` 时 `FlushSingleStartEvent` 触发 BT；
  5. 钉选若是本规划则自动释放。
  **全程不校验"是否今日候选"** → 自选购买的钥匙：mod 发同样的 CLICK_BUY 即可走全官方链。

### 2.4 表数据（Config_Talent）

- Type2 = 规划池 494 条，全部 Lv=1；`LvUpCost` 1~480；`WebIcon` 为网页相对路径（页内可直接 `<img>`）。
- `ConditionSetId`：0（无条件 253）/ 前置链 PlanLvGate（TalentNeed，198）/ 特殊条件（EventTag 22、
  DefenseLevel 2、NeedTrapObtainable 4、SurvivalToDay+家具 1 等）。
- `PlayerSelectId` 角色白名单 123 条（1:24 / 2:53 / 3:44 / "1;3":2）——照旧强制。
- `Available=0` 29 条是**官方主动下架**：反汇编实证 `IsValidTalentId` @0x0301D36B `cmp dword[rax+0x1C],0; je fail`
  ——Available==0 直接判无效（其中 12 条仍躺在活跃池 9102~9108 里，但抽取时被此字段测试拒绝；17 条仅在遗留组）。
  **名单审计（2026-10-24）**：官方引用全集（GroupType=2 池 ∪ DailyRandom.SpecifiedPlanID ∪ 前置目标）484 个 id，
  与本名单的差集恰为这 29 条下架行——`Type2 && Available==1`（465）与官方可抽集合**完全一致，零遗漏**；
  前置链指向全部有效；ShowType=3 唯一行（30561 沉着冷静）在册。Available=1 但官方零引用 10 条
  （拾荒无人机 Lv2、机器人图纸 31442~31450）合法展示为"不入池"。
- **角色名定案（2026-10-24，官方 `Config_PlayerSelect.Name_Local`）**：P1 打工仔 / P2 大学生 /
  P3 仓库管理员 / P4 退伍军人。官方 RogueSelect 锁定卡只显示"未 解 锁"**无原因文案可复用**
  （ConstantText 6055 条为字符串键表未随 gamekb 采集），未解锁原因文案为 mod 自组织；
  角色名实装运行时读表（同 RoleUnlock 联动定案），原型已替换（"仅限「打工仔」的专属规划"）。
- 解锁日可全静态推算：466/494 有最早出现日（D1:34 D7:57 D13:29 D20:212 D24:61 D33:67 D49:6）。

## 3. 实现选型（最小侵入两件套）

**C# 插件 + RogueSelect 页 JS 注入**（ViewportFit 巡检注入 / BetterTrade 包裹器 / RoleUnlock 内存表编辑三个既有模式的组合）：

**原生零接触承诺（作者 2026-10-24 定稿）**：候选随机（`GetRandomSurvivalPlanIds`）、刷新、
钉选、锁定计算与官方三卡的展示——一律不 patch 不改写不重算；注入仅 `appendChild` 级添加
自选入口按钮与弹层。官方候选内购买路径完全走原代码（prefix 判定"在今日候选"即零干预）。

1. **UI（JS 注入）**：C# 每 2s 巡检 RogueSelect WebView（ViewportFit 同款），页面就绪后注入
   `page.js`（内嵌资源，N 网三件套同款）：页脚"自选规划"按钮 + 全屏弹层
   （一级分类 tab + 二级筛选 chips + 状态筛选 + 排序 + 搜索 + 卡片列表 + 确认弹窗 + 原因弹泡）。
   确认弹窗只显示 原价/自选价/购买后余额 三行，**不加说明文案**（作者反馈定稿）。
   - **数据全部 C# 算好推送**（`ExecuteJavaScript` 一次性灌 JSON）：每张卡的
     字段契约见 §3.1（v1 草案的 `baseCost/lockState/lockReason/isToday` 已并入
     `cost/state/reasons[]/today`，以 §3.1 为准）；
     JS 零跨端推导（背包 UI 之训：显示层不自算，照抄 computed）。
   - state/reasons[] 由 C# 组合：已激活 > 天数未到 > 前置未购 > 角色不符 > 特殊条件未满足。
   - 点数不足不是 locked：图标不灰、按钮"点数不足"、点击 toast 实数。
2. **购买**：JS 直接 `window.parent.postMessage({type:'CLICK_BUY', data:{id}, sourcePageId:'RogueSelect'})`。
   官方链自动完成扣点/激活/`RA_BuySuccess`→Web 推送刷新（含 ownPoints）；mod 侧下一轮巡检重推真相数据。
3. **翻倍（唯一 Hook）**：Harmony prefix + finalizer 钩 `OnBuySurvivalPlan(int)`：
   - prefix：`talentId ∉ SaveChildData.SurvivalPlanShownIds`（今日候选）→ 判定自选购买 →
     备份该行 `LvUpCost` → 内存写 `cost × CostMultiplier`（RoleUnlock 同款表编辑）；
   - finalizer：无条件恢复原值（异常路径也还原）。
   - 折扣语义：翻倍后再吃主角折扣 = "实付翻倍"，与官方公式天然相容。
   - il2cpp int 参数实例方法 Harmony 有发版先例（CabinetExpand 钩 `BagComponent.OnInit`）。
4. **解锁判定复用官方**：`ConditionSetManager.CheckConditionSet`（public 实例，0x2833A30 链上同款）；
   模式拦截复刻 `IsValidTalentId` 语义（Available / DemoTwo / Endless / 纯无尽）。
5. **存档零写入**：所有购买经官方 `SaveGame`；mod 自身无落盘状态（cfg 除外）。
6. **与 RoleUnlock（角色互通）联动（2026-10-24 作者定稿，零耦合实现）**：角色锁判定读
   **运行时表现值**（每次推 payload 时现读 `cm.Get_Config_Talent(id).PlayerSelectId`，
   非 PlanCatalog 目录快照；读失败回退目录原值 fail-closed）。RoleUnlock 的"生存规划互通"
   清内存表功能类 46 张的白名单（空=不限，与官方 `IsRoleAllowed` 同语义）→ 同装时本 mod
   自动放行这 46 张可选（家具位 77 张 RoleUnlock 按作者定案不清，本 mod 照锁——范围自动
   跟随 RoleUnlock 的表现状）；只装本 mod 时表现值=官方原值，专属照锁。无跨 mod API、
   无加载顺序依赖（判定时点晚于任一 mod 的表编辑）、任一卸载自动还原。
   "专"徽章仍按表原始数据展示（标识官方本为专属内容，不因互通隐藏）。

### 3.1 payload 契约（`__PlanPickBoot` 单参 JSON，C# 全算 / JS 零推导）

- 顶层：`{plans[], categories[], today[], points, mult, day, role, roleNames{}}`
  （categories 固定四类 attribute/function/expand/material；today=今日候选 id 升序）。
- `plans[]` 每项：`{id, name, desc, icon, cost, isReget, cat, sub, unlockDay, today, exclusive, state, reasons[]}`。
- **严格 null 语义**：`sub`/`unlockDay` 要么 JSON `null` 要么值——`sub=null` 独立规划、
  `unlockDay=null` 从不入池（图纸/无人机，受 ShowUnpooled 开关）；page.js 一律 `== null` 判。
- `state ∈ {bought, locked, ok}`（点数是否够**不**在此算）；`reasons[]` 仅 locked 非空，
  优先级同上（bought 无原因；ShowLocked=false 时 locked 行整行不出现在 plans[]）。
- **JS 侧唯一计算 = 点数对比 too-expensive**（`cost × mult > points` → 按钮"点数不足"+toast 实数）；
  其余展示/筛选/排序/计数全部照抄 payload，零跨端推导（背包 UI 之训）。

## 4. 分类（运行期从表一次性构建，等价冻结）

**实装修订（2026-10-24）**：分类/系列改**运行期从表一次性构建**（`PlanCatalog.Build`：数据就绪后按下列规则
构建目录并缓存至进程结束——等价冻结，游戏更新**零维护**，无需随版本重跑脚本）；原型生成脚本
`prototype/gen_data.js` 保留为**对照真源**（同一套规则，换代时跑 diff 复核分类口径）。

**一级分类**（4 类）：

| 分类 | 规则（按序） | 约数 |
|---|---|---|
| 物资贸易 | RandomGroup 1005「物资类」/ 1009「更贵物资」成员 | 9 |
| 家具扩建 | 名称前缀 `扩建·` 或 9122「打工仔家扩建」池成员 | 83 |
| 属性成长 | ID ∈ 30000~30999 | 93 |
| 功能强化 | 其余 | 280 |

**二级分类**（作者 2026-10-24 反馈"一级不够找"追加）：

| 一级 | 二级 = | 实测 |
|---|---|---|
| 家具扩建 | 区域：`扩建·二楼中型位` 取 `二楼`（剥尾部 大/中/小/桌面位）；池内非 `扩建·` 名且带角色白名单 → `专属家具位` | 12 区 + 专属家具位×33 + 独立×6 |
| 属性成长 / 功能强化 / 物资贸易 | 系列：PlanLvGate 前置链成员取名字公共前缀（剥尾 ` Lv`/`·`，如 饱餐/节能/探索老手/滴酒不漏）；无链 → `独立规划` 桶 | 属性 21 系列+独立28；功能 64 系列+独立98；物资 9 全独立 |

全部 tab 不显示二级（系列跨类混合无意义），只留 状态筛选（全部/可购/未解锁/已激活）+
排序（默认/价格↑↓/解锁日）+ 搜索 三个找卡轴。

角色专属是徽章不是分类（跨分类存在）。游戏更新后分类随表自动重建（规则不变则结果不变）；
规则本身调整时用 `prototype/gen_data.js` 对照复核。

## 5. 配置（cfg `com.local.survivallog.planpick`）

| 项 | 默认 | 说明 |
|---|---|---|
| CostMultiplier | 2 | 自选消耗倍率（**1=原价（无加价）**；读取经 `Multiplier` 统一出口 sanitize：NaN/<1→1、>100→100——`Math.Max(1f,NaN)` 仍是 NaN，`(int)Round(NaN)` 会毒化表行） |
| ShowLocked | true | 未解锁项展示（置灰+原因） |
| ShowUnpooled | true | 不入池规划（图纸/无人机）展示 |
| ListOnReady | true | 首轮列统计（分类计数/候选名单） |
| Verbose | false | 每轮明细 |

## 6. 待装机验证清单（作者实测）

> 操作步骤清单见 **`VERIFY.md`**（11 项，含日志判读/卸载还原）；本节为设计口径核对点，两文互补。

1. Harmony prefix/finalizer 在 il2cpp `OnBuySurvivalPlan(int)` 生效且异常路径恢复原值。
2. 今日候选读取：`SurvivalPlanShownIds` 语义 = 当日展示名单（刷新候选后是否同步更新）。
3. 自选购买实付 = 原价×2（主角折扣档再核一遍）；今日候选内购买原价不受影响。
4. 购买后官方 UI 全刷新（余额/已激活印章）；钉选中的规划被自选购买后钉释放。
5. 边界：11 条不入池规划可购买；29 条 Available=0 不可见；IsReget 重购语义。
6. 卸载还原：无存档残留、表值无残留（断言 finalizer 后 LvUpCost == 原值）。
7. ~~P1~P4 ↔ 角色名对照~~ 已定案（2026-10-24 官方表）：打工仔/大学生/仓库管理员/退伍军人；
   ConstantText 全量（字符串键表）可在装机 ListOnReady 顺带核对是否有更贴切的官方句式可借。
8. 回归夹具：官方三选一全流程（含刷新/钉选/离开）不受注入影响。

## 7. 风险与红线

- 不改任何官方 JS/表文件（全内存 + 注入）；发版走 publish.ps1，进 `$suiteMods` 白名单与安装器
  [Files]/InstallDelete（探针红线无关：本 mod 非探针）。
- 改挂钩点必须同步 `tools/gamekb/moddeps.json`：Harmony 目标 `SurvivalResultsManager.OnBuySurvivalPlan`、
  表字段 `Config_Talent.{Type,Available,LvUpCost,WebIcon,ConditionSetId,PlayerSelectId}`、
  存档字段 `SaveChildData.SurvivalPlanShownIds`、通知 15003、页面 `UI/RogueSelect`。
- **moddeps 登记不了的运行期依赖**（modcompat 不覆盖，游戏更新 diff 时人工核对）：
  `State_UI_RogueSelect.CachedSurvivalPlanIds / Day`（今日候选/天数）、`State_Data_Talent.SurvivalPlanningIds`
  （已激活）、`State_Web_RogueSelect.OwnPoints`（点数 ReactiveProperty）、
  `BattleLogicWorld._ConditionSetManager`（interop 直读内部属性，特殊条件判定）、
  `ReduxUISystem.GetWebUILayer().canvasWebViewPrefab.WebView`（注入通道）、页面 `UI/RogueSelect`
  （Root 内 iframe id='RogueSelect'）、通知 15003（官方购买链上游，本 mod 不 patch 只复用）。
- 版本号作者定（version.json 单源）；v1.4.0 候选物料（`02_候选1_自选每日规划.png`）与本 mod 对应。

## 8. 原型（prototype/）

- `gen_data.js`：从 gamekb 表生成 `plan_data.json`（图标路径改写为 `/Res/...` 供本地预览；
  游戏内实装用原始 WebIcon 相对路径）。
- `index.html`：官方 RogueSelect 视觉同构（复用其 CSS 变量/卡片/页脚），叠加：
  - 顶部"原型调试条"（仅原型有）：模拟天数滑杆 / 生存点 / 当前角色 / 倍率；
  - 页脚"自选规划"按钮 → 弹层：一级分类 tab + 二级筛选 chips（仅分类 tab）+ 状态筛选 + 排序 + 搜索；
  - 官方三卡区为**纯背景复刻**（静态候选样例，不模拟锁定态——mod 对原生随机/锁定零接触）；
  - 状态机：可购 / 点数不足 / 已激活 / 未解锁（置灰+点击弹原因，原因按 §3 优先级组合）；
  - 购买 = 确认弹窗（只显示 原价/自选价/余额，无说明文案）→ 模拟扣点 + 已激活印章
    （原型内模拟，实装走官方链）。
- `serve.js`：`node prototype/serve.js` → `http://127.0.0.1:port/`（/Res 映射 gamekb webui 镜像，
  官方图标直接可用；playwright 禁 file:// 的既定绕法）。

## 9. 防盗署名气泡（套件统一）

`Load()` 末尾 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`；多 mod 去重、文案单源，同 RoleUnlock §6。

## 10. 文件

- `PlanPickPlugin.cs`：cfg + 巡检 Ticker（2s → Injector.Tick）。
- `PlanCatalog.cs`：Type2 规划目录（§4 规则，运行期从表一次性构建并缓存）。
- `PlanState.cs`：Redux 状态读取 + payload 组装（含 JsEscapePublic JSON 转义）。
- `Injector.cs`：Root 巡检注入器（iframe 条件安装 + 签名推送，实装注记见 §11）。
- `CostPatch.cs`：购买翻倍 Harmony prefix/finalizer（§3 第 3 条，内存表临时编辑 + 无条件恢复 + 入口自愈）。
- `page.js`（内嵌资源，资源名 `SurvivalLog.PlanPick.page.js`）：按钮/弹层/交互。
- `tests/`（fixture.html + run_tests.js）：page.js 浏览器回归夹具（58 断言；`node SurvivalLog.PlanPick/tests/run_tests.js`）。
- `VERIFY.md`：装机验证清单（§6，作者执行）。
- `prototype/`：本原型三件（不进发布包，gitignore 产物 json）。
- 依赖：ModShared（BagTable.Cm() + WelcomeTip，csproj 源码并入编译）+ interop `Vuplex.WebView.dll`。

## 11. 迭代维护清单

- 2026-10-24 v1：设计定稿 + 原型首版（作者口径：全展示/置灰/点击原因/翻倍默认×2）。
- 2026-10-24 v2（作者试玩原型三反馈）：
  1. 确认弹窗去掉说明句（只留 原价/自选价/余额 三行）；
  2. 官方三卡区改纯背景复刻、不模拟锁定态；DESIGN §3 增"原生零接触承诺"；
  3. 增二级分类（§4）：家具扩建→区域+专属家具位；其余→前置链系列+独立桶；
     全部 tab 只留 状态筛选/排序/搜索。
- 2026-10-24 v3（作者试玩原型四反馈）：
  1. 二级 chips 改自动换行、默认收起一行、溢出才出"更多 ▾/收起 ▴"展开——**永不出现横向滚动条**；
  2. 排序下拉配色修正（正文 text-main、option 深底浅字，防黑底黑字看不清）；
  3. 列表卡片图标 54→64px；
  4. 入口按钮移至页脚右侧"离开"旁（位置固定，不随生存点数字宽度位移；样式与主按钮并排）。
  注：1/2/4 同时适用于实装 page.js。
- 2026-10-24 v4（作者反馈：筛选联动）：状态筛选（可购/未解锁/已激活）**回写一二级分类计数**——
  一级 tab 与二级 chips 只统计当前状态下的规划；计数 0 的二级 chip 整组隐藏；
  当前选中的二级被筛没时自动回"全部"。实装 page.js 同口径。
- 2026-10-24 v5（作者反馈：锁因文案 + 遗漏盘点）：
  1. 角色锁因"仅限角色N"→"仅限「打工仔」…（官方 Config_PlayerSelect.Name_Local，实装运行时读表）；
     官方无锁因文案可复用（锁定卡只显示"未 解 锁"），原因文案为 mod 自组织；
  2. 名单审计闭环（§2.4）：Available==0 抽取即拒（反汇编 0x0301D36B 字段测试实证），
     465 名单与官方可抽集合零遗漏；调试条角色下拉同步真名并补 P4。
- 2026-10-24 实装（Task 1~5）：骨架/cfg → PlanCatalog → page.js（回归夹具 58 断言）→
  PlanState payload → Injector。注入器实装要点（相对 §3.1 草案的修正）：
  1. `ExecuteJavaScript(string, Action<string>)` 双参重载传 null（interop 实证存在；单参版返回 Task 不便）；
  2. 条件安装：iframe 窗口 `__PlanPickInstalled`（page.js 自置标记）未置才 eval——不每 tick 重装；
  3. 条件推送：payload 签名（长度+哈希）变化才 `__PlanPickBoot`——防弹层打开期间每 2s 重渲染
     （打断搜索输入）；签名标记存页面侧自愈 iframe 重建；跳过条件须 `#ppEntry` 已在 DOM
     （page.js 安装只在 boot 里重试，页面加载期的 boot 不能当"已送达"）；
     4. payload `exclusive` 为布尔（RoleIds.Count>0），page.js 徽章按布尔判断；
     `mult` 输出用 InvariantCulture（小数点逗号区域保 JSON 合法）。
- 2026-10-24 实装收尾（Task 6~7，按 docs/plans/2026-10-24-plankpick.md 子代理执行）：购买翻倍
  CostPatch + moddeps 登记 + §3.1 payload 契约成文 + §4 改运行期构建 + VERIFY.md 装机清单。
  含 Task6 评审携带修复：
  1. prefix 自愈加固——入口发现上次提升未收尾（理论同步重入）先还原该行再继续；
  2. 倍率 sanitize——`PlanPickPlugin.Multiplier` 统一出口（NaN/<1→1、>100→100），
    替换 CostPatch/PlanState 两处裸读（`Math.Max(1f,NaN)`=NaN，`(int)Round(NaN)` 会毒化表行）；
  3. cfg 文案——CostMultiplier 描述"1=免代价"改"1=原价（无加价）"（1 是原价不是免费）。
- 2026-10-24 终审 Minor×5（全分支评审收尾）：
  1. ListOnReady cfg 落地——Injector 首轮目录/候选统计打点受其开关（此前绑了配置但未生效）；
  2. payCost 取整对齐——page.js 自选价 `Math.round(cost×mult)`，与 C# 实扣
     `(int)Math.Round(orig*mult)` 一致（小数倍率下展示=实扣）；
  3. PlanCatalog 构建异常封顶——BuildLog 与上一条相同不再追加（每 2s 重试防无限增长）；
  4. §3 字段名勘误（本条上方）；moddeps 补 ConditionSet.BuffNeed/HistoryCount、
    删未读的 RandomGroup.GroupType。
- 2026-10-24 RoleUnlock 联动（作者定稿）：角色锁判定改读运行时表现值（每轮推 payload 现读
  PlayerSelectId，读失败回退目录原值）——同装 RoleUnlock（互通清功能类 46 张白名单）即自动放行，
  家具位 77 张仍锁（范围跟随 RoleUnlock 表现状）；只装本 mod 照锁；零跨 mod API、无加载顺序依赖。
  VERIFY.md 增 ⑫ 联动验证项。
