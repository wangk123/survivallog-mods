# 柜子扩容 CabinetExpand（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档/不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

家里的柜子、冰箱、车后备箱格子常常不够用。本 mod 把三大类容器分别放大——柜子族、冰箱族、载具组（各档后备箱 + 家门口购物袋），默认宽、高各 ×1.5，三组倍数独立可调。名单自动发现：游戏更新加了新柜子、新冰箱、新车都会自动跟上。原"冰箱扩容"功能已并入本 mod，无需另装。

## 二、功能清单

- 柜子族宽高各 ×1.5：置物架全家族（小/中/大/超大/材料架）、柜子 1-3、金属柜系列、工具柜、床头柜、衣柜、玄关台、抽屉柜、储物柜、箱子族——如大型置物架 12×10 → 18×15、超大型置物架 15×12 → 23×18
- 冰箱族独立倍数（默认 ×1.5）：双开门冰箱 8×7 → 12×11、大容量冰柜 12×10 → 18×15 等；未来新冰箱自动识别
- 载具组独立倍数（默认 ×1.5）：车行三档后备箱（自行车/轿车/SUV）自动跟随新车 + 家门口购物袋 12×10 → 18×15（每袋装更多；袋子数量上限仍为 2 个）
- 名单自动发现：游戏更新新增的柜子/冰箱/后备箱自动纳入扩容，无需等 mod 补名单
- 扩后宽度默认统一封顶 23 列（与超大型置物架同宽）：全部柜子页签同宽、界面只留一档缩放，切柜子不跳尺寸
- 自带视口适配守卫：修复大柜子页签把界面右栏挤出屏幕的问题

## 三、安全与还原

- 基线：不改存档 / 不改游戏文件 / 删 dll 即还原。还原时扩容区里已放的物品由游戏自动挪回合法格子，不会丢
- 只调整内存里的格子配置；容器负重不归本 mod 管（如需解除负重限制请搭配「柜子负重解放」）
- 倍数改动即时生效，柜子界面当帧变化
- 默认黑名单不扩：厨房电器（炉子/烤箱等，格子是烹饪槽位）、机器槽（老鼠笼/粉碎机等）、纸箱族（与工具桌共包）、门窗口暂存、无人机、场景搜刮货架——需要的话可在配置里按 BagId 加回
- 已知限制：部分家具共用同一套格子（如工具柜与中型置物架），无法只放大其中一件；家门口购物袋只扩格子、数量上限 2 个不变
- 进档后主角头顶会短暂显示作者署名欢迎语（约 3 秒；装多个本套件 mod 只弹一条）

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.CabinetExpand.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载）：柜子/冰箱/后备箱格子立即回到原版，已放的物品由游戏自动挪回合法格子。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.cabinetexpand.cfg`（倍数改完即时生效。用记事本可改）。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=还原官方原值并停写；下次启动不加载 |
| Expand.WidthMult | 1.5 | 柜子宽度倍数（四舍五入取整） |
| Expand.HeightMult | 1.5 | 柜子高度倍数（四舍五入取整） |
| Expand.FridgeWidthMult | 1.5 | 冰箱宽度倍数（四舍五入取整）。1=还原原生 |
| Expand.FridgeHeightMult | 1.5 | 冰箱高度倍数（四舍五入取整）。1=还原原生 |
| Expand.VehicleWidthMult | 1.5 | 载具后备箱与门口购物袋宽度倍数（四舍五入取整）。1=还原原生 |
| Expand.VehicleHeightMult | 1.5 | 载具后备箱与门口购物袋高度倍数（四舍五入取整）。1=还原原生 |
| Expand.MaxCols | 23 | 扩后宽度上限（倍数取整后再封顶；0=不封顶）。默认 23=与超大型置物架同宽，全部柜子页签同宽、切柜子不跳尺寸；材料架受影响：30→23 列（540→414 格） |
| Expand.ExtraIds | （空） | 追加要扩容的 BagId（逗号分隔，优先级最高）；全冷链电器的包自动归冰箱组。注意 id 撞号：包 10002 是金属柜，家具"小型置物架"用的是包 110002，别加错 |
| Expand.ExcludeIds | （空） | 追加要排除的 BagId（逗号分隔，柜子/冰箱两组都排）；默认黑名单已排除厨房电器、机器槽、床/垃圾桶、纸箱共包、门窗口暂存、无人机、载具、场景搜刮货架 |
| UiFit.Enabled | true | 视口适配守卫：修复大柜子页签把界面右栏挤出屏幕 |
| Debug.Verbose | false | 每轮输出明细日志 |
| Debug.ListOnReady | true | 首次生效时在日志列出全部受管包与扩容前后尺寸（核对名单用） |
| Debug.DiagOnOpen | false | 读档时在日志列出全部容器的 BagConfigId。排查"某个柜子不生效"：开此项读一次档，从日志找到该柜子的 id 加进 ExtraIds；平时关闭防刷屏 |

## 六、English

**Cabinet Expand** — Enlarge home cabinets, fridges and vehicle storage (all trunk tiers + the door-side shopping bags) by 1.5x in width and height by default, with independent multipliers per group.

**Features**
- Cabinet group x1.5: the whole shelf family (small/medium/large/extra-large/material rack), cupboards 1-3, metal lockers, tool cabinet, nightstands, wardrobes, drawer units, storage lockers, box family — e.g. large shelf 12x10 -> 18x15, extra-large 15x12 -> 23x18
- Fridge group with its own multipliers (default x1.5): french-door fridge 8x7 -> 12x11, large chest freezer 12x10 -> 18x15; fridges added by future updates are picked up automatically
- Vehicle group with its own multipliers (default x1.5): all trunk tiers (bike/car/SUV) auto-discovered, plus the door-side shopping bags 12x10 -> 18x15 (each bag holds more; the 2-bag limit is unchanged)
- Auto-discovered target list: cabinets/fridges/trunks added by game updates are covered without a mod update
- Width capped at 23 columns by default (matching the extra-large shelf) so all cabinet tabs share one size and UI scaling never jumps
- Built-in viewport guard keeps enlarged cabinet panels from pushing UI columns off screen

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; the game returns to vanilla immediately, and stored items are automatically relocated to valid slots.

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.cabinetexpand.cfg`. Key knobs: per-group width/height multipliers (cabinet / fridge / vehicle), MaxCols width cap (23 by default), ExtraIds/ExcludeIds to fine-tune the bag list, plus UiFit.Enabled for the viewport guard and the General.Enabled master switch.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
