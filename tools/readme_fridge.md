# SurvivalLog.FridgeExpand — 冰箱扩容

## 功能
- 冰箱格子：宽、高各 ×1.5（四舍五入取整）
- 默认覆盖游戏内全部可获得冰箱（BagId）：1004（双门冰箱/食堂冰箱/医用冷藏柜共用）、115000（双开门冰箱）、115001（冰柜/豪华版双门共用）、3002（小冰箱）
- 场景装饰类（非卖品巨型冷冻柜等）未含，需要可加 ExtraIds

## 安装
需先装好基座（见 SurvivalLog.Base 包）。把 `BepInEx\plugins\SurvivalLog.FridgeExpand.dll` 放进游戏 `BepInEx\plugins\`，启动游戏即生效。

## 配置

配置文件已随包预置在 `BepInEx\config\`（解压即有）；若被删除，首次启动游戏时会按默认值自动重建。修改后重启游戏生效。
| 项 | 默认 | 说明 |
|---|---|---|
| WidthMult / HeightMult | 1.5 | 宽高倍数（取整） |
| ExtraIds / ExcludeIds | 空 | 追加/排除 BagId |

## 卸载
删除 dll 即可还原；已放进扩容区域的物品会被游戏坐标矫正自动挪回，不会丢失。
