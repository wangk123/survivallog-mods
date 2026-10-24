# SurvivalLog.PreDisasterSpeed 设计文档

> 灾前加速：把官方五键倍速条（慢速/原速/二倍/三倍/暂停）原样覆盖到灾前（CoreUI0 HUD）。
> 决策记录与全部实证依据见 docs/superpowers/specs/2026-10-24-predisaster-speedup-design.md。

## 1. 机制（为什么零 Harmony 成立）

- 官方 `GameTimeManager.ChangeGameSpeed(int)` 无阶段校验（仅 speed≤4）；倍率是章节表乘数
  （灾前章节 WorldStateType==1：SpeedUp1=2、SpeedUp2=32——"三倍"键实为 ×32，官方原设计）。
- 灾后倍速条的门禁只在 UI 显示层（PermissionsManager.FuncMap[SpeedUpEntry]），
  点击链路 VM lambda→Ac_CoreUI1_SpeedChange.SendAction→reducer RA_SpeedChange→
  NotifyGlobal(10001) 全程无校验。
- 倒计时同步加速是官方原生：ChangeGameSpeed 末尾 NotifyCountDownTimerScaleSync 仅在
  WorldStateType==PreDisaster 重发 CountDownTimerStart；CoreUI0 JS 倒计时速率=
  decreasePerSecond×BattleTimeScale。

## 2. 实现

| 部件 | 文件 | 职责 |
|---|---|---|
| 入口 | PreDisasterSpeedPlugin.cs | cfg（Enabled/Verbose）+ Harmony PatchAll + 2s PdsTicker + WelcomeTip |
| 注入 | Injector.cs | WebView 获取（ReduxUISystem→WebUILayer→canvasWebViewPrefab）；page.js 幂等 eval 进 CoreUI0；payload（章节倍率）签名变化才推；安装回执喂自愈 |
| 桥+落地 | SpeedLink.cs | **Harmony prefix 观察者挂 WebUILayer.OnPageMessage**（不拦截不修改），识别 3/CoreUI0/PDS_SPEED → 调官方 Ac_CoreUI1_SpeedChange.SendAction；CoreUI0 消失且档位≠1 自动归 1x |
| UI | page.js（EmbeddedResource） | 五键条挂 **.action-row 尾部（官方位：左下横排，兜底 .bottom-left→.top-center）**；点击 postMessage 契约；官方 TimeScaleMsg 高亮映射；慢速本地态 |

## 3. 契约（改前必读）

- page.js 契约：`__PdsInstalled` 守卫 / `__PdsBoot({s1,s2})` / 点击 postMessage
  `{type:'PDS_SPEED',data:{speed},sourcePageId:'CoreUI0'}` / DOM `#pdsSpeed .pds-gear-{4,1,2,3,0}`。
- vuplex 协议：`3\x1EpageId\x1EeventType\x1Ejson`（Root.html 无条件中继任意 type）。
- 桥只认 `3/CoreUI0/PDS_SPEED`，其余消息零接触。
- tests/run_tests.js 是 page.js 行为回归夹具，改 page.js 必须先跑绿。

## 4. 依赖（moddeps.json "PreDisasterSpeed" 条目；harmony 槽位第一项为真补丁、第二项为存在性核对）

- **GameCore.HotUpdate.ReduxUI.WebUIVm.HandleCallback(string,string)（Harmony prefix，真挂钩点；typeId=3 唯一分发点，StackLimit999 同挂点在役）**
- GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange.SendAction(int)（落地入口，存在性核对）
- _Config_Chapter_Dict / Config_Chapter.{WorldStateType,TimeScale,SpeedUp1,SpeedUp2}
- WebUI_CoreUI0 页（.action-row/.bottom-left DOM、TimeScaleMsg/PauseMsg 通道）
- BaseSingleton<ReduxUISystem>.GetWebUILayer().canvasWebViewPrefab.WebView 取 WebView 路径

## 5. 已知边界

- 灾前"三倍"=×32（官方表值）；嫌猛的备选项=内存表临时编辑 SpeedUp2（未实施）。
- 手动档会取消睡眠临时加速（官方 CancelTempSpeedUp 行为）。
- LastSpeed 只记 mod 侧设置；官方侧改档（灾后官方条）不回写——自愈只在 CoreUI0 消失瞬间比对。
- 桥=Harmony prefix 观察者挂 WebUILayer.OnPageMessage（装机二轮定案）。首轮 MessageEmitted 事件订阅
  经 DelegateSupport.ConvertDelegate 编组，实机报 "Parameter type at 0 has mismatched native type
  pointers"（EventHandler 变体 sender 参数编组死角，无实机先例）——弃用；OnPageMessage 路零编组、纯观察不拦截。
- 安装回执的 Action<string> ConvertDelegate 变体装机一轮实证可用（CabinetEverywhere 同型先例）。

## 6. 迭代维护清单

- 改桥挂钩点/事件号/SendAction 签名 → 同步 moddeps.json + 本文档 §4 + 规格 §3/§5。
- 改 page.js 行为 → 先改 tests 夹具断言再改实现（红灯→绿灯）。
- 游戏更新后：node tools/gamekb/modcompat_check.js gamekb/<版本> 看本 mod 红灯项。
- 发版：作者定号后在 publish.ps1 $suiteMods 登记（本 mod 未发版前不动发版脚本）。

## 7. 迭代记录

- 2026-10-24 终审 I-1 修复：安装回执 v==2（新装页）时重置 payload 签名门强制下一 tick 重推——修菜单期签名门被烧掉、`__PdsBoot` 永不送达的隐患（当时表值恰=默认值无症状）；夹具 C 组 boot 换非默认 {s1:3,s2:33} 钉死"高亮由 payload 驱动"。
- 2026-10-24 装机二轮（作者实测两问题）：①点击无效——MessageEmitted 订阅实机编组失败，桥改 Harmony prefix 观察者挂 WebUILayer.OnPageMessage（page.js 点击契约不变）；②位置——五键条从 .top-center 尾部挪到官方位 .action-row 尾部（左下横排，对齐 CoreUI1 .action-panel 布局；兜底链 .action-row→.bottom-left→.top-center）。

- 2026-10-24 装机三轮：点击仍无效——反汇编 OnMessageFromJS 证 typeId=3 走 WebUIVm 虚方法分发，WebUILayer.OnPageMessage 零调用方（死方法，上轮挂空靶）；换挂 WebUIVm.HandleCallback（无重写唯一实现）；桥收到/SetSpeed 改无条件打点（装机期排查）。
