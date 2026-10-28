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
| 入口 | PreDisasterSpeedPlugin.cs | cfg（Enabled/TrueThreeX/Hotkeys/Verbose）+ Harmony PatchAll（桥）+ 三倍补丁手动挂载 + 2s PdsTicker（含快捷键轮询）+ WelcomeTip |
| 注入 | Injector.cs | WebView 获取（ReduxUISystem→WebUILayer→canvasWebViewPrefab）；page.js 幂等 eval 进 CoreUI0；payload（章节倍率）签名变化才推；安装回执喂自愈 |
| 桥+落地 | SpeedLink.cs | **Harmony prefix 观察者挂 WebUILayer.OnPageMessage**（不拦截不修改），识别 3/CoreUI0/PDS_SPEED → 调官方 Ac_CoreUI1_SpeedChange.SendAction；CoreUI0 消失且档位≠1 自动归 1x |
| UI | page.js（EmbeddedResource） | 五键条挂 **.action-row 尾部（官方位：左下横排，兜底 .bottom-left→.top-center）**；点击 postMessage 契约；官方 TimeScaleMsg 高亮映射；慢速本地态 |

### cfg 配置表（含 ModCenter 启停位，2026-10-25 补记）

| 键 | 默认 | 说明 |
|---|---|---|
| General.Enabled | true | 总开关（**ModCenter 面板启停位**——面板"灾前加速"页为仅启停形态，顶部开关直写该键；TrueThreeX/Hotkeys 不进面板）。false=`Injector.Tick` 首行早退（停注入；已挂 Harmony 桥仅观察 page.js 点击回程，页面不在即无事可做）——**重启级语义**，对已在档对局下次启动完全生效（cfg 描述原文口径） |
| General.TrueThreeX | true | 真×3 校正（§5） |
| Hotkeys.Enabled | true | 灾前快捷键 ~ / 1 / 2 / 3 / 4（§5） |
| Debug.Verbose | false | 注入/payload 明细日志 |

注：本键为**既有键复用**（ModCenter 计划 T12 核价确认零改动）；套件内其余 8 mod 的启停门为
热还原型（T9-T12 模板，false 触发运行中还原），本 mod 是重启级——面板停用时套件通用横幅
（"此处修改仍会写入 cfg 保存，但需重新启用后才会生效"）对该页如实适用（注入已停，改 cfg
确需重新启用+重启链路生效）。

## 3. 契约（改前必读）

- page.js 契约：`__PdsInstalled` 守卫 / `__PdsBoot({s1,s2})` / 点击 postMessage
  `{type:'PDS_SPEED',data:{speed},sourcePageId:'CoreUI0'}` / DOM `#pdsSpeed .pds-gear-{4,1,2,3,0}`。
- vuplex 协议：`3\x1EpageId\x1EeventType\x1Ejson`（Root.html 无条件中继任意 type）。
- 桥只认 `3/CoreUI0/PDS_SPEED`，其余消息零接触。
- tests/run_tests.js 是 page.js 行为回归夹具，改 page.js 必须先跑绿。

## 4. 依赖（moddeps.json "PreDisasterSpeed" 条目；harmony 槽位第一项为真补丁、第二项为存在性核对）

- **GameCore.HotUpdate.ReduxUI.WebUIVm.HandleCallback(string,string)（Harmony prefix，真挂钩点；typeId=3 唯一分发点，StackLimit999 同挂点在役）**
- **GameCore.HotUpdate.Battle.Logic.GameTimeManager.ChangeGameSpeed(int)（Harmony prefix+postfix，真三倍校正，手动挂载失败即回落）**
- GameCore.HotUpdate.ReduxUI.Ac_CoreUI1_SpeedChange.SendAction(int)（落地入口，存在性核对）
- _Config_Chapter_Dict / Config_Chapter.{WorldStateType,TimeScale,SpeedUp1,SpeedUp2}
- WebUI_CoreUI0 页（.action-row/.bottom-left DOM、TimeScaleMsg/PauseMsg 通道）
- BaseSingleton<ReduxUISystem>.GetWebUILayer().canvasWebViewPrefab.WebView 取 WebView 路径

## 5. 已知边界

- 灾前"三倍"官方表值=×32（ChapterSpeedUp2 与睡眠跳夜共用字段，改表/持久改字段都会把睡觉拖慢十倍，禁改）。装机四轮起 TrueThreeX=true：prefix 在"灾前+三档"时临时把字段换成 3、postfix 立即还原——手动三键真×3，睡眠保持官方×32。快捷键：~ / 1 / 2 / 3 / 4（Unity 侧轮询，仅 CoreUI0 可见期间；网页输入框聚焦时仍会触发，灾前无文本输入框未构成实际问题）。
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
- 版本与发版（2026-10-28 起）：版本号单源 = 根 `version.json` 的 `mods.PreDisasterSpeed` 字段（作者定义）；单 mod 发版走 `tools\release_mod.ps1 -Mod PreDisasterSpeed`；本 mod 的 GitHub 镜像仓由 `tools\sync_repos.ps1` 从 HEAD 快照同步（详见 docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md）。

## 7. 迭代记录

- 2026-10-25（ModCenter 任务 18 终审补漏）：DESIGN 补 cfg 配置表——`General.Enabled` 为
  **ModCenter 面板启停位**（面板"灾前加速"页仅启停形态直写该键；T12 复用既有键零改动，
  语义=重启级：Injector.Tick 首行早退停注入）。此前该键仅在 §2 概述括号里带过。
- 2026-10-24 终审 I-1 修复：安装回执 v==2（新装页）时重置 payload 签名门强制下一 tick 重推——修菜单期签名门被烧掉、`__PdsBoot` 永不送达的隐患（当时表值恰=默认值无症状）；夹具 C 组 boot 换非默认 {s1:3,s2:33} 钉死"高亮由 payload 驱动"。
- 2026-10-24 装机二轮（作者实测两问题）：①点击无效——MessageEmitted 订阅实机编组失败，桥改 Harmony prefix 观察者挂 WebUILayer.OnPageMessage（page.js 点击契约不变）；②位置——五键条从 .top-center 尾部挪到官方位 .action-row 尾部（左下横排，对齐 CoreUI1 .action-panel 布局；兜底链 .action-row→.bottom-left→.top-center）。

- 2026-10-24 装机三轮：点击仍无效——反汇编 OnMessageFromJS 证 typeId=3 走 WebUIVm 虚方法分发，WebUILayer.OnPageMessage 零调用方（死方法，上轮挂空靶）；换挂 WebUIVm.HandleCallback（无重写唯一实现）；桥收到/SetSpeed 改无条件打点（装机期排查）。

- 2026-10-24 装机四轮：点击变速全链打通（作者实测通过）；新增①快捷键 ~ / 1 / 2 / 3 / 4（Unity Input 轮询，仅灾前，键位同官方）；②真三倍校正 TrueThreeX（默认开）——反汇编证实 ExecuteTempSpeedUp（睡眠）无条件共用 ChapterSpeedUp2，故不可改表/持久改字段，改用 prefix/postfix 换档瞬间临时换字段法。

- 2026-10-24 装机五轮：①热键变档按钮不高亮——官方 TimeScaleMsg 灾前手动变速不保证推送（点击路径靠本地态掩盖），SetSpeed 成功后 C# 经 Injector.PushGear 直推 __PdsGear(N) 权威高亮（夹具 14→18 断言）；②作者疑问澄清：慢速档=官方空闲慢速语义（3 秒空闲→0.3 倍，动作即恢复），非缺陷。
