# SurvivalLog.CabinetExpand — 柜子扩容（家里全部柜子 + 冰箱）

## 功能
- 家里全部"柜子类"容器格子宽、高各 ×1.5（四舍五入取整）：置物架全家族（小/中/大/超大/材料架）、柜子 1-3、金属柜系列、工具柜、床头柜、衣柜、玄关台、抽屉柜、储物柜等
- **冰箱格子同样宽、高各 ×1.5**（原"冰箱扩容"mod 已并入本 mod，倍数独立可调）
- 名单自动发现：游戏更新新增柜子/冰箱自动跟上，无需改配置
- 默认不碰：老鼠笼/粉碎机等机器槽、纸箱、门窗口暂存、无人机、载具、外出场景的货架柜台——想要可用配置加回
- 材料架说明：它是"分行视图"（一行一种材料、按件数铺卡片），底层仍是格子——扩容后更高=能放更多种材料、更宽=每行放更多件

## 安装
需先装好基座（见 SurvivalLog.Base 包）。把 `BepInEx\plugins\SurvivalLog.CabinetExpand.dll` 放进游戏 `BepInEx\plugins\`，启动游戏即生效。
从旧版升级（装过原冰箱扩容）：删除 `SurvivalLog.FridgeExpand.dll` 即可，用安装器升级会自动清理。

## 配置

配置文件已随包预置在 `BepInEx\config\`（解压即有）；若被删除，首次启动游戏时会按默认值自动重建。修改后重启游戏生效。
| 项 | 默认 | 说明 |
|---|---|---|
| WidthMult / HeightMult | 1.5 | 柜子宽高倍数（取整） |
| FridgeWidthMult / FridgeHeightMult | 1.5 | 冰箱宽高倍数（取整）；改成 1 = 还原原生 |
| ExtraIds | 空 | 追加 BagId（优先级最高；全冷链电器的包自动归冰箱倍数组） |
| ExcludeIds | 空 | 追加排除的包（柜子/冰箱都排） |
| ListOnReady | true | 首次生效时在日志列出全部受管包与尺寸前后 |
| DiagOnOpen | false | 读档时在日志列出家里全部容器的 BagConfigId（用于确认名单） |

## 查自己家容器用的是哪个包
开 `DiagOnOpen=true` 读档后看 `BepInEx\LogOutput.log` 中 `[CabinetExpand][诊断] 容器初始化 bagConfigId=xxx（名字）` 行，把需要的数字加进 ExtraIds。
注意 id 撞号：包 10002 是金属柜，家具"小型置物架"用的是包 110002——加错数字会扩到别的柜子。

## 卸载
删除 dll 即可还原；已放进扩容区域的物品会被游戏坐标矫正自动挪回，不会丢失。
