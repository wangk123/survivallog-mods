# 柜子负重解放 CabinetBurden（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档/不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

原版不少储物家具有负重上限——格子明明没满，却因为"太重"放不进去。本 mod 把全部带负重的储物容器直接改为"无负重限制"（与官方冰箱/置物架天生的无负重走同一套逻辑），格子满之前想放多少放多少。安装后正常进档即生效，柜子界面的负重条也会像冰箱一样整行隐藏。

## 二、功能清单

- 全部原生带负重的储物容器清零为无限制：柜子 1/2/3（原上限 14400/24000/24000）、篮子、储物柜、货车车厢、无人机货舱、门口暂存格、窗台收纳、中控台货舱等 10 个包
- 清零 = 官方"无负重"语义：格子满之前没有任何重量检查，等同冰箱/置物架
- 名单自动发现：游戏更新后新出的带负重容器自动覆盖，无需改名单
- 柜子界面负重条与家具详情"负重上限"行整行隐藏（与冰箱/置物架观感一致）
- 默认不动的包：购物车/轿车（载具体系）、工作台抽屉——要解放可在配置 IncludeBagIds 里按 BagId 加回
- 只管负重上限，不碰格子数量，与格子扩容类 mod 零冲突；玩家自身的超重惩罚不受影响

## 三、安全与还原

- 基线：不改存档 / 不改游戏文件 / 删 dll 即还原。只在内存里把容器负重上限清零，格子数量、物品数据一概不碰
- 生效时机：负重是容器生成时的快照——mod 随游戏启动加载并抢先改写，正常流程开机进档即生效；老存档装 mod 后第一次进档即完成
- 还原时机：删 dll 或把 Enabled 设为 false 后，已存在的容器需重读一次存档才回到原版负重；新制作/新放置的家具立即按原版生效
- 原生柜与玩家可造的同名柜共用同一套容器定义，按"柜子种类"整体生效，无法也无需区分
- 进档后主角头顶会短暂显示作者署名欢迎语（约 3 秒；装多个本套件 mod 只弹一条）

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.CabinetBurden.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响）：容器负重上限需重读一次存档才回到原版数值（新制作/新放置的家具立即回原值）。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.cabinetburden.cfg`（改完即时写入内存配置表；已存在的容器需重进存档一次才体现。用记事本可改）。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| Burden.Enabled | true | true=储物包负重清零（无负重限制，等同冰箱/置物架；柜子界面负重条整行隐藏）；false=停用并写回原生值。切换后需重进存档才对已存在容器生效 |
| Burden.IncludeBagIds | （空） | 追加要解放的 BagId（逗号分隔）。默认不含：工作台抽屉 5010/5012/5013、购物车 3、轿车 4；要解放就填对应数字 |
| Burden.ExcludeBagIds | 3,4 | 排除的 BagId（优先级最高）。默认排除 3(购物车)/4(轿车)——载具包走背包装备体系 |
| Debug.ListOnReady | true | 首次生效时在日志列出全部受管储物包与负重改写（核对名单用） |
| Debug.Verbose | false | 每轮输出明细 |

注：旧版本的 BurdenMult（倍数）键已移除，老配置文件里残留的该键无效，可手动删除。

## 六、English

**Cabinet Burden Unbound** — Remove the weight limit from all storage furniture: cupboards, wardrobes, boxes, lockers and more become weight-unlimited (exactly like vanilla fridges and shelves) until every slot is filled.

**Features**
- All vanilla weight-limited storage bags set to unlimited: cupboards 1-3 (former limits 14,400/24,000/24,000), basket, storage locker, truck cargo, drone bay, door stash, windowsill storage, console bay — 10 bags in total
- Zero limit uses the game's own "no weight" path (same as vanilla fridges/shelves): no weight check until slots run out
- Target list auto-discovered: weighted containers added by future game updates are covered automatically
- The weight bar and "weight limit" line in cabinet UI are hidden entirely, matching the fridge/shelf look
- Left untouched by default: shopping cart / car (vehicle bags) and workbench drawers — add their BagIds in IncludeBagIds if you want them freed
- Only touches weight limits, never grid sizes or items — fully compatible with grid-expansion mods; the player's own overweight debuff is unaffected

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.CabinetBurden.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the game returns to vanilla after one save reload (newly crafted/placed furniture reverts immediately).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.cabinetburden.cfg`. Burden.Enabled toggles the unlimited mode (false = restore vanilla; reload the save to apply to existing containers), IncludeBagIds/ExcludeBagIds fine-tune the bag list, and two debug switches control logging. The obsolete BurdenMult key from the old version is ignored and can be deleted.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
