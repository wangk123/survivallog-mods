# 柜子容量 CabinetSize（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件 / 删 dll 即还原
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

家里的柜子（置物架家族、柜子、金属柜、冰箱、后备箱、门口购物袋……）格子不够用，原版只能"大柜小柜都那几格"。本 mod 把柜子容量改成**数值直调**：全部家里的柜子按 小/中/大/特大 四档给固定宽×高（默认 小 14×10 / 中 16×12 / 大 18×14 / 特大 20×16），13 个最常用的柜子（置物架家族 5 + 冰箱 4 + 载具后备箱 3 + 工具柜）还能在配置里逐个单独改，任何一个柜子都可以用"个性化清单"写死成你想要的宽高。

## 二、功能清单

- 四档容量数值直调：小 14×10 / 中 16×12 / 大 18×14 / 特大 20×16（按柜子原生格数自动归档，新柜子自动跟上）；
- 13 个常用包独立条目：置物架小/中/大/超大/材料架、双门冰箱/冰柜/大容量冰柜/箱子族、自行车/轿车/SUV 后备箱、工具柜——cfg 或面板里单独调，0 = 恢复该柜官方原值；
- 个性化清单 `BagOverrides`：任意柜子（含黑名单柜）单独定宽高，优先级最高；
- 全局上限默认 20×16（防出屏，可放宽）；改配置即时生效；
- 与旧「柜子扩容」mod 互斥：检测到同装会自动停用并日志提示（升级用户请删旧 dll）。

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原；卸载后超界物品由游戏官方坐标矫正自动挪回格子内。
- 旧「柜子扩容（CabinetExpand）」已被本 mod 取代，请勿同时安装。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.CabinetSize.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响），游戏回到原版。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.cabinetsize.cfg`（记事本可改；档位/上限/清单改动即时生效，13 包绑定键与个性化条目支持记事本热改）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=还原官方原值并停写；下次启动不加载 |
| 全局.MaxWidth | 20 | 全局宽度上限（档位/自定义都夹；0=不封顶，自担风险） |
| 全局.MaxHeight | 16 | 全局高度上限（同上） |
| 全局.ExcludeIds | （空） | 追加排除的 BagId（逗号分隔）：在默认黑名单之外恢复官方原值 |
| 全局.BagOverrides | （空） | 个性化清单 "id:宽x高,id:宽x高"：任意柜子单独定宽高，优先级最高 |
| 档位.小.Width / Height | 14 / 10 | 小档（原生格数≤60 的柜子自动落此档） |
| 档位.中.Width / Height | 16 / 12 | 中档（原生 61~99） |
| 档位.大.Width / Height | 18 / 14 | 大档（原生 100~149） |
| 档位.特大.Width / Height | 20 / 16 | 特大档（原生≥150，如超大置物架/材料架） |
| Bag.<id>.Width / Height | 各柜默认 | 13 个常用包的独立条目（置物架/冰箱/后备箱/工具柜）；0 = 恢复该柜官方原值 |
| 全局.Preset | 4 | 默认值预设版本（勿手改）：mod 升级后未手动调过的条目自动跟新默认 |
| Debug.ListOnReady | true | 首次生效时日志列出全部受管柜子与尺寸前后 |
| Debug.DiagOnOpen | false | 读档时日志列出全部容器 BagConfigId（排查某柜不生效用） |

## 六、English

**Cabinet Size** — Numeric cabinet capacity: all home cabinets resized by four tiers (S 14×10 / M 16×12 / L 18×14 / XL 20×16), with per-cabinet overrides for 13 common bags plus a free-form override list for anything else.

**Features**
- Four numeric tiers (S/M/L/XL = 14×10 / 16×12 / 18×14 / 20×16); cabinets auto-tier by their vanilla size, new cabinets included.
- 13 common bags (shelves, fridges, vehicle trunks, tool cabinet) have individual config entries; 0 = vanilla size.
- `BagOverrides` list ("id:WxH,...") can pin ANY cabinet to any size, highest priority.
- Global cap defaults to 20×16 to stay on screen; changes apply immediately.

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.CabinetSize.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the game returns to vanilla (out-of-range items are nudged back by the game's own placement fix). Replaces the old "Cabinet Expand" mod — do not install both.

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.cabinetsize.cfg`. Key knobs: tier sizes (档位.*), global caps (全局.MaxWidth/MaxHeight) and the per-cabinet override list (全局.BagOverrides).

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
