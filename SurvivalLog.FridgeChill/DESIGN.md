# 冰箱保鲜 FridgeChill — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。

## 1. 功能定位

把冰箱对食物的保鲜强度放大（默认 ×2：原生保鲜 5 倍 → 10 倍）。只改"冰箱的影响系数"，
**绝不直接改食物保质期**（这是本 mod 用一次事故换来的核心红线，见 §2）。

## 2. 实现原理（原生机制实证，反汇编 ComputeItemTimeScale / FoodPreservationRules）

- 家具保鲜时长倍率 = `1 / Config_Furniture.ColdRate`（ComputeItemTimeScale 运行时直读）。
  原生冰箱 ColdRate = 0.2 → 保质 ×5；本 mod 对名单内冰箱把 `ColdRate ÷ KeepMult`（默认 2）→ ×10。
- 天赋（Buff/AE_FridgeColdEnhance）走**计算层乘法**（2026-10-18 反汇编 @0x292E200 实证）：
  `有效值 = max(ColdRate × (1−天赋比率), 0.1)`——0.1=MinColdRate 是编译期常量，**乘完天赋后才封底**；
  天赋比率≤0 时直接返回表值、不经过下限。原生满天赋（比率 0.5）恰把 0.2 压到 0.1，即原生冰箱封顶 ×10。
- **与天赋/每日规划加成的关系（修正旧"独立叠加"表述）**：保鲜加成共四条通道——
  冰箱（AE_FridgeColdEnhance，走上式）、冷链房间（AE_ColdChainKeeper，同一函数作用于区域系数）、
  仓储（AE_StorageShelfLife）/背包（AE_BackpackShelfLife）（收尾 `×1/(1+比率)` 无下限）。
  后三条与本 mod 完全正交；**冰箱通道曾被 0.1 下限钳制**（表值 0.1 后任何天赋比率都被钳回，
  天赋零收益、显示恒 ×10——即"天赋不涨保鲜率"现象的根因）。
  本次起用 `TalentCapPatch` 把下限换成 `1/MaxTotalMult`（默认 ×100，可配；10=原生等价回退位），
  mod 与天赋恢复严格乘法正交："我们是我们，天赋是天赋"。补丁对原生玩家零影响
  （原生路径到不了新下限以下）；冷链封顶同步抬高属一致行为。
- 界面"保鲜率"显示读同一字段（Round(1/ColdRate)），自动跟随——**显示 10 倍 = mod 生效铁证**。
- 物品级快照：`Data_Item.TimeScale` 在物品**进入容器时**按当时系数算好存档，读档不重算。
  因此改系数只影响**新放入**的物品；已在冰箱里的旧食物**移出再放回一次**即按新系数重算。

### 核心红线：只改系数，绝不写 Data_Item（v1.1.0 事故，教训级实证）

- v1.1.0 曾直接把物品 `TimeScale ×0.5` 试图让旧物品立即翻倍：TimeScale 参与"进出容器换算"，
  直接改它 → 换算基准被污染 → **一进一出保质期减半**（用户实锤：菠菜 13 天一进一出变 6.5 天）。
- v1.2.0 起定稿正确语义（用户原话）："修改冰箱对食物保质期的影响系数，不是直接修改食物保质期"。
- v1.2.0 曾附带存档修复器（v1.1.0 残留 ts ×2 还原），**2026-10-18 彻底移除**：其判定区间
  [0.30,0.55] 只按事故值 0.385 标定，未覆盖合法天赋档位——当天实测误伤 6 件 ts=0.5 的物品
  （显示保质翻倍=西瓜 2000 天事件）。**自此全套件零物品写入、零存档写入，无任何例外。**
- 档位表保留仅作取证参考（判别存档里历史数值的语义用），不再驱动任何写操作。

### TimeScale 档位表（历史取证参考，不驱动任何写操作）

| 值 | 含义 |
|---|---|
| 0.769 | 家 buff / 仓储天赋全屋值（=1/1.3） |
| 0.385 | v1.1.0 把 0.769 写坏后的值（修复对象：∈[0.30,0.55] 区段 ×2 还原） |
| 0.0769 | 原生冰箱正确值 |
| 0.0385 | mod 新系数下的冰箱正确值（<0.1 一律不碰） |

## 3. 代码结构

| 文件 | 职责 |
|---|---|
| `FridgeChillPlugin.cs` | 入口：配置定义、Rerun、注入 Ticker、挂 TalentCapPatch |
| `ChillTicker`（同文件） | 5 秒轮询：RewriteFridgeChill（改系数）；KeepMult≤1.01 直接跳过 |
| `TalentCapPatch`（同文件） | Harmony Postfix 接管 `FoodPreservationRules.ApplyFridgeColdEnhance`：把 0.1 下限换成 1/MaxTotalMult；KeepMult≤1 或异常时交还原生结果。注意 ApplyColdChainEnhance 与其同一段原生代码，只 patch 一个名字即覆盖两条通道 |
| `ModShared\BagTableRw.cs` | 共享源码（编译进本 dll）：RewriteFridgeChill 按 `Config_Furniture.BagId ∈ 名单 && ColdRate>0` 定位冰箱家具 |

## 4. 配置项（com.local.survivallog.fridgechill.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| KeepMult | 2 | 保鲜倍数：2 = 保质期翻倍（ColdRate÷2）；1 = 不改 |
| MaxTotalMult | 100 | 总保鲜倍数上限：表值改写与天赋/规划等加成合并后的封顶；10 = 原生 ×10（不抬高）；低于 10 按 10 处理；KeepMult≤1 时不生效 |
| BagIds | 空 | 冰箱 BagId 名单（留空用默认 1004,115000,115001,115005,3002，可追加） |
| Verbose | false | 输出每条明细 |

## 5. 名单与依据

默认 `BagIds = { 1004, 115000, 115001, 115005, 3002 }`（与 FridgeExpand 一致，覆盖全部可获得冰箱；
115005 = 大容量冰柜，家具 15002，2026-10-08 游戏更新新增，原生 ColdRate 0.2）。
定位方式：遍历 `Config_Furniture` 表，取 `BagId ∈ 名单 && ColdRate > 0` 的家具——
既排除无冷藏能力的共用包家具，也不误伤 ColdRate>0 的非冰箱（垃圾堆/堆肥箱/货车不在 BagId 名单内）。

**范围外（刻意）**：冷链房间（FridgeAreaMap/areaScale，按房间区域生效的另一套机制）、堆肥箱 115002。

## 6. 与原生 / 其他 mod 的交互

- 天赋保鲜增强独立叠加（§2），玩家点天赋不会被 mod 吞收益。
- 与 FridgeExpand 同名单不同字段（Size vs ColdRate），互不干扰。
- 旧物品生效方式 = 移出再放回一次（用户已知晓）；已折损的保质时长不可恢复（v1.1.0 事故损失，已告知用户）。

## 7. 已知限制

- 改系数对已在冰箱里的存量物品不追溯（快照机制），移出放回一次即重算。
- 0.769 档"家 buff"物品若曾被 v1.1.0 波及，修复后仍是原语义值，不额外补偿。

## 8. 演进要点（详细见根 CHANGELOG.md）

- v1.1.0（2026-10-02）：错误方案（写 TimeScale）→ 菠菜事故；
- v1.2.0：定稿"只改 ColdRate 系数"，并带一次性存档修复；
- v1.3.0：名单/日志定稿；v1.3.1：统一套件版本号。
- 本次（2026-10-18，待发版）：反汇编实证原生公式与四通道后，新增 TalentCapPatch 抬高总保鲜
  封顶（MaxTotalMult 默认 ×100 可配），修复"冰箱通道天赋/规划加成被 MinColdRate=0.1 钳制"；
  修正本文档 §2 旧"独立叠加"表述。同日实测发现修复器误伤合法天赋档（日志实锤：6 件 ts=0.5
  被改 1.0，显示保质翻倍=西瓜 2000 天事件），**修复器彻底移除**——"绝不写 Data_Item"红线自此零例外。
  取证工具链：tools/il2cppdumper + tools/disasm_tool + 新增 tools/mdump（SRM 元数据 dumper）。

## 9. 迭代维护清单（改本 mod 时同步更新）

- 保鲜机制相关改动 → §2 必须附实证依据，**任何写 Data_Item 的提议都违反红线，直接否决**；
- 名单/配置调整 → §4 §5；
- 每次发版在根 CHANGELOG.md 补一节。

## 10. 防盗署名气泡（2026-10-14，套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入）。
- 行为：进档后主角头顶显示欢迎文字（约 3 秒）——主通道走游戏官方说话队列 `CharacterTalkManager.EnqueueTalk`（与原生气泡消息排队轮流显示、互不顶替；进档瞬间有原生推送也不会被覆盖）；管理器不可用时退 `AgentModel.Unit_ShowHeadTalk(text, 3)` 直显。实测主角头顶条为官方无框文字样式（silentHeadTalk 默认 false，各通道均无框，非缺陷）；During≈真实秒（传 360 会远超 3 秒）。每次进档一次，回主菜单再进再弹。
- 7 个套件 mod 全部调用，靠 `GameObject.Find("SurvivalLog.WelcomeTip")` 去重：先到者创建 DontDestroyOnLoad ticker，其余跳过——装多个只弹一条。无 cfg 开关（防盗署名为内置行为，防剥离）。
- 文案单源 `WelcomeTip.Text`（作者 Night · 抖音号 166660371）；改文案后 7 个 mod 需全部重编。
