# MOD 管理中心 ModCenter — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（作者定号，硬规则 1）。
> 菜单锚点单源：本目录 `ANCHORS.md`（任务 1 离线定位结论；改挂钩点必须同步该文件与
> `tools/gamekb/moddeps.json`）。当前状态：**任务 4 反射桥 API 实证完成**（构建 + 装机探针
> 全链通过，结论见 §4；任务 3 骨架之上的 `BridgeProbe` 探针保留为诊断功能，cfg `Debug.Probe` 默认关）。

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
| 注入层 | （任务 5 起）Ticker 注入巡检 | 找到主菜单 / ESC 菜单页的 WebView，注入入口按钮并装载 panel.js（内嵌资源）。锚点与装载方式见 ANCHORS.md |
| 前端面板层 | `panel.js`（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`；任务 7 移植） | 面板 UI 与交互。全局名 `__mcOutbox` / `__mcDrain` / `__McInstalled` 为计划既定契约，全程一致；键盘上报任务 8 |

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

## 5. 迭代维护清单

- 2026-10-24 任务 4：反射桥 API 实证。`ModCenterPlugin.cs` 增 `Debug.Probe` cfg 门控 +
  `BridgeProbe` 三拍探针（枚举→Config→Value 写→热生效验表→还原落盘）；结论固化 §4。
  探针代码常驻为诊断功能（默认关）。
