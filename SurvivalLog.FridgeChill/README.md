# 冰箱保鲜 FridgeChill（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档/不改游戏文件/不改物品数据
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

原版冰箱把食物保质期放慢 5 倍，中后期还是经常吃不完就坏。本 mod 只调整"冰箱对食物的保鲜系数"：默认翻倍，5 倍 → 10 倍；同时抬高原版 ×10 的总保鲜封顶（默认 ×100，可配），让保鲜天赋继续有真实收益。冰箱界面显示的"保鲜率"会自动跟随，看到 10 倍就是生效了。

## 二、功能清单

- 默认保鲜 ×2：原生 5 倍 → 10 倍，食物在冰箱里的保质期直接翻倍
- 抬高原版 ×10 的总保鲜封顶：冰箱天赋/每日规划的保鲜加成不再被原版上限压住，合并后的总封顶默认 ×100（可配；填 10 = 保持原版封顶不抬高）
- 覆盖全部可获得冰箱：双门冰箱、双开门冰箱、冰柜、大容量冰柜、小冰箱族（默认名单 1004,115000,115001,115005,3002，可自行追加）
- 只改冰箱保鲜系数，绝不直接修改任何食物的保质期数据、绝不写存档——这是本 mod 的硬性红线
- 冰箱界面"保鲜率"数字自动跟随新系数，无需额外操作
- 倍数随时可调：KeepMult 设 1 = 不改；总开关设 false = 立即还原官方原值

## 三、安全与还原

- 基线：不改存档 / 不改游戏文件 / 删 dll 即还原；此外本 mod 绝不写入任何食物（物品）数据，只调冰箱侧的保鲜系数
- 生效方式：保鲜系数在食物"放入容器时"计算并存进物品档，因此改动只影响之后放入的食物；已在冰箱里的旧食物，移出再放回一次即按新系数重算
- 已经折损的保质时长不会恢复（只影响之后的计算）
- 天赋、冷链房间、仓储、背包四条保鲜通道互不干扰：本 mod 不吞天赋收益（原版冰箱天赋最多到 ×10 封顶，本 mod 把封顶抬到可配的 ×100 后天赋继续有效）
- 卸载或关总开关后保鲜系数立即回到原生（5 倍）；mod 生效期间放入的食物按当时已算好的保质继续，不会损坏
- 进档后主角头顶会短暂显示作者署名欢迎语（约 3 秒；装多个本套件 mod 只弹一条）

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.FridgeChill.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响）：冰箱保鲜系数立即回到原生（5 倍），之后放入/挪动的食物按原版系数计算；已存食物按各自已算好的保质继续，不受影响。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.fridgechill.cfg`（倍数改完即时生效，影响之后放入冰箱的食物。用记事本可改）。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=还原官方原值并停写；下次启动不加载 |
| Chill.KeepMult | 2 | 保鲜倍数：2 = 食物在冰箱里的保质期翻倍（原生 5 倍→10 倍）；1 = 不改 |
| Chill.MaxTotalMult | 100 | 总保鲜倍数上限：系数改写与天赋/规划等加成合并后的封顶。默认 100；10 = 原生上限 ×10（即不抬高）；低于 10 按 10 处理；保鲜关闭（KeepMult≤1）时本项不生效 |
| Chill.BagIds | （空） | 冰箱 BagId 名单（逗号分隔；留空用默认 1004,115000,115001,115005,3002，可追加） |
| Debug.Verbose | false | 输出每条明细 |

## 六、English

**Fridge Chill** — Keep food fresh far longer by tuning only the fridge's chill factor: vanilla x5 preservation becomes x10 by default, with the total cap raised to a configurable x100 so preservation talents keep paying off.

**Features**
- Default x2 preservation: vanilla x5 -> x10 shelf life for food in fridges
- Raises the vanilla x10 total cap (default x100, configurable; set 10 to keep the vanilla cap) so fridge talents and daily-planning bonuses are no longer clamped
- Covers every obtainable fridge: double-door, french-door, freezer, large chest freezer and the mini-fridge family (default bag list 1004,115000,115001,115005,3002, extendable)
- Only rewrites the fridge chill factor — never touches food item data or saves (hard red line of this mod)
- The "preservation rate" shown in the fridge UI follows the new factor automatically
- Set KeepMult to 1 to disable; the master switch restores vanilla values instantly

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.FridgeChill.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the game returns to vanilla (x5) immediately — food already stored keeps its computed shelf life and is never damaged. Note: food that was already inside a fridge before installing recalculates after one take-out-and-put-back cycle.

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.fridgechill.cfg`. Key knobs: Chill.KeepMult (preservation multiplier, 1 = off), Chill.MaxTotalMult (total cap including talents, default 100), Chill.BagIds (custom fridge bag list), plus the General.Enabled master switch.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
