# ModShared — 扩容 mod 共享源码

`BagTableRw.cs`（编译时由 4 个扩容 mod 各自链入，产物为各自独立 dll，运行时互不依赖）。

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
| `RepairV11TimeScale()` | **一次性**修复 FridgeChill v1.1.0 写坏并存档的物品 TimeScale（0.385 区段 ×2；<0.1 冰箱正确值不碰），按 InstanceId 防重复 |

## 维护约定

- 本文件是 4 个 mod 的公共依赖：改动必须逐一评估对 Backpack/Cabinet/FridgeExpand/FridgeChill 的影响，
  并同步更新 4 份 DESIGN.md。
- 任何新增写操作先过红线检查：**不写 Data_Item / 不写存档**（唯一例外 RepairV11TimeScale 是修复历史损伤）。
