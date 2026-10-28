# 无尽守卫 EndlessGuard — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**（文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（发版号由作者定，本 mod 不自定版本）。
> 实证基线：gamekb `2026-10-23_26157732`（游戏 buildid 26157732 / RuntimeAssets 1.1.19611）；反汇编地址为该版 RVA。
> 定稿历程：2026-10-24 作者多轮拍板（数值三轮、内容层两轮、数量层四轮终定"采用官方探索物资难度方案"）。

## 1. 功能定位

无尽模式的"沦陷"系统对长期档是灭顶之灾：交易点、野外探险地图按阶段表逐个沦陷，全交易点沦陷后无人机交易入口直接收起，野外产出断绝。本 mod 四件事：

1. **永不沦陷**（PreventNewFalls）：拦掉唯一沦陷执行点，三模式（纯无尽/剧情无尽/休闲）通吃；
2. **旧档恢复**（RestoreFallenPoints）：已沦陷的交易点/探险图一次清账复活（营地除外）；
3. **倒计时清场**（StripPendingFallTags）：剥掉缓刑 tag，地图上的沦陷倒计时不再挂着；
4. **生存规划扩展**（ExtendScavengePlans）：注入 11 条拾遗系列新条目（超市拾遗·六~十 / 医院踏勘·三~五 / 学校摸排·三~五），D100-D300 每 20 天一条——**内容层**扩池解锁新物品（物资份数完全按官方"探索物资"难度，2026-10-25 作者定案撤数量层），给无尽后期持续供给 + 生存点回收（全买 494 点）。

配合逻辑：功能 1-3 保供给线（有图可去），功能 4 保产出线（去了有货、越买越富、点数有处花）。

## 2. 实现原理（反汇编实证）

### 2.1 沦陷机制全链（2026-10-24 定案）

```
读档 Initialize(0x2653EE0) ┐
每天 OnDayChanged(0x26586F0) ├→ RecomputeTier(0x265BB80) ─→ ApplyFallenMapPoints(0x2649140)
休闲模式 OnCasualDayChanged(0x26578C0) ┘                            │
                                                     ┌─────────────┴─────────────┐
                                              调用点1 表驱动路径          调用点2 探险defer路径
                                                     └────────→ FallMapPoint(0x264CD90)
```

- **FallMapPoint 是全游戏唯一沦陷执行点**：全段 E8/E9 扫描实证仅 `ApplyFallenMapPoints` 一个调用方（两个调用点都在其中）。防守失败/援助失败**不直接沦陷**——防守、营地援助、道具只提供"缓刑"（`TryConsumeReprieve` 0x26644D0 / `TryConsumeExploreDefer` 0x26642F0 在 ApplyFallenMapPoints 内部决定跳过还是执行），到期无缓刑才在日 tick 沦陷。
- **点名表驱动**：`Config_EndlessTier`（每阶段一行）三张列表 `FallMapPoints` / `StoryFallMapPoints` / `DroneFallMapPoints`（纯无尽/剧情无尽/无人机交易三套 List\<int\>）+ 探险地图的 DeferredFallTag（`TagManager` tag，由 `DeferredFallTag(pointId, tier)` 0x264C030 生成）。
- **FallMapPoint 内部落档动作**（反汇编调用序列实证）：
  `GameCounter.SetCounter("endless.fall_tier_<点ID>", tier)`（counter key 字面量 0x5BCC560）+ 取消该点援助请求（CancelAidRequestsOfFallenPoint 0x264AAE0）+ 营地特殊结算 + 公告文本生成（未解锁过的点静默沦陷）+ `GameSaveData.FallenMapPointIds` 加名单（List.Add 内联）。
- **封锁判定权威源 = `GameSaveData.FallenMapPointIds`**（`List<Int32>`，**字段偏移 0xE0**，getter 0x11B4A20 是裸 `mov rax,[rcx+0E0h]` 故 E8 扫描零命中）：`IsCampFallen`(0x2654390) 尾跳 `List.Contains` 实锤；UI 侧 `TradeMapStatusTools.Build`(0x27FE650) 同样读 0xE0 字段 + `GetFallDisplayDay`(0x26519F0，读 counter 仅展示"已沦陷第N天") + `HasTag`。**没有第四份暗账。**
- **全交易点沦陷封印**：`TrySealTradeAccess`(0x2665FC0) 遍历 Config_MapPoint 交易类点对名单 Contains，全灭则 `TagManager.AddTags("EndlessAllTradePointsFallen")`（字面量 0x5B80818），无人机交易入口据此收起。
- 游戏原版**无解沦陷路径**：TradePointStatus 的"解围 relieved"（`endless.relieved_sec_`）只是近期救援成功的 UI 展示态，不是恢复机制。

### 2.2 功能 1：永不沦陷

Harmony prefix 拦 `EndlessManager.FallMapPoint`（私有方法，签名 `(GameSaveData, TagManager, int, int, ref string, bool, bool)` 全非泛型，interop 兼容）恒 `return false`。上游反汇编实证 `test al,al; je` 跳过分支 = "未新沦陷"语义：无横幅、无遗产事件、无封锁写入、TrySealTradeAccess 永不触发。日 tick 每天只跑一次，无重试循环风险。

### 2.3 功能 2：旧档恢复（跑在补账后）

Harmony postfix 挂 `EndlessManager.Initialize(saveData, gameMode)`（读档补账 ApplyFallenMapPoints 之后执行，时序安全；功能 1 关闭时补账可能先沦几个点，恢复例程随后照样清掉——幂等）。非无尽模式空跑。四步全走原版 API：

1. 遍历 `FallenMapPointIds` 副本，`EndlessManager.IsCampMapPoint(id) == false` 的点从名单移除（**营地排除**，见 §6 红线）；
2. 每个恢复点 `GameCounter.SetCounter("endless.fall_tier_" + id, 0)`（GameCounter 无删除 API，清零即可——该 counter 只供展示，功能判定不走它）；
3. 若恢复了交易类点 → `TagManager.RemoveTags(["EndlessAllTradePointsFallen"])`（无人机入口封印解除）；
4. （并入功能 3）剥恢复点的 deferred fall tag。

全程 try/catch + trace 自证文件（恢复点清单/清了什么），半途异常只留不一致展示、下次读档幂等跑齐。

### 2.4 功能 3：倒计时清场

`TagManager.GetTagKeys()` 枚举 → 按 `TAG_DEFERRED_FALL_PREFIX` 前缀过滤 → `RemoveTags`。前缀**不硬编码**：运行时 interop 直读 `EndlessManager` 私有静态字段 `TAG_DEFERRED_FALL_PREFIX`（私有字段可读，RoleUnlock/PowerProbe 同款路数），读不到则降级跳过。UI 侧 `TradeMapStatusTools.Build` 每次开图重建（`BattleLogicWorld.OnRequestOpenMapFromUI` 0x305D6B0 / `AE_OpenMap.Run` 0x30A4590 实证），清理后重开地图即生效。

### 2.5 功能 4：生存规划扩展（洞察/发现等级系统实证 + 三层方案定稿）

#### 2.5.1 官方机制（2026-10-24 二次考古定案，全部反汇编实证）

- **洞察卡体系**：拾遗/踏勘/摸排系列 = `ExploreDiscoveryTools` 的"洞察卡"。`GetDiscoveryLv(图)`（0x2825E80）= 遍历该图卡注册表 `_cardsByMap[图]` 逐张 `HasActivated` 计数 → **发现等级 = 已购卡数**。
- **物品门槛**：物品池（Config_RandomGroup）每件物品带 `DiscoverLvList` 门槛，抽取时仅 门槛≤等级 的物品参与（`GetDiscoverThreshold` 0x2825E00；地面 `GroundLootManager.SpawnOneAnchor` 0x26EA440 / 柜子 `FurnitureLootTools.FillRandomLoot` 0x283D600 双通道都吃 discoverLv）。**卡的效果 = 解锁池中被封印物品（品质型），不是数量加成。**
- **注册表**：`EnsureRegistry`(0x2825710) 从 GameSetting 键 `ExploreInsight_Maps` / `ExploreInsight_Cards_<图>` / `ExploreInsight_LvGate_<图>` / `ExploreInsight_Ramp_<图>` 加载，默认值 ccctor 硬编码，`_registryLoaded` 一次性加载。**新 talent ID 不进注册表就没有任何效果。**
- **追加波（买卡立刻见效的官方机制）**：`FurnitureLootManager.RefreshForCurrentMap`(0x26D2DC0) 进图时对比 counter 快照 `furnitureloot.discoverlv.<图>` 与当前等级，落后则**逐级调 `AppendDiscoveryWave`(0x26D1BB0)**：全图每个可搜柜子 FillRandomLoot(count=该柜 LootRandomCount, **guaranteeLv=新等级**)——保底必出新解锁档物品。即"买卡 → 下次进图 → 全图柜子集体上一波含新货"。
- **探索物资难度（数量层官方旋钮，v5 采用）**：建档六维难度之一 `DifficultyParamType.ExploreLoot=6`，四档（Config_DifficultyParam 实数）：丰富=1.3（每件 30% 概率翻倍）/ 标准=1.0 / 稀少=0.75（25% 不出现）/ 极少=0.5（50% 不出现）。生效点 `DifficultyTools.RollExploreLootCopies`(0x31BC080) **生成时逐件摇份数，地面与柜子双通道都已接**。预设档：满仓无忧=全丰富、弹尽粮绝=稀少、尸海围城=标准。
- **刷新背景参数**（三图，Config_MapPoint 实数）：残破超市(1004) 19 锚点/轮激活 6-12/地面+家具间隔均 5 天；废弃医院(1005) 45 锚点/14-28/地面 5 天+**家具 0**；废弃学校(1002) 57 锚点/18-36/地面 5 天+**家具 0**。柜子每次填几种 = `Config_Furniture.LootRandomCount`（偏移 0x13C；池 0x128、角色变体池 P1-P4=0x12C-0x138）。地面每锚点每轮仅 1 种（`GetUniqueRandomItems(池,1)` 实锤）。

#### 2.5.2 数值总表（11 条目，2026-10-24 作者三轮拍板定稿）

全局每 20 天一条，D100 → D300；消耗超市 +8/档、医院学校 +12/档：

| 条目 | Talent ID | 条件集 ID | 解锁日（SurvivalToDay） | 前置（TalentNeed） | 消耗（LvUpCost） |
|---|---|---|---|---|---|
| 超市拾遗·六 | 31606 | 48201 | 100 | 31605（拾遗·五） | 30 |
| 医院踏勘·三 | 31623 | 48202 | 120 | 31622（踏勘·二） | 32 |
| 学校摸排·三 | 31633 | 48203 | 140 | 31632（摸排·二） | 32 |
| 超市拾遗·七 | 31607 | 48204 | 160 | 31606 | 38 |
| 医院踏勘·四 | 31624 | 48205 | 180 | 31623 | 44 |
| 学校摸排·四 | 31634 | 48206 | 200 | 31633 | 44 |
| 超市拾遗·八 | 31608 | 48207 | 220 | 31607 | 46 |
| 医院踏勘·五 | 31625 | 48208 | 240 | 31624 | 56 |
| 学校摸排·五 | 31635 | 48209 | 260 | 31634 | 56 |
| 超市拾遗·九 | 31609 | 48210 | 280 | 31608 | 54 |
| 超市拾遗·十 | 31610 | 48211 | 300 | 31609 | 62 |

- ID 分配实证：Talent 新 ID 全空闲；条件集镜像段 31633-31635 被 Config_ConditionSet 火锅条件占用，故走独立段 **48201-48211**（全表最大 48151，天然无冲突）。
- 官方曲线对照：超市 31601-05 消耗 10/12/14/16/18（+2/档）解锁=Supermarket_Open+PlanLvGate 链；医院 31621/22、学校 31631/32 消耗 12/16（+4/档）。新条目定位 D100+ 后期档（官方池 LvUpCost 上限 480）。全买合计 **494 生存点**。

#### 2.5.3 内容层：篮子 + 降档（每张卡 = 新物品一批 + 官方封印货压轴）

原则：**降档 = 只把官方埋在池中但门槛高于现有卡数的内容降到可解锁级**（保持相对顺序，不发明门槛语义）；**篮子 = 从全 3875 件物品表精选、与该图现有池去重后的 mod 自配内容**（同名物品以显式 itemId 定位，离线校验脚本程序化核对）。2026-10-24 实现期数据裁决：超市·六篮子 纯牛奶/车达芝士片（已在超市池 Lv0）替换为 面包/意面；超市·七 咖啡（同名双 ID）定为 现磨咖啡(itemId=7, ×1)。

| 卡 | 等级 | 篮子（扩池新增） | 压轴（降档/原生） |
|---|---|---|---|
| 超市·六 | Lv6 | 精制碘盐、白砂糖、黑胡椒粒、面包、意面、饼干、糖 | 万能加固件×2·纸箱（原Lv12降档） |
| 超市·七 | Lv7 | 德式大肉肠、速食味噌汤、现磨咖啡×1、草莓、西瓜 | 酿造酱油×2、陈醋×2（原生） |
| 超市·八 | Lv8 | 金枪鱼鱼段、小猫鱼、水果运输箱 | 开锁器（原Lv13降档） |
| 超市·九 | Lv9 | 半扇猪肋排、完整牛里脊、零食大礼包 | 乳清蛋白粉×2（原生）、撬棍·纸箱（原Lv14降档） |
| 超市·十 | Lv10 | 整只全羊、巨型火鸡、完整猪后腿（收官盛宴） | 万能加固件·货车（原生）、撬棍·货车（原Lv15降档） |
| 医院·三 | Lv3 | 维生素、康复新液、家庭急救手册 | 幸存者的应急药品（原Lv6降档） |
| 医院·四 | Lv4 | 治疗药、强效消毒喷雾、叮当罐子、荧光棒 | 开锁器（原生Lv4） |
| 医院·五 | Lv5 | 吗啡注射液、整箱功能饮料、高浓度消毒喷雾、照明弹 | 兴奋剂（原Lv7降档） |
| 学校·三 | Lv3 | 草莓、西瓜、卷烟 | 巧克力棒×2、无盐黄油×2（原生） |
| 学校·四 | Lv4 | 白玉菇种子、甜菜种子、韭菜种子 | 金枪鱼罐头（原生Lv4）、松茸种子（原Lv6降档） |
| 学校·五 | Lv5 | 松茸、灵芝（本体） | 乳清蛋白粉×2、冻奥尔良烤翅、撬棍·实验室（原生）+ 灵芝种子、冷冻整只火鸡（原Lv8降档） |

- 每级都有实解锁（无空档）；份数：日常类 ×2、大件肉 ×1，落表逐件定。
- 仍封印留白（明示）：学校尖叫鸡（原Lv7）、学校医务室应急药品（原Lv9，医院侧同款 Lv3 已可出）。

#### 2.5.4 数量层：已撤除（2026-10-25 作者定案）

> 作者定案："RichnessStep 按官方的默认难度来就行……这俩配置不要，放出来别人也看不懂。"

- **物资份数完全按存档"探索物资"难度档**（丰富 1.3/标准 1.0/稀少 0.75/极少 0.5），mod 不做任何叠加——原 `RollExploreLootCopies` postfix 与 `RichnessStep` 配置已删除，官方函数零接触。
- 拾遗卡只保留**品质层**效果：解锁池中被封印物品（篮子+降档，§2.5.3）。
- **历史否决项**（设计过程否决，勿复活）：私有"锚点+2/级、每点抽取+1/2级、刷新周期 5→2 天、每柜 LootRandomCount 加成"曲线；以及 v5 的"档位值+0.3×卡数"叠加公式（2026-10-25 一并撤除）。

#### 2.5.5 注入与生效实现

1. **表注入**（RoleUnlock 同款内存表，零存档写入）：`cm._Config_Talent_Dict` +11 行（字段照抄兄弟行：Type=2/Lv=1/Available=1/DemoTwoMode=0/EndlessMode=0/IsReget=false/Icon+WebIcon 同系列/Name_Local+Dec_Local 直写中文）+ `cm._Config_ConditionSet_Dict` +11 行（Source=`PlanLvGate_<TalentID>`、TalentNeed=[前置]、SurvivalToDay=解锁日）。
2. **注册表追加（效果生效的关键）**：mod 启动时在 `EnsureRegistry` 首次调用前把新 ID 追加进 GameSetting `ExploreInsight_Cards_<图>`（1004→31606-31610、1005→31623-31625、1002→31633-31635）；若时机不可控 → interop 置 `_registryLoaded=false` + 清 `_cardsByMap`/`_mapByCard` 强制重载；终极兜底 = postfix `GetDiscoveryLv` 返回值 += 新卡已购数。
3. **内容落表**：篮子 = 往对应池 `IdList/CountList/DiscoverLvList` 追加条目（挂各自等级门槛）；降档 = 改既有槽位门槛值。
4. **购买走官方链**：PlanPick 已证 `OnBuySurvivalPlan`(0x2833A30) 不查候选名单，扣点/激活/存档全原生；买卡后官方追加波（§2.5.1）下次进图自动上货。
5. **不进官方随机池**（RandomGroup GroupType=2 一律不加）：官方卡面走 ConstantText 本地化键查表会显示键名；新条目**只经 PlanPick 自选界面上架**。

### 2.6 存档安全性论证（功能 2 的红线级论证，2026-10-24 作者质询定案）

- **格式层零风险**：功能 2/3/4 从头到尾不碰存档文件，只改内存对象（名单/counter/tags/配置表/难度倍率），落盘由游戏自己的序列化在正常时机完成。写入 API 全是原版自用（`TagManager.RemoveTags` 0x27FD5B0 原版 76 处调用；`GameCounter.SetCounter` 即沦陷写档同款；名单是 GameSaveData 原生字段）。
- **清完状态 = 原版天然状态**：全新无尽档就是"空名单+无计数+无沦陷 tag"。
- **营地红线（2026-10-25 细化）**：仅已进入撤离剧情链的营地（campState=Evacuated/Lost）不恢复；普通日程沦陷的营地照常恢复（全量诊断实锤 1100/1101 两营地 campState=0，撤离链未跑、无剧情冲突——原一刀切误伤即作者验收"两个沦陷点不恢复"的根因）。
- 兜底：存档目录 .snap0-5 + .bak 轮转；全程 trace 自证；装机验证 = 复制存档目录当测试档先跑。

## 3. 代码结构（规划，实现时落地）

| 文件 | 职责 |
|---|---|
| `EndlessGuardPlugin.cs` | 入口：cfg 开关、Harmony 挂钩总装、防盗署名气泡接线 |
| `FallBlocker.cs`（暂名） | 功能 1：FallMapPoint prefix + 拦截计数自证 |
| `FallRestorer.cs`（暂名） | 功能 2+3：读档后四步清账 + tag 剥除 + trace |
| `ScavengePlans.cs`（暂名） | 功能 4：数值总表/篮子/降档**代码单源**；Talent+ConditionSet 表注入；洞察注册表追加；GameSetting 写入与重载兜底 |
| `ModShared\WelcomeTip.cs` | 共享源码（csproj 链入，套件统一防盗署名） |

## 4. 配置项（com.local.survivallog.endlessguard.cfg）

| 键 | 默认 | 说明 |
|---|---|---|
| `PreventNewFalls` | true | 拦截全部新沦陷（三模式通吃） |
| `RestoreFallenPoints` | true | 读档后恢复已沦陷交易点/探险图（营地按剧情链状态区分） |
| `ExtendScavengePlans` | true | 注入 11 条拾遗扩展条目（含注册表追加+内容层落表） |

配置只剩三键（2026-10-25 作者定案：StripPendingFallTags/RichnessStep 撤除——剥倒计时 tag 无条件执行、物资份数完全按官方"探索物资"难度档，零接触官方函数）；旧 cfg 文件里的孤儿键 BepInEx 自动忽略，无需手清。

注意 cfg 首跑固化坑（CabinetEverywhere 教训）：新键上线时旧 cfg 缺键给默认值并日志提示一次。

## 5. 与 PlanPick（生存规划自选）的兼容（作者指定必保，2026-10-24 源码级确认）

**零耦合原则**：两 mod 互不引用、无加载顺序依赖、任一卸载自动还原。兼容面四要素：

1. **目录可见性**：PlanPick `PlanCatalog.Build` 读 `cm._Config_Talent_Dict` 运行时活表，过滤 `Type==2 && Available==1` → 注入行天然入目录。PlanCatalog 惰性一次构建（首次规划页打开），本 mod Awake/Start 即注入 → 恒早于。
2. **解锁日展示**：新条目不进池 → `unlockDay=null` 走 `ShowUnpooled`（**默认 true**，展示）。真实日期门由 PlanCatalog 原生逻辑从 ConditionSet 读出（`SurvivalToDay > 0` → 卡面文案"生存至第100天"，PlanCatalog.cs:112）。
3. **锁定判定**：PlanState 走官方 `ConditionSetManager.CheckConditionSet`（PlanState.cs:135）→ 新条件集行自动生效。
4. **系列归属**：条件集 Source=`PlanLvGate_*` + 单前置 → PlanPick 链式系列推导自动归入"超市拾遗/医院踏勘/学校摸排"同组二级筛选（LCP 匹配）。

**已知交互（非缺陷，明示）**：PlanPick 自选翻倍（CostMultiplier 默认 200%）对非今日候选恒生效 → 新条目实付 = 表价 ×2（30→60 … 62→124，全买约 988 点）。符合后期点数回收定位；作者可调 PlanPick 的 CostMultiplier 全局调节。

## 6. 红线与边界

- **营地按剧情链状态区分**：campState=Evacuated(5)/Lost(6) 不恢复（撤离剧情链一致性）；campState=None 的日程沦陷营地照常恢复（2026-10-25 诊断定案，§2.6）；
- **不回滚已触发的一次性事件**（沦陷广播/遗产事件是历史新闻）；
- **不进官方随机池**（本地化键缺失 + 池污染，§2.5.5）；
- **不设数量层**（2026-10-25 撤除：物资份数完全按官方探索物资难度，零接触官方函数）；
- **孤儿 ID 风险**：买了新条目后卸载本 mod，SaveCache 留下无表行 ID——官方 `OnInitBuffs` 对缺行容错**必须装机验证**（实测前不得发布）；
- B 方案（运行时清 Config_EndlessTier 三张点名表）v1 不做，实测碍眼再议；
- 版本号作者定；本 mod 不进 publish.ps1 白名单直至作者发话。

## 7. 验证方案（硬规则 10：先实机再报"修好"）

1. 复制有沦陷点的无尽存档当测试档；
2. 装机启动 → 查 trace 自证（拦截计数/恢复点清单/剥 tag 数/注入条目数/注册表重载结果）；
3. 重开地图：沦陷点恢复可交易/可探险、无人机入口在、倒计时消失；连玩数日无新沦陷（跨阶段边界重点看）；
4. PlanPick 自选页：11 张新卡可见、D100 前锁"生存至第100天"、前置链与二级归组正确、购买走官方链扣点成功；
5. **注册表生效**：买 1 张新卡 → 探针读 `GetDiscoveryLv(图)` 返回值 +1；
6. **追加波**：买卡 → 进图 → 全图柜子出现该级新解锁物品（篮子/压轴货可见）；
8. 篮子物品与现有池程序化去重校验（零重复进池）；
9. 卸载测试：删 DLL 重进 → 旧档无异常（孤儿 ID 容错验证）。

**装机待定项（探针 5 分钟出数，不猜）**：①医院/学校 `FurnitureLootRefreshInterval=0` 语义（不回填 or 跟随地面周期——若是不回填属官方侧疑点，单独报作者定夺是否顺手修）；②每图柜子实体数（场景数据在加密 bundle，静态拿不到）。

## 8. 实证档案（改挂钩点/游戏更新时必查）

- **挂钩点**（实现时同步 `tools/gamekb/moddeps.json`，硬规则 11）：
  - Harmony prefix：`EndlessManager::FallMapPoint`（私有，0x264CD90）
  - Harmony postfix：`EndlessManager::Initialize(GameSaveData, GameMode)`（0x2653EE0）
  - （已撤）`DifficultyTools::RollExploreLootCopies` postfix——2026-10-25 数量层移除，官方函数零接触
  - 兜底（如需）：`ExploreDiscoveryTools::GetDiscoveryLv`（0x2825E80）/ `_registryLoaded` 静态字段重载
- **沦陷链地址**：ApplyFallenMapPoints 0x2649140 / RecomputeTier 0x265BB80 / OnDayChanged 0x26586F0 / OnCasualDayChanged 0x26578C0 / TrySealTradeAccess 0x2665FC0 / IsCampFallen 0x2654390
- **洞察/刷货链地址**：ExploreDiscoveryTools.GetDiscoveryLv 0x2825E80 / EnsureRegistry 0x2825710 / GetInsightMapForCard 0x2826440；FurnitureLootManager.RefreshForCurrentMap 0x26D2DC0 / AppendDiscoveryWave 0x26D1BB0；GroundLootManager.RollAndSpawn 0x26E9D20 / SpawnForCurrentMap 0x26E9F50 / SpawnOneAnchor 0x26EA440；FurnitureLootTools.FillRandomLoot 0x283D600；DifficultyTools.RollExploreLootCopies 0x31BC080
- **字段偏移**：GameSaveData.FallenMapPointIds @0xE0（List\<Int32\>，访问器内联属正常）；Config_Furniture：LootRandomGroupId@0x128 / P1-P4 角色变体池 0x12C-0x138 / LootRandomCount@0x13C
- **counter/GameSetting 键**：counter `endless.fall_tier_<点ID>`、`furnitureloot.discoverlv.<图>`、`furnitureloot.basefill.<图>`；tag `EndlessAllTradePointsFallen`、deferred fall 前缀=运行时读 `EndlessManager.TAG_DEFERRED_FALL_PREFIX`；GameSetting `ExploreInsight_Maps/Cards_<图>/LvGate_<图>/Ramp_<图>`
- **表锚点**（gamekb 2026-10-23）：Config_DifficultyParam ParamType=6 四档 1.3/1.0/0.75/0.5（预设 Levels 六维数组）；Config_Talent 31601-31605/31621-22/31631-32 既有系列；Config_ConditionSet PlanObtainGate_*/PlanLvGate_*；Config_MapPoint 超市 1004/医院 1005/学校 1002（刷新参数见 §2.5.1）；Config_RandomGroup 三图池与 DiscoverLvList 门槛分布
- 工具沉淀：`tools/_fall_probe.js`（方法地址反查，改读 gamekb）、`tools/_owner.js`（地址→所属方法，已改 gamekb 永久副本）、`tools/_lit.js`/`_lit2.js`（字面量槽查值，坑：JSON 字段小写 value/address）

## 9. 迭代维护清单（改本 mod 时同步更新）

- 沦陷/洞察机制相关改动 → §2 必须附实证依据（反汇编/探针），不许凭猜测改；
- 数值调整 → §2.5.2/2.5.3 总表是**代码单源**，三处同步：本文档、ScavengePlans.cs 常量、（如发版）CHANGELOG；
- PlanPick 兼容四要素任一断（目录时机/ShowUnpooled 语义变更）→ 重新源码级确认；
- 每次发版在根 CHANGELOG.md 补一节；改挂钩点必须同步 `tools/gamekb/moddeps.json`。
- 2026-10-24 实现落地：四功能块按本设计实施，装机验证清单见 plans/2026-10-24-endless-guard.md Task 8；moddeps 已同步。
- 2026-10-24 终审建议修4条（final-review ②）：④剥缓刑tag 移出 RestoreFallenPoints 门控（StripPendingFallTags 独立管，③剥封印留 Restore 门内=沦陷语义不变）；IsCampMapPoint 异常 fail-closed 改按营地留沦陷；ApplyContent 降档汇总 trace 改实写计数（demoted）；删 GuardTicker.PlansInjected 死符号。挂钩点未动（moddeps 无需更新）。
- 2026-10-25 T列表显示修复：作者截图实锤官方"已激活生存规划"列表把 Talent 行 `Name`/`Dec` 字段当本地化键走 `GetLocalText→GetLocalTxt`（0x2F67320），字典 miss 分支 `mov rax,rbx` **原样透传输入串**（对照 `ToConstantTextOrExposeKey` 0x2971BF0 miss 返 `string.Empty`，T列表非此路）。修法=Inject 改 upsert，`Name=p.Name`、`Dec=Dec_Local 同文案` 直接写字面中文，透传即正确显示，无需注入本地化表；PlanPick 读 `Name_Local` 不受影响。
- 2026-10-25 购买丢卡真根因（·六/·七/学校·三 三连定案，推翻此前"撞表重建窗口"归因）：官方购买链 `OnBuySurvivalPlan`(0x2833A30) 先扣点(0x2833BA6)后唯一落档点=`SurvivalPlanningComponent.AddBuff`(0x30187A0)；AddBuff 主门(0x3018A75 gate)=`BuffID>0 ∨ id==32111 ∨ GetInsightMapForCard>0 ∨ 在组件静态数组`，门不过**静默 return（点已扣卡不加零报错）**。`GetInsightMapForCard`(0x2826440) 只查 `_mapByCard` 反向表（静态偏移 0x18）——本 mod 此前只补 `_cardsByMap` 正向表，反向缺项→三次丢卡。修法=AppendRegistry 每轮同步反射补 `_mapByCard[卡]=图`（幂等，防 EnsureRegistry 重建换对象每轮重取实例）；两张已扣点卡经 `GrantPlanIds`（多卡补授 cfg，逗号分隔）免费补回。过门后官方链自动走全：+0x40 激活表 Add→RecordInsightBuy(0x2826A90)→NotifySurvivalPlanningUpdate(0x301D510, T列表即时刷新)；静态数组(bpl=1)尾段仅多背包扩格 ApplyBagGridExpansion，洞察卡无需。
- 2026-10-25 晚 官方管道锚点实弹翻车+回滚（教训入库）：465 项事故根因=启动竞态（注入完成 22:26:31，PlanPick 目录抢在 :29~:31 帧隙构建且终身缓存不重建）。曾按作者方向"复用官方方案"改挂官方装表 async 状态机 `ConfigManager.<Initialize>d__256`（MoveNext 0x30A4E70，E8 证调用方=EnterMain+LogInitFailure）步进随行注入、废 GuardTicker——**实机部署即翻车**：~10Hz 无限重注（2 分钟 1.3 万行），tier清空/篮子同被反复重放，表每步被重建回官方态，游戏卡在加载（疑似装表中途注入破坏加载→Initialize 重试循环，LogInitFailure 正是重试调用方），杀进程回滚。**结论：装表进行中禁碰表；巡检的静默失败期（表 null 即 return）恰好天然避开装表窗口，这是它整个下午稳定的原因。**回滚后 GuardTicker 恢复（Tick→OnConfigStep 更名保留，registry 表未齐 NRE 改同文 once-latch 保留）。465 事故的真正修复落在 PlanPick 侧目录行数失效复核（其 DESIGN §11）——注入晚到 2s 内目录自动重建，竞态后果消除。免巡检方向若再试：锚点改 `GameEntrance.EnterMain` 续体（Initialize 完成、表稳定后才推进），且必须先离线论证其状态机不被逐帧泵。
- 版本与发版（2026-10-28 起）：版本号单源 = 根 `version.json` 的 `mods.EndlessGuard` 字段（作者定义）；单 mod 发版走 `tools\release_mod.ps1 -Mod EndlessGuard`；本 mod 的 GitHub 镜像仓由 `tools\sync_repos.ps1` 从 HEAD 快照同步（详见 docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md）。

## 10. 防盗署名气泡（套件统一）

- `Load()` 末尾调用 `SurvivalLog.ModShared.WelcomeTip.Install(Log)`（共享源 `ModShared/WelcomeTip.cs`，csproj 已链入）。
- 行为与去重规则见 FridgeChill DESIGN §10（GameObject `SurvivalLog.WelcomeTip` 去重，套件 mod 装多个只弹一条）。文案单源 `WelcomeTip.Text`，改后全套件重编。
- 2026-10-25 深夜二版 免巡检方案落地（作者追责后按正确用法重做）：锚点改 `GameEntrance.<EnterMain>d__0.MoveNext`（0x2954C90）=官方装表完成事件。离线三重论证：①结构——d__0 仅一个 awaiter（唯一 await=Initialize，+0x32a），AsyncVoidMethodBuilder，**AsyncUniTask 泵包装器 0 个**（坏锚点 d__256 有 15 个，即其 MoveNext 可被持续泵的机器）；②指令（Iced）——call Initialize→GetAwaiter→挂起分支（AwaitUnsafeOnCompleted 注册续体后跳尾 return），续体=GameSaveManager.Initialize/FmodManager.OnLoadBank 等装表后启动序列；③实机——当晚 23:47 巡检版证明装表完成后表稳定（注入落表整局零重放）。安全边界：装表中 EnterMain 挂起、postfix 零调用（首版翻车窗口从构造上排除）；失败模式有界（挂载失败=缺卡不崩不循环）。GuardTicker 再次移除，注入/注册表/篮子/降档全部走 ConfigLoadPatch→OnConfigStep（幂等）。moddeps.apis 记入锚点+禁改 d__256 警示。
- 2026-10-26 凌晨 EnterMain 锚点二连翻车，免巡检二版回滚+铁律定案：离线三重论证（结构/指令/实机表稳定）后上机的 `GameEntrance.<EnterMain>d__0.MoveNext` 锚点实弹=**装表后启动序列卡死黑屏**（进程活、webview 活、窗口黑，内存卡 1.09GB&lt;健康 1.4GB+，注入本身 11/11 正常完成）。连同 d__256 事故定案真根因：**HarmonyX detour 挂编译器生成的 async 状态机 MoveNext 本身不可靠**——此类方法靠 XMM 非易失寄存器+栈保活状态机数据，蹦床不完整保存即静默损坏（两次均"锚点选对、钩法致死"）。铁律：async MoveNext 一律禁挂；免巡检迁移动候补锚=装表后续体里的**常规方法**（如 GameSaveManager.Initialize，与套件在役补丁同类），须离线论证全调用方+呈作者批后才上机。GuardTicker 回归（已第三次回到巡检版），PlanPick 行数失效复核继续在役（465 事故的用户可见修复，装机实证 476/476）。验证纪律升级：**进程活着≠启动成功**，必须核到"菜单达成"签名（内存≥1.3GB+PlanPick State_Web 行+表 mod 尾声行）。
