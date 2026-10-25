# 无尽守卫 EndlessGuard（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 存档只通过游戏自己的接口写入（不用任何私有格式）/ 不改游戏文件（详见第三节）
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

无尽模式玩到后期，交易点和野外探险图会按阶段一个个沦陷，全交易点沦陷后连无人机交易入口都会收起，长期档等于慢性死亡。本 mod 默认四件事一起做：新沦陷全部拦下（三种模式通用）、已沦陷的点读档复活、地图上的沦陷倒计时清掉，再补 11 条后期拾遗规划，让产出和生存点开销接得上。

## 二、功能清单

- **永不沦陷**（默认开）：拦截全部新沦陷，纯无尽 / 剧情无尽 / 休闲三模式通用。
- **旧档复活**（默认开）：读档后自动恢复已沦陷的交易点 / 探险图，无人机交易入口随之回来；已进入撤离剧情链的营地带剧情状态，保持不动（保剧情一致性）。
- **倒计时清场**（默认开）：地图上挂着的沦陷倒计时全部清掉，重开地图即消失。
- **11 条拾遗系列新规划**（默认开）：超市拾遗·六~十、医院踏勘·三~五、学校摸排·三~五，第 100 天起每 20 天解锁一条（D100~D300），全买共 494 生存点；每张卡解锁一批新物品，并把官方埋在池子里的高门槛好货降到可解锁。
- **越买越富**：探索物资数量 = 存档"探索物资"难度档位 + 每张已购新卡 +0.3 期望份数（步长可调，0=纯官方难度）；标准难度下超市五张买满约每件 2.5 份。
- **购买通道说明**：新规划不在每日三选一的随机池里，通过「自选规划」列表上架购买（与本套件 PlanPick 同装即出现在其目录中）；不装 PlanPick 时，前三项功能照常，新规划无处可买。

## 三、安全与还原

- 不改游戏文件 / 删 dll 即还原。
- 存档只通过游戏自己的接口写入：恢复已沦陷点 = 把点从游戏官方的沦陷名单里移除（这正是功能本身）；新规划的购买入档也走游戏官方存档。不使用任何私有格式——全新无尽档天然就是这套状态。
- 卸载后：已恢复的交易点 / 探险图、已购买的新规划都保留（它们已是游戏官方数据）；已购的 11 条新规划会留下无害的孤儿记录。新沦陷恢复官方判定（会继续按日程沦陷）。
- 不回滚已经触发过的一次性事件（沦陷广播、遗产事件是历史新闻，只拦以后）。
- 已进入撤离剧情链的营地不恢复（剧情一致性）。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **一键安装器（推荐）**：勾选本 mod，自动探测游戏目录，自带卸载器；
- **手动**：把 `SurvivalLog.EndlessGuard.dll` 放进 `<游戏目录>\BepInEx\plugins\`（需已装 BepInEx 基座）。

**卸载** = 删掉该 dll（或用安装器卸载），新沦陷恢复官方判定，已恢复的点与已购规划保留（官方存档数据）。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.endlessguard.cfg`（各开关按现值即时工作，无需重启；新规划条目关闭后自下次启动起完全消失）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| Fall.PreventNewFalls | true | 拦截全部新沦陷（三模式通吃） |
| Fall.RestoreFallenPoints | true | 读档后恢复已沦陷的交易点/探险图（营地除外，重复读档安全） |
| Fall.StripPendingFallTags | true | 剥掉沦陷倒计时 tag（内部读不到自动跳过，不影响其他功能） |
| Plans.ExtendScavengePlans | true | 注入 11 条拾遗扩展规划（超市·六~十/医院·三~五/学校·三~五，D100-D300） |
| Plans.RichnessStep | 0.3 | 数量层每张已购新卡的期望份数加成（在官方"探索物资"难度档位之上叠加；0=纯官方难度） |

## 六、English

**Endless Guard** — Endless mode never falls: block all new falls, revive fallen trade points/expedition maps, clear the fall countdowns, plus 11 late-game scavenge plans.

**Features**
- No more falls (default on): every new fall is blocked in all three endless modes (pure endless / story endless / casual).
- Old-save revival (default on): fallen trade points and expedition maps come back on save load, including the drone trade entrance; camps already in the evacuation story chain stay fallen.
- Countdown cleanup (default on): pending fall countdowns on the map are stripped.
- 11 new scavenge plans (default on): Supermarket VI–X, Hospital III–V, School III–V, one every 20 days from day 100 (D100–D300), 494 survival points in total; each card unlocks new items and lowers officially gated loot to reachable levels.
- Richer loot as you buy: expected copies per item = your save's official explore-loot difficulty + 0.3 per purchased new card (step adjustable; 0 = pure vanilla). At Standard difficulty with all five supermarket cards, roughly 2.5 copies per item.
- Purchase channel: the new plans are not in the daily random pool; they are listed and bought through the Plan Pick catalog (install the suite's Plan Pick to see them). Without Plan Pick, the first three features still work but the new plans cannot be bought.

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install via the one-click installer (pick this mod) or drop the DLL into BepInEx\plugins\. Uninstall = delete the DLL; new falls resume the vanilla schedule, while revived points and purchased plans are kept (official save data; the 11 purchased entries leave harmless orphan records).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.endlessguard.cfg`. Key knobs: the three fall-protection switches (all default on) and `Plans.RichnessStep` (default 0.3; 0 = pure vanilla loot amounts). Changes take effect without restarting.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
