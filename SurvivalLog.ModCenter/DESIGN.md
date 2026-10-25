# MOD 管理中心 ModCenter — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（作者定号，硬规则 1）。
> 菜单锚点单源：本目录 `ANCHORS.md`（任务 1 离线定位结论；改挂钩点必须同步该文件与
> `tools/gamekb/moddeps.json`）。当前状态：**全 18 任务实机定稿（2026-10-25）**——三档第三方
> 接入（Tier 0 通用页 §6.8 / Tier 1 声明式清单 §6.9 / Tier 2 深度对接 + 重启级 dll 禁用 §6.10）；
> 启停两层（套件 mod 直写 `General.Enabled` 热还原 §6.3/§6.7——PDS 重启级例外与 CabinetSize
> 未接 gate 现状见 §6.3；第三方三层分流 §6.10）；主菜单
> 与游戏内 ESC 菜单各一个**列表入口**（§1/§5/§6.5.3）；键盘门（§6.6）；回归夹具六模式
> `tools/mc_fixture.js`（§3）。发版接线：publish.ps1 `$suiteMods` + installer.iss 组件表已改稿
> **留作者工作树核入**（作者脏文件不代提交，清单见任务 18 报告）。作者人工验收清单（6 项体感）
> 见 `.superpowers/sdd/2026-10-24-modcenter/task-17-report.md` §5。
> （分任务细节：任务 15 Tier 1 声明式清单见 §6.9；任务 14 Tier 0 第三方通用页见 §6.8；任务 13 启停直写见 §6.3/§6.7；任务 8 键盘门
> 复用见 §6.6；任务 7 面板 UI 见 §6.5；任务 6 配置桥见 §6；任务 5 注入骨架见 §5；
> 任务 4 反射桥 API 实证结论见 §4，`BridgeProbe` 探针保留为诊断功能，cfg `Debug.Probe`
> 默认关）。

## 1. 功能定位

套件内 mod 的统一管理面板：在主菜单（MainUI）与游戏内 ESC 菜单（OutSetting）各注入一个
入口按钮，打开 WebUI 面板，集中查看 / 开关本套件各 mod（各 mod 的 `General.Enabled`
配置 gate），替代逐个手改 cfg 文件。不改存档、不改游戏文件，删 dll 即还原。

主菜单 WebView 可达性已由任务 2 实机实证 GO；锚点选择器（`.menu` 尾部 appendChild /
`.menu-list` insertBefore `.btn-restart`）见 ANCHORS.md，**禁止按文案匹配**（全 i18n）。

## 2. 三层架构

| 层 | 代码 | 职责 |
|---|---|---|
| C# 宿主层 | `ModCenterPlugin` / `ModCenterTicker` / `ConfigBridge` / `Meta` / `Payload`（任务 6）/ `FocusTick`（任务 8）/ `DllDisabler`（任务 16） | BepInEx 插件宿主。Ticker 每帧跑 `ConfigBridge.Pump`（outbox 消息分发 + Save 防抖计时，主线程），每 0.3s（unscaled，暂停照走）跑 `FocusTick.Tick`（键盘门沿交接，§6.6），每 2s（unscaled）跑注入巡检；`ConfigBridge`：快照 `BuildSnapshot()` / 写 `TrySet`（联动 writeKeys 等值）/ `SetModEnabled`（任务 13 直写各 mod `General.Enabled`；任务 16 第三方分流——Tier 2 软停回调优先，§6.10）/ `MC_EVIDENCE` 验证辅助；`DllDisabler`：第三方重启级 dll 禁用（[DisabledTp] marker + 退出分离进程改名 + 残留自愈，§6.10）；`Meta` 九表 + `Payload` DTO/手写 JSON。统一经 `ModCenterPlugin.Log(msg)` 静态门输出 |
| 注入层 | `WebBridge` / `MenuEntryInjector`（任务 5；实机 UI 三修改事件驱动） | `WebBridge.GetWebView()`（TagFilter.cs:64-77 原样）+ `Exec`（注入与 C#→JS 推送共用主通道，Root 上下文）/ `Eval`（JS→C# 消息回读，任务 6 起承载 outbox drain；回调 DelegateSupport 包装，异常只 Warn 不中断轮询）。`MenuEntryInjector.Tick` 每 2s **自愈守卫拍**（不再每拍全量重发）：极小表达式校验 `__mcObserver`（Root 侧常驻 MutationObserver）+ `__McInstalled`（面板）+ `__mcSelfCheck()`（双锚点入口未被抹除），任一失守才重发完整引导（幂等）；入口即时性由 Root 侧 Observer（body 子树，iframe 插入当场）+ 目标页 iframe load 钩（页面加载完成当场 install）承担；outbox 轮询节奏不变（回读 JSON 入队 `ConfigBridge`，分发在其主线程 Pump 执行） |
| 前端面板层 | `panel.js`（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`；任务 7 已长成完整面板） | 面板 UI 与交互（原型 v4 移植，机制与实测见 §6.5）。全局名 `__mcOutbox` / `__mcDrain` / `__McInstalled` / `__mcOpen(hostDoc)` / `__mcClose` / `__mcEsc` / `__mcRender(snapshot)` / `__mcEvent(event)`（调试镜像 `__mcLastSnapshot` / `__mcLastEvent` / 推送序号 `__mcSnapSeq`）为既定契约，全程一致；任务 8 起 `MC_OPEN`/`MC_CLOSE` 兼作键盘门信号 + Root 侧孤儿看门狗（§6.6） |

panel.js 作为内嵌资源随 dll 分发，不落盘、不改游戏资源文件（同 CabinetEverywhere
tagfilter.page.js 模式）。

## 3. 工程与验证约定

- 构建：`dotnet build SurvivalLog.ModCenter -c Release` 判退出码（硬规则 8，禁管道）。
- 装机自证：Load 写 `<BepInEx 根>/ModCenter_loaded.txt`（硬规则 9，不依赖 LogOutput.log；
  本机绝对路径 `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt`，
  代码用 `Paths.BepInExRootPath` 拼接以兼容发布到其他机器）。
- 回归夹具：`tools/mc_fixture.js` **六模式**（`menu` 全量对账 / `toggle` 启停热还原 /
  `enterworld` 开面板进对局 / `ingame` 对局共存 / `outsetting` 游戏内入口 / `loadgame` 兼容
  保留），exit 0=全 PASS（SKIP 不判负）/ 1=断言 FAIL / 2=环境性不结论。改 ConfigBridge /
  Meta / panel.js 后先跑 `menu` + `toggle` 两模式回归（连 DevTools 9222，主菜单阶段即可，
  无需读档；六模式口径与全量实证见任务 17 报告）。
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
ConfigEntry<float> entry;                       // 声明在 if 外（out var 声明不能直接作内嵌语句）
if (!cfgFile.TryGetEntry("机器人提速", "移速倍率", out entry)) return;
float cur = entry.Value;                        // 实测读到 2

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
   BepInEx 侧 API（Chainloader/BasePlugin.Config/ConfigFile/ConfigEntry）**不在 moddeps.json 登记口径内**
   ——那是游戏侧挂钩点/表字段/字节签名的登记表（硬规则 11 范围）；此处依赖的复核方式 =
   升级 BepInEx 后重验 §4.1 API 面（任务 4 的 Cecil dump 法，`tools/dump_bepinex_api.ps1`）。
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

- C# 侧每 2s（Ticker，unscaled，实机 UI 三修后为**自愈守卫拍**）：`GetWebView()` 为 null
  （主菜单前/重建中）静默跳过本轮；就绪则只 `Exec` 一个极小守卫表达式
  （`__mcObserver && __McInstalled && (!__mcSelfCheck || __mcSelfCheck())`）——失守
  （WebView/Root 重建、或运行中入口被页面异常抹除）才重发完整引导（panel.js + EntryJs，
  双守卫幂等）→ `Eval(drain)` 回读面板 outbox（节奏不变，非入口延迟来源）。
- 入口即时性（问题1 修复）由 Root 侧常驻设施承担（EntryJs 一次性安装，`__mcObserver`
  防重装）：① MutationObserver 观察 **document.body 子树**（childList+subtree，含
  `#iframe-container` 增删；观察 body 而非容器节点——Root.html 的 Vue mount 会克隆替换
  #app 内容、容器节点身份会换，tagfilter.page.js:899 同款）→ 回调当场跑幂等 install；
  ② 目标页 iframe（id MainUI/OutSetting）**load 事件钩**（元素属性防重挂）——Observer
  回调时 contentDocument 常还是 about:blank（锚点未在），真正就位时机 = load（页面脚本
  已跑、Vue 已 mount）→ 当场 install；③ `__mcSelfCheck` 供守卫拍判定"锚点已在而入口
  被抹"的自愈重装（保留旧免维护语义，TagFilter.cs:46-50）。
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
   —— host 字段方便日志定位面板挂在哪页。任务 5 时 C# 只 Log（回读成功实证见 5.3）；
   任务 6 起入队 ConfigBridge 主线程分发（MC_SET/MC_TOGGLE_MOD 协议见 §6.2）。

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
  同一脚本分支（仅锚点/插入方式不同）——**任务 7 已补实机断言（§6.5.3），4/4 PASS**。

## 6. 配置桥与消息协议（任务 6，2026-10-24）

### 6.1 组成与纪律

- `Meta.cs`：9 mod 页面定义唯一蓝本 = spec §7 v4 九张表逐行转写（结构对照原型 v4 MODS 数组）。
  **默认值与范围不写死为准**：快照时从 `ConfigEntry.BoxedValue`（当前值）与
  `Description.AcceptableValues`（`AcceptableValueRange.MinValue/MaxValue` / `AcceptableValueList`，
  closed generic 走 `GetType().GetProperty` 反射读）合入；Meta 里的 Def/Min/Max 只在范围缺失或
  entry 尚不存在时兜底（数值照 spec §7 表抄）。步进 st 无反射来源，永远兜底。
- `Payload.cs`：DTO（Snapshot/ModInfo/GroupInfo/ItemInfo/OvrInfo）+ 手写 JSON（AOT 零反射）；
  入向消息解析 `JsonDocument`（EndlessGuard 同款实绩 API）。契约签名冻结：任务 7 UI 逐字消费
  （`{self:{ver},mods,tp}` / `Item={k,l,t,v,min,max,st,b,note,opts}` / ovr 类 mod 另挂 `ovr:[{id,ms}]`）。
- `ConfigBridge.cs`：枚举/快照/写/启停/消息分发/Save 防抖，全部在 Ticker 主线程（Pump）执行；
  Eval 回调线程只入队（`ConcurrentQueue`）。
- 两处对 spec 的实证修正（实证优先）：**UiFit.Mode 合法值是 Scroll/Zoom/Off**（ViewportFit.cs
  描述明示；spec §7 表 "Fit" 为笔误，写 Fit 会静默按 Scroll 处理）；RoleUnlock `Plan.ClearPool/
  VaseRecipe` 两 entry 任务 8 才新增，快照先按 spec 默认 true 兜底展示（写会被拒并回 MC_SET_FAIL）。

### 6.2 消息协议（JS→C# 走 outbox drain；C#→JS 走 Exec 直推 Root）

| 入（type） | 字段 | 行为 |
|---|---|---|
| MC_OPEN | host | 键盘门开（任务 8：`FocusTick.NotifyOpen`）→ 推全量快照：`window.__mcRender(snapshot)` |
| MC_SET | guid, section, key, value | `TrySet` → 成功回 `__mcEvent({type:'MC_SET_OK',k,badge})`（k=Meta 展示键，badge=Item.b）；失败回 `{type:'MC_SET_FAIL',k,err}` |
| MC_TOGGLE_MOD | guid, enabled | `SetModEnabled` → 推新快照（`__mcRender`） |
| MC_CLOSE | why? | 键盘门双还原（任务 8：`FocusTick.NotifyClose`）→ 立即落盘全部脏 cfg（防抖收口）。`why='host-doc-gone'` 为 panel.js 孤儿看门狗补发（§6.6） |
| MC_EVIDENCE | tag | 验证辅助（只读）：写 `BepInEx/ModCenter_evidence.txt` 一行**多表断言快照**（任务 13 扩）：RobotSpeed 移速倍率 entry + Config_RobotAttribute.MoveSpeed 全行 + 判别动作行 During（9075/9098/9099/9100/9130/9131/1611/1615/1915）+ 判别包行 Size/Burden（背包 1/8/11、货舱 1061/1063、柜子 110001/110009、冰箱 1004/115000/3002、载具 1023）+ 冰箱家具 ColdRate 统计（受管 16 行中已压低行数@最小值）+ Talent 受限行数与花瓶 325 等级 + 储物柜白名单集合计数。toggle 前后各拍一轮对照（表/trace/cfg 三源之一）；正常玩家不触发 |

- **逐条 try（任务 7 落地，原 §6.2 旧债收口）**：HandleOutbox 的批内循环每条消息独立
  try/catch——单条异常只弃该条（warn 日志），不弃同批后续（面板滑条连发+关闭常同批到达）。

- **guid 路由**：完整 GUID / En 短名 / GUID 尾段均可（大小写不敏感；`'RobotSpeed'`、`'robotspeed'`、
  `'com.local.survivallog.robotspeed'` 同效）。
- **key 路由**：主键 / 任一 writeKeys / 组合展示键（联动项 `"Expand.WidthMult + HeightMult"`）均可；
  **实写键一律以 Meta.WriteKeys 为准**（联动 = 宽高两键等值写；Item.v 展示宽度键值）。
- **只写 Meta 声明过的键**：未登记的 section/key 一律拒绝（spec §2.2 暴露原则的写侧执行）。
- 值转换按 entry.SettingType：float/double/int/long/short/byte 系/bool/string/enum（enum 名大小写不敏感）。

### 6.3 启停（任务 13 实装）与 Save 防抖

- `SetModEnabled(guid, enabled)`：**直写目标 mod 自己的 `General.Enabled`**（节/键按
  `MetaMod.EnabledSection/EnabledKey`，默认 General:Enabled，与 T9-T12 九 mod Bind 一致；
  全套件无键名差异，字段留作个别 mod 例外位）。写入经 §4.1 同款事件链触发该 mod 现役
  `SettingChanged→Rerun`：热关闭=其 Ticker 首行 gate 按原值缓存还原并停写（≤其轮询周期），
  重开=重新落笔（例外：PDS 无 Rerun 订阅——`General.Enabled` 系既有键复用，重启级语义
  =Injector 停注入，见其 DESIGN；Meta 该页为仅启停形态。CabinetSize 未 Bind 该键，见 §7 T17）。
  Save 走统一防抖（MarkDirty 该 mod cfg）。**旧的本插件 cfg `[Disabled]`
  名单机制已移除**（避免双轨；快照真值源=各 mod entry 本身，旧 cfg 里残留的 `[Disabled]` 节
  无人再读，行为惰性）。dll 不在链上 / entry 不存在（旧版 dll）→ 拒写并 Log。
- 快照 `enabled = 插件在链上 && entry 值`（entry 缺失按 Bind 默认 true 兜底）。
- 防抖（DESIGN §4.3-2 落地）：首写某 ConfigFile 前 `SaveOnConfigSet=false`（记住原值）→ 标脏；
  **3s 节流**（首写起算，连写不重置——长拖动按 3s 批落盘）到点 / MC_CLOSE 时 `Save()` 全部脏文件
  并**恢复各自原 SaveOnConfigSet**。进程被强杀最多丢 3s 内的未落盘写（面板改值路径可接受）。

### 6.4 实测记录（2026-10-24，主菜单阶段，无需读档）

`tools/_mc_verify6_cdp.js`（连法照抄 `_mc_verify_cdp.js`）：注入脚本调 `__mcOpen()` → 断言
`__mcLastSnapshot`：mods 9 项（名字/启停/分组对账 Meta）+ 柜子扩容三项联动键 + 动作加速 ovr 数组 +
更好的储物柜 enabled=true 仅启停 + RoleUnlock 兜底项；实弹 MC_SET（RobotSpeed 移速倍率→4）→
MC_EVIDENCE 轮询读表：MoveSpeed 4.8→9.6（=2.4×4，≤5s）→ 写回 2 → 表回 4.8；MC_TOGGLE_MOD
一轮（false→名单落盘 `com.local.survivallog.modcenter.cfg [Disabled]`→true→恢复）；MC_CLOSE 即时
落盘断言。证据：`.superpowers/sdd/2026-10-24-modcenter/task-6-report.md`（输出全文）。

### 6.5 面板 UI（任务 7，2026-10-24）

#### 6.5.1 移植要点（原型 v4 → panel.js 的实装差异）

- **可点元素改造（T5 结论的执行）**：宿主页 clickThrough 清单是
  `.unity-interactive, button, input, select, a, [data-interactive]`——原型的 `<span>` 迷你开关
  改为 `<button>`（自带全量样式重置），左列 mod 行是 `<div>` 故显式带 `data-interactive`；
  数值/滑条/下拉天然在清单内。全部选择器锚定 `#__mc_overlay` + `__mc` 前缀（零样式泄漏）。
- **观感取舍**：原型遮罩的 `backdrop-filter:blur(3px)` 未迁（纯色 rgba(5,6,10,.8) 加深替代）——
  WebView 内全屏实时模糊的 性能/兼容 保守取舍，观感差异仅在背景糊化。
- **状态语义**：`ST.sel` 记住当前选中 mod（跨开关面板保持——重开面板回到上次页，实测确认）；
  `toggleMod` **乐观翻转直接改写快照对象内的 mod 字段**——`__mcLastSnapshot` 与 `ST.snap` 同引用，
  故"快照里 enabled 已变"≠"C# 已回推"；判真推送用 `__mcSnapSeq`（`__mcRender` 每次自增，
  CDP 断言/未来 UI 逻辑都应以此为准）。
- **改值链**：input 事件即时 `MC_SET`，滑条 100ms 节流（首条立即发、窗口内合并为末值补发；
  实测 5 连发 → 恰好 2 条消息 [首值,末值]；`__mcClose` 清空节流句柄，关闭后无补发）。数值框
  input 阶段发送钳制值但不重写在打文本，change 时归位显示；下拉走 change。bool/下拉/滑条全部
  乐观更新本地 `it.v`（C# 不为 MC_SET 推快照，重绘不打断拖动）。
- **MC_SET 路由（Fix Round 1 定案）**：`section` 发展示键 `k` 按首个 `.` 解析的节前缀，
  **`key` 一律发完整 `k` 原串**（"节.键" / 联动"节.键A + 键B"）——C# `Match` 的 `it.K` 分支
  逐字命中（单键项整串即 `it.K`，联动项即组合展示键）。**禁止**再发剥前缀的 key：联动项
  "WidthMult + HeightMult" 在 Key/WKeys/K 三候选全不命中（初版实坑，背包/柜子 4 滑条写必败，
  fixture 已补 4 条端到端断言回归）。`其他.Overrides` 走 ovrCommit 固定 section/key（it.Key 分支）。
  **T17 补（B3）**：`Match` 的 K 分支已**前置放行**（完整展示键逐字命中且 mod 内唯一时先于节守卫
  执行）——点分节名键（首例=柜子 v3 `Tier.Small.Width`，routeOf 首点切分出 section="Tier"）
  此前必被"节.键"守卫拦截全拒；短键/ovr 分支仍走节守卫，行为不变。
- **ovr 序列化（面板侧承担，T6 评审备忘 B 兑现）**：增/删/拖 → 本地数组编辑 → 序列化
  `id:ms,id:ms`（**按 id 升序 canonical**）整串 `MC_SET 其他.Overrides`；打开面板从快照 `ovr`
  数组初始化；实测 8012 增(1000)→拖(250)→快照往返 `[{id:8012,ms:250}]`→删("")→`[]`，cfg 盘上
  `Overrides = 8012:250` → `""` 精确还原。
- **toast**：按徽标四档小字（now≈5秒内/reload=读档后/cfg=生效视该 mod——本任务套件全有徽标，
  cfg 档文案就绪待 T14）；`MC_SET_FAIL` 到达时以错误样式覆盖乐观 toast。
- **tp 空数组**：渲染"第三方 MOD"分类头 + "（暂无第三方 mod 接入）"占位行（T14 填充后自动长行）。

#### 6.5.2 实机现状备忘：UiFit.Mode/Enabled 写路径（已随 T17 Meta 柜子位改稿过时，留档）

> 2026-10-25 T17 注：Meta 柜子位已对齐 CabinetSize v3（无 UiFit 条目），本节写路径不再可达；
> "entry 缺失→兜底展示+写拒绝回 MC_SET_FAIL"的设计行为本身仍有效（见 §7 任务 17 条目）。

装机版 CabinetExpand.dll（作者现装，10-20 构建）尚未 Bind `UiFit.Mode`（cfg 无该键；仓库源
ViewportFit.cs:44 已 Bind，属作者未部署的 WIP）——快照按 Meta 兜底展示，写被桥拒绝回
`MC_SET_FAIL`（与 RoleUnlock Plan.* 待 T8 同类，**设计内行为**，实测 FAIL 回执+错误 toast+cfg
零改动）。作者部署新版 CabinetExpand 后该写自动转 OK 路径，无需改本 mod。

#### 6.5.3 实测记录（2026-10-24，主菜单 41/41 + 游戏内 4/4）

- 主菜单（`tools/_mc_fixture.js menu`）：面板布局（样式/头/关闭/四徽标图例/toast/左列 10 行
  =9 套件+自身锁定/tp 空占位/三分类头）→ 9 页逐项对账（分组标题/项数/控件类型/联动动态注记
  与快照自洽；仅启停页=占位文案）→ **PlanPick Cost min=100/max=300 反射合入断言（T5/6 遗留 D，
  快照+DOM 双侧）** → 自身页防呆文案 → 滑条（消息形态 guid/section/key/value 数值型 + OK 回执
  + 配对数值框联动 + toast + 节流形态）→ bool（消息值=布尔型/UI 翻转/回执）→ 下拉（FAIL 路径，
  §6.5.2）→ reload 徽标 toast → 左列启停（消息形态/真回推 snapSeq/行置灰/停用横幅/[Disabled]
  落盘/还原）→ ovr 往返 → ✕ 关闭（overlay 移除+MC_CLOSE）→ 全部被写 cfg 还原基线。
  截图 `task-7-shot-panel.png`。
- 游戏内（遗留 A）：`loadgame` 模式点"继续生存灾后 · 第90天"进对局 → SendKeys ESC
  （`tools/_mc_sendkey.ps1`，AppActivate+SendKeys）→ `outsetting` 模式断言：入口
  `BUTTON.main-btn` 在 `.menu-list` 内、`insertBefore` 位置正确（本档暂停菜单无 .btn-restart，
  锚点自动退化为 .btn-exit——**相邻兄弟规则的退化路径首次实机验证**，entry 紧邻其后）→
  点击开面板（挂 OutSetting 文档，10 行渲染）→ ✕ 关闭干净 → 共存扫描（#ppEntry 所在页未开，
  前后扫描一致、无残留 overlay）。跑两轮均 4/4 PASS。截图 `task-7-shot-outsetting.png`。
- **读档安全处置**：进对局后游戏自身在继续时做存档槽轮转（.bytes→.bak）并在约 13s 后自动
  落盘一次（暂停菜单打开前后）；主存档被写入约 90 秒世界漂移。验证后已用游戏自己的 .bak
  **复制还原**（md5 与作者 18:48 原态一致），HistorySave 本就同哈希未动——作者存档零净变化。
  教训：该游戏"继续"≠只读，后续自动化进对局前必须先备份 Saves 目录。
- 生命周期红线遵守：steam:// 启动；期间作者两次短暂上号（18:42-18:48 干扰首轮运行、按红线
  未杀未等自然退出后重跑）；验证后 taskkill + sleep 3 + tasklist 原始输出确认死透。

## 6.6 键盘门复用（任务 8，2026-10-24）

### 6.6.1 机制（照抄 PlanPick Injector.cs:100-140 十二轮定案）

- **开**（`_panelOpen` 上升沿）：`WebUILayer.SetKeyboardEnabled(true)`（裸键/英文进浏览器的
  唯一开关，PlanPick S1/S3 实证）+ 记录进入前 `Input.imeCompositionMode` 后设 `On`
  （中文 IME 组合由 Vuplex 直投浏览器，PlanPick 十一轮实证）。面板打开期间覆盖 ID 数字框、
  滑条配对数值框等可实机键入，且游戏热键不触发（键全量进 WebView）。
- **关**（下降沿）：`SetKeyboardEnabled(false)` + `imeCompositionMode` 精确还原进入前值。
- **门控判据与 PlanPick 的差异**：PlanPick 是官方页 `IsPageActive` 三候选（uiKey 列表）；
  本面板不是官方页——门控 = 面板开合状态，由 `MC_OPEN`/`MC_CLOSE` 消息驱动
  （`ConfigBridge.HandleOutbox` 主线程 Pump 调 `FocusTick.NotifyOpen/NotifyClose` 置位，
  与 `FocusTick.Tick` 同在 Ticker 主线程，无竞态）。`Tick` 由 Ticker 每 0.3s（unscaled，
  暂停菜单打开也要照走——OutSetting 场景 timescale 不可依赖）驱动，沿触发、异常只 Warn。
- **对 PlanPick 的两处有意加固**（非照抄部分，代码注释标注）：
  1. `_imeSaved` 会话标记：层短暂丢失后重交接时不把当前值（已还原的原值）误记为"进入前值"，
     保证"还原精确回到进入前状态"闭环（PlanPick 未区分，重交接场景会把 On 记成原值）。
  2. 层丢失收尾（`layer == null` 且已交出）：PlanPick 只复位标志；本面板状态由 JS 消息驱动，
     层没了 ⇒ WebView/JS 也没了 ⇒ 面板必已消亡且永远等不到 MC_CLOSE——就地还原 IME 静态
     （Unity 侧，层死不影响）并复位开合状态，防键盘门漏开（键盘门标志随层销毁，新层默认门关）。
- **panel.js 孤儿看门狗**（Root 侧 1s interval，宿主 iframe 消亡不影响 Root 定时器）：
  宿主死亡判据 = `宿主 doc.defaultView.frameElement` 为 null 或失连（**T17 修复**：本引擎
  Vuplex/CEF 里被移除 iframe 的孤儿文档内元素 `isConnected` 恒 true——旧判据"面板根
  `!isConnected`"对宿主 iframe 被移除永不触发，开面板进对局键盘门漏开实锤；frameElement 对
  "iframe 被移除"与"文档被导航"两死法均实测归 null，且其自身属 Root 文档、isConnected 语义
  正常）。面板开着时宿主死亡（如暂停菜单被 Unity 侧 ESC 收起、开面板直接点继续进对局）⇒ 补发
  `MC_CLOSE{why:'host-doc-gone'}`（键盘门还原 + 脏 cfg 落盘收口）。✕/点遮罩仍是保底关闭
  路径（T5 结论：OutSetting 的 ESC 被 Unity 截走，ESC 仅增强）。前瞻脚注：若未来出现宿主 =
  Root 顶层文档的挂载形态，frameElement 恒 null 会在开面板 1s 后误关——当前架构入口只喂
  iframe 文档，不可达；Vuplex 升级后建议复跑 `enterworld` 模式验证两死法仍成立。
- **自证（硬规则 9）**：每次沿写一行 `<BepInEx 根>/ModCenter_kb.txt`（Load 时清空）：
  `OPEN kb=on ime Auto->On`（kb 读回自 `get_IsKeyboardEnabled`）/ `CLOSE kb=off ime->Auto`
  （还原值）/ `ORPHAN(layer-lost) ime->X kb-dies-with-layer`。CDP 合成键不受 Vuplex 门控，
  不能作门开证据——自证文件是装机断言的唯一权威。

### 6.6.2 实测记录（2026-10-24，主菜单阶段，不碰存档）

`tools/_mc_kb_cdp.js`（连法照抄 `_mc_fixture.js`）：入口点击开面板 → 轮询 ModCenter_kb.txt
断言 OPEN 行（kb=on、ime 进入前值→On）→ ✕ 关闭 → 断言 CLOSE 行（kb=off、还原值逐字等于
本轮记录的进入前值）→ 往返三轮共 6 行、无 ORPHAN 行；outbox spy 断言 ≥3 MC_OPEN +
≥3 MC_CLOSE 被 C# 取走；第 1 轮附带实机打字探针（面板开 → 动作加速页 → CDP focus
`#__mc_ovr_new` → `tools/_mc_kb_type.ps1` AppActivate+SendKeys '8012' → 回读 input.value
——实机物理键穿 Vuplex 门进浏览器，探针失败只 WARN 不判负）。证据全文存
`.superpowers/sdd/2026-10-24-modcenter/task-8-report.md`。

## 6.7 启停热还原断言（任务 13，2026-10-24，主菜单阶段）

工具 `tools/_mc_fixture.js toggle`（MC_TOGGLE_MOD 真实面板开关驱动 + `MC_EVIDENCE`
多表快照 + trace/cfg 双源；面板活性自愈——宿主页丢失自动重开面板，被接管则中止）。
**实测 42/42 PASS（2 项 SKIP：CabinetExpand 系——并行任务已把 Meta 的柜子扩容位换成
CabinetSize（c899f20 系列），面板无该页，热还原断言待套件组成定稿后补）**。断言面
（T9-T12 累积欠账收口）：

1. **三件套热还原 ×2 轮**（T9）：RS 面板关 → ≤5s MoveSpeed 4.8→2.4（×3 行）、
   9098/9099/9100/9131→300/300/60/300、货舱 1061/1063→4x2/4x3（9130 保持 60=QA 接管）；
   重开逐值回 4.8/150/8x2；BP（bag1 8x7/9600、bag11 9x11）与 QA（9075→3600 等）同过。
2. **BP ExcludeIds 继承性语义**（T9 评审 A）：ExcludeIds=8 预置（cfg 预设+重启，面板无
   此键）——bag8 全程停官方 9x8（"排除即停管"）；"先排除已扩容行再热关留残值"场景经
   面板不可达（ExcludeIds 不在 Meta，MC_SET 被拒实证——桥只写登记键）。
3. **9130 双 mod 同关**（T9 评审 B）：**QA/RS 各加共管让位守卫**（还原时"当前值>本缓存
   ⇒不回写"；QA 自愈分支改"只升不降"——此前 QA 会把 RS 还原的 300 压回其污染缓存 150）。
   实测：QA 单关（RS 开）9130=450（QA 按污染缓存还原，RS 让位守卫保持自家目标）；双关
   **B 序（QA 先关、RS 后关）终值=900 官方确定**（RS 干净缓存最后还原）；**A 序（RS 先关
   +6.5s 间隔保证 QA 轮询命中）终值=450**——QA 仍启用期间按己方目标重写 60、其还原再落
   污染缓存 450，**关序决定终值**（彻底收敛需跨 mod 原值共享注册表，超出最小修复范围，
   如实记录）；两序恢复全 60。
4. **FridgeChill**（T10）：热关 → cold 16 行全回 0.2 + 还原日志行；重开回 16/16@0.1；
   **KeepMult 2→1 热迁移**（FC 仍启用）→ 早退区也回写 cold 全 0.2、cfg=1 落盘；回 2 复写。
   CabinetExpand RestoreAllSizes 运行中路径随套件组成变化 SKIP（见上）。
5. **CabinetEverywhere**（T11）：热关 → 白名单集合 710→1（evidence 活计数）+ CE_gate_trace
   `RemoveAll` 移除前后计数行；重开重注入 710。RoleUnlock General 热关 → Talent 123 /
   花瓶 0/0 回官方 + RU_gate_trace 还原行；两开关（ClearPool/VaseRecipe）面板热切换
   123/0-0 ⇄ 77/1-1 双向 + cfg 落盘。
6. **PlanPick/PDS**：cfg 直写（false→true 落盘核对）+ 快照回推 + 行置灰/停用横幅
   （注入 UI 级留 T17 存档轮）。

过程实录（第 1 轮 23:12-23:19）：跑到一半被作者接管会话（面板 DOM 消亡后 fixture 盲等
超时，FC/CEW/RU/PP 段全"未达桥"；23:18:50 作者点继续 → WebView 关闭 → fixture 中止）；
第 2 轮修 fixture（活性自愈 + FC 滑块实参 bug + QA 守卫收窄）后全绿。证据全文：
`.superpowers/sdd/2026-10-24-modcenter/task-13-report.md` 与 task-13-ev-*.txt。

## 6.8 Tier 0 第三方通用页（任务 14，2026-10-24）

接入协议单源：`docs/modcenter-integration-guide.md` §二（mcSpec 1）。本节只记实现口径与实测。

### 6.8.1 机制（ConfigBridge `BuildTp`/`FindTp`/`TrySetTp`/`SetTpEnabled`）

- **枚举与豁免**：快照时枚举 `IL2CPPChainloader.Instance.Plugins`（§4.1① 同源 API），按
  Name 排序稳定输出。豁免 = Meta 登记（套件 9）+ ModCenter 自身 + **第一方 GUID 前缀
  `com.local.survivallog.`**（覆盖 Meta 未登记的本仓 mod：CabinetBurden/EndlessGuard/
  StackLimit999 等——简报名单之外的本机实装情形，实证后扩展）+ 探针名兜底
  （ActionProbe/ViewportProbe/ConfigDumpProbe，名字含即豁免，防 GUID 换族）。
- **通用页构建**：`BasePlugin.Config` → `cfg.Keys`（现役 API；`ConfigDefinitions` 已过时）
  逐 `ConfigDefinition` 取 entry → section 分组沿用对方 cfg 节名（首现顺序）→ Item：
  `k=节.键`、`l=键名`（Tier 0 英文键名即标签）、徽标一律 `cfg`、
  `ConfigDescription.Description`→note、AcceptableValueRange/List 反射合入（§6.1 同款
  `ApplyRange`，已从 BuildItem 抽出共用）、enum→下拉/无范围 string→**文本框（新 t=`str`）**/
  数值带范围→滑条（步进兜底：整型 1/浮点 0.1）。无 entry（无 ConfigFile 或空 cfg）→
  仅启停形态（空 groups + desc 说明，同灾前加速页）。
- **写路由（通用）**：`TrySet` 解析不到 Meta 时落 `FindTp`（完整 GUID/插件名/GUID 尾段）→
  `TrySetTp`：按 section/key 直取对方 entry——**只写实际存在的键**（指南 §二核心原则，
  无登记表）；面板 key 发整展示键 k 原串，首查未中时剥一次节前缀兜底。写入触发对方
  ClampValue+SettingChanged 链（订阅即热响应），Save 走统一防抖。回执 k 用展示键原串
  （`DisplayK`：已带节前缀不重复拼——初版重复拼出 `Features.Features.X` 实坑，fixture
  回执断言拦截）、badge 一律 `cfg`。
- **启停（标记名单，重启级）**：`SetModEnabled` 解析不到 Meta 时走 `SetTpEnabled`——回用
  T6 时代机制：本插件 cfg `[Disabled]` 节懒 Bind `<guid>` bool 标记（true=已标记停用），
  快照 enabled=标记取反。**本会话对方仍在运行**（面板横幅/toast 均重启级文案，T7 已备）。
  任务 16 起换"退出时 dll 改名 `.disabled`"真禁用，名单届时退役（横幅文案语义不变）。
- **panel.js**：T7 已备第三方分组渲染（空数组安全→自动长行）与 cfg 档徽标/toast 分支；
  本任务仅新增 `str` 文本控件（ctlHtml 渲染 + change 提交整串，不逐键发送）。
  套件侧不受影响：现役套件 string 项均带 Opts（sel），无 Opts 的 string 才降级 str。

### 6.8.2 实测记录（2026-10-24，主菜单，59/59 PASS）

靶 = 作者实装第三方 4 件（`com.ivmakk.survivallog.*`：Better Trade/Compact Inventory/
Project Cook/Stats UI，全 bool entry）。`tools/_mc_fixture.js` menu 模式新增 18 项 tp 断言组
（结构以**对方 cfg 文件盘上内容为基准**对账）：

- 快照 4 件全数入列（分组=cfg 节集合、逐项 k/l/b/t/v 对盘一致；注意 cfg.Keys=内存 Bind
  序 ≠ cfg 文件 Save 序（字典/排序差异），断言按集合比对）；豁免断言：tp 内无
  `com.local.survivallog.*`、无 probe 名（ViewportProbe/ConfigDumpProbe 在装未出现）；
- 面板：第三方分类头 + 4 行（含启停开关）；BetterTrade 页渲染（页题/分组/条目/bool 控件/
  note 数与快照自洽）；
- 交互：`General.Verbose` false→true（MC_SET 形态 guid/section/key=整串/value=布尔 →
  OK 回执 k=`General.Verbose` badge=`cfg` → 对方 cfg 防抖落盘 true → 写回 false 精确还原）；
- 启停：标记停用 → 快照回推 enabled=false + 行置灰 + 重启级横幅（"本次会话仍在运行，
  下次启动后不再加载"）→ modcenter cfg `[Disabled] com.ivmakk...bettertrade = true` 落盘
  → 恢复 → false；
- str 控件：合成快照（真实渲染路径）→ 文本框渲染 + change 提交 MC_SET（string 值）→
  MC_OPEN 刷新后合成行清除（C# string 写路径由 ovr Overrides 实弹久证）；
- 遗留 41 项全回归 PASS（侧列行数断言改自适应 10+N tp；tp 空占位断言改与 tp 数一致性）。
- 副作用披露：modcenter cfg `[Disabled]` 节经本轮 Save 后，T13 时代的 9 条套件残留项被
  BepInEx 自然清出（只写现役 Bind 项）——现该节仅 4 条 tp 标记（全 false 惰性），
  作者原计划"顺手删"的残留自动了结。
- 过程杂记：第 2 轮启动空跑（上局强杀后 webview 孤儿占 9222、新进程未起，fixture 误连
  孤儿后随其中止）——以 `ModCenter_loaded.txt` 时间戳为新一轮就绪判据重跑即过；
  第 1 轮 1 FAIL 即上文 `DisplayK` 重复拼前缀实坑，修复后全绿。

## 6.9 Tier 1 声明式清单（任务 15，2026-10-24）

接入协议单源：`docs/modcenter-integration-guide.md` §三（字段表即行为规范）。

### 6.9.1 机制（`ThirdParty.cs` 清单发现与合并层，叠在 §6.8 Tier 0 之上）

- **发现**：`BuildTpMod` 先按 Tier 0 构建，再交 `ThirdParty.Apply`——插件实例
  `GetType().Assembly.GetManifestResourceNames()` 匹配 `modcenter.json`（精确名或
  `*.modcenter.json` 后缀均认：指南 csproj 片段不带 LogicalName 时资源实名是
  `根命名空间.modcenter.json`）；同一程序集多份清单**取第一个**（数组序），其余忽略记日志。
  清单逐插件解析一次入缓存（会话不变）。
- **校验容错（指南"绝不报错"承诺）**：JSON 解析失败 / `mcSpec` 缺失或不认识（≠1）/
  `groups` 整段缺失 → **整单忽略降级 Tier 0**；清单键在实际 cfg 不存在 → 该项隐藏
  （容忍版本差异，计入日志）；组缺 title/项缺 key 跳过该组/该项；display/summary 缺项
  逐字段回落 Tier 0 值。`id` 与 GUID 不一致只记日志不拒（清单内嵌于该 dll 即归属该插件）。
- **合并渲染**：display/summary 覆盖显示名/简介（`en` 恒为插件名，短名路由不变）；
  groups **全量重组**——Tier 0"cfg 节名分组/键名即标签"被清单替代，**清单没列的键
  不出现**；组/项按 `order` 权重排序（小者在前，缺省 0，LINQ 稳定排序保清单序）；
  项的 label/note（优先于 entry Description）/min-max-step 按清单覆盖 Tier 0 推断值
  （范围覆盖**优先于** AcceptableValueRange 反射，缺省回落反射——指南字段表）；
  类型/当前值/选项仍来自 entry 反射；步进与"无范围 string→str"兜底同 Tier 0。
  整组项全隐藏 → 空组不渲染。
- **徽标归一**：指南 §六词汇 `now/reload/cold/缺省 cfg` → 面板契约
  `now/reload/reboot/cfg`（Payload.ItemInfo.B 口径，cold→reboot）；未知值诚实落 `cfg`。
  `MC_SET_OK` 回执 badge 经 `ThirdParty.BadgeOf` 取清单徽标（Tier 0/未列项回退 cfg）；
  查表 `Badges`（展示键→归一徽标）在 `Apply` 渲染每项时填充——首测漏填致回执恒 cfg，已修。
- **softDisable**：清单声明暂存（`SoftDisableOf`），任务 16 启停两层语义消费；快照
  `ModInfo` 增量字段 `tier`（Tier 1 输出 1，Tier 0 省略）供面板/fixture 判别。
- **写路由零改动**：Tier 1 项展示键仍 `节.键`，面板发整串 k，`TrySetTp` 剥前缀兜底
  既有链直用；注意 manifest min/max 只覆盖**显示**范围，实际写入仍走对方 entry 的
  ClampValue（如清单 0.5-2.0 覆盖显示、entry AcceptableValueRange 0.8-1.2 仍钳写入——
  BepInEx 语义如实保留，面板滑条在两者交集外拖动会被对方 cfg 钳回）。
- **panel.js 零改动**：分组/标签/徽标渲染 T7 已备，清单页全部由快照数据驱动。

### 6.9.2 实测记录（2026-10-24/25 主菜单，靶 = 2 个一次性假第三方 dll）

靶工程 `.superpowers/sdd/2026-10-24-modcenter/t15-target/`（ViewportProbe 插件壳副本，
**验后删除不发布**）：`Tier1Demo.dll`（GUID tier1.demo，内嵌 mcSpec 1 清单：中文
display/summary/softDisable=true/两组带 order + 中文 label/badge(now/reload/cold)/
min-max-step 覆盖 + 幽灵键 UI:Ghost 故意不 Bind + 未列键 UI:Secret 故意 Bind）与
`Tier0FallbackDemo.dll`（GUID tier0.fallback，两份 mcSpec 99 清单：精确名 LogicalName +
后缀名重复资源）。`tools/_mc_fixture.js` menu 模式新增 7 项 t15 断言（无靶机器 SKIP，
不计失败；T14 tpStruct 组自动跳过 tier=1 页）。关键事实见 task-15-report：
tier=1 标记/display"Tier1 示例"/summary 覆盖；分组 order（显示1<性能2，清单序反排）；
幽灵键与未列键双隐藏；Scale 清单范围 0.5-2.0/step 0.05 覆盖反射 0.8-1.2 + badge
cold→reboot 文案；RefreshMs 无 AcceptableValueRange 纯清单范围 100-5000/step 100/badge
now；滑条写路由（MC_SET 整展示键 → 回执 badge=reboot → 对方 cfg 落盘 1.1 → 还原 1）；
mcSpec 99 双清单 → Tier 0 兜底（tier 省略/Secret 照常显示/反射范围 0.8-1.2）。

## 6.10 Tier 2 深度对接 + 重启级 dll 禁用（任务 16，2026-10-24/25）

接入协议单源：`docs/modcenter-integration-guide.md` §四（Contracts）/§五（启停语义）。

### 6.10.1 机制

- **Contracts 编入**：指南 §4.1 接口源码原样编入 `Contracts/ModCenterIntegration.cs`
  （命名空间 `ModCenter.Integration`，接口 `IModCenterIntegration` + `McItemState`）。
  **发现机制 = 反射按类型名 duck-typing，禁止接口强转**——第三方 mod 编入的是自己那份
  接口副本，跨程序集接口类型互不可 cast。`ThirdParty.T2Of`：`asm.GetType("ModCenterIntegration")`
  （未中再按简单名扫 `GetTypes()` 兜底）→ 要求 `public static bool OnEnabledChanged(bool)`；
  静态属性 `McSpec` 在场且 ≠1 → 不识别降级 Tier 1/0（接口冻结）；`QueryStates` 可选。
  逐插件解析一次入缓存；任何缺失/异常都只是"没有 Tier 2"，绝不报错（指南承诺）。
- **软停回调**：`ThirdParty.FireEnabledChanged(info, enabled)` 反射调
  `OnEnabledChanged.Invoke(null, new object[]{enabled})`。返回 false/异常 → 降级重启级
  并标记 Degraded（本会话后续快照 Soft 同步回落）。`SetTpEnabled` 停用分流（指南 §五）：
  Tier 2 回调返回 true（软停，行为立即停止）或清单 `softDisable=true`（对方自实现 gate）
  → 面板软停语义文案；否则重启级文案。**三层统一写 [DisabledTp] marker**——Tier 2 行的
  "下次启动不再加载"由 dll 改名兜底（ModCenter 不依赖对方自实现 Load 首行 gate）。
- **状态回报**：`ThirdParty.QueryStatesOf(info)` 反射调 `QueryStates()` 读 `Key/State/Reason`
  属性（duck-typing，上限 64 项）；`ConfigBridge.ApplyStates` 合入快照：Key "Section:Key" →
  项 K "Section.Key" 置灰（`ItemInfo.Fail`=Reason，panel.js 锁控件+红字）；空 Key=整 mod 级
  （Desc 追加 ⚠）。State 只认 ok，非 ok 一律 failed。
- **DllDisabler**（重启级禁用执行方，指南 §五"工程细节"）：
  - **marker**：ModCenter 自身 cfg `[DisabledTp]` 节，key = **plugins 目录下 dll 相对名**
    （dll 禁用后插件不在链上，GUID 无从解析）。T6/T14 的 `[Disabled]<guid>` 名单退役
    （无读取方；游戏侧 cfg 的 9 条孤儿裸行随本任务清理，4 条 tp GUID 惰性条目保留）。
  - **退出执行**：运行中 dll 被锁（OnApplicationQuit 时本进程仍持有），改名交**分离进程**
    `powershell -NoProfile -WindowStyle Hidden -EncodedCommand <base64(UTF-16LE)>`
    （脚本 ASCII-only；EncodedCommand 彻底规避引号/反斜杠转义）循环 Rename-Item
    （`<名>.dll`→`<名>.dll.disabled`，**只改名绝不删除**；`-NewName` 喂 `Split-Path -Leaf`
    ——BepInEx 递归加载 plugins 子目录，marker 记的是子目录相对名如 `Pack\Mod.dll`，
    带路径喂 `-NewName` 必败→禁用静默失败，Fix Round 1 P2）直到成功/目标消失，超时 10 分钟。
    退出钩子 = **双钩子幂等**（`ModCenterTicker.OnApplicationQuit` 消息 +
    `Application.add_quitting` 事件，`RunQuitHooks._quitDone` 守卫只跑一次）；
    顺序 = 先 `ConfigBridge.FlushDirty()`（marker 必须先落盘）再拉分离进程。
    名字边界守卫 `IsSafeRelName`（剔绝对路径/盘符/`..` 越界；`'` 等合法字符单引号翻倍转义放行——
    EncodedCommand 无注入面，Fix Round 1 P3-1）。
  - **启用** = 反向改名：dll 未加载不锁，直接 `File.Move`（marker 存相对名→直接拼全路径，
    子目录天然支持）；失败才分离进程兜底（2 分钟，同样 Leaf）。同时撤销残留补拉进程
    （`Armed` 句柄表 Kill——玩家当场反悔时改名意图已失效）。
  - **强杀残留自愈**：`ScanOnLoad`（Load 时）解析自身 cfg 的 [DisabledTp] true 项 × plugins
    目录对照——marker=true 且 `.disabled` 在场 → 预 Bind（快照行/再启用路由就绪）；
    marker=true 但 dll 还在（退出钩子没跑）→ **残留**：补拉一次分离进程（本会话结束后
    生效）+ 面板下次打开 `MC_TOAST` 提示一轮（panel.js `toastRaw` 原文展示）+ Tier 2 者
    `ReapplySoft` 软停回调重放（Ticker 首帧——此时 Chainloader 全插件就绪）。
  - **缺席行**：`AbsentDisabled()` **递归**扫描 plugins `*.dll.disabled`（BepInEx 递归加载
    子目录——非递归扫不到子目录禁用文件，缺席行永不出现，Fix Round 1 P2；排除第一方
    `SurvivalLog.*`）→ 快照补 enabled=false 的"仅启停"行（guid=dll 相对名**剥 `.disabled` 后缀**，
    `Absent` 标记），供玩家重新启用（`TrySetAbsent` 路由按相对名拼全路径反向改名）。
    注意 guid 可含 `\`/`'`——CDP/fixture 查询一律属性值比对，勿拼进 CSS 选择器字符串。
- **panel.js 两层语义**：第三方停用横幅/toast 三档——缺席行（"dll 已禁用：当前未加载"）/
  软停（"行为已立即停止"）/ 重启级（"本次会话仍在运行"）；`MC_TOAST` 事件 → 原文 toast。

### 6.10.2 实测记录（2026-10-25，三轮，靶 = Tier2Demo + Tier0FallbackDemo 一次性 dll）

见 `.superpowers/sdd/2026-10-24-modcenter/task-16-report.md`（菜单断言组 + 正常退出改名 +
重启不加载 + 再启用恢复 + taskkill 强杀残留 toast/自愈全链路）。

## 7. 迭代维护清单

- 2026-10-24 实机 UI 三修（bugfix round 2，作者试用三问题）：①ESC 入口出现慢（≤2s 轮询
  等待）→ `MenuEntryInjector.cs` 改事件驱动常驻：EntryJs 一次性安装 MutationObserver
  （body 子树）+ 目标 iframe load 钩，回调当场跑幂等 install（锚点逻辑照旧，ANCHORS.md
  单源不变）；C# 2s 慢拍降级为守卫自愈（`__mcObserver`/`__McInstalled`/`__mcSelfCheck`
  三分量极小表达式，失守才重发完整引导）；DrainOutbox 节奏不动。②滑条轨道不可见（游戏
  WebView 不画 accent-color 原生默认轨道）→ panel.js 显式 `-webkit-appearance:none` +
  自绘 runnable-track（4px #2a2f3d）/ slider-thumb（13px 圆 #e8a33d，margin-top -4.5px
  居中）+ -moz 双保险。③动作加速"新增覆盖"ID 栏 number 框原生加减钮 → 改
  `type=text` + `inputmode=numeric`（去 min；parseInt 校验与防重复逻辑不变；补
  `.__mc-ovr-add input` flex 覆盖防通用 text 规则拉伸）。锚点/挂钩点零变更（moddeps.json
  不动）。实测见 `.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-report.md`。
- 2026-10-25 任务 18（文档终版 + 发版接线，零代码改动）：状态头改"全 18 任务实机定稿"口径
  （三档接入/启停两层/双列表入口/键盘门/六模式夹具 + task-17-report §5 验收清单衔接）；§3 补
  `mc_fixture.js` 六模式条目；§6.5.1 补 B3；§6.6.1 看门狗判据文案对齐 T17 修复；§7 T17 条目补
  B3/B4/验收清单。PreDisasterSpeed DESIGN 补 cfg 表与 ModCenter 启停位记载（既有键复用）；
  指南 §三补 BOM 勘误（清单文件存 UTF-8 无 BOM，BOM 会被容错层静默降级 Tier 0）。
  publish.ps1 `$suiteMods` 与 installer.iss 组件表加 ModCenter——两文件系作者脏文件，
  **只改内容不代提交**（改动行清单见 task-18-report）。moddeps.json 本任务补登 T13 证据表
  5 项漏登读依赖（`Config_Action.During` / `Config_Bag.Size,Burden` / `Config_Furniture.ColdRate,BagId` /
  `Config_Talent.PlayerSelectId` / `Config_ProductionList.craft_level,show_level`——WriteEvidence 只读，
  modcompat 实跑 ALL GREEN）；无新挂钩点/字节签名；T4 登记的 Config_RobotAttribute 读依赖在役不变。
- 2026-10-25 任务 17：Meta 柜子位对齐作者 CabinetSize v3 终表。原条目（`Expand.WidthMult/
  FridgeWidthMult/VehicleWidthMult` 联动倍率 + `UiFit.*`）是作者 CabinetSize 倍率原型期（c899f20
  换位时点）的形状；作者 aa20303→d02e985 演进为**档位模型**（Bind 面 = `Global.MaxWidth/
  MaxHeight/Preset/ExcludeIds` + `Tier.Small..XLarge.Width/Height` + `Debug.*`，无 `Expand`/`UiFit`
  节）后旧条目全部悬空——快照按 Def 兜底展示虚构值、全部写被拒（T16 run4 实证 cabinetsize cfg
  无 Expand 节、联动对 `orig:[null,null]` 静默跳过）。修正：Meta 柜子页改 10 键（上限 2 + 四档
  宽高 8，int 滑条，Badge now——全部键已接 SettingChanged→Rerun 热改）；`Preset`（勿手改）/
  `ExcludeIds`（同 BP 口径不暴露）/`Debug.*` 不进面板；UiFit 组撤销（v3 用官方自适应，§6.5.2
  随之过时）。附带语义记录：CabinetSize 尚未 Bind `General.Enabled`，面板启停该 mod 走"toggle
  拒绝 + 快照回推自愈"路径（HandleToggle 恒回推，行视觉瞬时翻转后复位；接 gate 属作者侧后续）。
  回归夹具定稿 `tools/mc_fixture.js`（`_mc_fixture.js` 退役删除；tp 组净机 SKIP 语义）。
  **T17 看门狗修复（实锤产品 bug）**：孤儿看门狗旧判据 `S.root.isConnected` 在本引擎对被移除
  iframe 的孤儿文档恒 true（与 DOM 规范不符，CDP 实测）——宿主 iframe 被移除时看门狗永不触发，
  开面板进对局（或 ESC 收暂停菜单）键盘门漏开（enterworld 实证 kb 恒 on，人工 ESC 才还原）。
  改判 `宿主 doc.defaultView.frameElement`（iframe 被移除或文档被导航均 null，两死法实测）
  + `frameElement.isConnected`（Root 文档内元素，语义正常）。§6.6 孤儿看门狗机制段随之生效。
  T17 另两修：③ `ConfigBridge.Match` 完整展示键逐字命中**前置放行**（B3，§6.5.1 路由段补记）
  ——点分节名键（首例=柜子 v3 `Tier.Small.Width`，routeOf 首点切分出 section="Tier"）此前
  必被节守卫全拒；④ CabinetSize 侧 `ApplyHotKeys` 与本 mod 防抖落盘竞态（B4：陈旧文件 mtime
  事件被 Ticker Poll 在新写之后处理，旧盘值被当"记事本热改"赋回，UI 与 cfg 脱钩）——修在对方
  mod（`_lastBindChangeUtc` 陈旧文件闸，见其 DESIGN），**作者复核项**。延后项 6 项全闭环与
  作者人工验收清单（6 项体感）见 task-17-report.md §3/§5。
- 2026-10-25 任务 16 Fix Round 1（评审 P2+P3）：P2 子目录 dll 禁用——分离进程 `-NewName` 改喂
  `Split-Path -Leaf`（带路径名必败→静默失败+重复残留 toast）、`AbsentDisabled` 改递归扫描且
  返回相对名**剥 `.disabled`**（原递归化时首版漏剥后缀，实机抽查抓出即修）、启用反向改名同 Leaf；
  P3-1 名字过滤白名单删除→`IsSafeRelName` 边界守卫（仅剔绝对路径/盘符/`..`，`'` 翻倍转义放行）；
  P3-2 fixture t14 组排除缺席行（`TPL` 过滤，防净机残留 .disabled 误报）+ clickRow/tpClickToggle/
  tpRows/offRow 改属性值比对（guid 含 `\`/`'` 时 CSS 选择器失效，实机抓出）+ tpBool 还原补点
  加按钮态前置检查（T15-run3 同款点击竞态，防误补点翻回错值）。冒烟：分离进程脚本带
  子目录+空格+`'`+`;` 假名双向往返 PASS；实机子目录假 .disabled 缺席行出现→面板启用恢复 PASS；
  fixture run2/run4 均 70 PASS/6 SKIP/0 FAIL。实测补记见 §6.10.2 与 task-16-report Fix Round 1。
- 2026-10-25 任务 16：Tier 2 深度对接 + 重启级 dll 禁用。新增 `Contracts/ModCenterIntegration.cs`
  （指南 §4.1 接口源码原样编入，ModCenter.Integration 命名空间）与 `DllDisabler.cs`
  （[DisabledTp]<dll 相对名> marker / 退出双钩子 FlushDirty→分离进程 EncodedCommand 循环
  Rename-Item 只改名不删除 / 启用反向改名+残留补拉撤销 / ScanOnLoad 残留自愈+MC_TOAST
  一轮提示 / AbsentDisabled 缺席行 / TrySetAbsent 再启用路由）；`ThirdParty.cs` 增 Tier 2
  反射 duck-typing 发现（类型名+静态签名，禁接口强转）与 FireEnabledChanged/QueryStatesOf/
  SoftOf（返回 false 降级 Degraded）；`ConfigBridge.cs`：SetTpEnabled 三层分流（Tier2 回调/
  清单 softDisable/重启级，统一写 marker）、BuildTpMod 真值源换 marker+Soft/Absent 标记+
  ApplyStates 状态回报、BuildTp 补缺席行、MC_OPEN 残留 toast、MarkDirty 收 internal；
  `Payload.cs`：ModInfo.Soft/Absent、ItemInfo.Fail；`ModCenterPlugin.cs`：Load 接 ScanOnLoad+
  quitting 双钩子、Ticker 首帧 ReapplySoft、OnApplicationQuit→RunQuitHooks；panel.js：第三方
  停用三档横幅/toast（软停/重启级/缺席）、MC_TOAST→toastRaw、failed 项置灰锁控件。
  旧 [Disabled]<guid> 名单退役（游戏侧 cfg 9 条孤儿裸行已清理，4 条 tp 惰性条目保留）。
  实测见 §6.10.2。
- 2026-10-24 任务 15：Tier 1 声明式清单。新增 `ThirdParty.cs`（清单发现=程序集内嵌资源
  精确名/后缀名匹配、重复取第一个；解析容错=mcSpec 不识别/解析失败/缺 groups 整单降级
  Tier 0、键不在 cfg 项隐藏、逐字段回落；合并=display/summary/groups/label/note/badge/
  min-max-step/order 覆盖、cold→reboot 徽标归一、softDisable 暂存 `SoftDisableOf` 供 T16）；
  `ConfigBridge.cs` 三处：`Entry/ApplyRange/TypeName` 收 internal 供共用、`BuildTpMod` 接
  `ThirdParty.Apply`（false 保持 Tier 0 形态）、`HandleSet` tp 回执 badge 走
  `ThirdParty.BadgeOf`；`Payload.cs` `ModInfo` 增 `tier?` 字段（Tier 1=1）；panel.js 零改动。
  `tools/_mc_fixture.js` 增 7 项 t15 断言（净机 SKIP 语义）+ tpStruct 组跳过 tier=1 页。
  实测见 §6.9.2。
- 2026-10-24 任务 14：Tier 0 第三方通用页。`ConfigBridge.cs` 增 `BuildTp`（枚举插件→豁免→
  通用页：section 分组沿用对方 cfg 节名/键名即标签/徽标一律 cfg/Description→note/
  ApplyRange 反射合入抽公用/无范围 string→str 文本框/步进兜底）、`FindTp`+`TrySetTp`
  （通用写路由：只写实际存在的键，展示键原串剥前缀兜底）、`SetTpEnabled`（启停回用
  T6 时代本插件 cfg `[Disabled]` 标记名单，重启级；T16 换 dll .disabled 真禁用）、
  `HandleSet` 回执 k 用 `DisplayK` 展示键原串（防节前缀重复拼）；`panel.js` 增 str 文本
  控件（change 提交）；`tools/_mc_fixture.js` menu 模式增 18 项 tp 断言组 + 侧列行数/
  空占位断言自适应。实测见 §6.8.2。
- 2026-10-24 任务 13：启停接线升级。`ConfigBridge.SetModEnabled` 从本插件 cfg `[Disabled]`
  名单改为**直写各 mod 自己的 `General.Enabled`**（`Meta.cs` 新增 `MetaMod.EnabledSection/
  EnabledKey`，默认 General:Enabled，全套件无例外）；写入触发目标 mod 现役 SettingChanged→
  Rerun 热还原/重应用链；快照 enabled 真值源改为各 mod entry；名单机制整体移除（`Marker()`
  删除，旧 cfg 残留 `[Disabled]` 节惰性无人读）。`MC_EVIDENCE` 扩为多表断言快照（§6.2）。
  `tools/_mc_fixture.js` 新增 `toggle` 模式（任务 13 热还原全断言，供 T17 回归），menu 模式
  toggle 持久化断言改 PDS 自身 cfg。实测见 §6.7。
- 2026-10-24 任务 8：键盘门复用。新增 `FocusTick.cs`（面板开 `SetKeyboardEnabled(true)` +
  IME On、关双还原；门控 = MC_OPEN/MC_CLOSE 消息而非 IsPageActive——面板非官方页；
  `_imeSaved` 会话标记 + 层丢失就地还原 IME 两处对 PlanPick 的加固；沿写自证
  `BepInEx/ModCenter_kb.txt`，Load 清空）；`ModCenterPlugin.cs` Ticker 增 0.3s（unscaled）
  FocusTick 泵 + Load 清自证；`ConfigBridge.cs` MC_OPEN/MC_CLOSE 分别置开合；`panel.js`
  Root 侧 1s 孤儿看门狗（面板根失连补发 `MC_CLOSE{why:'host-doc-gone'}`）；csproj 增
  `UnityEngine.InputLegacyModule` 引用（`UnityEngine.Input` 所在模块，PlanPick 同款）。
  验证工具 `tools/_mc_kb_cdp.js` + `tools/_mc_kb_type.ps1`。实测见 §6.6.2 / task-8-report。
- 2026-10-24 任务 7 Fix Round 1（评审 NO-GO 三项）：①C1——`sendSet` 改发完整展示键 `k` 原串
  （初版剥前缀的联动 key 在桥 Match 三候选全不命中，背包/柜子 4 联动滑条写必败；
  §6.5.1 路由段重写），fixture 补 4 条联动端到端断言（消息形态→OK 回执→两键同值落盘→还原）；
  ②`__mcClose` 清节流句柄（备忘 3）；③DESIGN 补观感取舍注（备忘 2）。实测见 task-7-report
  Fix Round 1。
- 2026-10-24 任务 7：面板 UI 完整移植。`panel.js` 空壳→完整面板（原型 v4 `<style>` 面板段 +
  renderSide/renderCfg/ctl/toast/toggle/ovr 逻辑；span 开关改 button、行 div 带
  data-interactive 以进 clickThrough；ST.sel 记忆选中；toggle 乐观翻转+`__mcSnapSeq` 真推送
  判据；滑条 100ms 节流；ovr 面板侧序列化 canonical 排序；tp 空占位）；`ConfigBridge.cs`
  HandleOutbox 逐条 try（单条异常不弃同批后续，§6.2 旧债收口）；新增
  `tools/_mc_fixture.js`（menu/loadgame/outsetting 三模式装机断言）与 `tools/_mc_sendkey.ps1`
  （游戏窗口 ESC 注入）。实测：主菜单 41/41、OutSetting 4/4（遗留 A）、PlanPick 反射范围断言
  （遗留 D）、ovr 序列化往返、UiFit.Mode 拒绝路径（§6.5.2）；DESIGN 新增 §6.5。
- 2026-10-24 任务 6：配置桥正式化。新增 `Meta.cs`（九表）/`Payload.cs`（DTO+手写 JSON）/
  `ConfigBridge.cs`（快照/写/启停名单/消息分发/Save 防抖/MC_EVIDENCE）；`ModCenterPlugin.cs`
  Ticker 接 Pump、BridgeProbe.Table 转 internal、探针注释去已证伪期望值（真原值 2.4 口径）；
  `MenuEntryInjector.DrainOutbox` 回读入队；`panel.js` 增 `__mcRender/__mcEvent` 存根；
  DESIGN §4.1④ 样例修 CS1023、§2/§4.3-4 措辞清理；新增 §6（协议/防抖/实测记录）。
- 2026-10-24 任务 5：注入骨架。`WebBridge.cs`（通道+Eval）、`MenuEntryInjector.cs`
  （双锚点入口 + outbox 轮询）、`panel.js`（占位→空壳面板：遮罩/标题/✕/ESC/outbox 契约）、
  Ticker 接线；主菜单阶段 CDP 18 项全绿 + 日志回读。关键结论固化 §5（Root 无 clickThrough →
  面板挂宿主 iframe；button UA 重置按属性声明；OutSetting ESC 归 Unity）。
- 2026-10-24 任务 4：反射桥 API 实证。`ModCenterPlugin.cs` 增 `Debug.Probe` cfg 门控 +
  `BridgeProbe` 三拍探针（枚举→Config→Value 写→热生效验表→还原落盘）；结论固化 §4。
  探针代码常驻为诊断功能（默认关）。
