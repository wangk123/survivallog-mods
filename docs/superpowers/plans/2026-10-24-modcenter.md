# ModCenter（游戏内 Mod 管理菜单）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新建 SurvivalLog.ModCenter 插件：主菜单与游戏内两阶段可开的 MOD 管理面板，9 个套件 mod 的精简配置页（实时生效）+ 启停（软停+重启级）+ 第三方 mod 三档接入（Tier 0 自动 / Tier 1 声明式 / Tier 2 深度）。

**Architecture:** 三层——JS 注入浮层（官方菜单列表入口，ExecuteJavaScript 双向，不挂 HandleCallback）；C# 配置桥（Chainloader 反射枚举各 mod ConfigFile，写 `ConfigEntry.Value` 触发各 mod 现役 `SettingChanged→Rerun` 热生效链）；元数据表（9 mod 页面定义硬编码，默认值/范围运行时反射读防漂移）。启停 = 各 mod 新增 `General.Enabled` gate（Ticker 首行 + Load 早退）。

**Tech Stack:** BepInEx 6 IL2CPP + HarmonyX + Il2CppInterop（互操作程序集引用游戏目录）；JS 注入复用 PlanPick/CabinetEverywhere 既有模式；CDP 9222 做白盒验证。

**Spec:** `docs/superpowers/specs/2026-10-24-modcenter-design.md`（v4 定稿）+ `docs/modcenter-integration-guide.md`（第三方协议）。执行者开工前先读这两份。

## Global Constraints

- 构建一律 `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build <目录> -c Release > build.log 2>&1; echo $?` 判退出码，**禁止管道**；构建失败禁止部署（会拿旧 dll 骨人）。
- 提交外科手术式：`git add <明确文件清单>`，**禁止 `git add -A`/`git add .`**；`tools/publish.ps1`、`installer/installer.iss` 是作者已脏文件——只改内容**不 stage**（随作者自己的提交走）。
- 版本号只由作者经 version.json 定（AGENTS 硬规则 1）；csproj/BepInPlugin 版本字段照抄套件现有 mod 的流水线写法。
- 游戏在跑（SurvivalLog.exe 且内存活跃）时整体中止择时重试；杀游戏后 `sleep 3` 回读 tasklist 确认死透再动文件；启动用 `powershell Start-Process 'steam://rungameid/4164790'`。
- UI 改动必须浏览器/实机断言通过才能报"完成"；同题两轮不过换道。
- 改 mod 必须同步该 mod 的 `DESIGN.md` 迭代清单；本计划无新增 Harmony 挂钩点/表字段/字节签名，`tools/gamekb/moddeps.json` 不动（Task 19 复核）。
- panel.js：无外部资源、幂等、页面侧 `__McInstalled` 守卫、全部元素 `__mc` 前缀、不碰官方 tabs 数组（只藏/加 DOM）。
- 不挂 `WebUIVm.HandleCallback`；回调内不改游戏数据。
- 互操作程序集引用路径照抄 `SurvivalLog.CabinetEverywhere/SurvivalLog.CabinetEverywhere.csproj:27-68`（游戏目录 BepInEx + interop）。
- 发版提醒（作者侧）：ViewportProbe 的 9222 cfg 发布前关闭。
- 所有验证脚本（node/ps1）遵守仓库编码规则：ps1 ASCII-only；node 复杂片段写 .js 文件执行。

---

### Task 1: 菜单锚点离线定位（无游戏，webui_src 静态分析）

**Files:**
- Create: `tools/webui_src/ANCHORS-MODCENTER.md`（锚点结论，后续任务引用）
- Read: `tools/webui_src/UI/MainUI/MainUI.html`、`gamekb/2026-10-23_26157732/webui/Root.html`

**Interfaces:**
- Produces: 主菜单入口锚点选择器（如 `#iframe-container` 下 MainUI iframe 内的菜单列表项容器选择器）+ 游戏内系统菜单页面名与列表锚点选择器，或"无稳定锚点→降级右上角按钮"结论。Task 5 消费。

- [ ] **Step 1: 定位主菜单菜单列表 DOM**
  读 `tools/webui_src/UI/MainUI/MainUI.html`，找到"继续游戏/新游戏/设置/退出"按钮的容器与类名，记录：iframe 页面名（MainUI）、容器选择器、按钮元素标签/类、appendChild 目标。
- [ ] **Step 2: 定位游戏内系统菜单（ESC 菜单）**
  在 `tools/webui_src/UI/` 目录清单中找暂停/系统菜单页（grep 关键词：`暂停`、`继续游戏`、`设置`、`PauseMenu`、`SystemMenu`、`EscMenu`）。找到则记录页面名+列表锚点；找不到或结构不稳定 → 结论"降级右上角按钮"（spec §4.1 允许）。
- [ ] **Step 3: 记录 Root 注入的 iframe 索引规律**
  从 Root.html 与 docs/06 页面清单（91 页）确认：iframe id/name 与页面名的对应方式（ModCenter 的引导脚本需要按页面名找 iframe document——照抄 TagFilter 引导脚本对 iframe 的遍历手法，TagFilter.cs:78-101 的 Probe js 片段）。
- [ ] **Step 4: 写结论文档并提交**
  `ANCHORS-MODCENTER.md` 写清：主菜单锚点（页面名/选择器/追加方式）、游戏内锚点或降级结论、iframe 定位手法。`git add tools/webui_src/ANCHORS-MODCENTER.md && git commit -m "docs(modcenter): 菜单锚点离线定位结论"`

### Task 2: 主菜单阶段 WebView 可达性实机实证

**Files:**
- Create: `tools/_probe_menu_cdp.js`（CDP 断言脚本，完成后保留入库供回归）
- 前提：ViewportProbe 已按 docs/06 部署且 cfg 开 9222；作者机器可启动游戏。

**Interfaces:**
- Produces: GO/NO-GO 结论写入 spec §9.1（GO=主菜单可注入；NO-GO=入口仅游戏内+右上角降级，后续 Task 5 按降级走）。

- [ ] **Step 1: 确认游戏未运行后启动（不读档，停在主菜单）**
  `tasklist` 查 SurvivalLog.exe；无则 `powershell Start-Process 'steam://rungameid/4164790'`；等约 55s 后 9222 可用（docs/06:35）。
- [ ] **Step 2: 写 CDP 断言脚本**
  `tools/_probe_menu_cdp.js`（node，CDP WebSocket 直连 9222，模式照 `tools/devtools_cdp_test.js`）：
  1. `Target.getTargets` 找到页面 target（MainUI 所在 WebView）；
  2. `Runtime.evaluate` 执行：读 `#iframe-container` 是否存在、能否取到 MainUI iframe 的 document；
  3. 模拟 Task 5 将要做的注入：往主菜单列表 appendChild 一个 `<span id="__mc_probe">MOD 管理</span>`，再 querySelector 验证存在，然后移除；
  4. 输出 PASS/FAIL 各项。
- [ ] **Step 3: 运行并记录结论**
  `node tools/_probe_menu_cdp.js`。全 PASS → spec §9.1 更新为"已实证"；任一 FAIL → 记录现象，入口按降级方案（游戏内右上角按钮，主菜单不注），同步 spec。**此结论直接决定 Task 5 形态，NO-GO 不算失败任务。**
- [ ] **Step 4: 提交**
  `git add tools/_probe_menu_cdp.js docs/superpowers/specs/2026-10-24-modcenter-design.md && git commit -m "chore(modcenter): 主菜单WebView可达性实证脚本与结论"`

### Task 3: ModCenter 项目骨架

**Files:**
- Create: `SurvivalLog.ModCenter/SurvivalLog.ModCenter.csproj`（抄 CabinetEverywhere.csproj:27-68 引用结构）
- Create: `SurvivalLog.ModCenter/ModCenterPlugin.cs`
- Create: `SurvivalLog.ModCenter/DESIGN.md`（首版骨架）

**Interfaces:**
- Produces: `ModCenterPlugin : BasePlugin`，`[BepInPlugin]` 照套件版本流水线写法；`Ticker` MonoBehaviour（每 2s，unscaled）；`Log(string)` 静态日志门。后续全部任务在此项目内扩展。

- [ ] **Step 1: 建 csproj**
  复制 `SurvivalLog.CabinetEverywhere/SurvivalLog.CabinetEverywhere.csproj` 改名改 Guid；加 `<EmbeddedResource Include="panel.js" />`（panel.js 先放空占位文件 `/* Task 7 移植 */`）；链接 `ModShared/WelcomeTip.cs`（publish.ps1:58-70 要求套件 mod 带 WelcomeTip，ModCenter 进套件后同样适用），Load 里调用 `WelcomeTip.Install(...)`（照抄 CabinetExpandPlugin 的调用形态）。
- [ ] **Step 2: 写插件类**
  ```csharp
  [BepInPlugin("survivallog.modcenter", "SurvivalLog ModCenter", "1.0.0")] // 版本字段随流水线统一，照套件现有写法
  public class ModCenterPlugin : BasePlugin
  {
      public static ModCenterPlugin Instance { get; private set; }
      public override void Load()
      {
          Instance = this;
          WelcomeTip.Install(Log, "MOD 管理中心");   // 署名气泡，参数照抄套件调用形态
          var go = new GameObject("ModCenterTicker");
          UnityEngine.Object.DontDestroyOnLoad(go);
          go.AddComponent<Ticker>();
          Log("loaded");
      }
      public static void Log(string msg) => ManualLogSource?.LogMessage(msg); // 按实际 API 写
  }
  public class Ticker : Il2CppBehaviourSkeleton // 照抄 BackpackExpandPlugin.cs:60-64 的 ClassInjector.RegisterTypeInIl2Cpp 模式
  {
      void Update() { /* Task 5 起填：注入巡检 + outbox 轮询 */ }
  }
  ```
  （IL2CPP MonoBehaviour 注册照抄 `BackpackExpandPlugin.cs:60-64` 的 `ClassInjector.RegisterTypeInIl2Cpp<T>()` + DontDestroyOnLoad 模式；计时用 `Time.unscaledTime`。）
- [ ] **Step 3: 构建判退出码**
  `"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.ModCenter -c Release > build.log 2>&1; echo $?` → 期望 `0`。
- [ ] **Step 4: 部署与自证**
  确认游戏未运行 → `cp SurvivalLog.ModCenter/bin/Release/net6.0/SurvivalLog.ModCenter.dll "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/"`（产物路径按 bin 实际层级调整）→ 启动游戏 → BepInEx LogOutput 或自证文件确认 "ModCenter loaded"（日志强杀不落盘就用 trace 文件：Load 里写 `BepInEx/ModCenter_loaded.txt`）。
- [ ] **Step 5: DESIGN.md 首版 + 提交**
  DESIGN.md 记：定位、三层架构、迭代清单（空）。`git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): 项目骨架（build/部署/自证通过）"`

### Task 4: 反射桥 API 实证（配置桥前置）

**Files:**
- Modify: `SurvivalLog.ModCenter/ModCenterPlugin.cs`（临时探针代码，cfg `Debug.Probe` 门控，验证后保留为诊断功能）
- Test: `tools/_probe_bridge_cdp.js` 或直接看游戏内效果

**Interfaces:**
- Produces: 验证过的反射链 API 签名（写入 DESIGN.md）：枚举插件 → 取 `ConfigFile` → 读 entry → 写 `Value` → 目标 mod `SettingChanged` 触发。Task 6 正式化消费。

- [ ] **Step 1: 写探针（以 RobotSpeed 为靶）**
  ```csharp
  // Debug.Probe cfg 默认 false；true 时 Load 后延时 30s 执行一轮：
  var plugins = BepInEx.Bootstrap.Chainloader.Instance.Plugins; // 若无此属性，候选：
  //   BepInEx.Unity.IL2CPP.Chainloader.PluginInfos (Dictionary<string, PluginInfo>)
  var target = plugins.Values.First(p => p.Metadata.Name.Contains("RobotSpeed"));
  var inst = target.Instance as BepInEx.BasePlugin;
  var cfgFile = (ConfigFile)AccessTools.Property(typeof(BasePlugin), "Config").GetValue(inst); // 可见性候选：直接强类型 .Config / AccessTools.Field
  if (cfgFile.TryGetValue(new ConfigDefinition("机器人提速", "移速倍率"), out ConfigEntry<float> entry))
  {
      ModCenterPlugin.Log($"probe read: {entry.Value}");     // 期望 2
      entry.Value = 3f;                                       // 写内存值
      ModCenterPlugin.Log($"probe wrote 3; WaitingChanged..."); // 期望 RobotSpeed 5s 内重写表（其 Rerun 生效）
  }
  ```
  在游戏内开机器人面板或读表验证移速确实变了（RobotSpeed Ticker 0.5s 周期，RobotSpeedPlugin.cs:132）。**API 名与真实签名不符时以反编译/源码为准修正，结论记 DESIGN.md。**
- [ ] **Step 2: 构建部署启动，抓探针输出**
  构建（判退出码）→ 部署（游戏未运行时）→ cfg 手工加 `Debug.Probe = true` → 启动进档 → 日志/trace 验证三行 probe 输出且移速倍率实际变为 3。完成后 cfg 改回 false。
- [ ] **Step 3: 结论固化 + 提交**
  DESIGN.md 追加"反射桥 API 实证记录"节（确切属性路径/转型写法/注意点）。`git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): 反射桥API实证（Chainloader→ConfigFile→Value写→热生效）"`

### Task 5: 注入骨架 + 官方菜单列表入口（两阶段）

**Files:**
- Create: `SurvivalLog.ModCenter/WebBridge.cs`（WebView 获取 + 巡检 + 自愈 + outbox 轮询）
- Create: `SurvivalLog.ModCenter/MenuEntryInjector.cs`（入口 JS）
- Modify: `SurvivalLog.ModCenter/ModCenterPlugin.cs`（Ticker 接线）
- Test: CDP 断言（主菜单与游戏内各一轮）

**Interfaces:**
- Consumes: Task 1 锚点结论、Task 2 GO/NO-GO。
- Produces: `WebBridge.GetWebView()`（抄 TagFilter.cs:64-77 原样）；`WebBridge.Eval(js, Action<string> cb)`（`DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>` 包装，抄 CabinetEverywhere DESIGN.md 附录 C5）；`Ticker` 每 2s：`EnsureInjected()`（入口+面板引导脚本幂等安装）+ `DrainOutbox()`（`ExecuteJavaScript("JSON.stringify(window.__mcDrain?__mcDrain():[])", cb)` 收 JS 侧待发消息，0.3s 周期可后调）。JS 侧全局：`__McInstalled`、`__mcOutbox`、`__mcDrain()`。

- [ ] **Step 1: WebBridge.GetWebView + Eval**
  原样移植 TagFilter.cs:64-77（命名空间 `GameCore.HotUpdate.BaseSingleton<ReduxUI.ReduxUISystem>`）；Eval 包 try/catch 异常只 Warn 不中断（照 TagFilter.cs:253-257 模式）。
- [ ] **Step 2: 入口注入脚本（按 Task 1 锚点）**
  引导脚本（注进 Root 上下文，幂等）：找目标 iframe（主菜单 MainUI / 游戏内系统菜单页或降级右上角按钮），往锚点 appendChild `<button id="__mc_entry">MOD 管理</button>`，样式克隆官方菜单项 + accent 边框（克隆手法照 PlanPick page.js:286,332-335 的 footer-right appendChild 先例；右上角降级样式用 fixed 小按钮）。iframe 重建自愈：脚本每次 EnsureInjected 无条件重跑（TagFilter.cs:46-50 免维护模式），`__mc_entry` 已在 DOM 则跳过。
  ```javascript
  // MenuEntryInjector 生成（示意骨架，选择器来自 Task 1 结论常量）
  (function(){
    var f = document.querySelector('#iframe-container iframe[name="<页面名>"]');
    if(!f || !f.contentDocument) return 0;
    var doc = f.contentDocument;
    if(doc.getElementById('__mc_entry')) return 1;
    var host = doc.querySelector('<锚点选择器>'); if(!host) return 0;
    var b = doc.createElement('button'); b.id='__mc_entry'; b.textContent='MOD 管理';
    /* 克隆官方样式类或内联样式 */ host.appendChild(b);
    b.addEventListener('click', function(){ window.__mcOpen(); });
    return 2;
  })()
  ```
- [ ] **Step 3: 面板开骨架（空壳）**
  panel.js 空壳版：`window.__mcOpen = function(){ /* Task 7 完整实现；现在先弹一个全屏遮罩+标题+关闭钮 */ }`；`window.__mcOutbox=[]; window.__mcDrain=function(){var m=window.__mcOutbox.splice(0);return m||[]}`。点击入口 → 遮罩出现；关闭钮/ESC → 消失。outbox 先发 `{type:'MC_OPEN'}` / `{type:'MC_CLOSE'}`（C# 侧 Task 8 消费做键盘门）。
- [ ] **Step 4: Ticker 接线**
  每 2s `EnsureInjected()`（入口）+ `DrainOutbox()`；收到消息暂只 Log。
- [ ] **Step 5: 构建部署，两阶段 CDP 实测**
  主菜单阶段：入口出现在菜单列表、点击出现面板、关闭正常（CDP `Runtime.evaluate` 断言 `#__mc_entry` 与面板根节点存在性）。游戏内阶段（读档后）：同断言；若 Task 2 NO-GO 则只测游戏内+降级按钮。
- [ ] **Step 6: DESIGN.md 迭代 + 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): 注入骨架+菜单列表入口两阶段实测通过"`

### Task 6: 配置桥正式化（快照/写/协议/元数据表）

**Files:**
- Create: `SurvivalLog.ModCenter/ConfigBridge.cs`
- Create: `SurvivalLog.ModCenter/Meta.cs`
- Create: `SurvivalLog.ModCenter/Payload.cs`（DTO）
- Modify: `SurvivalLog.ModCenter/WebBridge.cs`（消息分发）

**Interfaces:**
- Consumes: Task 4 验证过的反射 API。
- Produces（后续任务的契约）：
  - `Snapshot BuildSnapshot()`：`{self:{ver}, mods:[ModInfo], tp:[ModInfo]}`，`ModInfo = {guid, name, en, desc, enabled, groups:[{title, items:[Item]}]}`，`Item = {k, l, t, v, min?, max?, st?, b, note?, opts?}`（与原型 v4 数据结构一一对应）；
  - `bool TrySet(guid, section, key, value)`：写 `ConfigEntry.Value`（联动项写宽高两键等值，Meta 中声明 `writeKeys:[...]`）；返回是否成功；
  - `void SetModEnabled(guid, bool)`：Task 13 接 `General.Enabled`，本任务先落 marker（ModCenter cfg 区段 `Disabled` 记名单）供 UI 演进；
  - Save 节流：`_saveDirty` 标志 + 3s 定时器 + MC_CLOSE 时立即 `ConfigFile.Save()`。
  - 消息协议（spec §4.3）：入 `MC_OPEN`/`MC_SET`/`MC_TOGGLE_MOD`；出快照推送 `__mcRender(json)`。
  - `Meta`：9 mod 定义（**从 spec §7 v4 表逐行转写**：分组/中文label/徽标/writeKeys/说明；默认值与范围不写死——快照时反射 `ConfigEntry.DefaultValue`/`SettingDescription.AcceptableValueRange` 合入 Item）。

- [ ] **Step 1: Payload DTO + Meta 表转写**
  按 spec §7 九张表写 `Meta.cs`（每 mod 一段初始化器，字段与原型 v4 的 MODS 数组同构；`柜子直达→更好的储物柜` 显示名；QuickAction 含 `ovrParser`：`其他.Overrides` 字符串 ↔ `[{id,ms}]` 解析/序列化，格式 `id:ms,id:ms`）。
- [ ] **Step 2: ConfigBridge 实现**
  枚举（Task 4 API）→ 套件 mod 按 Meta 渲染、未登记插件本任务先跳过（Tier 0 在 Task 14）→ 快照 JSON（`JsonConvert` 或手写拼接，与套件现有 JSON 用法一致）。`TrySet`：定位 entry → 类型转换（float/int/bool/string/enum）→ 写 Value（联动 writeKeys 逐个写）→ 标脏。
- [ ] **Step 3: 消息分发接线**
  WebBridge 收到 outbox 消息：`MC_OPEN`→`__mcRender(BuildSnapshot())`；`MC_SET`→TrySet→回 `{type:'MC_SET_OK', k, badge}`；`MC_TOGGLE_MOD`→SetModEnabled→回新快照。
- [ ] **Step 4: 构建部署实弹断言（热生效核心验收）**
  游戏内打开面板 → CDP 侧模拟（或直接用面板 UI，Task 7 前可先用 CDP evaluate 调 `__mcRender` 后检查）→ 改 `RobotSpeed.移速倍率` 为 4 → CDP 断言 0.5~5s 内游戏表值变化（读 `Config_RobotAttribute` 内存表或观察移速）。**这是"实时生效"主张的装机证据，必须落日志记录。**
- [ ] **Step 5: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): 配置桥+元数据表+消息协议，RobotSpeed改值热生效实测"`

### Task 7: panel.js 完整移植（左列+配置页+控件+改值回传）

**Files:**
- Modify: `SurvivalLog.ModCenter/panel.js`（从原型移植，替换数据源）
- Test: CDP 断言脚本 `tools/_mc_fixture.js`（回归夹具雏形，Task 18 扩全量）

**Interfaces:**
- Consumes: Task 6 快照协议。
- Produces: 完整面板 UI（与原型 v4 视觉/交互一致）：左列（9+自身+第三方分组、状态点、启停开关=唯一启停入口）、右区（标题/简介/停用横幅/分组配置项）、四类控件+覆盖清单、徽标、toast、底部图例。

- [ ] **Step 1: 移植骨架**
  原型 `docs/superpowers/specs/2026-10-24-modcenter-prototype.html` 的 `<style>` 与面板 DOM/JS 逻辑迁入 panel.js：`renderSide/renderCfg/ctl/toast` 保持结构，数据源 `MODS/TP` 常量替换为 `__mcState`（`__mcRender(json)` 写入）；场景模拟代码（主菜单/游戏内背景、demo-bar）**不迁**。
- [ ] **Step 2: 交互改走协议**
  改值 → `__mcOutbox.push({type:'MC_SET',...})`；启停 → `MC_TOGGLE_MOD`；打开 → `MC_OPEN`；收到 `__mcRender` 全量重绘（JS 无 diff 全量重建，照官方模式）。覆盖清单增删改 → 本地编辑 + change 时整串 `MC_SET`（`其他.Overrides` 序列化回写）。
- [ ] **Step 3: 构建部署，CDP 全页断言**
  逐 mod 断言：9 页渲染项数与 Meta 对账；滑条/开关/下拉/覆盖清单（新增/拖动/删除）操作 → outbox 消息形态正确 → C# 回执 → 值落 cfg（`BepInEx/config/*.cfg` 文件断言，等 Save 节流或关闭面板后读文件）。
- [ ] **Step 4: 提交**
  `git add SurvivalLog.ModCenter tools/_mc_fixture.js && git commit -m "feat(modcenter): panel.js完整移植+CDP断言绿"`

### Task 8: FocusTick 键盘复用 + 输入体验

**Files:**
- Create: `SurvivalLog.ModCenter/FocusTick.cs`
- Modify: `SurvivalLog.ModCenter/WebBridge.cs`、`panel.js`（输入框 focus 状态上报）

**Interfaces:**
- Consumes: PlanPick Injector.cs:100-140 定案机制（`WebUILayer.SetKeyboardEnabled(true)` + `UnityEngine.Input.imeCompositionMode = IMECompositionMode.On`；dump.cs:250153/250156 API）。
- Produces: 面板打开期间 C# 收 `MC_OPEN` 开键盘、`MC_CLOSE` 双还原；数字输入（覆盖 ID）可键入；IME On 使文本输入可用（本面板仅数字/英文输入，但机制全量复用）。

- [ ] **Step 1: 移植 FocusTick**
  照抄 PlanPick Injector.cs:100-140：`GetWebUILayer()` → `SetKeyboardEnabled(bool)` + `imeCompositionMode` 设置/还原；门控：面板 open 状态（由 MC_OPEN/MC_CLOSE 驱动）。
- [ ] **Step 2: panel.js 上报 open/close**
  `__mcOpen` push `MC_OPEN`；关闭钮/ESC/遮罩点击 push `MC_CLOSE`（并立即本地关面板）。
- [ ] **Step 3: 实测**
  游戏内：开面板 → 覆盖 ID 输入框键入数字正常、游戏热键不触发；关面板 → 游戏键盘恢复（试官方快捷键）。主菜单阶段若 Task 2 GO，同测一轮。
- [ ] **Step 4: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): FocusTick键盘门复用（开面板解锁/关面板还原）"`

### Task 9: 启停 gate——纯表改写三件套（BackpackExpand/QuickAction/RobotSpeed）

**Files:**
- Modify: `SurvivalLog.BackpackExpand/BackpackExpandPlugin.cs`、`SurvivalLog.QuickAction/QuickActionPlugin.cs`、`SurvivalLog.RobotSpeed/RobotSpeedPlugin.cs`
- Modify: 各自 `DESIGN.md`

**Interfaces:**
- Produces（三 mod 同模板，后续任务复用）：cfg `General.Enabled`（bool，默认 true，Bind 放最前）；Ticker 首行 `if (!Enabled.Value) { RestoreOnce(); return; }`；`Load()` 在 Bind 完成后立即 `if (!Enabled.Value) return;`（保证 cfg 键已生成再早退）。`RestoreOnce()`：把已改写表值回写进程内原值缓存（各 mod 现役"mult=1/0=还原"路径复用——原值缓存字段已存在：BackpackExpand BagTableRw、QuickActionPlugin.cs:211/477-486、RobotSpeedPlugin.cs:112-119）。

- [ ] **Step 1: 三 mod 各加 gate（每处约 10 行）**
  以 RobotSpeed 为模板：
  ```csharp
  Enabled = Config.Bind("General", "Enabled", true, "总开关：false=还原官方原值并停写；下次启动不加载。");
  // RobotTicker 轮询体首行：
  if (!Enabled.Value) { if (_applied) { RestoreAll(); _applied = false; } return; }
  _applied = true; // 后接现役幂等重写
  ```
  `RestoreAll()` 用现役原值缓存把 5 字段/动作/货舱回写原值（能还原的都还原；说明注释标注 Burden 类快照残留边界）。
- [ ] **Step 2: 构建三个项目（判退出码）+ 部署**
- [ ] **Step 3: 实测每 mod**
  cfg 手改 `General.Enabled=false` → 进档 → 行为还原（机器人移速回官方/动作耗时回原值/背包尺寸回原值）；`=true` 恢复；重启后 false 时该 mod 完全无日志。
- [ ] **Step 4: 三份 DESIGN.md 迭代清单 + 提交**
  `git add SurvivalLog.BackpackExpand SurvivalLog.QuickAction SurvivalLog.RobotSpeed && git commit -m "feat(gate): 纯表改写三mod加General.Enabled启停门（还原实测）"`

### Task 10: 启停 gate——CabinetExpand 与 FridgeChill（含早退还原补丁）

**Files:**
- Modify: `SurvivalLog.CabinetExpand/CabinetExpandPlugin.cs`（gate）+ `ViewportFit.cs`（Enabled false 时 Active 强制 false）
- Modify: `SurvivalLog.FridgeChill/FridgeChillPlugin.cs`（gate + **早退还原补丁**：`:99` 现状 `KeepMult≤1.01` 早退不回写 ColdRate——改为早退时也执行一次原值回写；禁用时 scratch 写 0.1，走现成 `UpdateFloor` 逻辑 :270-289）
- Modify: 两份 `DESIGN.md`

**Interfaces:**
- Consumes: Task 9 模板。

- [ ] **Step 1: 两 mod gate + FridgeChill 补丁**
  FridgeChill 关键改动（示意，落位以现文件为准）：
  ```csharp
  if (!Enabled.Value) { RestoreColdRates(); UpdateFloor(0.1f); /* scratch 回原生 */ return; }
  if (KeepMult.Value <= 1.01f) { RestoreColdRatesOnce(); return; } // 补丁点：早退也还原
  ```
- [ ] **Step 2: 构建+部署**
- [ ] **Step 3: 实测**
  关 CabinetExpand → 柜子尺寸/视口脚本停；关 FridgeChill → 保鲜回官方（天赋上限回原生 0.1 语义）；重开恢复。
- [ ] **Step 4: DESIGN.md×2 + 提交**
  `git add SurvivalLog.CabinetExpand SurvivalLog.FridgeChill && git commit -m "feat(gate): CabinetExpand/FridgeChill启停门+FridgeChill早退还原补丁"`

### Task 11: 启停 gate——CabinetEverywhere（白名单对称移除补丁）与 RoleUnlock（两新开关）

**Files:**
- Modify: `SurvivalLog.CabinetEverywhere/CabinetEverywherePlugin.cs`（gate）、`CraftSourceInjector.cs:31-45`（**对称 Remove**：保留 `_added` 集合，禁用/EnableCraftSource=false 时从 `CraftSourceFurnitureIds` 移除 `_added` 全部）、`RefreshCoalescer.cs:49-55`（prefix 首行 `if (!Enabled) return true;` 放行）
- Modify: `SurvivalLog.RoleUnlock/RoleUnlockPlugin.cs`（gate + 新 cfg `Plan.ClearPool`/`Plan.VaseRecipe`（默认 true），套用 :306-325 现役还原模式；禁用即还原官方白名单/配方等级）
- Modify: 两份 `DESIGN.md`

**Interfaces:**
- Consumes: Task 9 模板。

- [ ] **Step 1: CabinetEverywhere gate + Remove 补丁**
  ```csharp
  // CraftSourceInjector 增加：
  public static void RemoveAll(){
      var set = ToolCabinetConfig.CraftSourceFurnitureIds;
      foreach (var id in _added) set.Remove(id);
      _added.Clear();
  }
  // 禁用路径调用 RemoveAll()；已打开界面重开后收敛（原生重建页签）
  ```
- [ ] **Step 2: RoleUnlock gate + 两开关**（照 :306-325 allow-list 还原手法）
- [ ] **Step 3: 构建+部署+实测**
  关更好的储物柜 → 制作台不再显示柜子取料（重开界面断言）；关 RoleUnlock → 生存规划池/花瓶配方回官方。
- [ ] **Step 4: DESIGN.md×2 + 提交**
  `git add SurvivalLog.CabinetEverywhere SurvivalLog.RoleUnlock && git commit -m "feat(gate): 更好的储物柜白名单对称移除+RoleUnlock两开关"`

### Task 12: 启停 gate——PlanPick 入口接线（PreDisasterSpeed 零改动）

**Files:**
- Modify: `SurvivalLog.PlanPick/PlanPickPlugin.cs`（cfg `General.Enabled` 复用其现有开关语义或新增，注入器首行检查，禁用=藏入口按钮+FocusTick 停）
- Modify: `SurvivalLog.PlanPick/DESIGN.md`

**Interfaces:**
- Consumes: Task 9 模板（注入型变体：gate 在 Injector 巡检首行，禁用时移除 `#ppEntry`）。
- 注：PreDisasterSpeed 已有 `General.Enabled`（PreDisasterSpeedPlugin.cs:30），**零代码改动**，仅 Task 6 的 Meta 把面板开关映射到它。

- [ ] **Step 1: PlanPick gate**（Injector 巡检首行 `if (!Enabled.Value) { RemoveEntry(); return; }`）
- [ ] **Step 2: 构建+部署+实测**（入口出现/消失）
- [ ] **Step 3: DESIGN.md + 提交**
  `git add SurvivalLog.PlanPick && git commit -m "feat(gate): PlanPick入口启停门"`

### Task 13: 启停 UI 接线（左列开关 → General.Enabled → 停用横幅）

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（`SetModEnabled` 实装：写目标 mod `General.Enabled` Value → 其 Rerun 立即跑还原路径；Save 节流）
- Modify: `SurvivalLog.ModCenter/panel.js`（左列开关 → `MC_TOGGLE_MOD`；禁用页横幅文案=套件两层语义/第三方重启级语义，同原型 v4）

**Interfaces:**
- Consumes: Task 9-12 的各 mod `General.Enabled`。

- [ ] **Step 1: SetModEnabled 实装**（反射定位目标 mod 的 `General/Enabled` entry 写值；找不到（PlanPick 键名差异）按 Meta 里声明的 enabledKey 取）
- [ ] **Step 2: 构建+部署+装机实测**
  面板关 RobotSpeed → ≤5s 行为还原（移速回官方）；侧栏行置灰+横幅出现；重新启用恢复。PDS 开关 → 注入 UI 消失。逐 mod 过一遍。
- [ ] **Step 3: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): 启停接线全9mod装机实测"`

### Task 14: Tier 0 第三方通用页

**Files:**
- Modify: `SurvivalLog.ModCenter/ConfigBridge.cs`（枚举未登记插件 → 通用页构建：section 分组、entry 类型→控件映射、Description 副文本、AcceptableValueRange 滑条、缺省徽标 `cfg`）
- Modify: `SurvivalLog.ModCenter/panel.js`（第三方分组渲染，复用同组件）

**Interfaces:**
- Produces: 快照 `tp:[ModInfo]` 填充（Task 6 预留）；无 cfg 插件 → 仅启停形态（同更好的储物柜页）。启停走 Task 16 重启级（本任务 UI 横幅文案先按重启级）。

- [ ] **Step 1: 通用页构建**（含豁免名单：ActionProbe/ViewportProbe/ConfigDumpProbe/ModCenter 自身）
- [ ] **Step 2: 构建+部署实测**
  临时部署一个非套件 dll（用 ViewportProbe 已在装的现状即可作靶）→ 面板出现其通用页（它有 cfg `DevTools.Enabled` 等）→ 开关与文本项渲染正确。
- [ ] **Step 3: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): Tier0第三方通用页"`

### Task 15: Tier 1 声明式清单（modcenter.json）

**Files:**
- Modify: `SurvivalLog.ModCenter/ThirdParty.cs`（新文件：`asm.GetManifestResourceStream("modcenter.json")` 读取 → 反序列化 → 合并进通用页渲染（label/badge/min-max-step/order 覆盖））
- Create: `SurvivalLog.ModCenter/modcenter.schema.md` 或并入指南（字段表已在指南 §3，代码侧加 JSON 校验容错：键不存在项自动隐藏、mcSpec 不识别降级 Tier 0）

**Interfaces:**
- Consumes: 指南 §3 schema（`mcSpec/id/display/summary/softDisable/groups[].items[]`）。

- [ ] **Step 1: 清单解析与合并**
- [ ] **Step 2: 自测靶**
  做一个 5 分钟的假第三方 dll（复用 ViewportProbe 源码副本改 Guid+内嵌 modcenter.json）→ 部署 → 面板显示定制中文页；清单键故意写一个不存在的 cfg 键 → 该项隐藏不报错。
- [ ] **Step 3: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): Tier1声明式清单解析"`

### Task 16: Tier 2 Contracts + 重启级 dll 禁用

**Files:**
- Create: `SurvivalLog.ModCenter/Contracts/ModCenterIntegration.cs`（指南 §4.1 接口源码编入；**发现机制=反射按类型名** `asm.GetType("ModCenterIntegration")` + `GetMethod("OnEnabledChanged").Invoke(...)`——不同程序集接口类型不可强转，必须 duck-typing，禁止 cast）
- Create: `SurvivalLog.ModCenter/DllDisabler.cs`（marker 存 ModCenter cfg 区段 `DisabledTp`；**退出执行**：启动分离进程 `powershell -NoProfile -Command "..."`（ASCII-only，循环 Rename-Item 重试直到成功——运行中 dll 被锁，OnApplicationQuit 时仍锁，靠分离进程等进程退出后改名）；
  **残留检测**：Load 时 marker 与 dll 状态不一致 → 再拉一次分离进程 + 下次面板打开 toast 提示）
- Modify: `ConfigBridge.cs`（SetModEnabled 分流：套件/softDisable/Tier2 回调→软停；否则走 marker+禁用）

**Interfaces:**
- Consumes: 指南 §4/§5。
- Produces: 第三方启停闭环（软停回调 or 重启级）。

- [ ] **Step 1: Contracts 发现与回调调用**（OnEnabledChanged 返回 false 或类型缺失 → 降级重启级）
- [ ] **Step 2: DllDisabler**（`Paths.PluginPath` 定位；`.disabled` 后缀；启用=反向改名同样分离进程处理——启用时 dll 未加载不锁，直接改名即可，无需分离进程）
- [ ] **Step 3: 实测**
  假第三方靶（Tier 2 版）：停用 → 回调触发行为停止 + 标记；退出游戏 → 等分离进程完成 → 确认 dll 已 `.disabled`；重启 → 靶未加载；面板启用 → 改回 → 再重启恢复。强杀路径：任务管理器杀游戏 → dll 未改名 → 再启动 ModCenter 检出残留并补拉分离进程。
- [ ] **Step 4: 提交**
  `git add SurvivalLog.ModCenter && git commit -m "feat(modcenter): Tier2回调+重启级dll禁用（含强杀残留自愈）"`

### Task 17: 回归夹具全量 + 装机验收清单

**Files:**
- Create: `tools/mc_fixture.js`（正式回归夹具，扩 `tools/_mc_fixture.js` 为全量：两阶段入口、9+第三方页对账、默认值与 cfg Bind 一致性反射断言、改值热生效、启停还原、互扰检查 `__mc` 与 `#ppEntry`/`__Pds` 共存）

**Interfaces:**
- Consumes: spec §10 全部断言项。

- [ ] **Step 1: 写全量夹具**（每断言输出 PASS/FAIL 汇总，exit code 反映结果）
- [ ] **Step 2: 两个游戏会话跑绿**（主菜单会话 + 读档会话）
- [ ] **Step 3: 陪作者过装机验收清单**（spec §10 七项：主菜单改柜子扩容倍率读档生效/游戏内 5s 生效/停 RobotSpeed/重启不加载/复启用恢复/覆盖清单增删/键盘正常）
- [ ] **Step 4: 提交**
  `git add tools/mc_fixture.js && git commit -m "test(modcenter): 回归夹具全量绿"`

### Task 18: 文档同步与发版接线

**Files:**
- Modify: `SurvivalLog.ModCenter/DESIGN.md`（终版：架构/协议/第三方接入/迭代清单）
- Modify: 9 个 mod 的 `DESIGN.md`（Task 9-12 已逐个更新，此处终审补漏）
- Modify(不提交): `tools/publish.ps1:17`（`$suiteMods` 加 `'ModCenter'`）、`installer/installer.iss`（组件表加 ModCenter；[Files]/InstallDelete 对齐）——**作者已脏文件：只改内容不 stage**
- 复核: `tools/gamekb/moddeps.json` 无需变更的确认（无新挂钩点）

**Interfaces:**
- Consumes: 全部前序任务。

- [ ] **Step 1: DESIGN.md 终版与 9 mod 清单终审**
- [ ] **Step 2: publish.ps1/installer.iss 内容修改（不 stage，向作者报告改动点）**
- [ ] **Step 3: moddeps.json 复核声明**（grep 本计划全部 Harmony 改动：仅 RefreshCoalescer 既有 prefix 加 gate 行，无新目标）
- [ ] **Step 4: 提交 DESIGN 系**（仅 stage ModCenter 与 9 mod 目录）+ 向作者汇报：改动清单、ViewportProbe 9222 发布前关闭提醒、待作者定号发版

---

## Self-Review 记录

- **Spec 覆盖**：§2 范围（T3/5/6/7/9-14）；§3 架构（T5/6）；§4 UI（T5/7/8，入口=菜单列表 T1/2/5，图标关闭钮随原型移植）；§5 配置桥（T4/6，默认值反射=T6 Step1，第三方枚举=T14）；§6 启停（T9-13 + 第三方 §5 分流 T16）；§7 配置项清单（T6 Meta 转写，覆盖清单 T7，柜子负重不在名单✓）；§7.10/指南（T14/15/16）；§8 改动清单（T9-12 + moddeps 不动 T18）；§9 风险实证（T1/2/4/8 对应 #1/#3/#2/#4，第三方 dll 风险 T16 含自愈实测）；§10 测试（T17）；§11 里程碑顺序与本计划一致。无缺口。
- **占位扫描**：panel.js 完整 UI 的"移植"指向已提交的原型文件（真实工件非占位）；所有关键新逻辑（gate/桥/Remove/早退补丁/禁用器/发现机制）均有代码块。
- **类型一致性**：Snapshot/Item 结构在 T6 定义、T7/T14 消费同名同构；`SetModEnabled` T6 骨架→T13 实装→T16 分流签名不变；`__mcOutbox/__mcDrain/__McInstalled` 全计划统一。
