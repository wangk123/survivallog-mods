# 彻底拆分迁移 — 实施计划（v2 执行）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** 开发主场迁入 12 个组织仓 + 主页仓扩容为套件中枢（suite），本地重组为父项目夹 `E:\Game\Mod\survivallog-mods\`，mod_stack 对账后删除。

**Architecture:** 12 mod 仓=各自开发主场（仓内 release.ps1 自包含：闸门/构建/打包+cfg 归一/update.json/发版）；suite=中枢（工具链/安装器/文档/ModShared 唯一权威/物料/存档，带 mod_stack 过滤后的 git 历史）；父夹非 git 仓，统一根寻址。

**Spec:** `docs/superpowers/specs/2026-10-28-full-repo-split-dev-migration-design.md`（v2，含决策记录 §1）

## Global Constraints

- 继承 v1 全部红线（版本作者定义/外科手术提交/ASCII ps1/BOM 校验/N网确认门/资产名 ASCII）。
- **filter 历史迁移只在副本目录做**（`E:\Game\Mod\_mig_suite\`），mod_stack 本体在 T7 作者确认前**一个字节不动**（它是全程备份与对账基准）。
- force push 仅允许作用于主页仓（survivallog-mods/survivallog-mods，建仓不足一日，内容以 mod_stack 内单源为准），其余仓禁 force。
- `tools\secrets\`、`gamekb\`、`sdk\`、`release\`、`thirdparty\` 永不入任何 git 仓（迁移时保持 ignore/排除）。
- 作者 WIP 批次入库已获作者明确授权（"处理吧"，2026-10-28）——仅此一次 blanket 提交。
- 新路径根：`E:\Game\Mod\survivallog-mods\`（父夹）；中枢 clone 别名 `suite\`；mod clone 夹名=仓名。

## Tasks

### Task 1: WIP 入库 + 12 仓终次同步 + CabinetSize 补全
- `git add -A`（唯一一次，授权范围内）→ commit "chore: 作者 WIP 批次入库（拆分迁移前置；含 11 份 mod README、EndlessGuard/PlanPick 代码与 DESIGN、PlanCatalog、docs08、assets 旧物料清理、CabinetSize README 新增）"
- `sync_repos.ps1`（全量 12+1，CabinetSize README 已入库应能同步成功）
- CabinetSize 补发：`release_mod.ps1 -Mod CabinetSize -Yes -UseExistingZip`（zip 现成）
- 验：12/12 仓 v1.7.0 Release 双资产；工作树净（除 ignore）。

### Task 2: 中枢仓建立（历史迁移 + force push + 本地父夹）
- 副本 `E:\Game\Mod\_mig_suite\`：clone mod_stack（本地 file clone 保留全史）→ `git filter-repo --invert-paths` 剔除 12 个 `SurvivalLog.<Mod>` 目录（保留 ModShared/探针/StackLimit999/CabinetExpand/tools/docs/installer/assets/根文件）
- 根 README 替换为落地页内容（源 `tools/repo_sync/profile/README.md`）；删除 `tools/repo_sync/profile/`；补 commit "suite: 根 README=组织落地页"
- remote 指向 `survivallog-mods/survivallog-mods` → force push main
- 建父夹 `E:\Game\Mod\survivallog-mods\`；clone 主页仓为 `suite\`（本地 clone 从 _mig_suite 移动或重新 clone）
- 验：suite 仓含全史（log 条数≈mod_stack 剔除前-0）、落地页 README 在根、无 12 mod 目录、无 secrets/gamekb。

### Task 3: 12 子项目 + 仓内 release.ps1
- 父夹下 clone 12 仓到 `<Mod>\`
- 每仓新增：`release.ps1`（ASCII；闸门=本仓干净树（放行 CHANGELOG）+CHANGELOG 小节；csproj 权威+Plugin.cs 一致校验；构建用 `E:\Game\Mod\survivallog-mods\sdk\dotnet\dotnet.exe`；打包 dll+cfg 归一（**各 mod 的归一块从 make_mods_release.ps1 拆入**，QuickAction 用其生成式预置）+ReleaseREADME.md（源 `tools\readme_<mod>.md` 迁入各仓）；update.json（gamekb id 读 `..\gamekb\` 最新快照）；-PackageOnly 模式；tag+gh release+N网提醒）+ `ReleaseREADME.md`
- 每仓 commit+push（"feat(release): 仓内单发流水线 release.ps1（自包含构建打包+cfg归一+update.json）"）
- 验：12 仓 release.ps1 解析零错/ASCII；QuickAction 仓 `-PackageOnly` 产出与现有 zip 等价（dll 版本/条目）。

### Task 4: 中枢工具链重写（suite 内）
- 新 `tools\push_modshared.ps1`（比对 suite\ModShared 与 12 仓副本→差异仓提交推送+报告；首轮应全一致 no-op）
- `version.json` 瘦身（只留 suite）+ 根 CHANGELOG 保留套件视角
- 新 `tools\make_base_release.ps1`（自 make_mods_release 拆 Base 段）
- `publish.ps1` 重写多仓编排：pull 12 仓→逐仓 `release.ps1 -PackageOnly`→Base→安装器→说明→回归→网盘组装→suite tag `suite-v<套件号>`+Release（仅说明）→旧仓 origin 概念移除（套件 Release 走中枢仓）
- `build_installer.ps1`/`installer.iss`：/D 版本源改读各仓 csproj（pull 后 grep）
- 删除退役件：sync_repos.ps1 / release_mod.ps1 / repo_sync/manifest.json（profile 部分并入发布说明）/ make_mods_release.ps1 / make_quickaction_release.ps1 / rebuild_base_zip 相应调整
- 验：push_modshared 首轮 no-op；publish 冒烟（-SkipTest -SkipGithub）在父夹新布局下全绿。

### Task 5: 本地大件迁入父夹 + 新结构 PackageOnly 全验
- 移动：mod_stack\gamekb→父夹\gamekb；sdk→父夹\sdk；thirdparty→父夹\thirdparty；发版物料→父夹\发版物料（**复制而非移动 gamekb（1.2GB 对账基准留在 mod_stack）**；sdk/thirdparty/发版物料 可移动）
- 12 仓 × `-PackageOnly` 全跑（验证新寻址：sdk/gamekb 相对父夹）
- 验：全部产出 zip 与现 release\ 等价。

### Task 6: 文档全面改写（suite 内 + 12 仓 DESIGN）
- AGENTS.md：路径表全改（父夹/suite/12 仓/gamekb/sdk）、工作流改（游戏更新→修→各仓发版；套件发版=publish 编排）、硬规则 15 改写（中枢+子项目模型）、命令速查改
- docs/05 单 mod 发章节改（release.ps1 仓内流）；docs/00/02 相应路径
- 12 仓 DESIGN 迭代清单行改路径引用；suite 根 README（落地页）微调"各仓自带构建发版"
- 验：grep 无 `mod_stack` 残留引用（suite+12 仓全扫，历史提交除外）。

### Task 7: 对账清单 → 作者确认门（停）
- 产出对账清单：mod_stack 目录树逐项去向表（12 仓/suite/父夹本地/随删除丢弃），未跟踪文件单列（release 产物/build 日志/be788 残留/secrets 留否确认）
- **停：呈作者确认**。确认后执行：删 `E:\Game\Mod\mod_stack\`（含 .git）→ 旧个人仓弃用（README 横幅指向组织 + archive）→ 更新记忆文件路径。
