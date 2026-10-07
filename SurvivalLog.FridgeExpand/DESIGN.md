# 冰箱扩容 FridgeExpand — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json；机制与实证结论长期有效。

## 1. 功能定位

把全部**可获得**冰箱的格子容量放大（默认宽、高各 ×1.5）。不改存档、不改游戏文件，删 dll 即还原。
与 FridgeChill（保鲜）同名单但职责独立：本 mod 只管格子数量，不管保鲜系数。

## 2. 实现原理（原生机制实证）

与 BackpackExpand 同架构（详见 `ModShared\README.md` 与 BackpackExpand DESIGN §2）：

- 改写 `ConfigManager._Config_Bag_Dict` 名单条目 `Size`（三层实时直读）+ `_bagSizeSet` 置 null；
- 5 秒轮询幂等改写；不动 Burden。

### 冰箱识别的关键实证：按 BagId，不按家具特征

- **ColdRate>0 不是冰箱专属**（垃圾堆/堆肥箱/货车也有 ColdRate）；反过来医用冷藏柜（家具 66114）
  ColdRate=0 但用 bag 1004。因此**识别必须按 BagId 名单**，不能按"是否有冷藏系数"等家具特征。
- 同名/共用包情况：1004 被双门冰箱/食堂冰箱/医用冷藏柜**共用**；115001 被冰柜/豪华双门共用；
  3002（小冰箱）被家具"废弃纸箱 90046"共用（共用包波及，无害）。

## 3. 代码结构

| 文件 | 职责 |
|---|---|
| `FridgeExpandPlugin.cs` | 入口：配置定义、Rerun、注入 Ticker |
| `FridgeTicker`（同文件） | 5 秒轮询幂等改写（同 Backpack 架构） |
| `ModShared\BagTableRw.cs` | 共享源码（编译进本 dll） |

## 4. 配置项（com.local.survivallog.fridgeexpand.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| WidthMult | 1.5 | 宽度倍数（AwayFromZero 取整） |
| HeightMult | 1.5 | 高度倍数（同上） |
| ExtraIds | 空 | 追加 BagId |
| ExcludeIds | 空 | 排除 BagId（优先级最高） |
| Verbose | false | 输出每条明细 |

## 5. 默认名单与依据

`DefaultIds = { 1004, 115000, 115001, 3002 }`

| BagId | 覆盖家具（实测） |
|---|---|
| 1004 | 双门冰箱 / 食堂冰箱 / 医用冷藏柜（共用包） |
| 115000 | 双开门冰箱 |
| 115001 | 冰柜 / 豪华双门冰箱（共用包） |
| 3002 | 小冰箱（波及废弃纸箱 90046，无害） |

**刻意不含**：场景装饰类非卖品（巨型冷冻柜 2001 等）——玩家建家不可获得，无扩容意义；需要可加 ExtraIds。

## 6. 与原生 / 其他 mod 的交互

- 与 FridgeChill 共用同一份 BagId 名单值（4 个 id 一致）：格子数（本 mod）与保鲜系数（Chill）互不干扰，
  两个 mod 可只装其一。
- 与 BackpackExpand / CabinetExpand 名单零重叠。
- 不写存档；卸载即还原。

## 7. 已知限制

- 共用包波及（§5）无法避免；同类家具一起变大属预期行为。
- 若游戏更新新增冰箱 BagId，需 dump 确认后加名单（CabinetExpand 的 DiagOnOpen 诊断可复用定位）。

## 8. 演进要点（详细见根 CHANGELOG.md）

- v1.3.0（2026-10-02）：套件首发形态（宽高各 ×1.5 定稿）。
- v1.3.1：统一套件版本号。

## 9. 迭代维护清单（改本 mod 时同步更新）

- 名单调整 → §5（注明实测依据）；
- 配置增删 → §4；
- 每次发版在根 CHANGELOG.md 补一节。
