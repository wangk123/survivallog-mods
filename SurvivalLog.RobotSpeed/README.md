# 机器人提速 RobotSpeed（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档 / 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

原版三种机器人（修理/整理/种植）干活比人慢半拍：动作自带减速系数、跑得慢、充电久、货舱小。本 mod 把机器人动作速度对齐手动操作，并默认给移速、充电、续航各翻倍、每趟耗电减半、货舱格数翻倍——每一项倍率独立可调，装好即用。

## 二、功能清单

- 动作速度对齐手动：清掉官方给机器人动作内置的"比人慢"系数，机器人每个动作的耗时与手动操作一致；工速模块的加速照常叠加，不会被吞
- 移动速度 ×2（默认，可调）；移速模块加成照常叠加
- 充电速率 ×2、电池续航趟数 ×2、每趟耗电 ÷2（默认，各自可调）；快充/电池模块加成照常叠加
- 机器人专属动作（去取/去放/放下/正在种植）时长 ÷2（默认，可调；单动作下限 0.3 秒，防动画爆闪）
- 货舱格数 ×2（默认 2 格→4 格）；装扩舱模块换用的大货舱档同步放大，官方"模块换大舱"的递进保持，装模块不会盖掉效果
- 不含插槽与模块相关的改动（插槽数量、插槽扩展模块不在本 mod 范围）；也不碰玩家动作（玩家动作提速见「动作提速」mod）

## 三、安全与还原

- 不改存档 / 不改游戏文件 / 删 dll 即还原
- 全部改写走内存配置表：机器人读档时从表取值，装好后启动游戏读档即生效
- 想还原：任一倍率设 1、开关设 false，或把 General.Enabled 设为 false，即恢复官方原值；已在场机器人的个别内部数值要重新读档一次才会完全回到官方值

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.RobotSpeed.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载），游戏回到原版机器人属性（下次启动游戏后生效）。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.robotspeed.cfg`（注意：本 cfg 的节名与键名是中文）。改完即时写入配置表；已在场机器人的属性快照需重新读档一次才刷新，货舱格数重开一次机器人面板即可看到。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关：false=还原官方原值并停写；下次启动不加载。 |
| 机器人提速.动作速度对齐手动 | true | 把官方给机器人动作内置的变慢系数置 0（机器人动作时长与手动操作一致）。工速模块的加速不受影响，继续在基础上叠加。false=恢复官方原值。 |
| 机器人提速.移速倍率 | 2 | 机器人移动速度倍率（作用于机型基础移速；移速模块加成照常叠加）。1=不变。 |
| 机器人提速.充电速率倍率 | 2 | 充电速率倍率（每趟电量的回充时间等比缩短；快充模块加成照常叠加）。1=不变。 |
| 机器人提速.续航倍率 | 2 | 电池续航趟数倍率（电池模块加成照常叠加）。1=不变。 |
| 机器人提速.耗电降低倍率 | 2 | 每趟耗电按此倍率降低：2=耗电减半，4=四分之一。下限保护：至少为 1。1=不变。想保留原版充电压力就设 1。 |
| 机器人提速.动作时长倍率 | 2 | 机器人专属动作（去取/去放/放下/正在种植）时长除以此倍率。下限保护：单动作不低于 0.3 真实秒，防动画/音效爆闪。只影响机器人专属动作，不碰玩家动作（修理/农业等共用动作请用「动作提速」）。1=不变。 |
| 机器人提速.货舱格数倍率 | 2 | 机器人货舱宽度倍率（格数同倍，默认 2 格→4 格）；基础货舱与扩舱模块的大货舱档同倍放大，装模块不会盖掉效果。1=不变。机器人面板打开一次即可看到新格数。 |

## 六、English

**Robot Speed** — The three robots (repair / tidy / planting) work at hand speed, with mobility, charging, battery and cargo doubled by default.

**Features**
- Robot work speed matches manual operation (the official slow-down factor is removed); work-speed modules still stack on top
- Move speed ×2, charge rate ×2, battery endurance ×2, power cost per trip ÷2 (defaults, each adjustable; module bonuses still stack)
- Robot-only action durations (fetch/put down/planting) ÷2, with a 0.3 s floor
- Cargo slots ×2 (2→4 by default); the bigger cargo tier from the expand module is scaled too, so the module progression stays intact
- No slot or module modifications (slot count / slot-expansion module are out of scope); player actions are untouched (see Quick Action)

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; the game returns to vanilla robot stats on the next launch (load a save to refresh robots already on the field).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.robotspeed.cfg` (sections and keys are in Chinese). Every multiplier defaults to 2 (1 = vanilla), plus the hand-speed toggle and a master switch; on-field robots refresh their stats on the next save load.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
