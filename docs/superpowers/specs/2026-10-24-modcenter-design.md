# ModCenter（游戏内 Mod 管理菜单）设计文档

- 日期：2026-10-24
- 状态：待作者评审（配套可交互原型：[2026-10-24-modcenter-prototype.html](./2026-10-24-modcenter-prototype.html)）
- 决策记录：brainstorming 两轮澄清已定——①管理范围 = 10 mod（8 套件 + PlanPick + PreDisasterSpeed，**不含 StackLimit999**）；②停用语义 = 立即软停 + 下次启动彻底不加载（评估结论：改动量小、还原路径大半为现役代码，风险低）。

## 1. 背景与目标

套件 mod 已达 10 个，玩家改配置只能手编 BepInEx cfg 文本，门槛高、无生效反馈、无启停能力。目标：

1. 游戏内统一管理菜单，**主菜单（读档前）与游戏内（读档后）两阶段均可打开**；
2. 每 mod 独立配置页，覆盖其全部对玩家有意义的 cfg 可配置项；
3. 支持 mod 启用/停用：关闭立即生效（软停），下次启动该 mod 不再加载；
4. 配置修改**实时生效**：能实时的立即生效，不能的（快照类字段）在页面上明示生效边界。

## 2. 范围

### 2.1 管理名单（10）

| Mod | 一句话职责 | 接入方式 |
|---|---|---|
| BackpackExpand | 背包扩容 | 新增 Enabled gate |
| CabinetExpand | 柜子扩容 + 视口自适应 | 新增 gate（UiFit 已有热停先例沿用） |
| FridgeChill | 保鲜增强 | 新增 gate + 补 Ticker 早退还原 |
| QuickAction | 动作耗时分类改写 | 新增 gate（0=恢复原值语义已备） |
| CabinetEverywhere | 柜子直达 + 标签筛选 | 新增 gate + 补白名单对称移除 |
| CabinetBurden | 柜子负重解放 | 新增 gate（mult=1 即还原已备） |
| RoleUnlock | 角色专属解锁 | 新增 gate + 补 2 个无开关项 |
| RobotSpeed | 机器人提速 | 新增 gate（原值缓存幂等已备） |
| PlanPick | 自选生存规划 | 入口注入加 gate（已具 Enabled 语义基础） |
| PreDisasterSpeed | 灾前加速 | `General.Enabled` 已存在，仅接元数据 |

排除：**StackLimit999**（作者拍板排除）；三个取证探针（ActionProbe/ViewportProbe/ConfigDumpProbe，永不进套件，硬规则 5）。

### 2.2 非目标

- 不做 BepInEx 插件级运行时卸载（平台无此能力，用两层启停语义替代，见 §6）；
- 不做 mod 安装/卸载/更新（安装器职责不变）；
- 不渲染元数据表未登记的插件（含玩家自装的第三方 mod；反射桥能枚举到它们，v1 不展示）；
- QuickAction 一次性迁移项（boot.ini 读取、DefaultsV21 迁移，QuickActionPlugin.cs:68-101/125-150）不进菜单——属安装器一次性事务（COLD）；
- ModCenter 自身**不允许在面板里停用**（列表中显示但开关置灰 + 提示"防呆：停用后本面板将不可用"）。

## 3. 总体架构

新插件 `SurvivalLog.ModCenter`（命名待作者定稿），进 publish.ps1 `$suiteMods` 白名单与安装器组件。三层：

```
┌─ UI 层（注入式浮层）────────────────────────────┐
│ JS：__mc 浮层（Root 层 + MainUI 页）             │
│   齿轮入口 → 全屏面板（左 mod 列表 / 右配置页）  │
└────────────┬────────────────────────────────────┘
      ExecuteJavaScript(JSON 直推) / 回调收 JSON
┌────────────┴────────────────────────────────────┐
│ ModCenter C#                                     │
│  元数据表（10 mod 页面定义） + 配置桥             │
└────────────┬────────────────────────────────────┘
             反射：Chainloader → BasePlugin.Config → ConfigEntry.Value 写
┌────────────┴────────────────────────────────────┐
│ 各 mod 现役热生效链（零改动）                     │
│  SettingChanged → Rerun=true → Ticker ≤5s 幂等重写 │
└──────────────────────────────────────────────────┘
```

依赖的现役事实（实证依据）：

- 注入骨架四先例：CabinetEverywhere(TagFilter.cs:64-76)、PreDisasterSpeed(Injector.cs:123-132)、PlanPick(Injector.cs:161-179)、ViewportProbe(ViewportProbePlugin.cs:148-160)——判据均为"WebView 拿到"，无读档门槛；
- 全套件热生效链：`SettingChanged += Rerun=true` 置标志 + 主线程 Ticker 消费（BackpackExpandPlugin.cs:53-64、RobotSpeedPlugin.cs:91-97 等 8 处）——跨线程写 `ConfigEntry.Value` 天然安全；
- 键盘/IME：PlanPick FocusTick 十二轮定案（`WebUILayer.SetKeyboardEnabled(true)` + `Input.imeCompositionMode=On`，Injector.cs:100-140），整套复用。

## 4. UI 设计

### 4.1 入口

- **游戏内**：Root 层注入左上角悬浮齿轮（fixed，不碰 TAB_SWITCH 数组，符合"界面禁改数组只准藏/加 DOM"红线）；
- **主菜单**：MainUI 页角落注入同一齿轮（MainUI 即 Root WebView 下的 iframe，docs/06:64-71、:115）；
- 所有注入元素统一 `__mc` 前缀 + 独立锚点，杜绝与前序注入元素互扰（有漏审前科，见风险 §9.3）；
- 注入为轮询自愈模式（幂等脚本 + 页面侧 `__McInstalled` 守卫，WebView 重建后自动重装）。

### 4.2 面板

点击齿轮 → 全屏浮层（半透明遮罩 + 居中面板，PlanPick 同款手法）：

- **左列**：10 个 mod 条目 = 状态点（绿=启用/灰=已停用）+ 名称 + 启停开关；
- **右区**：选中 mod 的配置页——标题 + mod 级启停 + 功能简介 + 按分组（section）排列的配置项；
- **每项**：中文名 + cfg 键（小号等宽字体）+ 控件 + 当前值 + 生效徽标；
- **控件映射**：float 倍率 → 滑条+数值框；int → 步进器（含范围）；bool → 开关；string 名单 → 文本框（IME 可输中文）；enum → 下拉；
- **生效徽标三级**：⚡立即（≤5 秒，SettingChanged 触发）/ 📁读档后（表值即时改，但既有容器的快照字段要重读档）/ 🔄重启后（仅个别启动期判定项）。

### 4.3 通信协议

- **C#→JS**：`ExecuteJavaScript` 直推 JSON（打开面板推全量快照；值变更回推确认）；
- **JS→C#**：`ExecuteJavaScript(js, 回调)` 收 JSON——**不走 WebUIVm.HandleCallback prefix**，主菜单阶段不赌 VM 分发链可达性，两阶段同一通路；
- 消息：`{type:'MC_OPEN'}` 请求快照；`{type:'MC_SET', guid, section, key, value}` 写入 → 回 `{ok, badge}`；`{type:'MC_TOGGLE_MOD', guid, enabled}` 启停；
- 红线照守：回调内不改游戏数据（docs/03:48）；cfg 写入走 Value setter + 节流 Save（面板关闭时 + 3s 防抖），只动 mod 配置不动游戏存档。

### 4.4 键盘

面板激活期间复用 FocusTick（`SetKeyboardEnabled(true)` + `imeCompositionMode=On`），关面板双还原；主菜单阶段键盘行为实施时验证（§9.4），机制同一套。

## 5. 配置桥

1. **枚举**：BepInEx Chainloader `PluginInfos` → `BasePlugin` 实例 → 反射取 `Config`（BasePlugin.Config 属性，可见性实施首日验证——BepInEx 生态 ConfigurationManager 同款成熟手法）；
2. **写**：`entry.Value = 新值` → 触发目标 mod 现役 `SettingChanged → Rerun` → 其 Ticker ≤5s 幂等重写内存表；
3. **读**：启动快照 + 每次打开面板刷新（防玩家外部手改 cfg 文件造成脏读）；
4. **元数据表**：ModCenter 内置 10 mod 页面定义（guid、显示名、分组、排序、项定义：section/key、控件、范围、徽标、中文描述）。10 个 mod 是作者可控封闭集合，硬编码可维护；新增 mod = 加一段定义；
5. **首跑固化坑无关**：管理菜单写内存 Value + Save，不走 Bind 默认值路径，与"cfg 首跑固化"坑（ModShared/README.md:43）不相干。

## 6. 启停机制（两层语义）

每 mod cfg 新增 `General.Enabled`（bool，默认 true）：

- **立即软停**（开关关闭当下）：各 mod Ticker/prefix 首行 gate 检查；false 时先把表值回写进程内原值缓存（现役"mult=1 / 0=恢复原值 / bool=false 还原"路径）再停写；
- **重启彻底禁用**（下次启动）：各 mod `Load()` 首行 `if (!Enabled.Value) return;`——不挂 Harmony、不启动 Ticker、不写任何表；
- **重新启用**：开关打开即恢复（cfg 值未销毁）；停用期间的 cfg 修改仍可进行（面板不锁输入，仅提示"当前已停用，重新启用后生效"）；
- UI 文案明确两层："停用立即生效，且下次启动不再加载"。

### 6.1 各 mod 接入明细与还原边界

| Mod | gate 落点 | 还原边界（面板标注） |
|---|---|---|
| BackpackExpand | Ticker 首行 | 尺寸立即还原；既有容器**负重读档后**还原（📁） |
| CabinetExpand | Ticker 首行 + ViewportFit 沿用 UiFit.Enabled | 尺寸立即；视口脚本立即停 |
| FridgeChill | Ticker 首行；**补**：KeepMult≤1.01 早退时也回写原值（FridgeChillPlugin.cs:99 现状早退不还原）；字节补丁行为停用 = scratch 写 0.1 原生语义（:270-289 现成） | ColdRate 立即还原；字节补丁结构进程内残留但零行为 |
| QuickAction | Ticker 首行 | 全量立即还原（原值缓存自愈，:477-486） |
| CabinetEverywhere | Ticker 首行 + RefreshCoalescer prefix 首行 gate；**补**：CraftSource 白名单对称 Remove（CraftSourceInjector.cs:31-45 现状只增不删） | 白名单移除立即；已打开界面重开后收敛 |
| CabinetBurden | Ticker 首行 | 新写立即停；既有容器**读档后**还原（📁） |
| RoleUnlock | Ticker 首行；**补**：生存规划清白名单、花瓶配方 325 两个开关（RoleUnlockPlugin.cs:31-34 现状无开关"mod 在即生效"） | 可还原项立即（:163-189、:306-325 现成还原模式） |
| RobotSpeed | Ticker 首行 | 表值立即；已生成机器人属性跟随其读取周期 |
| PlanPick | 入口注入 gate（藏入口按钮） | 入口立即消失；已购规划不清退（保持官方语义） |
| PreDisasterSpeed | `General.Enabled` 已有（PreDisasterSpeedPlugin.cs:30，关=不注入不挂钩）；真三倍逐调用读值（SpeedLink.cs:136） | 立即（钩子无匹配即零行为） |

## 7. 配置项清单（元数据表蓝本）

徽标：⚡立即 ≤5s / 📁表即时改·既有容器读档后体现 / 🔄重启后。

### BackpackExpand（背包扩容）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Expand.WidthMult / HeightMult | float | 1.5 / 1.5 | ⚡ |
| Expand.BurdenMult | int | 2 | 📁 |
| Expand.ExtraIds / ExcludeIds | string | 空 | ⚡ |
| Debug.Verbose | bool | false | ⚡ |

### CabinetExpand（柜子扩容）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Expand.WidthMult / HeightMult（普通柜） | float | 1.5 / 2.0 | ⚡ |
| Expand.FridgeWidthMult / FridgeHeightMult | float | 1.5 / 2.0 | ⚡ |
| Expand.VehicleWidthMult / VehicleHeightMult | float | 1.5 / 2.0 | ⚡ |
| Expand.MaxCols | int | 20 | ⚡ |
| Expand.ExtraIds / ExcludeIds | string | 空 | ⚡ |
| UiFit.Enabled / UiFit.Mode | bool / enum | true / Scroll | ⚡ |
| Debug.Verbose / ListOnReady / DiagOnOpen | bool | false/true/false | ⚡ |

### FridgeChill（保鲜增强）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Chill.KeepMult | float | 2 | ⚡ |
| Chill.MaxTotalMult（天赋上限） | float | 100 | ⚡（scratch 热写，FridgeChillPlugin.cs:270-280） |
| Chill.BagIds | string | 空 | ⚡ |
| Debug.Verbose | bool | false | ⚡ |

注：若启动时字节补丁安装失败（:220 签名扫描不中），MaxTotalMult 改值无效——面板经反射读 FridgeChill 补丁安装状态静态字段，失败时该项置灰并提示。

### QuickAction（快捷动作）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| 分类修改.物品使用Ms / 家具功能Ms / 房屋维护Ms / 杂项交互Ms / 娱乐锻炼Ms | int | 500 | ⚡（0=恢复原值） |
| 其他.Overrides（ID:毫秒） | string | 空 | ⚡ |

### CabinetEverywhere（柜子直达+标签筛选）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Access.EnableCraftSource / EnableCooking / EnableRatCage / EnableBrew / EnableDye | bool | true | ⚡ |
| Access.IncludeFurnitureIds / ExcludeFurnitureIds / ExcludeBagIds | string | 空/空/大黑名单 | ⚡ |
| Filter.Enable | bool | true | ⚡ |
| Scenario.View_Cooking…View_DroneHub（10 项） | enum | 见 cfg | ⚡ |
| Debug.ListOnReady / DumpFurniture | bool | true / false | ⚡ |
| Debug.Verbose | bool | false | 日志⚡ / 探针安装🔄（:131 仅 Load 判定） |

### CabinetBurden（柜子负重解放）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Burden.BurdenMult | int | 1000 | 📁 |
| Burden.IncludeBagIds / ExcludeBagIds | string | 空 / "3,4" | ⚡ |
| Debug.ListOnReady / Verbose | bool | true / false | ⚡ |

### RoleUnlock（角色解锁）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Vine.UnlockVine / SkipDefenseGate / UnlockFlowerSeeds | bool | true/false/false | ⚡ |
| Defense.ShareDefenseRecipes | bool | true | ⚡ |
| （新增）Plan.ClearPool / Plan.VaseRecipe | bool | true | ⚡ |
| Debug.ListOnReady / Verbose | bool | true / false | ⚡ |

### RobotSpeed（机器人提速）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| 机器人提速.动作速度对齐手动 | bool | true | ⚡ |
| 机器人提速.移速/充电速率/续航/耗电降低/动作时长/货舱格数倍率 | float | 2 | ⚡（货舱面板重开即见） |

### PlanPick（自选生存规划）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| Cost.CostMultiplierPercent | int | 200（100~300） | ⚡ |
| List.ShowLocked / ShowUnpooled | bool | true / true | ⚡ |
| Debug.ListOnReady / Verbose | bool | true / false | ⚡ |

### PreDisasterSpeed（灾前加速）
| cfg | 类型 | 默认 | 徽标 |
|---|---|---|---|
| General.Enabled | bool | true | 即时（钩子零行为） |
| General.TrueThreeX（真三倍校正） | bool | true | ⚡（逐调用读值） |
| Hotkeys.Enabled | bool | true | ⚡ |
| Debug.Verbose | bool | false | ⚡ |

## 8. 现役 mod 改动清单

1. 8 个套件 mod：+`General.Enabled` cfg + Ticker/prefix 首行 gate（每 mod 约十行级）；
2. 三处小补丁：FridgeChill Ticker 早退还原（:99）、CabinetEverywhere 白名单对称 Remove（CraftSourceInjector.cs:31-45）、RoleUnlock 两项补开关；
3. PlanPick 入口 gate、PreDisasterSpeed 仅接元数据；
4. 同步义务（硬规则 11）：全部受影响 mod 的 DESIGN.md 迭代清单 + 新建 ModCenter/DESIGN.md；
5. `tools/gamekb/moddeps.json`：**不动**——无新增 Harmony 挂钩点/表字段/字节签名（gate 是 mod 内部逻辑；RoleUnlock 新开关用的都是已登记字段）。

## 9. 风险与前置实证（实施第一步，半天级）

| # | 风险 | 实证/缓解 |
|---|---|---|
| 1 | 主菜单阶段 `canvasWebViewPrefab.WebView` 可达性（强证据间接：MainUI 即 Root iframe + 注入判据只看 WebView，但无 mod 实注过主菜单） | ViewportProbe + CDP 一轮实证；降级方案：主菜单不显示入口、进游戏后开放（不阻塞主线） |
| 2 | 反射桥 API 细节（BasePlugin.Config 可见性、Chainloader 枚举） | 10 行探针首日验证（ConfigurationManager 同款手法） |
| 3 | 自注入元素互扰（前科：漏审） | `__mc` 前缀 + 独立锚点 + 回归夹具照 CabinetEverywhere 模式 |
| 4 | 主菜单阶段 SetKeyboardEnabled/IME 行为未验证 | 实施时随 #1 一并验证；机制同一套 |
| 5 | FridgeChill/CabinetEverywhere 两处现役补丁 | 装机回归（属"改 mod 必同步 DESIGN.md"范围） |
| 6 | Vuplex 回调线程写 cfg | 各 mod Rerun 为置标志模式，主线程 Ticker 消费，天然安全；仍以探针确认回调线程上下文 |

## 10. 测试与验收

### 回归夹具（CDP 断言脚本，照 `tools/devtools_cdp_test.js` 模式）

1. 两阶段入口出现性（主菜单页 + 游戏内 Root）；
2. 10 页渲染完整（元数据对账）；
3. 改值热生效：改一个倍率 → ≤5s 内表值变化（探针读表断言）；
4. 启停还原：关闭 → 原值回归；重新启用 → 恢复；
5. `__mc` 元素与既有注入（PlanPick/PreDisasterSpeed/TagFilter）共存无互扰。

### 装机实测（作者验收清单）

1. 主菜单打开面板改 CabinetExpand 倍率 → 进游戏读档 → 生效；
2. 游戏内改倍率 → ≤5s 生效；
3. 停用 RobotSpeed → 移速等行为停止；
4. 停用后重启游戏 → 该 mod 未加载（日志/自证）；
5. 重新启用 → 恢复原配置效果；
6. 名单类文本框中文输入（IME）正常。

### 发布形态

ModCenter 进 `$suiteMods` + 安装器自选组件；发版号由作者定（硬规则 1），随下次发版走 publish.ps1 流水线。

## 11. 里程碑顺序

1. 前置实证（§9.1/9.2，半天级）→ 若降级触发，回报作者重议主菜单入口；
2. ModCenter 骨架：插件 + 注入 + 浮层空壳 + 反射桥读通；
3. 元数据表 10 页 + 写链路热生效；
4. 启停 gate 接入 10 mod + 三处小补丁；
5. 回归夹具绿 → 装机实测 → 作者验收"可以了"；
6. spec 定稿归档（含原型评审反馈）。
