# 角色专属解锁 RoleUnlock（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

原版有不少内容锁角色：爬山虎种子只有女大学生能发现、花瓶配方只有大学生剧情送、部分生存规划只刷给对应角色，换个角色就要错过一截内容。本 mod 把这些角色限制放开，让任何角色都能玩到全套；默认全部开启，每一项都可以单独关掉还原。

## 二、功能清单

- 藤蔓全角色：任何角色收获作物时都可能发现爬山虎种子，能种藤蔓、安装到家具、获得藤蔓护盾（发现种子的防御等级门槛保留官方规则）
- 花种子全角色：任何角色收获作物时都可能发现花朵种子、能种花插瓶（原本也只有大学生会）
- 防御配方互通：燃烧瓶、电网这类防线道具配方全角色可解锁，各防御等级门槛照旧不是白送（当前游戏版本官方已自行统一，本开关留作保险，未来版本若分列则继续兜底）
- 生存规划互通：46 项功能类专属规划（种花、做菜、存粮、搬东西等）全角色可刷可买；已购规划不会清退；各角色的房屋扩建/家具位类规划绑定各自地图，不通融
- 花瓶配方等级化解锁：制作等级到 1 级全角色都能在工作台学会做花瓶（原本只有大学生的剧情会送）
- 全部功能都有开关，关=还原官方行为；不改任何概率与数值平衡

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原
- 全部改写在内存配置表上完成，改动后自动重写/还原，无需重启
- 已购/已学的规划与配方属于存档侧数据：关闭开关或卸载后这些保留不退，只影响之后的抽取与解锁

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.RoleUnlock.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载），游戏回到原版角色限制（已学到的规划/配方保留在存档里）。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.roleunlock.cfg`（改完自动重写或还原，无需重启）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=全部改回游戏原样；下次启动完全不加载本插件。 |
| Vine.UnlockVine | true | 藤蔓全角色：任何角色收庄稼时都可能捡到爬山虎种子、能种藤蔓挡尸潮（原本只有大学生会）。false=改回游戏原样。 |
| Vine.UnlockFlowerSeeds | true | 花种子全角色：任何角色收庄稼时都可能捡到花朵种子、能种花插瓶（原本只有大学生会）。false=改回游戏原样。 |
| Defense.ShareDefenseRecipes | true | 防御配方互通：让所有角色都能做燃烧瓶、电网这类防线道具（当前游戏版本已自带此效果，此项留作保险，不用管）。false=改回游戏原样。 |
| Plan.ClearPool | true | 生存规划全角色：每日规划商店里，别的角色的专属规划（种花、做菜、存粮、搬东西这些共 46 项）也会刷出来，谁都能买（已买的不退）。注意：各角色的房屋扩建类规划不会互通。false=改回游戏原样（只能刷到自己的）。 |
| Plan.VaseRecipe | true | 花瓶全角色：制作等级到 1 级，谁都能在工作台学会做花瓶（原本只有大学生的剧情会送）。false=改回游戏原样。 |

## 六、English

**Role Unlock** — Removes role locks: vine seeds, flower seeds, cross-role life plans and the vase recipe become available to every character.

**Features**
- Any character can discover vine (ivy) seeds when harvesting, plant vines and get the vine shield; the official defense-level gate for seed discovery is kept
- Any character can discover flower seeds and grow flowers for vases
- Defense recipes (Molotovs, electric nets, etc.) become unlockable for all characters at their normal defense levels (the current game version already unifies this officially; kept as a safety net)
- 46 function-type exclusive life plans can appear in the daily plan shop and be bought by any character; purchased plans are never removed; room-expansion / furniture-slot plans stay role-bound
- The vase recipe becomes learnable at crafting level 1 (originally only given by one character's story)
- Every feature has its own switch; off = vanilla behavior

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; the game returns to vanilla role restrictions (plans and recipes you already own stay in your save).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.roleunlock.cfg`. Five independent switches (vine, flower seeds, defense recipes, plan sharing, vase recipe) plus a master switch; changes rewrite or restore automatically without a restart.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
