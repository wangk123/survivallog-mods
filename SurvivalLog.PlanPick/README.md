# 自选规划 PlanPick（Survival Log MOD）

> 作者 Night · 抖音 166660371 · 免费分享 · 不改存档（零新增存档字段）/ 不改游戏文件
> 版本随套件统一管理；适配游戏版本以仓库兼容说明为准。

## 一、这是干什么的

每天的生存规划只随机发三张，想要的那张迟迟不来。本 mod 在生存规划三选一页的页脚加一个「自选规划」入口：点开是全部 465 项规划的目录，分类、筛选、排序、搜索随便挑，任何规划都能买——不在今日三张里的按默认两倍价（×200%，可调 100~300）购买，今日候选内的照常原价。

## 二、功能清单

- **全量 465 项目录**：四大类（物资贸易 / 家具扩建 / 属性成长 / 功能强化）+ 二级系列/区域筛选 + 状态筛选（可购/未解锁/已激活）+ 排序（默认/价格/解锁日）+ 关键词搜索（支持中文输入）；筛选与搜索的计数实时联动。
- **未解锁项置灰并显示原因**：生存至第 N 天 / 前置未购 / 角色不符 / 特殊条件未满足——点一下就知道还缺什么，能规划整条成长线。
- **自选购买默认消耗 ×200%**（可调 100~300 整数，100=原价）：实付=原价×百分比四舍五入取整，主角折扣照常参与；今日候选内购买永远原价。
- **全走官方代码**：购买、扣点、激活、存档全部走游戏自己的逻辑，零新增存档字段，删 dll 即完全还原。
- **专属规划照官方限制**：带「专」徽章的角色限定规划沿用官方白名单；与本套件「角色解锁」mod 同装时，自动跟随其互通设置。

## 三、安全与还原

- 不改存档（零新增存档字段）/ 不改游戏文件 / 删 dll 即还原。
- 官方三选一的随机、刷新、钉选、锁定计算全部走游戏原逻辑，本 mod 零干预；候选内的购买完全是原路径。
- 唯一改动点是价格：自选购买的瞬间在内存里临时调整该项规划的点数消耗，买完立即恢复原值（异常路径同样恢复）。
- 弹窗打开期间键盘（含输入法）交给网页，游戏快捷键（如 H）暂不触发，关闭即还原——这是输入搜索词所必需的。

## 四、安装与卸载

**前置**：Steam 正版《生存日志》（AppID 4164790）+ BepInEx 6（IL2CPP）注入链。
- **Full 完整包（推荐新玩家）**：解压到游戏根目录（Steam 库 `steamapps\common\Survival Log`），自带 BepInEx 6（IL2CPP）基座，装完即用；
- **PluginOnly 纯插件包**：已装 BepInEx 6 的玩家，把 `SurvivalLog.PlanPick.dll` 放进 `<游戏目录>\BepInEx\plugins\`。

**卸载** = 删掉该 dll（Full 包玩家要彻底清基座：再删 `BepInEx\` 目录、`winhttp.dll`、`dotnet\` 目录；存档不受影响），每日三选一恢复官方原样，已购买的规划保留（本来就是官方存档数据）。游戏更新后失效属正常，等套件适配新版即可；本 mod 不改任何游戏文件。

## 五、配置说明

配置文件：`BepInEx\config\com.local.survivallog.planpick.cfg`（改完几秒内自动生效，无需重启）。用记事本可改。

| 节.键 | 默认 | 说明（人话） |
|---|---|---|
| General.Enabled | true | 总开关。false=隐藏「自选规划」入口并停止注入；下次启动不加载 |
| Cost.CostMultiplierPercent | 200 | 自选购买（不在今日候选名单）的生存点消耗百分比。整数 100~300：100=原价，200=翻倍（默认），300=三倍；实收=原价×百分比/100 四舍五入取整。今日候选内购买始终原价 |
| List.ShowLocked | true | 列表展示未解锁规划（置灰 + 点击弹原因） |
| List.ShowUnpooled | true | 列表展示从不进入抽取池的规划（机器人图纸/拾荒无人机） |
| Debug.ListOnReady | true | 首次数据就绪时在日志列统计（分类计数/今日候选） |
| Debug.Verbose | false | 每轮推送输出明细 |

## 六、English

**Plan Pick** — Browse the full 465-item survival plan catalog and buy any plan at a configurable premium (default ×2); today's three picks always cost the original price.

**Features**
- Full catalog of 465 plans: 4 categories, sub-filters (series/zone), status filter, sorting by price/unlock day, and keyword search with Chinese IME support; filter counts update live.
- Locked plans are shown greyed out with the exact reason (survive to day N / prerequisite not bought / role mismatch / special condition).
- Off-list purchases default to ×200% cost (adjustable 100–300; 100 = original price), rounded to the nearest integer; leader discounts still apply. Today's candidates always cost the original price.
- Purchase, point deduction, activation and saving all run through the game's own code — no new save fields; delete the DLL and everything reverts.
- Role-exclusive plans keep the official restriction badge; automatically follows the suite's Role Unlock mod when installed together.

**Requirements / Install / Uninstall**
Survival Log (Steam AppID 4164790) + BepInEx 6 IL2CPP. Install the Full package (bundled BepInEx 6 base — extract into the game root, `steamapps\common\Survival Log`) or the PluginOnly package (drop `SurvivalLog.PlanPick.dll` into `BepInEx\plugins\` if BepInEx 6 is already installed). Uninstall = delete the DLL; the daily 3-pick page returns to vanilla and purchased plans are kept (official save data).

**Configuration**
Config file: `BepInEx\config\com.local.survivallog.planpick.cfg`. Key knob: `Cost.CostMultiplierPercent` (default 200, range 100–300) — the premium on off-list picks; changes apply within seconds.

**Credits**
By Night (Douyin 166660371). Free for personal use. Do NOT re-upload or redistribute anywhere without permission.
