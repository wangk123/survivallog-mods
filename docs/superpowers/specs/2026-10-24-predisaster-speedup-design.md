# 灾前加速（PreDisasterSpeed）—— 设计文档

> 2026-10-24 头脑风暴定稿（Spike 取证 + 作者拍板参数）。
> 实施时结论落入 `SurvivalLog.PreDisasterSpeed/DESIGN.md`（套件文档规则），本文件保留为决策记录。
> **版本号由作者在 version.json 定义，本文不预先指定。**

## 1. 背景与目标

官方在灾后（CoreUI1 HUD）提供点击时钟旁倍速条：慢速/原速/2倍/3倍/暂停五档。灾前
（CoreUI0 HUD）无此入口，玩家只能近实时（章节时标 5）等待倒计时。

目标：**把官方倍速功能原样覆盖到灾前**——同一套引擎、同一套档位语义、同一套倒计时
同步行为，仅补一个灾前 UI 入口。不自制任何时间逻辑。

## 2. 核心决策记录（均已作者拍板）

| 决策点 | 结论 |
|---|---|
| 档位集合 | 官方五键原样：慢速(4)/原速(1)/2倍(2)/3倍(3)/暂停(0)，顺序同 CoreUI1 |
| 倍率值 | **全按官方章节表，不改表**。灾前章节（ID=1）`SpeedUp1=2、SpeedUp2=32`——"3倍"键在灾前实际是 ×32（有效推进 160 > 灾后常速 120）。作者知情并接受；实测若嫌猛，备选项=内存表临时编辑 `SpeedUp2`（默认不做） |
| 暂停档 | 保留（官方暂停=时标 1e-4，倒计时/动作全冻结，等于挂机暂停） |
| 实现形态 | 方案 C：JS 注入官方样式按钮到 CoreUI0 + mod C# 订阅 Vuplex 事件桥接 + 调官方 `NotifyGlobal(10001)`。**零 Harmony、零表改动、零字节补丁** |
| 倒计时同步 | 不做任何额外工作——官方原生（见 §3.4） |

## 3. 官方机制实证（gamekb/2026-10-23_26157732）

### 3.1 加速引擎无阶段门禁

`GameTimeManager.ChangeGameSpeed(int)`（RVA 0x26D5A90，已反汇编 `tools/_dis_speedchange.txt`）：
仅校验 `speed ≤ 4`，无任何灾前/灾后判断。档位分派：

| speed | BattleTimeScale | 备注 |
|---|---|---|
| 0 | 0.0001（PauseTimeScale） | 暂停，SyncPauseTipUI 推暂停提示 |
| 1 | 1.0 | 原速 |
| 2 | ChapterSpeedUp1（章节表） | 灾前=2，灾后=2 |
| 3 | ChapterSpeedUp2（章节表） | 灾前=32，灾后=4 |
| 4 | 不改 scale，置 IsSlowGearActive | 空闲 3s 后 0.3 倍（IdleSlowTargetScale） |

写入后调 `TimeManager.SetBattleTimeScale` 全局生效；时标是章节基础时标之上的**乘数**
（原速=×1，非绝对值）。

### 3.2 唯一门禁在 UI 显示层

CoreUI1 倍速条由 `speedUpActive`（C# `ObjSpeedUpActive` ← `PermissionsManager.FuncMap[FuncSwitch.SpeedUpEntry=5]`）
控制 `v-if`。灾变时翻 true。**点击事件链全程无校验**：JS `UnitySendEvent('SPEED_CHANGE',{speed})`
→ VM lambda（0x2B18170，仅 JSON 解析）→ `Ac_CoreUI1_SpeedChange.SendAction` → reducer
`RA_SpeedChange`（0x2AEFE10）→ **`EventDispatcherTools.NotifyGlobal(10001, speed)`** →
GameTimeManager 以委托订阅 10001（即 ChangeGameSpeed，故 E8 直扫无调用方）。

### 3.3 灾前 HUD（CoreUI0）无 SPEED_CHANGE 绑定

`WebUI_CoreUI0_Event` 事件表（dump.cs 271774）无 SPEED_CHANGE——事件发给 CoreUI0 页无人接。
故纯 JS 注入不成立，必须有 C# 桥（本设计 §4.2）。

### 3.4 倒计时同步加速是官方原生行为

- `ChangeGameSpeed` 末尾调 `NotifyCountDownTimerScaleSync`（0x26D6FC0）：`cmp [rbx+64h],1`
  ——**仅 WorldStateType==PreDisaster(1) 时**重发 `EnumNotify_Disaster.CountDownTimerStart(11003,
  remain, total)`。
- CoreUI0 JS 倒计时速率 = `decreasePerSecond × BattleTimeScale`（CoreUI0.html 2100 行），
  暂停状态走官方 `WebUI_CoreUI0_PauseMsg`。
- 这套同步链为灾前场景而写（灾前睡眠临时加速一直在用），手动加速复用即得。

### 3.5 桥接协议（Root.html 实证）

iframe `window.parent.postMessage({type, data, sourcePageId})` → Root **对任意 type 无条件
转发**（Root.html 428-436 行）→ `window.vuplex.postMessage("3\x1EpageId\x1EeventType\x1Ejson")`
→ Unity 侧 `WebUILayer.OnMessageFromJS`（dump.cs 252361）解析分发。mod 订阅同一
Vuplex `MessageEmitted` 多播事件即可收到，与官方处理并行、互不干扰。

## 4. 设计

### 4.1 UI 注入（仿 PlanPick 巡检注入器）

- 2s ticker（ClassInjector 注册 MonoBehaviour，PlanPick 同款）→ 取 Root WebView
  （`BaseSingleton<ReduxUISystem>.Instance.GetWebUILayer().canvasWebViewPrefab.WebView`）。
- 幂等 eval 内嵌资源 `page.js` 进 `CoreUI0` iframe（安装守卫标记随 iframe 重建自愈）。
- `page.js`：注入五键条（DOM/CSS 照抄 CoreUI1 `speed-controls`，图标语义慢速/▶/▶▶/▶▶▶/⏸），
  挂到 CoreUI0 钟表/倒计时区域。点击 → `window.parent.postMessage({type:'PDS_SPEED',
  data:{speed:N}, sourcePageId:'CoreUI0'})`。
- 仅在 CoreUI0 可见时安装（iframe 隐藏/不存在=不装，天然只在灾前存在）。

### 4.2 C# 桥（事件订阅，非挂钩）

- 订阅 WebView 的 `MessageEmitted`（Vuplex 多播），解析 `3\x1ECoreUI0\x1EPDS_SPEED\x1E{...}`。
- 命中后调 `EventDispatcherTools.NotifyGlobal((EnumNotify_SceneRat)10001, speed)` ——
  与官方 reducer 同一入口同一参数。泛型在 interop 的确切调用形态构建时定（机制已实证，
  参数实传 int）。

### 4.3 状态回显

- 注入 JS 自听 iframe 的 window message 中官方已有的 `WebUI_CoreUI0_TimeScaleMsg`
  （data.timeScale）→ 按 scale 映射高亮当前档：<0.001→暂停、1→原速、
  ChapterSpeedUp1→2倍、ChapterSpeedUp2→3倍；其它值（睡眠临时/空闲 0.3）→全部不高亮。
- **慢速档(4)例外**：它不改 BattleTimeScale（见 §3.1），无法从 TimeScaleMsg 推导，
  高亮用本地点击态，收到任一官方 scale 同步即清除。
- 章节倍率值（2/32）由 C# 侧启动时从 Config_Chapter 读出随注入 payload 推给 JS，
  JS 不硬编码。

### 4.4 灾变过渡自愈

ticker 发现 CoreUI0 消失（灾变切 HUD）且 mod 记录的最后档位 ≠ 1 时，自动补发
`NotifyGlobal(10001, 1)` 归原速，防止倍率带入灾后。灾后官方倍速条接管。

### 4.5 cfg

- `Enabled`（默认 true）：总开关，关闭=不注入不订阅。
- `Verbose`（默认 false）：调试日志。

## 5. moddeps.json 登记（硬规则 11）

新增条目（本 mod 无 Harmony/表/字节依赖，登记的是 API/协议依赖）：

| 依赖 | 类型 |
|---|---|
| `GameCore.HotUpdate.EventDispatcherTools.NotifyGlobal(enum, T1)` | 方法签名（落地入口） |
| `EnumNotify_GameTime.ChangeGameSpeed = 10001` | 枚举常量 |
| `WebUI_CoreUI0` 页面存在性 + `WebUI_CoreUI0_TimeScaleMsg`/`PauseMsg` 通道 | WebUI 页 |
| Vuplex `MessageEmitted` 事件 + Root 协议 typeId 3 | 桥协议 |
| `BaseSingleton<ReduxUISystem>.GetWebUILayer().canvasWebViewPrefab.WebView` | 取 WebView 路径 |

## 6. 风险与备选项

| 风险 | 评估/对策 |
|---|---|
| 灾前 ×32 观感过猛 | 官方表值，作者已知情。备选=内存表临时编辑 `Config_Chapter[1].SpeedUp2`（PlanPick 技法），默认不做 |
| 睡眠临时加速互作 | 官方行为：手动档触发 `CancelTempSpeedUp`（取消睡眠加速）。如实写进用户说明 |
| NotifyGlobal interop 调用形态 | 构建期解决；若泛型实例化不可调，降级方案=反射调 `ChangeGameSpeed` 私有方法（需 entity 实例，从单例 facade 取），登记到 DESIGN 备选 |
| 动作进行中加速 | 官方引擎本就支持（灾后常态 ×4），动作进度同步缩放 |
| 反作弊 | 改的是全局推进速率而非逐物品 TimeScale 字段（CheatEvidenceType.ItemTimeScaleImpossible 针对后者）；且与官方睡眠加速同一机制 |

## 7. 测试与验证

- **静态夹具**（node，仿 PlanPick tests/）：协议解析（`3\x1E` 分包/非法输入）、
  scale→档位映射、page.js `node --check`。
- **构建绿**：dotnet build 判退出码（硬规则 8）。
- **VERIFY.md 装机清单**（作者执行）：灾前五键出现/各档生效/暂停冻结倒计时/倒计时按
  倍率同步消耗/慢速档空闲 3s 生效/睡眠加速互作/灾变过渡自动归 1x/灾后官方倍速条不受扰/
  关 Enabled 后无残留。
- 探针类取证脚本（`_rev_call.js` 等）不进发布物。

## 8. 开放项（实现时定，不阻塞）

1. CoreUI0 内五键条的具体挂载点（钟表 DOM 邻域实测后定）。
2. `NotifyGlobal` interop 泛型形态（§6）。
3. 灾前章节在无尽模式/挑战模式下的 WorldStateType 一致性（核心链路只依赖
   ChangeGameSpeed 与 CoreUI0 存在性，模式差异只影响观感验证项）。

## 9. 迭代维护清单

- 改桥协议/事件号/NotifyGlobal 签名 → 同步 moddeps.json + 本文档 §3/§5。
- 改 UI（键位/样式/挂载点）→ 同步 mod DESIGN.md + 夹具。
- 游戏更新后跑 `modcompat_check.js`，本 mod 红灯项=§5 表列。
