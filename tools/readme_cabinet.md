# SurvivalLog.CabinetExpand — 工具柜/储物柜扩容

## 功能
- 柜子格子：宽、高各 ×1.5（四舍五入取整）
- 默认覆盖（BagId）：110001（**工具柜**，与中型置物架共用包）、10007（工具柜）、1002/1003（柜子）、10002/10005/10006/10008（金属柜系列）
- 注意：家中"工具柜"家具与"中型置物架"使用同一个包，扩容会同时生效；不希望置物架变大可在配置里用别的手段取舍（两者无法分开）

## 安装
需先装好基座（见 SurvivalLog.Base 包）。把 `BepInEx\plugins\SurvivalLog.CabinetExpand.dll` 放进游戏 `BepInEx\plugins\`，启动游戏即生效。

## 配置

配置文件已随包预置在 `BepInEx\config\`（解压即有）；若被删除，首次启动游戏时会按默认值自动重建。修改后重启游戏生效。
| 项 | 默认 | 说明 |
|---|---|---|
| WidthMult / HeightMult | 1.5 | 宽高倍数（取整） |
| ExtraIds | 空 | 追加 BagId。置物架(110000/110008)、床头柜(110003)、纸箱(1010/2003)、抽屉柜(1006)等未含，需要可自行添加 |
| ExcludeIds | 空 | 排除 BagId（优先级最高） |
| DiagOnOpen | true | 读档时在日志列出家里全部容器的 BagConfigId（用于确认名单） |

## 查自己家容器用的是哪个包
开游戏读档后看 `BepInEx\LogOutput.log` 中 `[CabinetExpand][诊断] 容器初始化 bagConfigId=xxx（名字）` 行，把需要的数字加进 ExtraIds。

## 卸载
删除 dll 即可还原；已放进扩容区域的物品会被游戏坐标矫正自动挪回，不会丢失。
