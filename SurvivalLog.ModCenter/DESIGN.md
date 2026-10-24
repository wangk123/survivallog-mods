# MOD 管理中心 ModCenter — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（作者定号，硬规则 1）。
> 菜单锚点单源：本目录 `ANCHORS.md`（任务 1 离线定位结论；改挂钩点必须同步该文件与
> `tools/gamekb/moddeps.json`）。当前状态：**任务 5 注入骨架完成**（主菜单阶段 CDP 18 项断言
> 全绿 + 实机日志回读；游戏内 OutSetting 入口已部署、实机断言留任务 7 首轮，见 §5；
> 任务 4 反射桥 API 实证结论见 §4，`BridgeProbe` 探针保留为诊断功能，cfg `Debug.Probe` 默认关）。

## 1. 功能定位

套件内 mod 的统一管理面板：在主菜单（MainUI）与游戏内 ESC 菜单（OutSetting）各注入一个
入口按钮，打开 WebUI 面板，集中查看 / 开关本套件各 mod（各 mod 的 `General.Enabled`
配置 gate），替代逐个手改 cfg 文件。不改存档、不改游戏文件，删 dll 即还原。

主菜单 WebView 可达性已由任务 2 实机实证 GO；锚点选择器（`.menu` 尾部 appendChild /
`.menu-list` insertBefore `.btn-restart`）见 ANCHORS.md，**禁止按文案匹配**（全 i18n）。

## 2. 三层架构

| 层 | 代码 | 职责 |
|---|---|---|
| C# 宿主层 | `ModCenterPlugin` / `ModCenterTicker` /（任务 6 起）`ConfigBridge` | BepInEx 插件宿主。Ticker 每 2s（unscaled）心跳；各 mod Enabled 配置的读写（`SetModEnabled`）；轮询面板 outbox 执行面板命令。统一经 `ModCenterPlugin.Log(msg)` 静态门输出 |
| 注入层 | `WebBridge` / `MenuEntryInjector`（任务 5） | `WebBridge.GetWebView()`（TagFilter.cs:64-77 原样）+ `Exec/Eval`（Eval 回调 DelegateSupport 包装，异常只 Warn 不中断轮询）。`MenuEntryInjector.Tick` 每 2s：panel.js 幂等安装（Root 上下文）→ 双锚点入口注入（每轮无条件重跑自愈，脚本幂等：`__mc_entry` 已在目标 DOM 则跳过）→ outbox 轮询（`JSON.stringify(window.__mcDrain?__mcDrain():[])` 回读，暂只 Log，任务 8 起消费） |
| 前端面板层 | `panel.js`（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`；任务 7 长出完整面板） | 面板 UI 与交互。全局名 `__mcOutbox` / `__mcDrain` / `__McInstalled` / `__mcOpen(hostDoc)` / `__mcClose` / `__mcEsc` 为计划既定契约，全程一致；键盘上报任务 8 |

panel.js 作为内嵌资源随 dll 分发，不落盘、不改游戏资源文件（同 CabinetEverywhere
tagfilter.page.js 模式）。

## 3. 工程与验证约定

- 构建：`dotnet build SurvivalLog.ModCenter -c Release` 判退出码（硬规则 8，禁管道）。
- 装机自证：Load 写 `<BepInEx 根>/ModCenter_loaded.txt`（硬规则 9，不依赖 LogOutput.log；
  本机绝对路径 `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt`，
  代码用 `Paths.BepInExRootPath` 拼接以兼容发布到其他机器）。
- 引用结构抄 CabinetEverywhere.csproj（BepInEx core + interop 游戏目录引用，`Private=false`
  不拷贝产物）；`ModShared/WelcomeTip.cs` 链接编译（套件防盗署名气泡，publish.ps1 要求）。

## 4. 反射桥 API 实证记录（任务 4，2026-10-24 实机；任务 6 ConfigBridge 逐字消费）

> 验证方式：`BridgeProbe`（`ModCenterPlugin.cs` 尾部，cfg `Debug.Probe` 门控），
> 实机一轮三拍：A 枚举+读+写 3 → B 验表/写回 → C 终验/落盘。
> 证据文件 `BepInEx/ModCenter_probe.txt`（验后已删，全文存
> `.superpowers/sdd/2026-10-24-modcenter/task-4-report.md`）。
> 静态 API 面来自 Mono.Cecil dump 本机 `BepInEx/core/BepInEx.Core.dll` +
> `BepInEx.Unity.IL2CPP.dll`（本机构 BepInEx 为 IL2CPP BE 构型，带 XML 文档但文档不全，
> **勿按 5.x 记忆猜 API**）。

### 4.1 确切可用写法（编译通过 + 实机跑通，全部强类型、零反射）

```csharp
// ① 枚举插件：本构型【无】静态 Chainloader.PluginInfos！实例字典路径如下
//    （类型是 IL2CPPChainloader，继承 BepInEx.Bootstrap.BaseChainloader<BasePlugin>）
var plugins = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins;
//    → System.Collections.Generic.Dictionary<string, BepInEx.PluginInfo>（public 实例属性）
//    实测枚举到 18 个插件；键 = GUID 字符串。

// ② 定位目标插件：PluginInfo.Metadata（public，BepInPlugin）与 .Instance（public，object）
var info = plugins.Values.FirstOrDefault(p => p.Metadata.GUID == "com.local.survivallog.robotspeed");

// ③ 取 ConfigFile：BasePlugin.Config 是【public getter 公共属性】，转型后直取，无需 AccessTools
if (!(info?.Instance is BepInEx.Unity.IL2CPP.BasePlugin bp)) return;
var cfgFile = bp.Config;   // BepInEx.Configuration.ConfigFile

// ④ 读 entry：泛型 TryGetEntry（T 必须与目标 mod Bind 时的类型字面一致，此处 float）
if (cfgFile.TryGetEntry("机器人提速", "移速倍率", out ConfigEntry<float> entry))
    float cur = entry.Value;                       // 实测读到 2

// ⑤ 写值热生效：typed Value 直接赋值
entry.Value = 3f;
//    → 链路（IL 反汇编实证）：ConfigEntry<T>.set_Value → ClampValue(AcceptableValue) →
//      新旧 Equals 相同则【直接 return 不发事件】→ ConfigEntryBase.OnSettingChanged →
//      ConfigFile.OnSettingChanged → ConfigFile.SettingChanged 事件
//    → RobotSpeed 的订阅回调（Config.SettingChanged += Rerun=true）实测收到：
//      内存表 Config_RobotAttribute.MoveSpeed 5s 内 4.8→7.2（=其缓存原值 2.4 × 3），
//      写回 2f 后表回 4.8（= 2.4 × 2）。热生效链闭环。

// ⑥ 枚举全部 entry（任务 6 通用页用）：ConfigFile 还公开
//    GetConfigEntries() → ConfigEntryBase[]；this[ConfigDefinition]/this[sec,key] → ConfigEntryBase；
//    ConfigDefinitions → ReadOnlyCollection<ConfigDefinition>；Count/ContainsKey（IDictionary 实现）。
//    注意 Entries 字典属性是 protected（Family），外部不可直读。

// ⑦ 落盘：SaveOnConfigSet 属性（实测 RobotSpeed cfg = True，每写即存）；
//    兜底显式 cfgFile.Save()（public）。
```

### 4.2 类型与线程上下文

- **全链无反射**：IL2CPP 互操作只发生在游戏侧（ConfigManager 表读）；插件侧 Chainloader/
  PluginInfo/ConfigFile/ConfigEntry 全是 BepInEx 托管对象，普通 LINQ/泛型可用。
- **ConfigEntry<T> 的 T 按目标 Bind 字面类型取**：float→`ConfigEntry<float>`；不确定类型时走
  非泛型路径 `cfgFile[new ConfigDefinition(sec,key)]` 得 `ConfigEntryBase`，用 `SettingType`
  判型后 `BoxedValue`（object get/set，写同样触发事件链）。
- **线程**：探针在 `ModCenterTicker.Update`（Unity 主线程）执行，与各 mod Ticker 同上下文；
  Value 读写本身是纯托管操作，实测主线程安全。任务 6 的 outbox 命令也应在 Ticker 主线程执行。
- **事件语义**：`entry.SettingChanged`（entry 自身）与 `cfgFile.SettingChanged`（文件级，参数
  `SettingChangedEventArgs.ChangedSetting` 可判哪个 entry）都公开可订阅。

### 4.3 踩坑与注意点（任务 6 必读）

1. **同值写入不发事件**：`set_Value` 对 `Equals(旧值)` 的写入直接短路返回——
   "重写当前值以触发 Rerun" 的思路行不通，必须值真的变化才触发 SettingChanged。
2. **写 cfg 即落盘**：SaveOnConfigSet=True（默认）时每次 Value 写立即重写该 mod 的 cfg 文件；
   探针写 3f 曾瞬时落盘，靠写回原值 + 显式 Save() 收尾（验后 robotspeed cfg 确认 = 2 无脏值）。
   面板滑条拖动会产生大量中间值写 → 每次都落盘，任务 6 要考虑**防抖**或先
   `SaveOnConfigSet=false` 改完再恢复 + Save 一次。
3. **gamekb 表快照 ≠ 原始基线**：gamekb 的 Config_RobotAttribute MoveSpeed=4.8 是**套件生效中**
   dump 的（RobotSpeed ×2 后）；真原始值 = 2.4（本轮由 4.8→7.2→4.8 反推并与其幂等缓存语义
   吻合）。任务 6 做"恢复默认值"时禁止拿 gamekb 数值当原始基线，应读 entry.DefaultValue。
4. **本构型 API 与常见记忆不符**：无 `Chainloader.Instance.Plugins` 静态、无
   `Chainloader.PluginInfos` 静态字典；正确入口 `IL2CPPChainloader.Instance.Plugins`（实例）。
   升级 BepInEx 后此节需复核（改挂钩点须同步 moddeps.json 的口径同样适用于此处依赖）。
5. **`TryGetEntry` 区分大小写与全角**：section/key 必须与目标 mod Bind 字符串逐字一致
   （本靶为中文 `机器人提速`/`移速倍率`，UTF-8 无障碍）。
6. **探针门控**：`Debug.Probe` 默认 false 常驻 `com.local.survivallog.modcenter.cfg` [Debug] 节，
   开一轮跑一次三拍后自动停（状态机终态），证据文件在 BepInEx 根目录 `ModCenter_probe.txt`。

### 4.4 实测时间线（2026-10-24 17:01–17:02）

启动（steam:// 协议）→ 17:01:50 RobotSpeed 首效（13s，ConfigManager 主菜单前就绪）→
17:02:15 探针 A（枚举 18 插件 / entry=2 / 写 3f）→ 17:02:20 B（entry=3、表 7.2×3 行、写回 2）→
17:02:23 C（entry=2、表回 4.8、Save）。全程未读档，主菜单阶段即验完。

## 5. 注入骨架机制与实证（任务 5，2026-10-24）

### 5.1 通道与落点

- C# 侧每 2s（Ticker，unscaled）：`GetWebView()` 为 null（主菜单前/重建中）静默跳过本轮；
  就绪则 `Exec(panel.js)`（Root 上下文，`__McInstalled` 守卫幂等）→ `Exec(EntryJs)`（每轮
  **无条件重跑**的免维护自愈，TagFilter.cs:46-50 模式；脚本内部幂等）→ `Eval(drain)` 回读
  面板 outbox。
- EntryJs 在 Root 扫 `#iframe-container iframe`（pathname 优先 / id 兜底，ANCHORS.md §三），
  命中 MainUI / OutSetting 后进目标 `contentDocument` 插入口按钮；点击闭包在 **Root realm**
  执行（`window.__mcOpen` 即 Root 契约函数），按钮元素归属 iframe 文档。

### 5.2 关键取证结论（改 UI 前必读）

1. **面板 DOM 必须挂在入口所在的 iframe 文档内，不挂 Root**：各页 webui-core.js 的
   clickThrough（selector `.unity-interactive, button, input, select, a, [data-interactive]`）
   把命中元素矩形经 Root 以协议 9 同步给 Unity，实机点击才落进 WebView；**Root 文档自身
   不跑 clickThrough**（Root.html:471 `syncInteractiveRects` 只收子页 postMessage），挂 Root
   的按钮收不到鼠标。故 `__mcOpen(hostDoc)` 把面板建进宿主 iframe；overlay 带
   `data-interactive`（打开期间全屏矩形进同步，点击不穿透游戏），入口/关闭钮均为
   `<button>` + `data-interactive` 双保险。
2. **button 元素注入两锚点的样式策略**：克隆官方类（MainUI=`.menu-item` 内层 `.bar`+
   `.menu-text`；OutSetting=`.main-btn`），入口选择器用**每页一份的 id 选择器样式块**
   （`#__mc_entry{...}`）只重置 button 的 UA 默认值（background/border/font-family 等），
   声明哪些属性就只压哪些（CSS 特异性按属性算，不误伤官方类）；accent 用蓝色系
   （bar 渐变 `#7fb8e8→#2f6ea8`、文字 `#a9cdf5`、OutSetting 左边条 `#4f9fe0`）区分官方金色。
   实测 computed 与官方项逐属性一致（font-size 29.6px/weight 700/letter-spacing 6px/
   padding 5px 15px 5px 5px/bar 8×34px 全同），仅颜色为 accent。
3. **OutSetting 锚点约束执行**：`insertBefore(btn, list.querySelector('.btn-restart, .btn-exit'))`
   —— restart 行 v-if 消失时锚点自动退化为 `.btn-exit`；永不插在两者之间（617 行相邻兄弟规则）。
4. **ESC 的现实**：OutSetting 页收不到 ESC（页面 1351 行注释：Unity 侧处理）。面板 ESC 关闭
   的 JS 接线在（Root + 宿主 doc 各挂 keydown → `__mcEsc`），DevTools/焦点在 WebView 时生效；
   **实机 OutSetting 场景下 ESC 可能先被 Unity 拿去关暂停菜单**——✕ 按钮与遮罩点击是保底
   关闭路径；任务 8 键盘门（消费 MC_CLOSE）落地时再统一处理。
5. **outbox 消息形状**：`{type:'MC_OPEN', host:'<宿主页 pathname>'}` / `{type:'MC_CLOSE'}`
   —— host 字段方便日志定位面板挂在哪页。C# 侧暂只 Log（已实测回读成功，见 5.3）。

### 5.3 实测记录（2026-10-24 17:28–17:33，主菜单阶段）

- 工具：`tools/_mc_verify_cdp.js`（连法照抄 `_probe_menu_cdp.js`；18 项断言）+
  实机 LogOutput 回读。截图 `task-5-shot-menu.png` / `task-5-shot-panel-open.png`
  （.superpowers/sdd/2026-10-24-modcenter/）。
- 结果：**18/18 PASS**——入口（button.menu-item、data-interactive、`MOD 管理`、.menu 尾部、
  单例、跨 tick 幂等、官方样式生效）→ 点击（同 iframe 文档 overlay+panel、标题、✕）→
  outbox（MC_OPEN/MC_CLOSE 均在 10s 内被 C# 轮询取走，`__mcDrain` 补丁留痕证明 C# 侧真在调）→
  关闭（✕ 移除 overlay、ESC keydown 关闭）→ 终态干净（1 入口、无 overlay）。
- 日志回读（BepInEx LogOutput.log）：`WebView 通道就绪，注入巡检启动` +
  `面板消息: [{"type":"MC_OPEN","host":".../UI/MainUI/MainUI.html"}]` 等 5 条——
  Eval 回调委托（DelegateSupport 包装）实测可用。
- **游戏内 OutSetting 断言未跑**（读档会动作者存档，自动化不冒险），代码路径与主菜单
  同一脚本分支（仅锚点/插入方式不同），留任务 7 首轮实机复核——这是本任务唯一未实证点。

## 6. 迭代维护清单

- 2026-10-24 任务 5：注入骨架。`WebBridge.cs`（通道+Eval）、`MenuEntryInjector.cs`
  （双锚点入口 + outbox 轮询）、`panel.js`（占位→空壳面板：遮罩/标题/✕/ESC/outbox 契约）、
  Ticker 接线；主菜单阶段 CDP 18 项全绿 + 日志回读。关键结论固化 §5（Root 无 clickThrough →
  面板挂宿主 iframe；button UA 重置按属性声明；OutSetting ESC 归 Unity）。
- 2026-10-24 任务 4：反射桥 API 实证。`ModCenterPlugin.cs` 增 `Debug.Probe` cfg 门控 +
  `BridgeProbe` 三拍探针（枚举→Config→Value 写→热生效验表→还原落盘）；结论固化 §4。
  探针代码常驻为诊断功能（默认关）。
