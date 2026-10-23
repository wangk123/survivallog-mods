# PlanPick 装机验证清单（作者执行）

> 前置：`SurvivalLog.PlanPick.dll` 已拷入 `<游戏>\BepInEx\plugins\`（本清单第一步含拷贝命令）。
> 全程不需要改任何官方文件；任一步异常 → 看 `BepInEx\LogOutput.log` 里 `[PlanPick]` 行。
> 预计 10~15 分钟（一个存档日内可完成 ①~⑧）。

## 0. 部署（作者在场时）

确认游戏未在跑（`tasklist` 原始输出核对 SurvivalLog.exe）后：

```bash
cp SurvivalLog.PlanPick/bin/Release/SurvivalLog.PlanPick.dll "E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/plugins/"
```

发版前门禁（2026-10-24 实装时已过，供复跑口径）：

- 构建：`dotnet build SurvivalLog.PlanPick -c Release` → exit 0
- 回归夹具：`node SurvivalLog.PlanPick/tests/run_tests.js` → exit 0（58 断言全 PASS）
- 依赖核对：`node tools/gamekb/modcompat_check.js gamekb/2026-10-23_26157732` → exit 0（全绿）

## 验证清单（按序，①~⑧ 一个存档日内完成）

- [ ] **① 进档触发规划页**：启动游戏（Steam 方式）进档，过一天触发 RogueSelect（"生 存 规 划"页）。
  预期：页脚右侧"离开"旁出现 **自选规划** 按钮。
- [ ] **② 启动日志**：LogOutput.log 依次出现
  `[PlanPick] loaded：自选规划入口待注入（倍率 x2）` → `[PlanPick] 目录: …`（四分类计数+不入池计数）→
  `[PlanPick] 规划 N/465 项（今日候选 3，第 X 天，点数 Y，倍率 x2）`。
- [ ] **③ 弹层结构**：点开弹层——四个一级 tab（属性成长/功能强化/家具扩建/物资贸易）、
  二级筛选 chips（家具扩建=区域+专属家具位；其余=系列+独立）、状态筛选回写计数、排序、搜索；
  找一张带"专"徽章的锁定卡，点击 → 原因弹泡显示**角色真名**（"仅限「打工仔」…"），
  未到解锁日的卡显示"第 N 天解锁（当前第 M 天）"。
- [ ] **④ 今日候选原价**：在**官方三卡**里买一张（或在弹层里找 today=true 的卡——卡片有"今日"标记）。
  预期：扣**原价**；日志**无** `[PlanPick][Cost]` 行（今日候选零干预）。
- [ ] **⑤ 自选翻倍**：弹层里选一张**不在今日候选**的可购卡 → 确认弹窗显示 原价/自选价(×2)/余额 三行 →
  购买。预期：扣点=自选价；日志恰有两条 `[PlanPick][Cost] 自选 <id> 原价 A -> B` 与
  `已恢复 <id> 原价 A`；重开规划页该卡盖"已激活"印章、点数与官方页一致。
- [ ] **⑥ 钉释放**：官方三卡里**钉选**一张，再从自选弹层买同一张。
  预期：购买成功且钉自动释放（官方链第 5 步语义，无残留钉）。
- [ ] **⑦ 不入池可买**：弹层搜"图纸"（机器人图纸 31442~31450）或"拾荒无人机 Lv2"，
  确认可正常购买（unlockDay=null 项，受 ShowUnpooled=true 展示）。
- [ ] **⑧ ShowLocked 开关**：关 cfg（`BepInEx/config/com.local.survivallog.planpick.cfg` →
  `ShowLocked = false`）重启/重进规划页。预期：未解锁项（置灰卡）全部消失，仅 可购/已激活。
- [ ] **⑨ 卸载还原**：删掉 plugins 下 dll 重启游戏。预期：规划页回到官方原样（无自选按钮）；
  存档读档正常、已购规划仍在（全走官方存档）、无任何残留字段；表值无残留
  （finalizer 已在每次购买后恢复 LvUpCost，长线可再抽查一张昨日自选购的原价与 wiki/表一致）。
- [ ] **⑩ Verbose 推送节奏**：`Verbose = true` 后观察日志 `[PlanPick] 巡检推送 sig=…`。
  预期：payload 未变化时不重复推送（sig 相同即跳过）；买卡/过天后 sig 变化才推一次——
  弹层打开期间搜索框**不被 2s 一轮的重渲染打断**。
- [ ] **⑪ 双击自愈**：快速**双击**自选卡购买按钮（制造两次 CLICK_BUY 竞速）。
  预期：第二次购买要么正常原价（已激活拦截/点数不足），要么 ×2 一次——
  日志中 `-> B` 的 B 值**不出现 原价×2×2 的嵌套计价**（prefix 入口自愈加固的自证）。

## 判读备注

- `[PlanPick][Cost]` prefix/恢复**成对出现**是核心健康信号；只出现第一条=finalizer 未跑，立即回报。
- 任何 `注入异常` / `推送异常` Warning 行（各只打一次）→ 连同上下文回报。
- cfg 改动即时生效项：ShowLocked/ShowUnpooled/Verbose/倍率（下一轮 2s 巡检自动重推）。
