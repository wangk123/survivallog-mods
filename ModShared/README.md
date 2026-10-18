# ModShared — 套件共享源码

- `BagTableRw.cs`：Config_Bag / Config_Furniture 内存表直写工具（编译时由 4 个扩容系 mod 各自链入，产物为各自独立 dll，运行时互不依赖）。
- `WelcomeTip.cs`：防盗署名气泡（2026-10-14，编译时由全部 7 个套件 mod 链入）。

## 职责

Config_Bag / Config_Furniture **内存表直写工具**：表访问、进程内原值记忆、幂等改写。
每个 mod 的 DESIGN.md（§实现原理）都引用这里的机制。

## 实证依据（2026-10-01/02 反汇编 + 运行时 dump）

- `Config_Bag.Size` = `List<int>`，[0]=宽 [1]=高；逻辑/天赋/UI 三层实时直读，**无快照**。
- `Config_Bag.Burden` 是 `BagComponent.OnInit / SetBagConfigId` 的**快照**；改表后需重新读档。
- `Config_Furniture.ColdRate` = 腐烂速率，保鲜时长 = 1/ColdRate（ComputeItemTimeScale 直读偏移 0x108）。
- `ConfigManager._bagSizeSet` 尺寸白名单懒重建：改 Size 后置 null 即可，下一次 HasBagSize 按新表重建。
- 全部操作**幂等**：进程内字典记住每条配置的原值（首次读到时），重复执行永远按 原值×倍数 计算。

## 公开 API

| 方法 | 用途 |
|---|---|
| `Cm() / BagDict(cm) / FurnDict(cm)` | 单例与表访问（未就绪返回 null，调用方据此重试） |
| `ParseIds(string)` | 解析 "1,2;3" 风格 id 串 |
| `WantSize(id, w0, h0, wMult, hMult)` | 倍数取整（AwayFromZero：7×1.5=10.5→11） |
| `RewriteSize(targets, wMult, hMult, verbose)` | 改名单内 Config_Bag.Size + 白名单置空；(scanned, changed, lines)，未就绪 scanned=-1 |
| `RewriteBurden(targets, mult, verbose)` | 改名单内 Config_Bag.Burden（负重快照，需重读档体现） |
| `RewriteFridgeChill(bagTargets, keepMult, verbose)` | 改 `BagId∈名单 && ColdRate>0` 家具的 ColdRate ÷ keepMult |
| `InvalidateBagSizeIndex(cm, lines)` | `_bagSizeSet = null` 懒重建 |

## WelcomeTip.cs（防盗署名气泡，2026-10-14）

- 目的：限制恶意转载收费引流——进档后主角头顶弹 3 秒原生对话气泡（欢迎语+署名+抖音号）。
- 入口 `WelcomeTip.Install(Log)`：`GameObject.Find("SurvivalLog.WelcomeTip")` 去重，先到者建
  DontDestroyOnLoad ticker → 装任意一个套件 mod 即有气泡，装多个只弹一条。
- 气泡通道（实测定稿，六修）：主通道 `CharacterTalkManager.EnqueueTalk`——游戏官方说话队列，
  与原生气泡消息排队轮流显示、互不顶替（直显会被进档瞬间的原生推送覆盖）；管理器不可用时
  兜底 `Unit_ShowHeadTalk(text, 3)` 直显。主角模型 `WebGm.GetLeadingRoleModel(out string)` 一行获取。
  主角头顶条为官方**无框文字**样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；
  During≈真实秒（传 360 会远超 3 秒）。
- 生命周期：轮询 `BaseSingleton<BattleLogicWorld>.IsInstanceCreated` 沿 false→true（进档）触发、
  true→false（回菜单）复位；等主角模型最长 15s，超时静默放弃。全程只读，不改游戏数据。
- 无 cfg 开关（防盗内置行为，防剥离，也避开 cfg 首跑固化坑）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；**改动需 7 个 mod 全部重编**。

## 维护约定

- `BagTableRw.cs` 是 4 个扩容系 mod 的公共依赖：改动必须逐一评估对 Backpack/Cabinet/FridgeExpand/FridgeChill 的影响，
  并同步更新对应 DESIGN.md。
- `WelcomeTip.cs` 是全部 7 个套件 mod 的公共依赖：改动需同步更新 7 份 DESIGN.md 的"防盗署名气泡"节并全量重编。
- 任何新增写操作先过红线检查：**不写 Data_Item / 不写存档，零例外**（曾有的唯一例外
  RepairV11TimeScale 已于 2026-10-18 连同其调用点彻底移除——其判定区间误伤合法天赋档位；
  WelcomeTip 只读状态不写任何数据）。
