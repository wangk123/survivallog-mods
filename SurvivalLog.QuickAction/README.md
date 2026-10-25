# 动作提速 QuickAction（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

原版里吃饭、拆包裹、搜刮、看书这些日常动作动辄要等几秒到十几秒，玩久了很磨人。本 mod 按游戏官方的动作分类，把这些动作的耗时统一改写为 0.5 秒（可调）。装好即生效，五类动作各有一个开关，收益会随时间缩水的动作会被自动保护起来不动。

## 二、功能清单

- 官方五大类动作统一提速，默认全部 500 毫秒：物品使用（吃/喝/药品，约 2750 条）、家具功能（拆封包裹/种植/烹饪等，约 900 条）、房屋维护（布置/安装/移动/拆除陷阱等，约 78 条）、杂项交互（搜查/翻找/开关电器/拾取等，约 135 条）、娱乐锻炼（看书/听音乐/洗澡/按摩等，约 173 条）；每类毫秒数可单独调，0=该类恢复原值
- 睡觉与社交动作保持原速不动（睡觉兼任时间推进，社交收益按时间结算），不提供开关，防误设
- 收益保护三道守卫：收益随耗时增长的动作自动跳过（生吃冷冻食品、跳舞、游戏机、锻炼、发电等）；收益按时间结算但配置表看不见的动作点名拦截（做俯卧撑、手动发电）；修理/加固门窗设施类已验证收益是固定值，提速无损
- 没被分类覆盖到的动作可在 Overrides 里点名单独调速（如上厕所 9075），优先级最高
- 游戏更新新增的动作自动按官方分类归入对应开关，无需更新 mod
- 只减不增：原本就快于目标时间的动作不会被放慢

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原
- 改写只发生在内存里的动作配置表上，不落存档：删掉 dll 后所有动作耗时回到原版
- 想临时关掉：把对应分类设 0、清空 Overrides，或把 General.Enabled 设为 false，都会当场恢复官方原值
- 首次生效会在 BepInEx 目录生成两个说明文件：ActionList.txt（全部动作的 ID/名称/耗时清单，用 Overrides 时可查 ID）、QuickActionTrace.txt（本次改写明细），可以打开看、也可以删

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.QuickAction.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载），游戏回到原版动作耗时。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.quickaction.cfg`（改完即时生效，改动后几秒内自动重写，无需重启）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=还原官方原值并停写；下次启动不加载。 |
| 分类修改.物品使用Ms | 500 | 吃/喝/品尝/吞咽/药品等入口动作（约 2750 条可改）。挂周期结算效果的（生吃冷冻类）自动跳过。0=恢复原值。 |
| 分类修改.家具功能Ms | 500 | 家具按钮动作（约 900 条：拆封包裹/种植/烹饪/升级大门/改装/救援建造）。周期结算类自动跳过。0=恢复原值。 |
| 分类修改.房屋维护Ms | 500 | 房屋维护动作（约 78 条：布置/安装/移动/拆除陷阱、安装家具）。修理/加固主体为固定收益、正常提速；个别收益随耗时增长的自动跳过。0=恢复原值。 |
| 分类修改.杂项交互Ms | 500 | 杂项交互（约 135 条：搜查/翻找/查看/开关电器/拾取/家务）。内部行为（情绪播报/呕吐腹泻/系统动作）自动跳过。0=恢复原值。 |
| 分类修改.娱乐锻炼Ms | 500 | 娱乐与锻炼（约 173 条：看书/听音乐/按摩/运动/洗澡/空调吹风）。跳舞/游戏机/锻炼/发电等周期结算自动跳过。0=恢复原值。 |
| 迁移.DefaultsV21 | false | v2.1 默认值迁移标记（五个分类开关统一默认 500）。勿手改。 |
| 其他.Overrides | （空） | 上面没列到的动作单独覆盖：ID:毫秒，逗号分隔（优先级最高）。例：100009001:500（观察便签）。ID:0=恢复原值。完整名单看 BepInEx\ActionList.txt（含类型列；删掉该文件下次启动会重新生成）。 |

## 六、English

**Quick Action** — Cuts everyday action time to half a second by default (eating, opening packages, scavenging, reading, etc.), grouped by the game's own action categories.

**Features**
- Five official categories (item use / furniture functions / house maintenance / misc interactions / entertainment & exercise), all defaulting to 500 ms; each category is individually adjustable, 0 = restore vanilla
- Sleep and social actions are never touched
- Gain guards: actions whose rewards scale with duration (raw frozen food, dancing, exercising, power generation, etc.) are automatically kept at vanilla speed; a few with hidden time-based gains (push-ups, hand-crank generator) are blocked by name; door/window repairs have fixed gains and speed up losslessly
- An Overrides list lets you fine-tune any individual action
- New actions added by game updates are categorized automatically

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; the game returns to vanilla action durations (all changes are in-memory only).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.quickaction.cfg`. Per-category speed in milliseconds (0 = vanilla), a per-action Overrides list, and a master switch. Changes apply within seconds, no restart needed.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
