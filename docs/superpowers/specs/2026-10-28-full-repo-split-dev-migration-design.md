# 彻底拆分——开发主场迁入 12 组织仓 + 套件中枢仓 — 设计（v2）

> 2026-10-28 定稿。**本 spec 推翻 v1（2026-10-28-repo-split-and-online-update-design.md）的"开发主战场=mod_stack 单仓"决策**，其余决策（org/短仓名/双轨版本语义/update.json 契约/下载回退链/LICENSE/红线）全部继承。作者口径：总仓拆完核对无遗漏后删除；新名 `survivallog-mods`。

## 0. 目标态

| 层 | 内容 |
|---|---|
| GitHub 组织 `survivallog-mods` | 12 个 mod 仓 = **各自开发主场**（公开，源码+ModShared 副本+CHANGELOG+README+LICENSE+仓内 release.ps1）；`survivallog-mods/survivallog-mods` = **套件中枢仓**（公开：落地页 README 为门面 + 安装器/publish 工具链/文档/物料/取证脚本/探针与存档 mod——与旧个人仓同暴露面） |
| 本地磁盘 | **一个父项目夹 `E:\Game\Mod\survivallog-mods\`**（父夹本身**不是 git 仓**），内装多个子项目：`suite\`（中枢仓 clone，套件基础设施子项目）、12 个 `<Mod>\`（各 mod 仓 clone，夹名=仓名）、`gamekb\`（**只留本地**，1.2GB，从 mod_stack 迁入）、`sdk\`（便携 SDK）、`thirdparty\`、`发版物料\`（本地） |
| 退役 | `E:\Game\Mod\mod_stack\`：迁移+对账完成、作者确认后**整仓删除**；旧个人仓 `wangk123/survivallog-mods` 同步执行弃用（横幅+archive） |

不变的红线：gamekb 与 `tools\secrets\` 永不入库（gitignore 随迁）；探针三件/StackLimit999/CabinetExpand **不建独立仓**（随中枢存档，绝不发 Release）；N网确认门；版本号只由作者改（各仓 csproj）。

## 1. 决策记录（作者已拍板 + 执行裁定）

| 项 | 结论 |
|---|---|
| 拆分方向 | 彻底拆到 12 仓开发，mod_stack 保留至核对无遗漏后删除（作者） |
| 命名 | `survivallog-mods`（作者）；与组织同名 → 中枢=现有主页仓扩容（GitHub 机制上唯一解） |
| ModShared 跨仓 | **中枢仓权威 + push 脚本推送 12 仓**（裁定：各仓自带完整副本保持单仓可编译；submodule 玩家侧易踩坑否决） |
| 版本权威 | **各仓 csproj 自治**（裁定：mod 版本=本仓 csproj `<Version>`，作者直改；中枢 version.json 只留 `suite`，`mods` 字段撤销） |
| 作者 WIP 批次 | **拆分前置条件：作者先在 mod_stack 提交**（裁定：不代提交未完成工作；执行到该步若未提交则停下等作者） |
| 已发的 11 个 Release/update.json 契约 | 不受影响，原样有效（契约不变） |

## 2. 各 mod 仓（开发主场）形态

```
BackpackExpand/                 ← 仓库根 = 开发主场
├─ README.md  LICENSE  CHANGELOG.md  .gitignore(bin/obj)
├─ release.ps1                  ← 仓内单发流水线（自带，见 §3）
├─ SurvivalLog.BackpackExpand\  ← csproj(..\ModShared\ 相对路径不变) + 源码
└─ ModShared\                   ← 完整副本（中枢权威的投影，勿手改）
```

- 开发流：clone 本地 `E:\Game\Mod\<Mod>\` → 改码 → 提交 → 改 CHANGELOG → 改 csproj 版本 → `release.ps1`。
- 快车道不变：游戏更新 → gamekb 同步核对 → 修 mod → 单仓发版。

## 3. 仓内 release.ps1（单发流水线，替代原 release_mod.ps1）

每仓一份（骨架相同 ~100 行 + **本 mod 专属 cfg 归一块**从 make_mods_release.ps1 拆入——mod 专属知识随 mod 走）：

1. 闸门：本仓干净树（只放行 CHANGELOG.md 脏）+ CHANGELOG 有 `## v<版本>` 小节。
2. 版本：csproj `<Version>` 即权威（无下发步骤）；Plugin.cs 常量校验一致。
3. 构建：绝对路径 HintPath 指游戏目录（不变）；SDK 用 `E:\Game\Mod\sdk\dotnet\dotnet.exe`。
4. 打包：dll + 本 mod cfg 归一 + `tools_readme`（readme_*.md 迁入各仓为 `ReleaseREADME.md`）→ `SurvivalLog.<Mod>_v<版本>.zip`。
5. update.json（契约同 v1 §6：mod/version/asset/md5/gamekb；gamekb id 读 `E:\Game\Mod\gamekb\` 最新快照目录）。
6. tag `v<版本>` + gh release（zip+update.json）+ N网提醒不自动。

## 4. 中枢仓 `survivallog-mods/survivallog-mods`

- 门面：落地页 README（现内容保留，"12 仓链接表"不变）。
- 迁入（自 mod_stack，git 历史尽量保留——采用**保留历史的迁移**：把 mod_stack 仓整体过滤掉 12 个 mod 目录后作为中枢仓历史基座，落地页/工具链延续真史）：
  - `installer\`、`tools\`（发版/构建/取证脚本；`secrets\` 保持 gitignore 不入）、`docs\`、`assets\`、`ModShared\`（**唯一权威**）、探针三件/StackLimit999/CabinetExpand 源码夹（存档，不发）、`version.json`（瘦身只留 suite）、根 README/CHANGELOG（套件视角）。
- 新增：`push_modshared.ps1`（改 ModShared 后一键提交推送 12 仓，附每仓比对报告）；`publish.ps1` 重写为多仓编排：pull 12 仓 → 逐仓 `release.ps1 -PackageOnly`（只打包不发版）→ Base 打包 → 安装器编译 → 回归 → 网盘组装 → 中枢仓 tag `suite-v<套件号>` + Release（仅说明，安装包仍只走网盘——v1 政策不变）。
- 原 `sync_repos.ps1` / `release_mod.ps1` / `manifest.json`（mod 部分）**退役删除**。

## 5. 本地磁盘迁移（父项目 + 子项目）

```
E:\Game\Mod\survivallog-mods\        ← 父项目夹（非 git 仓，纯容器）
├─ suite\                            ← 中枢仓 clone（survivallog-mods/survivallog-mods；工具链/安装器/文档/物料/存档）
├─ BackpackExpand\ … PreDisasterSpeed\   ← 12 个 mod 子项目（各自 git 仓 clone，开发主场）
├─ gamekb\                           ← 游戏资料集（只留本地，不入任何仓）
├─ sdk\                              ← 便携 .NET SDK（本地）
├─ thirdparty\                       ← 本地存档（不入库）
└─ 发版物料\                          ← 发布素材工作区（本地；产物图随 suite\assets 入中枢）
```

子项目各自负责自己的内容（各自 git 仓、各自发版流水线）；父夹只提供统一根路径，脚本一律以 `E:\Game\Mod\survivallog-mods\` 为根寻址兄弟子项目。`mod_stack\` 对账删除后 `E:\Game\Mod\` 下只剩这一个父夹。

## 6. 对账与删除（"没有遗漏就删"）

1. 迁移完成后产出**对账清单**：mod_stack 全目录树逐项标注去向（12 仓/中枢/本地平铺/删除）；**未跟踪文件单列**（含 gamekb 工具缓存、release 产物、build 日志、be788 测试残留）。
2. 作者过目清单 → 确认 → 删除 `E:\Game\Mod\mod_stack\`（含 .git）。
3. 旧个人仓弃用：README 顶部加迁移横幅指向组织 → archive。

## 7. 风险与边界

- **WIP 丢失风险**：mod_stack 工作树未提交内容在删除时即消失——前置条件（作者先提交）+ 对账清单双保险。
- **工具路径重写面大**：所有脚本/文档里的 `E:\Game\Mod\mod_stack` 引用要改（AGENTS 路径表全改）。
- **中枢历史迁移**：git-filter-repo 剔除 12 mod 目录保留其余历史——一次性操作，出错可重来（mod_stack 删除前是完整备份）。
- **12 份 release.ps1 骨架重复**：接受的代价（各仓自包含优先）；骨架改动走"改一份→脚本铺开"或逐仓手改。
- **公开开发**：WIP 推送即公开（与旧仓现状同面）；不推送即私有，闸门仍在。

## 8. 实施顺序（writing-plans 展开）

1. **前置门：作者提交 mod_stack WIP 批次**（唯一作者动作）
2. 中枢仓历史迁移（filter-repo 剔除 mod 目录 → 推主页仓）+ 本地中枢 clone 建立
3. 12 仓升级：release.ps1（含各自 cfg 归一块+ReleaseREADME 迁入）+ 本地 12 clone 建立
4. ModShared 权威定中枢 + push_modshared.ps1 + 首轮 no-op 验证
5. version.json 瘦身 + publish.ps1 多仓编排重写 + 安装器 define 源改（读各仓 csproj）
6. gamekb/sdk/thirdparty/发版物料 本地迁出 + 全部路径引用更新
7. 文档改写：AGENTS（路径表/工作流/硬规则 15 修订）、docs/05、各 DESIGN
8. 对账清单 → 作者确认 → 删 mod_stack → 旧仓弃用（横幅+archive）
