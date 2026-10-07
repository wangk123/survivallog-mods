# 柜子扩容 CabinetExpand — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。

## 1. 功能定位

把储物类家具（工具柜/储物柜/金属柜系列）的格子容量放大（默认宽、高各 ×1.5）。不改存档、不改游戏文件，删 dll 即还原。

## 2. 实现原理（原生机制实证）

与 BackpackExpand 同架构，差异只在**目标名单**（详见 `ModShared\README.md` 与 BackpackExpand DESIGN §2）：

- 改写 `ConfigManager._Config_Bag_Dict` 内名单条目的 `Size`（三层实时直读）+ `_bagSizeSet` 置 null 重建白名单；
- 5 秒轮询幂等改写（防读档/切场景表重载覆盖）；
- 本 mod 不动 Burden（柜子没有负重语义）。

### 本 mod 独有的关键实证：家具名 ≠ Bag 表名（v1.3.0 定案）

- 游戏内叫"工具柜"的家具 70006，实际用 **bag 110001**——与"中型置物架"**共用同一个包**。
  bag 表里的名字（110001 显示为中型置物架）不代表家具种类；**识别必须走"家具 → BagId"映射**，
  这是默认名单里 110001 的来源，也是 DiagOnOpen 诊断存在的原因。
- 工具柜界面**不走 ToolTable reducer**，走背包双栏视图（A/B 栏）——v1/v2 两代诊断钩子
  挂载成功但从不触发，最终实证可靠的通道是 `BagComponent.OnInit` 实例方法 postfix（见 §3）。

## 3. 代码结构

| 文件 | 职责 |
|---|---|
| `CabinetExpandPlugin.cs` | 入口：配置定义、Rerun、挂诊断补丁、注入 Ticker |
| `BagInitDiag`（同文件） | Harmony postfix `BagComponent.OnInit(int)`：读档时列出全部容器实体的 BagConfigId/名称/当前尺寸（DiagOnOpen=true 时） |
| `CabinetTicker`（同文件） | 5 秒轮询幂等改写（同 Backpack 架构） |
| `ModShared\BagTableRw.cs` | 共享源码（编译进本 dll） |

### 诊断钩子的三代演进（都是实锤，复用前必读）

1. v1：patch `Ac_ToolTable_Open.SendAction`（泛型 List 参数）——挂载成功但**从不触发**（interop 泛型坑第二次实锤）；
2. v2：patch `Reducer_Web_ToolTable.Trigger_RA_Open`——同样不触发（根因：界面根本不走 ToolTable）；
3. v3（现行）：patch **实例方法** `BagComponent.OnInit`——实测可靠，读档即打印全部容器清单，一轮定位。
   结论：**patch 静态方法/带泛型参数的方法不可靠，实例方法可靠**（与旧 mod get_StackLimit getter 先例一致）。

## 4. 配置项（com.local.survivallog.cabinetexpand.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| WidthMult | 1.5 | 宽度倍数（AwayFromZero 取整） |
| HeightMult | 1.5 | 高度倍数（同上） |
| ExtraIds | 空 | 追加 BagId |
| ExcludeIds | 空 | 排除 BagId（优先级最高） |
| Verbose | false | 输出每条明细 |
| DiagOnOpen | false | 读档时在日志列出全部容器的 BagConfigId（确认名单用，平时关防刷屏） |

## 5. 默认名单与依据

`DefaultIds = { 110001, 10007, 1002, 1003, 10002, 10005, 10006, 10008 }`

| BagId | 对应家具（实测） |
|---|---|
| 110001 | 家具 70006"工具柜"（与中型置物架共用包）——用户家工具柜不生效问题的正解 |
| 10007 | 工具柜 406 |
| 1002, 1003 | 储物柜/柜子 |
| 10002, 10005, 10006, 10008 | 金属柜系列 |

**刻意不含**（用户可选加）：置物架 110000/110008、床头柜 110003、纸箱 1010/2003、抽屉链 5012/5013（用户已明确撤销）。

**某柜子不生效的标准排查法**：cfg 开 `DiagOnOpen=true` → 读档 → 看 LogOutput.log 里 `OnInit bagConfigId=xxx` 行 → 把该 id 加进 ExtraIds。

## 6. 与原生 / 其他 mod 的交互

- 与 BackpackExpand / FridgeExpand 名单零重叠，可任意组合。
- 110001 与置物架共用包意味着：扩 110001 会同时放大中型置物架（共用包的固有属性，无害）。
- 不写存档、不碰物品；卸载即还原。

## 7. 已知限制

- 共用包（如 110001）扩容会波及共用者，无法只放大单一家具。
- 场景固定容器（不可搬运）若用名单外 BagId，默认不受管（用 §5 排查法补名单）。

## 8. 演进要点（详细见根 CHANGELOG.md）

- v1.3.0（2026-10-02）：默认名单由"猜测名单"修正为 OnInit 诊断实锤名单（+110001 等）；DiagOnOpen 默认 false 防刷屏。
- v1.3.1：统一套件版本号。

## 9. 迭代维护清单（改本 mod 时同步更新）

- 名单调整 → §5（并注明实测依据）；
- 诊断通道变化 → §3（含不触发的通道记录）;
- 配置增删 → §4；
- 每次发版在根 CHANGELOG.md 补一节。
