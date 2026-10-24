# ModCenter（游戏内 Mod 管理菜单）设计文档

- 日期：2026-10-24（v2，作者原型评审反馈已并入）
- 状态：待作者复评（配套可交互原型：[2026-10-24-modcenter-prototype.html](./2026-10-24-modcenter-prototype.html)）
- 决策记录：
  - ①管理范围 = **9 mod**（7 套件 + PlanPick + PreDisasterSpeed；**不含 StackLimit999**；**柜子负重 CabinetBurden 移出**——作者计划后期并入柜子扩容，v1 不纳入管理）；
  - ②停用语义 = 立即软停 + 下次启动彻底不加载（评估结论：改动量小、还原路径大半为现役代码，风险低）；
  - ③v2 原型评审反馈：配置项全面精简重梳（见 §7）；入口从左上角齿轮改为**官方菜单列表注入**；关闭按钮改图标化（修复 v1 文本溢出）；全 mod 不暴露 Debug 组。

## 1. 背景与目标

套件 mod 已达 9+ 个，玩家改配置只能手编 BepInEx cfg 文本，门槛高、无生效反馈、无启停能力。目标：

1. 游戏内统一管理菜单，**主菜单（读档前）与游戏内（读档后）两阶段均可打开**；
2. 每 mod 独立配置页，只暴露对玩家有意义的配置项（直观命名）；
3. 支持 mod 启用/停用：关闭立即生效（软停），下次启动该 mod 不再加载；
4. 配置修改**实时生效**：能实时的立即生效，不能的（快照类字段）在页面上明示生效边界。

## 2. 范围

### 2.1 管理名单（9）

| Mod | 面板显示名 | 一句话职责 | 接入方式 |
|---|---|---|---|
| BackpackExpand | 背包扩容 | 单倍率自动算宽高 + 负重倍率 | 新增 Enabled gate |
| CabinetExpand | 柜子扩容 | 普通/冰箱/载具三档倍率 + 视口自适应 | 新增 gate（UiFit 已有热停先例沿用） |
| FridgeChill | 保鲜增强 | 保鲜时长倍率 + 天赋上限倍率 | 新增 gate + 补 Ticker 早退还原 |
| QuickAction | **动作加速** | 五分类动作耗时 + 单动作覆盖清单 | 新增 gate（0=恢复原值语义已备） |
| CabinetEverywhere | 柜子直达+筛选 | 直达开关 + 标签筛选 + 场景视图 | 新增 gate + 补白名单对称移除 |
| RoleUnlock | 角色解锁 | 逐项"解锁 xxx"开关 | 新增 gate + 补 2 个无开关项 |
| RobotSpeed | 机器人提速 | 六维倍率 + 对齐手动 | 新增 gate（原值缓存幂等已备） |
| PlanPick | 自选生存规划 | 自选规划 + 加成百分比 | 入口注入加 gate |
| PreDisasterSpeed | 灾前加速 | 灾前速度条增强 | **仅启停开关**；`General.Enabled` 已存在 |

排除：**StackLimit999**（作者拍板排除）；**CabinetBurden 柜子负重**（作者将并入柜子扩容，v1 不纳入；合并后随柜子扩容页管理）；三个取证探针（硬规则 5）。

### 2.2 非目标与暴露原则

- 不做 BepInEx 插件级运行时卸载（平台无此能力，用两层启停语义替代，见 §6）；
- 不做 mod 安装/卸载/更新（安装器职责不变）；
- 不渲染元数据表未登记的插件（含第三方 mod）；
- **面板只暴露 §7 清单内的配置项**；未列出的 cfg 键（各 mod 的 Debug.* 组、名单类 ExtraIds/ExcludeIds/BagIds/FurnitureIds、PreDisasterSpeed 的 TrueThreeX/Hotkeys 等）**仍在 mod 侧保留生效**，供高级用户手编 cfg 文件调整，面板不展示——mod 代码零删减；
- QuickAction 一次性迁移项（boot.ini、DefaultsV21，QuickActionPlugin.cs:68-101/125-150）不进菜单；
- ModCenter 自身**不允许在面板里停用**（列表中显示但开关置灰 + 防呆提示）。

## 3. 总体架构

新插件 `SurvivalLog.ModCenter`（命名待作者定稿），进 publish.ps1 `$suiteMods` 白名单与安装器组件。三层：

```
┌─ UI 层（注入式）──────────────────────────────────┐
│ JS：__mc 浮层（Root 层 + MainUI 页）               │
│   入口 = 官方菜单列表注入项 → 全屏面板              │
│   （左 mod 列表 / 右配置页）                        │
└────────────┬──────────────────────────────────────┘
      ExecuteJavaScript(JSON 直推) / 回调收 JSON
┌────────────┴──────────────────────────────────────┐
│ ModCenter C#                                       │
│  元数据表（9 mod 页面定义） + 配置桥                 │
└────────────┬──────────────────────────────────────┘
             反射：Chainloader → BasePlugin.Config → ConfigEntry.Value 写
┌────────────┴──────────────────────────────────────┐
│ 各 mod 现役热生效链（零改动）                        │
│  SettingChanged → Rerun=true → Ticker ≤5s 幂等重写  │
└────────────────────────────────────────────────────┘
```

依赖的现役事实（实证依据）：

- 注入骨架四先例：CabinetEverywhere(TagFilter.cs:64-76)、PreDisasterSpeed(Injector.cs:123-132)、PlanPick(Injector.cs:161-179)、ViewportProbe(ViewportProbePlugin.cs:148-160)——判据均为"WebView 拿到"，无读档门槛；
- 全套件热生效链：`SettingChanged += Rerun=true` 置标志 + 主线程 Ticker 消费（BackpackExpandPlugin.cs:53-64、RobotSpeedPlugin.cs:91-97 等）——跨线程写 `ConfigEntry.Value` 天然安全；
- 官方页寄生先例：PlanPick 往 RogueSelect 页 `.footer-right` 首位 appendChild 入口按钮（page.js:286,332-335）——菜单列表注入同款手法；
- 键盘/IME：PlanPick FocusTick 十二轮定案（`WebUILayer.SetKeyboardEnabled(true)` + `Input.imeCompositionMode=On`，Injector.cs:100-140），整套复用（覆盖 ID 输入框需要键入）。

## 4. UI 设计

### 4.1 入口（v2 定稿：官方菜单列表注入，弃左上角齿轮）

- **主菜单**：往 MainUI 页官方菜单列表（继续游戏/新游戏/…）追加一项"MOD 管理"（appendChild 手法，样式克隆官方菜单项 + 轻度 accent 区分）；
- **游戏内**：往官方系统菜单（ESC 菜单）的按钮列表追加同名入口；具体锚点以 `tools/webui_src` 快照定位（先官方后自制铁律），**降级方案**：官方菜单无稳定 DOM 锚点时，改挂右上角小按钮（贴 HUD 区，不挡视野）；
- 所有注入元素统一 `__mc` 前缀 + 独立锚点，杜绝与前序注入元素互扰；
- 注入为轮询自愈模式（幂等脚本 + 页面侧 `__McInstalled` 守卫，WebView/页面重建后自动重装）。

### 4.2 面板

点击入口 → 全屏浮层（半透明遮罩 + 居中面板，PlanPick 同款手法）：

- **左列**：9 个 mod 条目 = 状态点（绿=启用/灰=已停用）+ 显示名 + 启停开关；末尾"面板自身"条目（锁死）；
- **右区**：选中 mod 的配置页——标题 + mod 级启停 + 功能简介 + 分组配置项；
- **每项**：直观中文名 + cfg 键（小号等宽）+ 控件 + 当前值 + 生效徽标（⚡立即 ≤5s / 📁读档后 / 🔄重启后）；个别项带补充说明小字（如"自动计算宽与高，超上限自动封顶"）；
- **控件映射**：倍率 → 滑条+数值框；int 毫秒 → 滑条；bool → 开关；enum → 下拉；**动作覆盖 → 动态清单**（ID 输入 + 新增按钮，清单行 = 滑条 + 数值 + 删除钮，增删改即时写回）；
- **关闭按钮**：右上角图标化"✕"（v1 文本"✕ 关闭"在 30px 按钮内溢出，已修复）；ESC / 点遮罩同样关闭。

### 4.3 通信协议

- **C#→JS**：`ExecuteJavaScript` 直推 JSON（打开面板推全量快照；值变更回推确认）；
- **JS→C#**：`ExecuteJavaScript(js, 回调)` 收 JSON——**不走 WebUIVm.HandleCallback prefix**，主菜单阶段不赌 VM 分发链可达性，两阶段同一通路；
- 消息：`{type:'MC_OPEN'}` 请求快照；`{type:'MC_SET', guid, section, key, value}` 写入 → 回 `{ok, badge}`；`{type:'MC_TOGGLE_MOD', guid, enabled}` 启停；
- 红线照守：回调内不改游戏数据（docs/03:48）；cfg 写入走 Value setter + 节流 Save（面板关闭时 + 3s 防抖），只动 mod 配置不动游戏存档。

### 4.4 键盘

面板激活期间复用 FocusTick（`SetKeyboardEnabled(true)` + `imeCompositionMode=On`），关面板双还原；主菜单阶段键盘行为实施时验证（§9.4），机制同一套。

## 5. 配置桥

1. **枚举**：BepInEx Chainloader `PluginInfos` → `BasePlugin` 实例 → 反射取 `Config`（可见性实施首日验证——BepInEx 生态 ConfigurationManager 同款成熟手法）；
2. **写**：`entry.Value = 新值` → 触发目标 mod 现役 `SettingChanged → Rerun` → 其 Ticker ≤5s 幂等重写内存表；**单倍率项由面板同时写宽高两键等值**（见 §7，mod cfg 结构不动）；
3. **读**：启动快照 + 每次打开面板刷新（防玩家外部手改 cfg 造成脏读）；动作覆盖清单解析 `其他.Overrides` 字符串（`id:ms` 逗号分隔），写回序列化同格式；
4. **元数据表**：ModCenter 内置 9 mod 页面定义（guid、显示名、分组、排序、项定义：section/key、控件、范围、徽标、中文描述、联动写键规则）。9 mod 是作者可控封闭集合，硬编码可维护；新增 mod = 加一段定义；
5. **首跑固化坑无关**：管理菜单写内存 Value + Save，不走 Bind 默认值路径（ModShared/README.md:43 坑不相干）。

## 6. 启停机制（两层语义）

每 mod cfg 新增 `General.Enabled`（bool，默认 true）：

- **立即软停**（开关关闭当下）：各 mod Ticker/prefix 首行 gate 检查；false 时先把表值回写进程内原值缓存（现役"mult=1 / 0=恢复原值 / bool=false 还原"路径）再停写；
- **重启彻底禁用**（下次启动）：各 mod `Load()` 首行 `if (!Enabled.Value) return;`——不挂 Harmony、不启动 Ticker、不写任何表；
- **重新启用**：开关打开即恢复（cfg 值未销毁）；停用期间的 cfg 修改仍可进行（面板不锁输入，横幅提示"重新启用后生效"）；
- UI 文案明确两层："停用立即生效，且下次启动不再加载"。

### 6.1 各 mod 接入明细与还原边界

| Mod | gate 落点 | 还原边界（面板标注） |
|---|---|---|
| BackpackExpand | Ticker 首行 | 尺寸立即还原；既有背包**负重读档后**还原（📁） |
| CabinetExpand | Ticker 首行 + ViewportFit 沿用 UiFit.Enabled | 尺寸立即；视口脚本立即停 |
| FridgeChill | Ticker 首行；**补**：KeepMult≤1.01 早退时也回写原值（FridgeChillPlugin.cs:99 现状早退不还原）；字节补丁行为停用 = scratch 写 0.1 原生语义（:270-289 现成） | ColdRate 立即还原；字节补丁结构进程内残留但零行为 |
| QuickAction | Ticker 首行 | 全量立即还原（原值缓存自愈，:477-486） |
| CabinetEverywhere | Ticker 首行 + RefreshCoalescer prefix 首行 gate；**补**：CraftSource 白名单对称 Remove（CraftSourceInjector.cs:31-45 现状只增不删） | 白名单移除立即；已打开界面重开后收敛 |
| RoleUnlock | Ticker 首行；**补**：生存规划清白名单、花瓶配方 325 两个开关（RoleUnlockPlugin.cs:31-34 现状无开关"mod 在即生效"） | 可还原项立即（:163-189、:306-325 现成还原模式） |
| RobotSpeed | Ticker 首行 | 表值立即；已生成机器人属性跟随其读取周期 |
| PlanPick | 入口注入 gate（藏入口按钮） | 入口立即消失；已购规划不清退（保持官方语义） |
| PreDisasterSpeed | `General.Enabled` 已有（PreDisasterSpeedPlugin.cs:30，关=不注入不挂钩）；真三倍逐调用读值（SpeedLink.cs:136） | 立即（钩子无匹配即零行为） |

## 7. 配置项清单（v2 精简版，元数据表蓝本）

原则（作者 v2 评审定）：背包/柜子**只给一个倍率**（面板同时写宽高两键等值，超上限 mod 既有逻辑封顶）；**不暴露**宽高单项、追加/排除名单、Debug 组；命名直观（"解锁 xxx"）；灾前加速只留启停。

徽标：⚡立即 ≤5s / 📁表即时改·既有容器读档后体现 / 🔄重启后。

### 背包扩容（BackpackExpand）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 背包倍率 | Expand.WidthMult + HeightMult（联动等值写） | 滑条 1~3 | 1.5 | ⚡ |
| 负重倍率 | Expand.BurdenMult | 滑条 1~10 | 2 | 📁 |

### 柜子扩容（CabinetExpand）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 普通柜倍率 | Expand.WidthMult + HeightMult（联动） | 滑条 1~3 | 1.5 | ⚡ |
| 冰箱倍率 | Expand.FridgeWidthMult + FridgeHeightMult（联动） | 滑条 1~3 | 1.5 | ⚡ |
| 载具倍率 | Expand.VehicleWidthMult + VehicleHeightMult（联动） | 滑条 1~3 | 1.5 | ⚡ |
| 启用视口自适应 | UiFit.Enabled | 开关 | true | ⚡ |
| 自适应模式 | UiFit.Mode | 下拉 Scroll/Fit/Off | Scroll | ⚡ |

### 保鲜增强（FridgeChill）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 保鲜时长倍率 | Chill.KeepMult | 滑条 1~50 | 2 | ⚡ |
| 天赋保鲜上限倍率 | Chill.MaxTotalMult | 滑条 1~500 | 100 | ⚡（scratch 热写，FridgeChillPlugin.cs:270-280；补丁安装失败置灰并提示） |

### 动作加速（QuickAction）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 物品使用 / 家具功能 / 房屋维护 / 杂项交互 / 娱乐锻炼（毫秒） | 分类修改.*Ms | 滑条 0~5000 | 500 | ⚡（0=官方原值） |
| 动作覆盖清单（增删改） | 其他.Overrides（`id:ms` 串解析/写回） | 清单：ID 输入+新增；行=滑条 0~5000+删除 | 空 | ⚡ |

### 柜子直达+筛选（CabinetEverywhere）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 制作取料 / 烹饪台 / 鼠笼 / 酿造 / 染色 直达 | Access.Enable*（5 键） | 开关 | true | ⚡ |
| 启用标签筛选 | Filter.Enable | 开关 | true | ⚡ |
| 场景固定视图（10 项：灶台/酿造/工作台/无人机枢纽等） | Scenario.View_*（10 键） | 下拉 Food/Material/Browser/Off | 各异 | ⚡ |

### 角色解锁（RoleUnlock）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 解锁藤蔓角色 | Vine.UnlockVine | 开关 | true | ⚡ |
| 藤蔓免防御等级门槛 | Vine.SkipDefenseGate | 开关 | false | ⚡ |
| 解锁藤蔓专属花种子 | Vine.UnlockFlowerSeeds | 开关 | false | ⚡ |
| 解锁防御系角色专属配方（全角色共享） | Defense.ShareDefenseRecipes | 开关 | true | ⚡ |
| 解锁角色专属生存规划（46 项功能类） | （新增）Plan.ClearPool | 开关 | true | ⚡ |
| 解锁花瓶配方 | （新增）Plan.VaseRecipe | 开关 | true | ⚡ |

### 机器人提速（RobotSpeed）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 动作速度对齐手动 | 机器人提速.动作速度对齐手动 | 开关 | true | ⚡ |
| 移速 / 充电速率 / 续航 / 耗电降低 / 动作时长倍率 | 机器人提速.*倍率（5 键） | 滑条 0.5~10 | 2 | ⚡ |
| 货舱格数倍率 | 机器人提速.货舱格数倍率 | 滑条 0.5~10 | 2 | ⚡（面板重开即见） |

### 自选生存规划（PlanPick）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 加成百分比（100=原值） | Cost.CostMultiplierPercent | 滑条 100~300 | 200 | ⚡ |
| 展示未解锁规划（置灰+原因） | List.ShowLocked | 开关 | true | ⚡ |
| 展示不入池规划 | List.ShowUnpooled | 开关 | true | ⚡ |

### 灾前加速（PreDisasterSpeed）
| 面板项 | 背后 cfg | 控件 | 默认 | 徽标 |
|---|---|---|---|---|
| 灾前加速（仅启停） | General.Enabled | 开关 | true | 即时（钩子零行为） |

> 注：TrueThreeX / Hotkeys.Enabled / Debug.Verbose 等仍存在于 cfg 并生效，面板不展示（§2.2 暴露原则）。

## 8. 现役 mod 改动清单

1. 7 个套件 mod：+`General.Enabled` cfg + Ticker/prefix 首行 gate（每 mod 约十行级）；
2. 三处小补丁：FridgeChill Ticker 早退还原（:99）、CabinetEverywhere 白名单对称 Remove（CraftSourceInjector.cs:31-45）、RoleUnlock 两项补开关；
3. PlanPick 入口 gate、PreDisasterSpeed 仅接元数据（面板单开关映射 General.Enabled）；
4. 同步义务（硬规则 11）：全部受影响 mod 的 DESIGN.md 迭代清单 + 新建 ModCenter/DESIGN.md；
5. `tools/gamekb/moddeps.json`：**不动**——无新增 Harmony 挂钩点/表字段/字节签名（gate 是 mod 内部逻辑；RoleUnlock 新开关用的都是已登记字段）。

## 9. 风险与前置实证（实施第一步，半天级）

| # | 风险 | 实证/缓解 |
|---|---|---|
| 1 | 主菜单阶段 `canvasWebViewPrefab.WebView` 可达性（强证据间接，无 mod 实注过主菜单） | ViewportProbe + CDP 一轮实证；降级：主菜单不显示入口、进游戏后开放 |
| 2 | 反射桥 API 细节（BasePlugin.Config 可见性、Chainloader 枚举） | 10 行探针首日验证（ConfigurationManager 同款手法） |
| 3 | **游戏内官方系统菜单锚点**（v2 新增）：ESC 菜单是否 WebUI 渲染、列表项 DOM 是否稳定可寄生 | 从 `tools/webui_src` 快照定位；无稳定锚点则降级右上角小按钮 |
| 4 | 主菜单阶段 SetKeyboardEnabled/IME 行为未验证 | 实施时随 #1 一并验证；机制同一套 |
| 5 | 自注入元素互扰（前科：漏审） | `__mc` 前缀 + 独立锚点 + 回归夹具照 CabinetEverywhere 模式 |
| 6 | FridgeChill/CabinetEverywhere 两处现役补丁 | 装机回归（属"改 mod 必同步 DESIGN.md"范围） |
| 7 | Vuplex 回调线程写 cfg | 各 mod Rerun 为置标志模式天然安全；仍以探针确认回调线程上下文 |

## 10. 测试与验收

### 回归夹具（CDP 断言脚本，照 `tools/devtools_cdp_test.js` 模式）

1. 两阶段入口出现性（主菜单菜单列表项 + 游戏内系统菜单项/降级按钮）；
2. 9 页渲染完整（元数据对账，含覆盖清单解析往返）；
3. 改值热生效：改一个倍率 → ≤5s 内表值变化（探针读表断言）；联动项宽高两键同值；
4. 启停还原：关闭 → 原值回归；重新启用 → 恢复；
5. `__mc` 元素与既有注入（PlanPick/PreDisasterSpeed/TagFilter）共存无互扰。

### 装机实测（作者验收清单）

1. 主菜单打开面板改柜子扩容倍率 → 进游戏读档 → 生效；
2. 游戏内改倍率 → ≤5s 生效；
3. 停用 RobotSpeed → 移速等行为停止；
4. 停用后重启游戏 → 该 mod 未加载（日志/自证）；
5. 重新启用 → 恢复原配置效果；
6. 动作覆盖清单新增/滑条/删除 → 即时生效；
7. 覆盖 ID 数值输入键盘正常（FocusTick 门生效）。

### 发布形态

ModCenter 进 `$suiteMods` + 安装器自选组件；发版号由作者定（硬规则 1），随下次发版走 publish.ps1 流水线。

## 11. 里程碑顺序

1. 前置实证（§9.1/2/3，半天级）→ 若降级触发（主菜单不可注 / 系统菜单无锚点），回报作者重议入口；
2. ModCenter 骨架：插件 + 菜单列表入口注入 + 浮层空壳 + 反射桥读通；
3. 元数据表 9 页 + 写链路热生效 + 覆盖清单；
4. 启停 gate 接入 9 mod + 三处小补丁；
5. 回归夹具绿 → 装机实测 → 作者验收"可以了"；
6. spec 定稿归档（含 v2 评审反馈已并入）。
