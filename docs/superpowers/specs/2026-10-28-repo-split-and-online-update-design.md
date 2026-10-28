# 单 mod 拆仓 + GitHub 组织 + 在线更新通道 — 设计

> 2026-10-28 头脑风暴定稿。目标读者：作者 + 后续实施计划（writing-plans）的执行者。
> 关联：`docs/05-版本管理与发版.md`（现有发版机制）、`docs/08-N网自动发版与更新.md`（N网确认门）、
> 2026-10-28 游戏内更新可行性定案（检测+下载+落盘可游戏内、热生效不可能须重启）。

## 0. 背景与目标

**现状**：全部 mod 在 `mod_stack` 单仓开发；GitHub 侧只有一个源码仓 `wangk123/survivallog-mods`（Release 仅 tag+更新说明，**不带任何安装包资产**，publish.ps1 第 131 行注释明确此政策）；发版为套件统一制（version.json `suite` 单号，12 mod 跟号，唯 QuickAction 独立 v2.1.0）；分发走百度网盘安装器 + N网确认门。

**目标**：每个套件 mod 在 GitHub 组织 `survivallog-mods` 下获得独立仓库，各自独立版本线 + 独立 Release（附 zip 资产），成为 ModCenter 游戏内在线更新的拉取源，同时兼作 N网 mod 页的源码链接目标。

## 1. 决策记录（作者已拍板，2026-10-28）

| 决策项 | 结论 |
|---|---|
| 组织 | `survivallog-mods`（**已建**，Free 计划；作者=Owner admin/active；gh 凭据 repo+read:org 已验证连通） |
| 仓库名 | 短名（= mod 文件夹后缀），组织内天然带命名空间 |
| Release 资产范围 | **仅 12 个 mod 的 zip**；Base 包与安装器 exe 不上 GitHub，仍走网盘 |
| 版本模型 | **双轨制**：mod 独立版本线 + 安装器继续套件号 |
| 首轮基线 | 12 mod 首轮统一 **v1.7.0**（含 QuickAction，自 2.1.2 归位），此后各 mod 分开发布、独立演进 |
| 下载通道 | 直连优先 + 镜像回退链（镜像列表进 cfg，可配置） |
| 同步机制 | **快照同步**（脚本组装内容→比对远端→有差异才提交推送） |
| 契约资产 | 每 Release 附 `update.json`（版本/资产名/md5/验证依据） |
| LICENSE | 自定义条款（源码公开供学习，禁止二次分发/改名再发） |
| 旧仓处置 | `wangk123/survivallog-mods` **先不动**（publish.ps1 照旧 push）；之后所有更新走新组织；**弃用动作后续由作者触发**（届时加迁移横幅+archive） |
| 开发主战场 | `mod_stack` 单仓不变，组织仓=脚本同步的发布镜像 |

## 2. 仓库拓扑

**组织 `survivallog-mods` 下 12 个镜像仓**（= publish.ps1 `$suiteMods` 白名单同口径）：

`BackpackExpand`、`CabinetSize`、`CabinetEverywhere`、`CabinetBurden`、`FridgeChill`、`QuickAction`、`RoleUnlock`、`RobotSpeed`、`ModCenter`、`EndlessGuard`、`PlanPick`、`PreDisasterSpeed`

**外加 1 个组织主页仓** `survivallog-mods/survivallog-mods`（GitHub 惯例：与组织同名的仓，其 README 渲染为组织落地页）。内容：套件简介 + 12 仓链接表 + 安装渠道指引（网盘/N网）。这是"集合落地页"，随总仓 README 的 MOD 一览同步维护。

**绝不建仓（红线，写入 AGENTS.md）**：
- 探针三件：`ActionProbe` / `ConfigDumpProbe` / `ViewportProbe`（取证工具，非玩家 mod）
- `StackLimit999`（有未修复缺陷，禁止发布）
- `CabinetExpand`（已废弃，被 CabinetSize 取代）

**旧仓**：现阶段完全不动（含 publish.ps1 的 push/tag/Release 步骤）；弃用（横幅+archive+总仓 origin 去向）是后续独立动作，作者触发时另行处理。

## 3. 单仓布局与同步机制

### 布局（csproj 零改动、两边构建行为一致）

```
BackpackExpand/                  ← 仓库根
├─ README.md                     ← 复制自 SurvivalLog.BackpackExpand\README.md
├─ LICENSE                       ← 自定义条款（见 §9）
├─ SurvivalLog.BackpackExpand\   ← 原封不动（csproj 的 ..\ModShared\ 相对路径在仓内依然成立）
├─ ModShared\                    ← 原封不动复制（WelcomeTip.cs / BagTableRw.cs / README.md）
└─ .gitignore                    ← bin/ obj/
```

### 快照同步（`tools\sync_repos.ps1`）

- 对每个 mod 组装上述布局 → 与远端仓当前内容比对 → **有差异才** commit+push；commit message 带总仓 SHA（`sync from mod_stack @<sha>`）。
- **幂等**：内容一致不产生提交，可随意重跑。
- 仓内历史语义 = "本 mod 两次同步之间改了什么"；真考古在总仓。
- 触发点：① `release_mod.ps1` 发版前内嵌同步该 mod；② 独立跑 `sync_repos.ps1`（处理非发版变更：README 修订、ModShared 变更等——快照性质天然覆盖全仓）。
- 已否决的替代方案：`git subtree split` 保历史（单前缀限制导致 ModShared 带不进去、仓内构建断链）、`git-filter-repo` 一次性迁移（复杂度高、历史噪声大、收益低）。

## 4. 版本模型（双轨制）

### version.json 扩展（硬规则 1 不变：两个字段都只由作者定义，脚本只读）

```json
{
  "suite": "1.7.0",
  "mods": {
    "BackpackExpand": "1.7.0", "CabinetSize": "1.7.0", "CabinetEverywhere": "1.7.0",
    "CabinetBurden": "1.7.0", "FridgeChill": "1.7.0", "QuickAction": "1.7.0",
    "RoleUnlock": "1.7.0", "RobotSpeed": "1.7.0", "ModCenter": "1.7.0",
    "EndlessGuard": "1.7.0", "PlanPick": "1.7.0", "PreDisasterSpeed": "1.7.0"
  }
}
```

- **mod 线**：改哪个 mod 升哪个（如 1.7.0 → 1.7.1），走 `release_mod.ps1` 单发。
- **套件线**（`suite`）：只管安装器与总仓 tag；安装器捆绑 `release\` 下各 mod 当期最新 zip。
- 初始值 = 统一基线 **1.7.0**（作者口径 2026-10-28：首轮全按 1.7.0，之后各 mod 分开发布版本）。QuickAction 自 2.1.2 归位：Plugin.cs `Version` 常量与 csproj（现为陈旧 2.0.0）同步改 1.7.0 后重建。注意：cfg 持久键 `[迁移].DefaultsV21` 是已落盘的配置键，与版本号无关，**不得**因版本归位改名。

### CHANGELOG 拆分

- 每 mod 新增 `SurvivalLog.X\CHANGELOG.md`（进镜像仓，作为 release notes 来源）。
- 总仓根 `CHANGELOG.md` 保留：套件/安装器视角（发版闸门角色不变）。
- 首轮拆分：各 mod CHANGELOG 以当前版本为初始条目，内容取总仓 CHANGELOG v1.7.0 小节中该 mod 的条目；更早历史留在总仓不拆。

### publish.ps1 适配（仅一处语义变化）

第 2 步"csproj/Plugin.cs 版本**覆写**为套件号"改为"**校验** csproj 与 `version.json.mods` 一致，不一致拒发"（版本权威从 suite 单源变为 mods 字段）。其余步骤（含第 9 步对旧仓的 push/tag/Release）现阶段不动。

## 5. 单 mod 发版流水线（`tools\release_mod.ps1 -Mod <Name>`）

1. 读 `version.json.mods[Mod]`；校验该 mod 的 `CHANGELOG.md` 已有该版小节（闸门，仿套件发版）。
2. 同步 csproj `<Version>` 与 Plugin.cs `Version` 常量 ← 从 **mods 字段**（不再跟 suite）。
3. 构建（便携 SDK，判退出码）→ 打 zip（复用 `make_mods_release.ps1` 单 mod 逻辑，命名不变 `SurvivalLog.X_v<ver>.zip`，纯 ASCII 合规）。
4. 快照同步该 mod 仓 → push。
5. 生成 `update.json`（见 §6）→ tag `v<ver>` → `gh release create` 附 **zip + update.json** 两资产，notes 取自该 mod CHANGELOG。
6. 结束提示一句"N网如需推送另走确认门"，**绝不自动**。

**试点**：QuickAction（本就是独立版本，天然示范仓）。

**首轮回填**：12 仓统一以 **v1.7.0** 发首个 Release（首轮基线，见 §1），保证 ModCenter 上线时映射表完整。前 11 个 zip 直接用现有产物；**QuickAction 需重打**——dll 版本常量归位 1.7.0 后重建打包（现产物 `SurvivalLog.QuickAction_v1.7.0.zip` 虽为 1.7.0 命名，但内装 dll 上报 2.1.2，须以归位后的新构建替换）。

## 6. Release 资产与 update.json 契约

每个 Release 固定两资产：

| 资产 | 内容 |
|---|---|
| `SurvivalLog.X_v<ver>.zip` | 与网盘渠道同源的单 mod 安装包（命名即契约） |
| `update.json` | 几 KB 契约文件，流水线第 5 步自动生成 |

`update.json` 格式：

```json
{
  "mod": "BackpackExpand",
  "version": "1.7.1",
  "asset": "SurvivalLog.BackpackExpand_v1.7.1.zip",
  "md5": "<zip 的 MD5>",
  "gamekb": "2026-10-23_26157732"
}
```

- `md5`：完整性校验（非防篡改签名，本场景够用）。
- `gamekb`：本 release 构建/验证依据的 gamekb 快照版本（取 `gamekb\` 最新快照目录名）；语义 = "适配到此"，供 ModCenter 做游戏版本不匹配警示。警示策略（警告/拦截）在 ModCenter 立项时定。
- tag 命名 `v<mod版本>`（各仓独立 tag 空间，互不冲突）。

## 7. ModCenter 在线更新契约（本设计只冻结接口，功能另立项）

ModCenter 更新功能是独立项目（另行 spec + 计划），其冻结输入：

- **映射**：mod id → `survivallog-mods/<仓>`，ModCenter 内硬编码默认表 + cfg 可覆盖。
- **发现**：`GET /repos/survivallog-mods/{repo}/releases/latest`（api.github.com，境内通常可达）→ 拉 `update.json`。
- **决策**：比对已装 dll 版本（BepInEx PluginInfo）与 update.json.version。
- **下载**：直连资产 URL → 失败按 cfg `Mirrors` 前缀链依次重试（`<镜像前缀>/https://github.com/.../xxx.zip` 形式；列表可配置，默认直连优先，镜像换了不改代码）。
- **校验**：zip md5 对 update.json。
- **落盘**：zip 内 dll 解到 `plugins\`；**已加载 dll 用改名交换**（旧 dll → `.old`，新 dll 落位——ModCenter 现有 `.disabled` 改名机制已证明 Windows 只锁删除/覆盖、不锁改名）→ 提示重启（`steam://rungameid/4164790`）。
- **自更新**：ModCenter 自身同改名交换机制。
- **待立项时实证**：游戏内读取当前游戏版本并与 `gamekb` 字段比对的通道。

## 8. 渠道分工（本设计落地后）

| 渠道 | 内容 | 变化 |
|---|---|---|
| 百度网盘 | 安装器 + Base + 全量 zip（新玩家主渠道） | 不变 |
| GitHub org 12 仓 | 源码 + 单 mod Release（zip + update.json） | **新**：在线更新通道，兼作 N网"源码"链接 |
| N网 | per-mod 页面（docs\08 确认门） | 流程不变；填源码链接直接用组织仓地址 |
| 旧仓 `wangk123/survivallog-mods` | 总仓 push/tag/套件 Release | 现阶段照旧；后续作者触发弃用 |

## 9. LICENSE（自定义条款，文本实施时作者过目定稿）

要点：源码公开仅供学习参考；允许个人下载、使用、修改自用；**禁止**二次分发、改名再发、闭源搬运、去除署名。条款全文在实施第 1 步起草交作者审定后固化进 12 仓。

## 10. 文档与规则同步清单（实施收尾必做）

- `AGENTS.md`：硬规则 1 单源描述更新（suite + mods 双字段）；新增"组织仓黑白名单"规则（§2 红线名单）；命令速查加 `release_mod.ps1` / `sync_repos.ps1`。
- `docs/00-目录说明.md`：文档地图补本 spec。
- `docs/05-版本管理与发版.md`：双轨版本模型 + 单 mod 发版流程新章节。
- 总仓 `README.md`："本仓库不提供安装包下载"措辞更新 + 12 组织仓链接表。
- 各 mod `DESIGN.md`：迭代维护清单各补一条（版本管理方式变更）。
- `ModShared\README.md`：注明其在镜像仓中的同步角色。

## 11. 风险与边界

- **csproj HintPath 为本机绝对路径**（`E:\SteamLibrary\...`）：镜像仓仅装了游戏且路径一致的机器能构建。对作者无影响；镜像仓 README 注明即可，不引入环境变量机制（YAGNI）。
- **公共 ghproxy 类镜像时好时坏**：直连优先 + cfg 可配；将来可自建 Cloudflare Worker 代理（可选项，非必须）。
- **md5 只防传输损坏不防篡改**：接受（玩家群体与威胁模型不需要签名）。
- **Base/安装器不上 GitHub**：游戏内全新安装与基座修复仍依赖网盘（作者已定的边界）。
- **镜像仓历史 = 同步快照**：无逐提交历史，考古回总仓。
- **QuickAction 版本归位的存量影响**：存量玩家 dll 上报 2.1.2，数值上"高于"后续 1.7.x，版本比较会压住它的更新提示，直到该 mod 版本再次超过 2.1.2（届时可直接跳 2.2.0）或 ModCenter 比较策略改为展示远端版本线而非静默跳过（ModCenter 立项时定）。

## 12. 实施顺序（供 writing-plans 展开）

1. LICENSE 文本定稿（作者过目）
2. version.json 扩 `mods` 字段（首轮全部 1.7.0）+ 各 mod csproj/Plugin.cs 版本对齐校验（QuickAction 归位重戳 1.7.0 + 重建）
3. 每 mod `CHANGELOG.md` 拆分（含 v1.7.0 小节条目提取）
4. `tools\sync_repos.ps1`（快照同步，幂等）
5. 组织主页仓（落地页 README）
6. 12 仓初始化首灌（含 LICENSE/README/.gitignore）
7. `tools\release_mod.ps1`（含 update.json 生成）
8. 12 仓首轮回填 Release（当前版本 + 现有 zip）
9. QuickAction 全链路试点验证
10. publish.ps1 第 2 步适配（覆写→校验）
11. 文档/AGENTS 同步（§10 清单）
12. 后续独立立项：ModCenter 在线更新功能（消费 §7 契约）；旧仓弃用（作者触发）

## 13. 非目标（明确不做）

- 不做 Base 包 / 安装器的 GitHub 分发。
- 不把开发迁移到 12 仓（单仓开发 + 镜像发布不变）。
- 不动 N网确认门流程。
- 不在本设计内实现 ModCenter 更新功能（只冻结其消费契约）。
- 不为镜像仓引入独立构建系统（沿用总仓 csproj 原样）。
