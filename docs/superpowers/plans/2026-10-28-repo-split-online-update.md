# 单 mod 拆仓 + GitHub 组织 + 在线更新通道 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 12 个套件 mod 各自镜像到 GitHub 组织 `survivallog-mods` 下的独立仓库，建立单 mod 独立发版流水线（Release 附 zip + update.json 契约资产），为 ModCenter 游戏内在线更新提供拉取源。

**Architecture:** 开发主战场保持在 `mod_stack` 单仓不动；`sync_repos.ps1` 从 **git HEAD**（绝不碰工作树 WIP）快照同步每个 mod（mod 文件夹 + ModShared 同级布局，csproj 零改动）到组织镜像仓；`release_mod.ps1` 走"版本闸门→干净树校验→版本戳→构建→复用现有打包→update.json→同步→gh release"单发流水线；版本号双轨（`version.json` 的 `suite`=安装器线，`mods`=单 mod 线）。

**Tech Stack:** PowerShell 5.1 脚本（新脚本一律 ASCII-only）、git（archive/clone/commit）、gh CLI（repo create/api/release）、.NET 便携 SDK（`sdk\dotnet\dotnet.exe`）、Inno Setup（安装器）。

**Spec:** `docs/superpowers/specs/2026-10-28-repo-split-and-online-update-design.md`（本计划的所有决策依据；执行者必读其 §1 决策记录与 §6 契约格式）。

## Global Constraints（每个任务隐含遵守）

- **版本号只由作者定义**：单源 = 根 `version.json`（`suite` + `mods` 双字段）；执行者绝不自定/自增任何版本号。
- **外科手术式提交**：总仓内禁止 `git add -A` / `git add .`；只 add 任务清单里点名的文件。工作树常带作者未提交 WIP（当前 `EndlessGuardPlugin.cs`、`ScavengePlans.cs`、多份 README/DESIGN 为脏），**绝不 stage 它们**（Task 11 的文档同步是唯一预期例外，见该任务注记）。
- **新 ps1 一律 ASCII-only**（无中文）；中文内容只进数据文件（manifest.json / CHANGELOG.md / LICENSE / README）。
- **已有带 BOM 的 ps1（publish.ps1 / make_mods_release.ps1 / make_quickaction_release.ps1 / build_installer.ps1）与 iss 被编辑后必验 BOM**：`head -c 3 <文件> | od -An -tx1` 应输出 `ef bb bf`；若丢失，修复命令：
  `powershell -NoProfile -Command "$p='<文件>'; $t=[IO.File]::ReadAllText($p); [IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding($true)))"`
- **构建判退出码**：`"sdk\dotnet\dotnet.exe" build <目录> -c Release > log 2>&1; echo $?`；禁止 `| tail` 管道吃退出码。
- **PS5.1 + EAP=Stop 禁给原生命令加重定向**（2>&1/2>$null 会触发 NativeCommandError）——一律裸调用、只看 `$LASTEXITCODE`。（本计划个别验证命令为 bash 管道取尾部输出，属只读检查，不受此限。）
- **绝不建仓/发布**：探针三件（ActionProbe/ConfigDumpProbe/ViewportProbe）、StackLimit999、CabinetExpand。
- **N网推送绝不自动**：脚本最多打印提醒；N网走 docs/08 确认门。
- **GitHub 资产名必须纯 ASCII**（zip 命名 `SurvivalLog.<Mod>_v<版本>.zip` 沿用现状；契约资产固定名 `update.json`）。
- **发布源码必须已提交**：发版脚本从 HEAD 打快照；正常发版路径有干净树闸门（仅放行 CHANGELOG.md 与 version.json 两个发版准备文件的脏改动）。
- 组织已建：`survivallog-mods`（作者=Owner admin/active；gh 凭据 wangk123 已登录，scopes repo+read:org 已验证）。

## File Structure（全景）

| 文件 | 动作 | 职责 |
|---|---|---|
| `tools/repo_sync/LICENSE` | 新建 | 镜像仓 LICENSE 单源（自定义条款） |
| `tools/repo_sync/manifest.json` | 新建 | 12 mod + 组织主页仓的元数据（仓名/中文描述） |
| `tools/repo_sync/profile/README.md` | 新建 | 组织主页仓落地页内容单源 |
| `tools/sync_repos.ps1` | 新建 | 快照同步（HEAD→镜像仓，幂等，-Create 建仓） |
| `tools/release_mod.ps1` | 新建 | 单 mod 发版流水线 |
| `version.json` | 修改 | 加 `mods` 字段（首轮全 1.7.0） |
| `SurvivalLog.QuickAction/SurvivalLog.QuickAction.csproj` | 修改 | Version 2.0.0 → 1.7.0 |
| `SurvivalLog.QuickAction/QuickActionPlugin.cs` | 修改 | Version 常量 2.1.2 → 1.7.0 |
| `SurvivalLog.<12 mod>/CHANGELOG.md` | 新建×12 | 单 mod 更新日志（发版闸门） |
| `tools/make_mods_release.ps1` | 修改 | 版本源切换 + `-Mod` 单发参数 |
| `tools/make_quickaction_release.ps1` | 修改 | 版本源切换（suite → mods.QuickAction） |
| `tools/publish.ps1` | 修改 | 步骤 2 覆写→校验；步骤 8 产物名按 mod 版本 |
| `tools/build_installer.ps1` | 修改 | 传 per-mod 版本 /D defines |
| `installer/installer.iss` | 修改 | mod 组件路径 `_v{#SuiteVer}` → `_v{#Ver<Mod>}` |
| `AGENTS.md`、根 `README.md`、`docs/00`、`docs/05`、`ModShared/README.md`、12× `DESIGN.md` | 修改 | 文档与规则同步（Task 11） |

---

### Task 1: LICENSE 草案定稿（作者审批门）

**Files:**
- Create: `tools/repo_sync/LICENSE`

**Interfaces:**
- Produces: `tools/repo_sync/LICENSE`（Task 5 sync_repos.ps1 复制进每个镜像仓根目录）。

- [ ] **Step 1: 写 LICENSE 草案文件**

内容全文如下（UTF-8）：

```markdown
生存日志 MOD 套件 · 源码使用许可（自定义条款）
版权所有 © 2026 Night（抖音 166660371）

本仓库为《生存日志》(Survival Log, Steam AppID 4164790) 的 BepInEx 6 (IL2CPP) 插件源码。

一、你可以（无需询问）
1. 下载、编译本源码，并将构建产物用于自己的游戏；
2. 阅读与学习本源码的实现；
3. 在本机修改自用（不分发）。

二、你不可以
1. 以任何形式二次分发本仓库内容或其构建产物（包括但不限于转载、打包再发、网盘/镜像站分发、视频教程附带分发）；
2. 去除、修改署名或防盗提示后分发；
3. 基于本源码制作衍生 mod 并分发（含改名再发、换皮再发）；
4. 将本源码或其衍生版本用于任何商业用途。

三、正确分享方式
想推荐本 mod，请直接分享本仓库地址或作者的官方发布渠道（百度网盘安装包 / Nexus Mods 页面），而不是文件本身。

四、反馈
建议与 bug 欢迎在本仓库 Issues 留言，或通过抖音联系作者。

五、免责
本源码按"现状"提供，不附带任何明示或默示的担保；使用本源码产生的任何后果由使用者自行承担。本 mod 不改存档、不改游戏本体文件，但请自行做好存档备份。

English summary: Source is public for personal use and study only. Redistribution, re-upload, derivative distribution, and commercial use are prohibited. Share by linking to this repository or the author's official channels instead of redistributing files.
```

- [ ] **Step 2: 呈作者审批（人工门，不过不进 Task 2）**

把 Step 1 全文展示给作者，明确问："这份 LICENSE 条款定稿可以吗？要改哪里？" 作者说可以才继续；要改就改完再确认。

- [ ] **Step 3: 提交**

```bash
git add "tools/repo_sync/LICENSE"
git commit -m "feat(repo_sync): 镜像仓自定义 LICENSE 定稿（作者审定）"
```

---

### Task 2: version.json 双轨扩展 + QuickAction 版本归位

**Files:**
- Modify: `version.json`
- Modify: `SurvivalLog.QuickAction/SurvivalLog.QuickAction.csproj`（`<Version>2.0.0</Version>` 行）
- Modify: `SurvivalLog.QuickAction/QuickActionPlugin.cs:49`（`public const string Version = "2.1.2";`）

**Interfaces:**
- Produces: `version.json` 新增 `mods` 对象（12 键，首轮全 `"1.7.0"`）——Task 4/5/6/10 脚本按 `$vj.mods.<Name>` 读取。
- Produces: QuickAction 版本三处（csproj/Plugin.cs/dll）齐一为 1.7.0。

- [ ] **Step 1: 改 version.json**

改为（保留原 BOM；`_note` 更新为双源说明）：

```json
{
  "suite": "1.7.0",
  "mods": {
    "BackpackExpand": "1.7.0",
    "CabinetSize": "1.7.0",
    "FridgeChill": "1.7.0",
    "QuickAction": "1.7.0",
    "CabinetEverywhere": "1.7.0",
    "CabinetBurden": "1.7.0",
    "RoleUnlock": "1.7.0",
    "RobotSpeed": "1.7.0",
    "ModCenter": "1.7.0",
    "EndlessGuard": "1.7.0",
    "PlanPick": "1.7.0",
    "PreDisasterSpeed": "1.7.0"
  },
  "_note": "Suite = installer/总仓 tag 线；mods = 单 mod 发版线（GitHub 组织通道）。两者都只由仓库作者定义，脚本只读。"
}
```

- [ ] **Step 2: QuickAction 归位**

- csproj：`<Version>2.0.0</Version>` → `<Version>1.7.0</Version>`
- QuickActionPlugin.cs：`public const string Version = "2.1.2";` → `public const string Version = "1.7.0";`
- **不动** cfg 持久键 `迁移.DefaultsV21`（已落盘的配置键，与版本号无关，改名会破坏存量配置）。

- [ ] **Step 3: 验 BOM 未丢 + 重建 QuickAction + 验 dll 版本**

```bash
head -c 3 version.json | od -An -tx1        # 期望 ef bb bf
"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.QuickAction -c Release > build_qa_v170.log 2>&1; echo $?
powershell -NoProfile -Command "[Reflection.AssemblyName]::GetAssemblyName('E:\Game\Mod\mod_stack\SurvivalLog.QuickAction\bin\Release\SurvivalLog.QuickAction.dll').Version.ToString()"
```

期望：退出码 0；dll 版本输出 `1.7.0`。

- [ ] **Step 4: 全套件版本一致性核验**

```bash
cd "E:/Game/Mod/mod_stack" && for m in BackpackExpand CabinetSize FridgeChill QuickAction CabinetEverywhere CabinetBurden RoleUnlock RobotSpeed ModCenter EndlessGuard PlanPick PreDisasterSpeed; do echo "$m: $(grep -m1 '<Version>' SurvivalLog.$m/SurvivalLog.$m.csproj | grep -o '[0-9.]*') / $(grep -rh 'const string Version' SurvivalLog.$m/*Plugin.cs | grep -o '\"[0-9.]*\"')"; done
```

期望：12 行全部 `1.7.0 / "1.7.0"`。

- [ ] **Step 5: 提交**

```bash
git add version.json SurvivalLog.QuickAction/SurvivalLog.QuickAction.csproj SurvivalLog.QuickAction/QuickActionPlugin.cs
git commit -m "feat(version): version.json 扩 mods 双轨字段(首轮全1.7.0); QuickAction 自 2.1.2/2.0.0 归位重戳 1.7.0"
```

---

### Task 3: 12 × 每 mod CHANGELOG.md 拆分

**Files:**
- Create: `SurvivalLog.<Mod>/CHANGELOG.md` ×12

**Interfaces:**
- Produces: 每 mod 的更新日志，含 `## v1.7.0（2026-10-25）` 小节——Task 6 release_mod.ps1 的发版闸门与 release notes 都从这里取。

- [ ] **Step 1: 建 12 份文件**

统一模板（`<中文名>` 见下表；正文条目规则见后）：

```markdown
# 更新日志（SurvivalLog.<Mod> / <中文名>）

> 本文件只记本 mod 的更新；套件/安装器版本史见总仓根 CHANGELOG.md。
> 版本号单源：总仓 version.json 的 mods 字段（作者定义）。

## v1.7.0（2026-10-25）

<条目>
```

条目来源（从根 `CHANGELOG.md` 的 v1.7.0 小节**逐字复制**指定条目块，保持缩进）：

| Mod | 中文名 | 条目 |
|---|---|---|
| EndlessGuard | 无尽守卫（囤鼠桃园） | 以 `- **新 MOD：无尽守卫 EndlessGuard（囤鼠桃园）**：` 开头的整块（到下一个一级条目前） |
| PlanPick | 自选规划 | 以 `- **新 MOD：自选规划 PlanPick**：` 开头的整块 |
| PreDisasterSpeed | 灾前加速 | 以 `- **新 MOD：灾前加速 PreDisasterSpeed**：` 开头的整块 |
| CabinetBurden | 柜子负重解放 | 以 `- **柜子负重解放语义改版（×1000 → 无限制）**：` 开头的整块 |
| ModCenter | MOD 管理中心 | 以 `- **新 MOD：MOD 管理中心 ModCenter**：` 开头的整块 |
| RoleUnlock | 角色专属解锁 | 以 `- **新 MOD：角色专属解锁 RoleUnlock**：` 开头的整块 |
| BackpackExpand | 背包扩容 | 以 `- **背包扩容改版（倍率制→数值制）**：` 开头的整块 |
| CabinetSize | 柜子容量 | 以 `- **柜子扩容 → 柜子容量 CabinetSize（数值型改版，旧 mod 废弃）**：` 开头的整块 |

其余 4 个当版无独立条目（FridgeChill=冰箱保鲜、QuickAction=动作提速、CabinetEverywhere=储藏柜直达、RobotSpeed=机器人提速），`<条目>` 写：

```markdown
- 随套件 v1.7.0 发布（本 mod 当版无独立条目；历史更新说明见总仓根 CHANGELOG.md）。
```

其中 QuickAction 的这行替换为：

```markdown
- 随套件 v1.7.0 发布并统一版本基线（此前插件版本线为 v2.x，历史见总仓根 CHANGELOG.md）。
```

- [ ] **Step 2: 核验**

```bash
cd "E:/Game/Mod/mod_stack" && for m in BackpackExpand CabinetSize FridgeChill QuickAction CabinetEverywhere CabinetBurden RoleUnlock RobotSpeed ModCenter EndlessGuard PlanPick PreDisasterSpeed; do echo "$m: $(grep -c '^## v1.7.0' SurvivalLog.$m/CHANGELOG.md)"; done
```

期望：12 行全部 `1`。

- [ ] **Step 3: 提交**

```bash
git add SurvivalLog.BackpackExpand/CHANGELOG.md SurvivalLog.CabinetSize/CHANGELOG.md SurvivalLog.FridgeChill/CHANGELOG.md SurvivalLog.QuickAction/CHANGELOG.md SurvivalLog.CabinetEverywhere/CHANGELOG.md SurvivalLog.CabinetBurden/CHANGELOG.md SurvivalLog.RoleUnlock/CHANGELOG.md SurvivalLog.RobotSpeed/CHANGELOG.md SurvivalLog.ModCenter/CHANGELOG.md SurvivalLog.EndlessGuard/CHANGELOG.md SurvivalLog.PlanPick/CHANGELOG.md SurvivalLog.PreDisasterSpeed/CHANGELOG.md
git commit -m "docs(changelog): 12 mod 各建独立 CHANGELOG(v1.7.0 条目自总仓拆出)——单 mod 发版闸门与 release notes 来源"
```

---

### Task 4: 打包脚本版本源切换 + `-Mod` 单发参数

**Files:**
- Modify: `tools/make_mods_release.ps1`（头部版本读取、Base 段包裹、循环过滤与命名）
- Modify: `tools/make_quickaction_release.ps1:4-5`（版本读取行）

**Interfaces:**
- Produces: `make_mods_release.ps1` 支持 `param([string]$Mod = '')`：有值时**跳过 Base 段**、只打包该 mod；无值时行为不变（Base+11 mod，publish.ps1 调用不变）。
- Produces: mod zip 命名从 suite 版本改为 **mod 自身版本**（`$vj.mods.<Name>`）。Task 6 release_mod.ps1 以 `& make_mods_release.ps1 -Mod <Name>` 消费。

- [ ] **Step 1: 改 make_mods_release.ps1 头部**

把第 5-6 行：

```powershell
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver      = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
```

替换为：

```powershell
param([string]$Mod = '')
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
# $ver = suite（Base 包/安装器线）；$mver = mods（单 mod 线，mod zip 命名用）
$vj  = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.suite
$mver = @{}
$vj.mods.PSObject.Properties | ForEach-Object { $mver[$_.Name] = $_.Value }
```

- [ ] **Step 2: Base 段包裹**

`# ---------- BASE package ----------` 到 Base zip 的 Compress-Archive 行（现第 8-38 行）整段包进 `if (-not $Mod) { ... }`（单发模式不做 Base）。

- [ ] **Step 3: 循环过滤与命名**

`foreach ($m in $mods) {` 后第一行起加：

```powershell
    if ($Mod -and $m.Name -ne $Mod) { continue }
    $mv = $mver[$m.Name]
    if (-not $mv) { throw ('version.json mods missing entry: ' + $m.Name) }
```

并把循环内两处版本命名 `$ver` 换成 `$mv`：

```powershell
    $pkg = Join-Path $relRoot ('SurvivalLog.' + $m.Name + '_v' + $mv)
```

```powershell
    $zip = Join-Path $relRoot ('SurvivalLog.' + $m.Name + '_v' + $mv + '.zip')
```

（新鲜度守卫、cfg 归一、README 拷贝逻辑一行不动。）

- [ ] **Step 4: 改 make_quickaction_release.ps1 版本源**

第 4-5 行：

```powershell
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$ver      = (Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json).suite
```

替换为：

```powershell
# 版本号唯一来源：根目录 version.json 的 mods.QuickAction（仅作者定义，脚本只读）
$vj  = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.mods.QuickAction
if (-not $ver) { throw 'version.json mods.QuickAction missing' }
```

- [ ] **Step 5: 验 BOM + 单发冒烟**

```bash
cd "E:/Game/Mod/mod_stack"
head -c 3 tools/make_mods_release.ps1 | od -An -tx1     # 期望 ef bb bf
head -c 3 tools/make_quickaction_release.ps1 | od -An -tx1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/make_quickaction_release.ps1
ls release/ | grep QuickAction
```

期望：两个 BOM 都在；QuickAction 打出 `SurvivalLog.QuickAction_v1.7.0.zip`（内装 dll 为 Task 2 重戳后的 1.7.0 构建）。

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/make_mods_release.ps1 -Mod BackpackExpand
ls release/ | grep BackpackExpand
ls release/ | grep -c "Base_v.*\.zip"
```

期望：打出 `SurvivalLog.BackpackExpand_v1.7.0.zip`，且**没有**新生成 Base 包时间戳变化（Base 段被跳过，现存 Base_v1.7.0.zip 为旧文件，比较修改时间应早于本次运行）。

- [ ] **Step 6: 提交**

```bash
git add tools/make_mods_release.ps1 tools/make_quickaction_release.ps1
git commit -m "feat(packaging): 打包脚本版本源切换——mod zip 按 version.json mods 字段命名; make_mods_release 增 -Mod 单发参数(单发跳过 Base)"
```

---

### Task 5: manifest.json + sync_repos.ps1（快照同步）

**Files:**
- Create: `tools/repo_sync/manifest.json`
- Create: `tools/sync_repos.ps1`

**Interfaces:**
- Consumes: `tools/repo_sync/LICENSE`（Task 1）。
- Produces: `sync_repos.ps1`，参数 `-Mod <Name> | -Create | -ListOnly`；快照源 = **git HEAD**（mod 文件夹 + ModShared 同级 + 根 README/LICENSE/.gitignore）。Task 6 release_mod.ps1 以 `& sync_repos.ps1 -Mod <Name>` 消费；Task 7/8 以 `-Create` 建仓。

- [ ] **Step 1: 写 manifest.json**（UTF-8，中文描述用于 GitHub 仓库描述）

```json
{
  "org": "survivallog-mods",
  "mods": [
    { "mod": "BackpackExpand",    "repo": "BackpackExpand",    "description": "《生存日志》背包扩容：宽/高/负载数值可调（默认 11/10/48000），天赋加成照常叠加" },
    { "mod": "CabinetSize",       "repo": "CabinetSize",       "description": "《生存日志》柜子容量：全部柜子四档统一固定宽×高，13 常用包微调 + 任意柜个性化定值" },
    { "mod": "FridgeChill",       "repo": "FridgeChill",       "description": "《生存日志》冰箱保鲜：保鲜 5 倍 → 10 倍，天赋加成上限可调" },
    { "mod": "QuickAction",       "repo": "QuickAction",       "description": "《生存日志》动作提速：77 项动作逐项可调，默认 0.5 秒" },
    { "mod": "CabinetEverywhere", "repo": "CabinetEverywhere", "description": "《生存日志》储藏柜直达：储物柜进工作台/无人机交易/烹饪等界面容器页签" },
    { "mod": "CabinetBurden",     "repo": "CabinetBurden",     "description": "《生存日志》柜子负重解放：全部带负重上限的储物家具负重无限制" },
    { "mod": "RoleUnlock",        "repo": "RoleUnlock",        "description": "《生存日志》角色专属解锁：藤蔓种子全角色可发现，防御配方四角色互通" },
    { "mod": "RobotSpeed",        "repo": "RobotSpeed",        "description": "《生存日志》机器人提速：动作对齐手速，移速/充电/续航倍率可调" },
    { "mod": "ModCenter",         "repo": "ModCenter",         "description": "《生存日志》MOD 管理中心：游戏内面板集中开关/调参全套件 mod" },
    { "mod": "EndlessGuard",      "repo": "EndlessGuard",      "description": "《生存日志》无尽守卫（囤鼠桃园）：无尽模式永不沦陷 + 11 条后期拾遗规划" },
    { "mod": "PlanPick",          "repo": "PlanPick",          "description": "《生存日志》自选规划：全部 465+ 项规划可搜可买，非今日候选两倍价" },
    { "mod": "PreDisasterSpeed",  "repo": "PreDisasterSpeed",  "description": "《生存日志》灾前加速：官方五键倍速条搬到灾前，按钮+快捷键变速" }
  ],
  "profile": { "repo": "survivallog-mods", "description": "《生存日志》(Survival Log) MOD 套件——12 个 BepInEx IL2CPP 插件的合集主页与在线更新通道" }
}
```

- [ ] **Step 2: 写 sync_repos.ps1**（全文如下，ASCII-only）

```powershell
# ============ Snapshot sync: monorepo -> per-mod mirror repos under GitHub org ============
# Usage: tools\sync_repos.ps1 [-Mod <Name>] [-Create] [-ListOnly]
#   -Mod      sync one mod repo only (org profile repo not touched)
#   -Create   create missing repos first (one-time; requires gh login with repo scope)
#   -ListOnly show what would change; commit/push nothing
# Snapshot source = git HEAD of this monorepo (NEVER the working tree: author WIP stays out).
# Mirror repo layout: README.md, LICENSE, .gitignore, SurvivalLog.<Mod>\, ModShared\
# (csproj keeps its ..\ModShared\ relative links - both trees build identically).
# Design: docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md
param(
    [string]$Mod = '',
    [switch]$Create,
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
$tools    = Join-Path $projRoot 'tools'
$ghExe    = Join-Path $tools 'gh\bin\gh.exe'
$workRoot = Join-Path $env:TEMP 'slrepo_sync'

$man = Get-Content (Join-Path $tools 'repo_sync\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$org = $man.org
if ($Mod -and -not (@($man.mods | Where-Object { $_.mod -eq $Mod }).Count)) {
    throw ('unknown mod in manifest: ' + $Mod)
}

function Ensure-Repo([string]$RepoName, [string]$Description) {
    & $ghExe api ('repos/' + $org + '/' + $RepoName) --silent
    if ($LASTEXITCODE -eq 0) { return }
    if (-not $Create) { throw ('repo missing, run once with -Create: ' + $org + '/' + $RepoName) }
    & $ghExe repo create ($org + '/' + $RepoName) --public
    if ($LASTEXITCODE -ne 0) { throw ('gh repo create failed: ' + $RepoName) }
    # description via API body file: keeps non-ASCII off the command line (PS5.1 + gh trap)
    $descJson = Join-Path $env:TEMP ('slrepo_desc_' + $RepoName + '.json')
    [IO.File]::WriteAllText($descJson, (ConvertTo-Json @{ description = $Description }), (New-Object Text.UTF8Encoding($false)))
    & $ghExe api ('repos/' + $org + '/' + $RepoName) -X PATCH --input $descJson
    if ($LASTEXITCODE -ne 0) { throw ('set description failed: ' + $RepoName) }
    & $ghExe repo edit ($org + '/' + $RepoName) --add-topic survival-log --add-topic bepinex --add-topic il2cpp
    if ($LASTEXITCODE -ne 0) { throw ('set topics failed: ' + $RepoName) }
}

function Sync-Repo($entry) {
    $repoName = $entry.repo
    $isMod = [bool]$entry.mod
    Ensure-Repo $repoName $entry.description

    # 1. stage snapshot from git HEAD (archive to zip file = binary-safe on PS5.1, no piping)
    $stage = Join-Path $workRoot ('stage_' + $repoName)
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item $stage -ItemType Directory -Force | Out-Null
    $archZip = Join-Path $workRoot ('arch_' + $repoName + '.zip')
    if ($isMod) {
        git -C $projRoot archive --format=zip --output=$archZip HEAD -- ('SurvivalLog.' + $entry.mod) ModShared
    } else {
        git -C $projRoot archive --format=zip --output=$archZip HEAD -- tools/repo_sync/profile
    }
    if ($LASTEXITCODE -ne 0) { throw ('git archive failed: ' + $repoName) }
    Expand-Archive $archZip -DestinationPath $stage
    if ($isMod) {
        Copy-Item (Join-Path $stage ('SurvivalLog.' + $entry.mod + '\README.md')) (Join-Path $stage 'README.md') -Force
        [IO.File]::WriteAllText((Join-Path $stage '.gitignore'), "bin/`r`nobj/`r`n", (New-Object Text.UTF8Encoding($false)))
    } else {
        # profile staging: flatten tools/repo_sync/profile/* to repo root
        $p = Join-Path $stage 'tools\repo_sync\profile'
        Get-ChildItem $p | Move-Item -Destination $stage -Force
        Remove-Item (Join-Path $stage 'tools') -Recurse -Force
    }
    Copy-Item (Join-Path $projRoot 'tools\repo_sync\LICENSE') (Join-Path $stage 'LICENSE') -Force

    # 2. local clone cache (byte-stable: autocrlf off)
    $clone = Join-Path $workRoot ('repo_' + $repoName)
    if (-not (Test-Path (Join-Path $clone '.git'))) {
        if (Test-Path $clone) { Remove-Item $clone -Recurse -Force }
        git clone ('https://github.com/' + $org + '/' + $repoName + '.git') $clone
        if ($LASTEXITCODE -ne 0) { throw ('git clone failed: ' + $repoName) }
        git -C $clone config core.autocrlf false
        $br = (git -C $clone symbolic-ref --short HEAD) | Select-Object -First 1
        if ("$br" -ne 'main') {
            git -C $clone branch -m main
            if ($LASTEXITCODE -ne 0) { throw ('branch rename to main failed: ' + $repoName) }
        }
    }

    # 3. apply snapshot into clone (.git protected by /XD)
    robocopy $stage $clone /MIR /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ('robocopy failed: ' + $repoName) }
    $dirty = @(git -C $clone status --porcelain)
    if ($dirty.Count -eq 0) { Write-Host ('  ' + $repoName + ': up to date'); return }
    if ($ListOnly) {
        Write-Host ('  ' + $repoName + ' WOULD SYNC:')
        $dirty | ForEach-Object { Write-Host ('    ' + $_) }
        return
    }
    $sha = (git -C $projRoot rev-parse --short HEAD) | Select-Object -First 1
    git -C $clone add -A
    git -C $clone commit -m ('sync from mod_stack @' + $sha)
    if ($LASTEXITCODE -ne 0) { throw ('commit failed: ' + $repoName) }
    git -C $clone push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw ('push failed: ' + $repoName) }
    Write-Host ('  ' + $repoName + ': pushed (sync from mod_stack @' + $sha + ')')
}

New-Item $workRoot -ItemType Directory -Force | Out-Null
if ($Mod) {
    Sync-Repo ($man.mods | Where-Object { $_.mod -eq $Mod })
} else {
    foreach ($e in $man.mods) { Sync-Repo $e }
    Sync-Repo $man.profile
}
Write-Host 'done.'
```

- [ ] **Step 3: 冒烟（仓未建，应报 missing 并退出——正确行为）**

```bash
cd "E:/Game/Mod/mod_stack" && powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_repos.ps1 -Mod QuickAction 2>&1 | tail -3
```

期望：报 `repo missing, run once with -Create: survivallog-mods/QuickAction`（仓在 Task 7 才建，此处只验脚本可运行且闸门有效）。

- [ ] **Step 4: 提交**

```bash
git add tools/repo_sync/manifest.json tools/sync_repos.ps1
git commit -m "feat(repo_sync): 镜像仓元数据 manifest + 快照同步脚本(HEAD快照/幂等/-Create建仓/描述走API体避开中文参数坑)"
```

---

### Task 6: release_mod.ps1（单 mod 发版流水线）

**Files:**
- Create: `tools/release_mod.ps1`

**Interfaces:**
- Consumes: `version.json` `mods` 字段（Task 2）；`SurvivalLog.<Mod>/CHANGELOG.md` 闸门与 notes（Task 3）；`make_mods_release.ps1 -Mod` / `make_quickaction_release.ps1`（Task 4）；`sync_repos.ps1 -Mod`（Task 5）。
- Produces: 每 Release 两资产——`SurvivalLog.<Mod>_v<版本>.zip` + **`update.json`**（固定资产名；内容 `{"mod","version","asset","md5","gamekb"}`，spec §6）；tag `v<版本>`；总仓内外科手术 release 提交。ModCenter 未来按此契约消费。

- [ ] **Step 1: 写 release_mod.ps1**（全文如下，ASCII-only）

```powershell
# ============ Per-mod release pipeline (GitHub org channel) ============
# Usage: tools\release_mod.ps1 -Mod <Name> [-Yes] [-UseExistingZip]
#   -Mod            mod name as in version.json mods map (e.g. QuickAction)
#   -Yes            skip confirmation prompt
#   -UseExistingZip skip build+package, release release\SurvivalLog.X_v<ver>.zip as-is
#                   (backfill only; artifact must already exist and match HEAD)
# Version source: version.json -> mods.<Name> (author-defined ONLY).
# Gate: SurvivalLog.<Name>\CHANGELOG.md must contain a "## v<version>" section.
# Nexus Mods upload is NEVER automated here (separate author-confirmed step, docs/08).
param(
    [Parameter(Mandatory = $true)][string]$Mod,
    [switch]$Yes,
    [switch]$UseExistingZip
)
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
$tools    = Join-Path $projRoot 'tools'
$ghExe    = Join-Path $tools 'gh\bin\gh.exe'
$relRoot  = Join-Path $projRoot 'release'
$repo     = 'survivallog-mods/' + $Mod

function Set-SrcText($path, $text) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($path, $text, $enc)
}

# ---- 1. version + changelog gate ----
$vj = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.mods.$Mod
if (-not $ver) { throw ('version.json mods has no entry: ' + $Mod) }
$modDir = Join-Path $projRoot ('SurvivalLog.' + $Mod)
$logPath = Join-Path $modDir 'CHANGELOG.md'
if (-not (Test-Path $logPath)) { throw ('missing: ' + $logPath) }
$logTxt = [IO.File]::ReadAllText($logPath)
if ($logTxt -notmatch (('##\s*v' + [regex]::Escape($ver)) + '\b')) {
    throw ($modDir + '\CHANGELOG.md has no v' + $ver + ' section. Add it first.')
}
$zip = Join-Path $relRoot ('SurvivalLog.' + $Mod + '_v' + $ver + '.zip')
if (-not $Yes) {
    $a = Read-Host ('Release ' + $Mod + ' v' + $ver + ' to ' + $repo + ' ? [Y/N]')
    if ($a -notmatch '^[Yy]') { throw 'cancelled' }
}

if ($UseExistingZip) {
    if (-not (Test-Path $zip)) { throw ('existing zip missing: ' + $zip) }
    Write-Host ('using existing artifact: ' + $zip)
} else {
    # ---- 1b. clean-tree gate: released source must be committed ----
    # allowed dirty: CHANGELOG.md + version.json (release-prep files); everything else blocks.
    $dirty = @(git -C $projRoot status --porcelain -- version.json ('SurvivalLog.' + $Mod) ModShared)
    $blocking = @($dirty | Where-Object { $_ -notmatch 'CHANGELOG\.md\s*$' -and $_ -notmatch 'version\.json\s*$' })
    if ($blocking.Count -gt 0) {
        $blocking | ForEach-Object { Write-Host ('  ' + $_) }
        throw ('uncommitted changes in SurvivalLog.' + $Mod + ' or ModShared - commit or stash first. Never release from a dirty tree.')
    }

    # ---- 2. sync version constants from version.json.mods ----
    $csproj = Join-Path $modDir ('SurvivalLog.' + $Mod + '.csproj')
    $t  = [IO.File]::ReadAllText($csproj)
    $t2 = [regex]::Replace($t, '(<Version>)[\d.]+(</Version>)', ('${1}' + $ver + '${2}'))
    if ($t2 -ne $t) { Set-SrcText $csproj $t2; Write-Host ('csproj version -> ' + $ver) }
    $plug = Get-ChildItem $modDir -Filter '*Plugin.cs' | Select-Object -First 1
    $c = $null; $c2 = $null
    if ($plug) {
        $c  = [IO.File]::ReadAllText($plug.FullName)
        $c2 = [regex]::Replace($c, '(public const string Version\s*=\s*")[\d.]+(")', ('${1}' + $ver + '${2}'))
        if ($c2 -ne $c) { Set-SrcText $plug.FullName $c2; Write-Host ($plug.Name + ' -> ' + $ver) }
    }

    # ---- 2b. surgical release commit: version stamps + release-prep files only ----
    $relFiles = @()
    if ($t2 -ne $t) { $relFiles += ('SurvivalLog.' + $Mod + '\SurvivalLog.' + $Mod + '.csproj') }
    if ($plug -and $c2 -ne $c) { $relFiles += ('SurvivalLog.' + $Mod + '\' + $plug.Name) }
    foreach ($l in $dirty) {
        $p = ($l -replace '^..\s+', '')
        if ($p -match 'CHANGELOG\.md$' -or $p -eq 'version.json') { $relFiles += $p }
    }
    $relFiles = @($relFiles | Select-Object -Unique)
    if ($relFiles.Count -gt 0) {
        git -C $projRoot add -- $relFiles
        if ($LASTEXITCODE -ne 0) { throw 'git add release files failed' }
        git -C $projRoot commit -m ('release(' + $Mod + '): v' + $ver)
        if ($LASTEXITCODE -ne 0) { throw 'git commit release failed' }
        Write-Host ('release commit: ' + ($relFiles -join ', '))
    }

    # ---- 3. build ----
    $dotnetExe = Join-Path $projRoot 'sdk\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnetExe)) { $dotnetExe = 'dotnet' }
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    Push-Location $modDir
    try { & $dotnetExe build -c Release --nologo | Out-Host }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw ('build failed: SurvivalLog.' + $Mod) }

    # ---- 4. package (reuses existing packaging incl. cfg normalization) ----
    if ($Mod -eq 'QuickAction') {
        & (Join-Path $tools 'make_quickaction_release.ps1')
    } else {
        & (Join-Path $tools 'make_mods_release.ps1') -Mod $Mod
    }
    if (-not (Test-Path $zip)) { throw ('zip missing after packaging: ' + $zip) }
}

# ---- 5. update.json contract asset (fixed asset name; spec clause 6) ----
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$gk = Get-ChildItem (Join-Path $projRoot 'gamekb') -Directory |
    Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_\d+$' } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $gk) { throw 'no gamekb snapshot dir found (expected name like 2026-10-23_26157732)' }
$updatePath = Join-Path $env:TEMP 'update.json'
$uj = @{ mod = $Mod; version = $ver; asset = (Split-Path $zip -Leaf); md5 = $md5; gamekb = $gk.Name } | ConvertTo-Json
[IO.File]::WriteAllText($updatePath, $uj, (New-Object Text.UTF8Encoding($false)))
Write-Host $uj

# ---- 6. snapshot sync this mod's repo (source at HEAD) ----
& (Join-Path $tools 'sync_repos.ps1') -Mod $Mod

# ---- 7. release notes from mod CHANGELOG ----
$notes = [regex]::Match($logTxt, ('(?s)##\s*v' + [regex]::Escape($ver) + '\b.*?(?=\r?\n(---|#{1,2}\s)|\z)')).Value.Trim()
$notesFile = Join-Path $env:TEMP ('relnotes_' + $Mod + '_v' + $ver + '.md')
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))

# ---- 8. tag + GitHub release with both assets ----
& $ghExe release create ('v' + $ver) --repo $repo --title ('v' + $ver) --notes-file $notesFile $zip $updatePath
if ($LASTEXITCODE -ne 0) { throw ('gh release create failed: ' + $repo) }
Write-Host ('=== ' + $Mod + ' v' + $ver + ' released: https://github.com/' + $repo + '/releases/tag/v' + $ver + ' ===')
Write-Host 'Nexus Mods (if desired) stays a separate author-confirmed step (docs/08). Not automated.'
```

- [ ] **Step 2: 干净树闸门负测试**

```bash
cd "E:/Game/Mod/mod_stack" && touch SurvivalLog.FridgeChill/FridgeChillPlugin.cs && powershell -NoProfile -ExecutionPolicy Bypass -File tools/release_mod.ps1 -Mod FridgeChill -Yes 2>&1 | tail -4; git checkout -- SurvivalLog.FridgeChill/FridgeChillPlugin.cs
```

期望：抛 `uncommitted changes ... Never release from a dirty tree` 后脚本退出，`git checkout --` 还原 touch（注意：touch 只改 mtime 不产生内容差异，porcelain 可能不显示——若如此，改用追加一行空注释 `echo "// gate-test" >> SurvivalLog.FridgeChill/FridgeChillPlugin.cs` 做同样测试并还原）。当前工作树 EndlessGuard 本就带作者 WIP，`-Mod EndlessGuard` 同样应被闸门拒绝——这正是设计意图，顺带验证一次即可。

- [ ] **Step 3: 提交**

```bash
git add tools/release_mod.ps1
git commit -m "feat(release): 单mod发版流水线 release_mod.ps1——版本闸门/干净树校验/外科手术release提交/复用打包/update.json契约资产/同步后建Release"
```

---

### Task 7: 试点 QuickAction 全链路（建仓 + 同步 + 首 Release）

**Files:** 无仓库文件改动（操作性任务；脚本已由 Task 5/6 提交）。

**Interfaces:**
- Consumes: `sync_repos.ps1 -Create`、`release_mod.ps1 -Mod QuickAction`。
- Produces: `survivallog-mods/QuickAction` 仓 + `v1.7.0` Release（zip + update.json）——全链路样板，后续 11 仓照此复制。

- [ ] **Step 1: 建仓并同步**

```bash
cd "E:/Game/Mod/mod_stack" && powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_repos.ps1 -Mod QuickAction -Create
```

期望输出末尾：`QuickAction: pushed (sync from mod_stack @<sha>)`。

- [ ] **Step 2: 验仓内容与描述**

```bash
./tools/gh/bin/gh.exe api repos/survivallog-mods/QuickAction/contents --jq '.[].name'
./tools/gh/bin/gh.exe api repos/survivallog-mods/QuickAction --jq .description
```

期望：文件名列表 = `.gitignore, LICENSE, ModShared, README.md, SurvivalLog.QuickAction`；description 为 manifest 里的中文描述。

- [ ] **Step 3: 发版（完整流水线，非回填）**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release_mod.ps1 -Mod QuickAction -Yes
```

期望：干净树闸门通过（Task 2 已提交版本戳）→ 构建 → 打 `SurvivalLog.QuickAction_v1.7.0.zip` → update.json 打印 → sync `up to date`（快照刚推过）→ Release 创建成功。

- [ ] **Step 4: 验 Release 资产与契约**

```bash
./tools/gh/bin/gh.exe release view v1.7.0 --repo survivallog-mods/QuickAction --json tagName,assets --jq '{tag: .tagName, assets: [.assets[].name]}'
```

期望：`{"tag":"v1.7.0","assets":["SurvivalLog.QuickAction_v1.7.0.zip","update.json"]}`。

```bash
powershell -NoProfile -Command "$z=Invoke-RestMethod 'https://api.github.com/repos/survivallog-mods/QuickAction/releases/latest'; $u=$z.assets | Where-Object name -eq 'update.json'; $ujson=Invoke-RestMethod $u.browser_download_url; $zip=$z.assets | Where-Object name -eq 'SurvivalLog.QuickAction_v1.7.0.zip'; $tmp=Join-Path $env:TEMP 'qa_zip_check.zip'; Invoke-WebRequest $zip.browser_download_url -OutFile $tmp; $md5=(Get-FileHash $tmp -Algorithm MD5).Hash.ToLower(); \"update.json md5: $($ujson.md5)`nactual  md5: $md5`nmatch: $($ujson.md5 -eq $md5)`ngamekb: $($ujson.gamekb)\""
```

期望：`match: True`；`gamekb: 2026-10-23_26157732`（若 gamekb 此后新增快照则为最新目录名，属正常）。

再验 zip 内 dll 版本归位：

```bash
powershell -NoProfile -Command "Expand-Archive (Join-Path $env:TEMP 'qa_zip_check.zip') (Join-Path $env:TEMP 'qa_zip_ext') -Force; [Reflection.AssemblyName]::GetAssemblyName((Join-Path $env:TEMP 'qa_zip_ext\BepInEx\plugins\SurvivalLog.QuickAction.dll')).Version.ToString()"
```

期望：`1.7.0`。

- [ ] **Step 5: 幂等复验**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_repos.ps1 -Mod QuickAction
```

期望：`QuickAction: up to date`（不产生新提交）。

---

### Task 8: 组织主页仓 + 全量建仓同步（12 mod + 主页 = 13 仓）

**Files:**
- Create: `tools/repo_sync/profile/README.md`

**Interfaces:**
- Consumes: `sync_repos.ps1 -Create`（全量模式含 profile）。
- Produces: 组织落地页仓 `survivallog-mods/survivallog-mods`（其 README 渲染为组织主页）+ 其余 11 个 mod 仓。

- [ ] **Step 1: 写主页 README**（内容单源在总仓，UTF-8）

```markdown
# 生存日志 MOD 套件（Survival Log Mods）

《生存日志》(Survival Log, Steam AppID 4164790) 的 BepInEx 6 (IL2CPP) 插件套件——12 个独立 mod，单 DLL 部署、不改存档、不改游戏文件，卸载即还原原版。

## MOD 一览（各仓独立发版）

| MOD | 功能 | 仓库 |
|---|---|---|
| 背包扩容 BackpackExpand | 背包格子宽、高各 ×1.5（约 2.2 倍面积），负重 ×2 | [BackpackExpand](BackpackExpand) |
| 柜子容量 CabinetSize | 柜子按四档统一固定宽×高，可个性化定值 | [CabinetSize](CabinetSize) |
| 冰箱保鲜 FridgeChill | 保鲜 5 倍 → 10 倍 | [FridgeChill](FridgeChill) |
| 动作提速 QuickAction | 77 项动作逐项可调，默认 0.5 秒 | [QuickAction](QuickAction) |
| 储藏柜直达 CabinetEverywhere | 储物柜进工作台/交易/烹饪等界面页签 | [CabinetEverywhere](CabinetEverywhere) |
| 柜子负重解放 CabinetBurden | 储物家具负重无限制 | [CabinetBurden](CabinetBurden) |
| 角色专属解锁 RoleUnlock | 藤蔓种子/防御配方全角色互通 | [RoleUnlock](RoleUnlock) |
| 机器人提速 RobotSpeed | 动作对齐手速，移速/充电/续航翻倍 | [RobotSpeed](RobotSpeed) |
| MOD 管理中心 ModCenter | 游戏内面板集中开关/调参全套件 mod | [ModCenter](ModCenter) |
| 无尽守卫 EndlessGuard（囤鼠桃园） | 无尽模式永不沦陷 + 11 条后期拾遗规划 | [EndlessGuard](EndlessGuard) |
| 自选规划 PlanPick | 全部 465+ 项规划可搜可买 | [PlanPick](PlanPick) |
| 灾前加速 PreDisasterSpeed | 官方五键倍速条搬到灾前 | [PreDisasterSpeed](PreDisasterSpeed) |

每个仓库的 Release 页提供该 mod 最新版安装包（`SurvivalLog.<Mod>_v<版本>.zip`），并附带 `update.json` 版本契约（游戏内「MOD 管理中心」后续支持一键在线更新）。

> 源码仅供阅读学习；自行编译需本机安装《生存日志》并按各仓 csproj 中的 HintPath 配置游戏路径。

## 安装

- **新玩家完整安装**（含 BepInEx 基座，推荐）：使用作者的百度网盘安装器（链接见作者发布渠道）。
- **已装基座的玩家**：到对应 mod 仓库的 Release 页下载 zip，解压到游戏根目录（`Steam\steamapps\common\Survival Log`）。

## 反馈与支持

- 建议/bug：各仓库 Issues，或抖音联系作者（Night，166660371）。
- 免费分享，仅供个人使用；禁止二次分发（各仓 LICENSE）。
```

- [ ] **Step 2: 提交主页 README**

```bash
git add tools/repo_sync/profile/README.md
git commit -m "feat(repo_sync): 组织主页仓落地页内容(12 mod 链接表+安装指引+更新通道说明)"
```

- [ ] **Step 3: 全量建仓同步**

```bash
cd "E:/Game/Mod/mod_stack" && powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_repos.ps1 -Create
```

期望：11 个 mod 仓 + profile 仓依次 `pushed`（QuickAction 已存在应 `up to date`）。

- [ ] **Step 4: 验仓全景**

```bash
./tools/gh/bin/gh.exe repo list survivallog-mods --limit 20 --json name --jq '.[].name' | sort
```

期望 13 行：BackpackExpand、CabinetBurden、CabinetEverywhere、CabinetSize、EndlessGuard、FridgeChill、ModCenter、PlanPick、PreDisasterSpeed、QuickAction、RobotSpeed、RoleUnlock、survivallog-mods。

抽查两仓内容结构（应同 Task 7 Step 2 的五件套）：

```bash
./tools/gh/bin/gh.exe api repos/survivallog-mods/EndlessGuard/contents --jq '.[].name'
./tools/gh/bin/gh.exe api repos/survivallog-mods/survivallog-mods/contents --jq '.[].name'
```

期望：EndlessGuard = 五件套（.gitignore/LICENSE/ModShared/README.md/SurvivalLog.EndlessGuard）；主页仓 = LICENSE + README.md。

**注**：作者 WIP（EndlessGuardPlugin.cs / ScavengePlans.cs 未提交）不入 HEAD 快照——仓内源码 = 已提交状态，与 v1.7.0 已发布产物一致，属预期正确行为。

---

### Task 9: 其余 11 mod 首轮回填 Release（-UseExistingZip）

**Files:** 无仓库文件改动（操作性任务）。

**Interfaces:**
- Consumes: `release_mod.ps1 -UseExistingZip`（跳过构建，直接用 release\ 下现有 v1.7.0 产物——它们由已提交状态构建，与 HEAD 快照一致）。
- Produces: 11 个仓各一个 `v1.7.0` Release（zip + update.json）。

- [ ] **Step 1: 逐个回填**

```bash
cd "E:/Game/Mod/mod_stack" && for m in BackpackExpand CabinetSize FridgeChill CabinetEverywhere CabinetBurden RoleUnlock RobotSpeed ModCenter EndlessGuard PlanPick PreDisasterSpeed; do powershell -NoProfile -ExecutionPolicy Bypass -File tools/release_mod.ps1 -Mod $m -Yes -UseExistingZip || echo "FAILED: $m"; done
```

期望：11 个 `=== <Mod> v1.7.0 released: ... ===`，无 FAILED。

- [ ] **Step 2: 验全部 Release**

```bash
for m in BackpackExpand CabinetSize FridgeChill CabinetEverywhere CabinetBurden RoleUnlock RobotSpeed ModCenter EndlessGuard PlanPick PreDisasterSpeed QuickAction; do ./tools/gh/bin/gh.exe release view v1.7.0 --repo survivallog-mods/$m --json tagName,assets --jq '(.tagName) + \" -> \" + ([.assets[].name] | join(\",\"))'; done
```

期望：12 行，每行 `v1.7.0 -> SurvivalLog.<Mod>_v1.7.0.zip,update.json`。

- [ ] **Step 3: 抽验一个 update.json 的 md5**

```bash
powershell -NoProfile -Command "$z=Invoke-RestMethod 'https://api.github.com/repos/survivallog-mods/BackpackExpand/releases/latest'; $u=Invoke-RestMethod ($z.assets | Where-Object name -eq 'update.json').browser_download_url; $tmp=Join-Path $env:TEMP 'bp_zip_check.zip'; Invoke-WebRequest ($z.assets | Where-Object name -like '*.zip').browser_download_url -OutFile $tmp; 'match: ' + ($u.md5 -eq (Get-FileHash $tmp -Algorithm MD5).Hash.ToLower())"
```

期望：`match: True`。

---

### Task 10: 套件通道适配（publish.ps1 / build_installer.ps1 / installer.iss）

**Files:**
- Modify: `tools/publish.ps1:41-56`（步骤 2 覆写→校验）、`tools/publish.ps1:118-124`（步骤 8 产物名按 mod 版本）
- Modify: `tools/build_installer.ps1`（传 per-mod /D defines）
- Modify: `installer/installer.iss`（mod 组件路径版本 define 化）

**Interfaces:**
- Consumes: `version.json` `mods` 字段（Task 2）；`make_mods_release.ps1` per-mod 命名（Task 4）。
- Produces: 套件安装器发版在 mod 版本与套件版本分离后依然全绿（网盘渠道行为不变）。

- [ ] **Step 1: publish.ps1 步骤 2 改校验**

把第 41-56 行整段（`# ---------- 2. 插件版本常量同步 ...` 到该 foreach 的闭括号）替换为：

```powershell
# ---------- 2. 版本一致性校验（双轨：csproj/Plugin.cs 必须与 version.json.mods 一致；单 mod 发版由 release_mod.ps1 维护版本戳） ----------
$vj = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
foreach ($m in $suiteMods) {
    $mver = $vj.mods.$m
    if (-not $mver) { throw ('version.json mods 缺少字段: ' + $m) }
    $dir = Join-Path $projRoot ('SurvivalLog.' + $m)
    $csproj = Join-Path $dir ('SurvivalLog.' + $m + '.csproj')
    $t = [System.IO.File]::ReadAllText($csproj)
    if ($t -notmatch ('<Version>' + [regex]::Escape($mver) + '</Version>')) {
        throw ($m + '.csproj Version 与 version.json mods(' + $mver + ') 不一致——单 mod 发版走 tools\release_mod.ps1，或手动对齐后重试。')
    }
    $plug = Get-ChildItem $dir -Filter '*Plugin.cs' | Select-Object -First 1
    if ($plug -and (([System.IO.File]::ReadAllText($plug.FullName)) -notmatch ('public const string Version\s*=\s*"' + [regex]::Escape($mver) + '"'))) {
        throw ($plug.Name + ' Version 常量与 version.json mods(' + $mver + ') 不一致。')
    }
}
Write-Host '双轨版本一致性校验通过（12 mod csproj/Plugin.cs == version.json.mods）'
```

- [ ] **Step 2: publish.ps1 步骤 8 产物名按 mod 版本**

把：

```powershell
foreach ($m in @('Base') + $suiteMods) {
    $artifacts += (Join-Path $relRoot ('SurvivalLog.' + $m + '_v' + $ver + '.zip'))
}
```

替换为：

```powershell
foreach ($m in @('Base') + $suiteMods) {
    $mv = if ($m -eq 'Base') { $ver } else { $vj.mods.$m }
    if (-not $mv) { throw ('version.json mods 缺少字段: ' + $m) }
    $artifacts += (Join-Path $relRoot ('SurvivalLog.' + $m + '_v' + $mv + '.zip'))
}
```

- [ ] **Step 3: build_installer.ps1 传 per-mod defines**

整文件替换为（在原逻辑上加 per-mod defines；产物校验段保持原样）：

```powershell
$ErrorActionPreference = 'Stop'
$projRoot = 'E:\Game\Mod\mod_stack'
# 版本号唯一来源：根目录 version.json（仅作者定义，脚本只读）
$vj = Get-Content (Join-Path $projRoot 'version.json') -Raw | ConvertFrom-Json
$ver = $vj.suite
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'
if (-not (Test-Path $iscc)) { throw ('ISCC 不存在: ' + $iscc) }

Write-Host ('编译安装器 v' + $ver + ' ...')
# per-mod 版本 define（双轨：mod 包目录名 = mod 自身版本；Base 用 SuiteVer）
$defines = @('/DSuiteVer=' + $ver)
foreach ($p in $vj.mods.PSObject.Properties) { $defines += ('/DVer' + $p.Name + '=' + $p.Value) }
& $iscc @defines (Join-Path $projRoot 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) { throw ('ISCC 编译失败，退出码 ' + $LASTEXITCODE) }

$exe = Join-Path $projRoot ('release\生存日志MOD安装器_v' + $ver + '.exe')
if (-not (Test-Path $exe)) { throw ('安装器编译产物缺失: ' + $exe) }
Write-Host ('OK: ' + $exe + '  ' + [math]::Round((Get-Item $exe).Length / 1MB, 2) + ' MB')
```

- [ ] **Step 4: installer.iss define 化 mod 路径**

1) 在 `#define RelRoot` 的 `#endif` 之后追加：

```
; per-mod 版本（双轨：mod 包目录名 = mod 自身版本；由 tools\build_installer.ps1 注入 /DVer<Mod>=x.y.z）
#ifndef VerBackpackExpand
#define VerBackpackExpand "1.7.0"
#endif
#ifndef VerCabinetSize
#define VerCabinetSize "1.7.0"
#endif
#ifndef VerFridgeChill
#define VerFridgeChill "1.7.0"
#endif
#ifndef VerQuickAction
#define VerQuickAction "1.7.0"
#endif
#ifndef VerCabinetEverywhere
#define VerCabinetEverywhere "1.7.0"
#endif
#ifndef VerCabinetBurden
#define VerCabinetBurden "1.7.0"
#endif
#ifndef VerRoleUnlock
#define VerRoleUnlock "1.7.0"
#endif
#ifndef VerRobotSpeed
#define VerRobotSpeed "1.7.0"
#endif
#ifndef VerModCenter
#define VerModCenter "1.7.0"
#endif
#ifndef VerEndlessGuard
#define VerEndlessGuard "1.7.0"
#endif
#ifndef VerPlanPick
#define VerPlanPick "1.7.0"
#endif
#ifndef VerPreDisasterSpeed
#define VerPreDisasterSpeed "1.7.0"
#endif
```

2) `[Files]` 段所有**mod 组件行**（含 `Components: mod_` 的行）把 `SurvivalLog.<Mod>_v{#SuiteVer}` 替换为 `SurvivalLog.<Mod>_v{#Ver<Mod>}`（例：`{#RelRoot}\SurvivalLog.BackpackExpand_v{#VerBackpackExpand}\BepInEx\plugins\...`）。**Base 组件行（`Components: base`）保持 `{#SuiteVer}` 不动**；`MyAppVer` 仍等于 `SuiteVer`。InstallDelete 段只引用 dll/md 名（无版本号），核对后不动。

- [ ] **Step 5: 验 BOM + 冒烟（跳过回归与 GitHub，验证全链打包）**

```bash
cd "E:/Game/Mod/mod_stack"
for f in tools/publish.ps1 tools/build_installer.ps1 installer/installer.iss; do echo "$f: $(head -c 3 $f | od -An -tx1)"; done
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1 -Yes -SkipTest -SkipGithub
```

期望：三个文件 BOM 都在（`ef bb bf`）；publish 冒烟全绿——重点：步骤 2 输出"双轨版本一致性校验通过"、步骤 4 后 `release\` 下 12 个 mod zip 全为 `_v1.7.0` 命名、安装器编译成功、网盘组装目录 13 件套齐（安装器 exe + 说明 + Base + 12 mod zip）。

- [ ] **Step 6: 提交**

```bash
git add tools/publish.ps1 tools/build_installer.ps1 installer/installer.iss
git commit -m "feat(suite-channel): 套件通道适配双轨版本——publish 步骤2覆写改校验+步骤8产物名按mod版本; 安装器 per-mod /DVer 注入与路径 define 化"
```

---

### Task 11: 文档与规则同步

**Files:**
- Modify: `AGENTS.md`（硬规则 1 措辞、新增硬规则 15、常用命令）
- Modify: `README.md`（安装渠道措辞 + 组织链接）
- Modify: `docs/00-目录说明.md`（CHANGELOG 闸门条目补单 mod 口径）
- Modify: `docs/05-版本管理与发版.md`（新增"单 mod 发版"章节）
- Modify: `ModShared/README.md`（镜像仓角色注记）
- Modify: `SurvivalLog.<Mod>/DESIGN.md` ×12（迭代维护清单各补一行）

**Interfaces:** 无代码接口；这是 spec §10 的清单落地。

- [ ] **Step 1: AGENTS.md 三处**

1) 硬规则 1 改为：

```markdown
1. **版本号只由作者本人定义**。助手不得自定/自增版本号，一切以 `version.json` 为单源——`suite` 字段=安装器/套件线，`mods` 字段=单 mod 发版线（GitHub 组织通道）；CHANGELOG 可代写。
```

2) 硬规则列表末尾（规则 14 后）新增：

```markdown
15. **组织镜像仓红线**：GitHub 组织 `survivallog-mods` 下的 12 个 mod 仓是 `tools\sync_repos.ps1` 从 git HEAD 打的**发布镜像**（开发只在单仓，镜像仓永不手改）；快照源是 HEAD 而非工作树，作者 WIP 绝不入镜像。探针三件（ActionProbe/ConfigDumpProbe/ViewportProbe）、StackLimit999、CabinetExpand **绝不建仓**。单 mod 发版走 `tools\release_mod.ps1 -Mod <名>`（发版源码必须已提交，脚本有干净树闸门）；N网推送永远另走确认门（规则 14）。
```

3) "三、常用命令速查"代码块内（发版节后）追加：

```bash
# 单 mod 发版（GitHub 组织通道；版本号=version.json 的 mods 字段，作者定义）
powershell -File tools/release_mod.ps1 -Mod QuickAction
# 组织镜像仓快照同步（幂等；-Create 仅首次建仓用）
powershell -File tools/sync_repos.ps1 [-Mod <名>] [-Create]
```

- [ ] **Step 2: README.md 安装节**

把：

```markdown
安装包只在**百度网盘**分享（链接由作者在分享渠道发布），本仓库不提供安装包下载。
```

替换为：

```markdown
- **完整安装**（新玩家推荐，含 BepInEx 基座）：安装包只在**百度网盘**分享（链接由作者在分享渠道发布）。
- **单 mod 更新**：12 个 mod 在 GitHub 组织 [survivallog-mods](https://github.com/survivallog-mods) 各有独立仓库，Release 页提供最新版单 mod 安装包（`SurvivalLog.<Mod>_v<版本>.zip`，附 `update.json` 版本契约）；游戏内「MOD 管理中心」后续支持一键在线更新。
```

并在"MOD 一览"表格后追加一行：

```markdown
> 各 mod 独立仓库与更新包：**https://github.com/survivallog-mods**（组织主页有全部链接）。
```

- [ ] **Step 3: docs/00 与 docs/05**

docs/00 第 3 条末尾追加一句：`；单 mod 的更新说明在该 mod 文件夹的 CHANGELOG.md（release_mod.ps1 发版闸门）`。

docs/05 末尾新增章节：

```markdown
## 单 mod 发版（GitHub 组织通道，2026-10-28 起）

版本双轨：`version.json` 的 `suite` 只管安装器与总仓 tag；`mods` 是各 mod 独立版本线（首轮基线全 v1.7.0，QuickAction 自 2.1.2 归位）。

- **单发**：`powershell -File tools\release_mod.ps1 -Mod <名>`——CHANGELOG 闸门 → 干净树校验（仅放行 CHANGELOG/version.json 脏改动）→ 版本戳 + 外科手术 release 提交 → 构建 → 复用打包（cfg 归一规则不变）→ 生成 update.json（md5 + gamekb 快照版本）→ 镜像仓同步 → tag `v<版本>` + GitHub Release（zip + update.json 两资产）。
- **镜像仓**：组织 `survivallog-mods` 下 12 仓 + 组织主页仓；`tools\sync_repos.ps1` 从 git HEAD 快照同步（幂等），仓内布局 = mod 文件夹 + ModShared 同级（csproj 零改动）。
- **在线更新契约**（ModCenter 消费）：`releases/latest` → 资产 `update.json`（`mod/version/asset/md5/gamekb`）→ 校验 md5 → 拉 zip（直连→镜像前缀链回退）→ 已加载 dll 改名交换落盘 → 提示重启。详见 `docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md`。
- 套件安装器发版（publish.ps1）不受影响：步骤 2 改为双轨一致性校验，产物按 mod 自身版本命名。
- 旧仓 `wangk123/survivallog-mods` 现阶段照旧；弃用（横幅+archive）为后续作者触发的独立动作。
```

- [ ] **Step 4: ModShared/README.md 与 12× DESIGN.md**

ModShared/README.md 末尾新增：

```markdown
## 在镜像仓中的角色

GitHub 组织 `survivallog-mods` 的 12 个 mod 仓各携带一份 ModShared 快照（`tools\sync_repos.ps1` 从总仓 HEAD 同步）。本目录是唯一改动源；镜像仓内的副本是生成物，勿手改。
```

12 个 `SurvivalLog.<Mod>\DESIGN.md` 的"迭代维护清单"小节各追加一行：

```markdown
- 版本与发版（2026-10-28 起）：版本号单源 = 根 `version.json` 的 `mods.<本mod>` 字段（作者定义）；单 mod 发版走 `tools\release_mod.ps1 -Mod <名>`；本 mod 的 GitHub 镜像仓由 `tools\sync_repos.ps1` 从 HEAD 快照同步（详见 docs\superpowers\specs\2026-10-28-repo-split-and-online-update-design.md）。
```

- [ ] **Step 5: 提交**

```bash
git add AGENTS.md README.md docs/00-目录说明.md docs/05-版本管理与发版.md ModShared/README.md SurvivalLog.BackpackExpand/DESIGN.md SurvivalLog.CabinetSize/DESIGN.md SurvivalLog.FridgeChill/DESIGN.md SurvivalLog.QuickAction/DESIGN.md SurvivalLog.CabinetEverywhere/DESIGN.md SurvivalLog.CabinetBurden/DESIGN.md SurvivalLog.RoleUnlock/DESIGN.md SurvivalLog.RobotSpeed/DESIGN.md SurvivalLog.ModCenter/DESIGN.md SurvivalLog.EndlessGuard/DESIGN.md SurvivalLog.PlanPick/DESIGN.md SurvivalLog.PreDisasterSpeed/DESIGN.md
git commit -m "docs: 拆仓与在线更新通道规则同步——AGENTS 硬规则1/15+命令速查; README 安装渠道; docs05 单发章节; ModShared 镜像角色; 12×DESIGN 迭代清单"
```

**注**：其中多份 README/DESIGN 当前带作者未提交改动——本任务对其**只追加本任务的新内容**，提交按上面清单 add 整文件（文档同步的预期例外：作者的 README/DESIGN 现有改动会随之入库；若执行时作者在场且反对，改为只改不提交，留给作者自行提交）。

---

### Task 12: 终验清单（全景核验）

**Files:** 无（操作性任务）。

- [ ] **Step 1: 仓全景**

```bash
./tools/gh/bin/gh.exe repo list survivallog-mods --limit 20 --json name --jq '.[].name' | sort | wc -l
```

期望：`13`。

- [ ] **Step 2: 12 Release 全在且资产齐**

```bash
for m in BackpackExpand CabinetSize FridgeChill QuickAction CabinetEverywhere CabinetBurden RoleUnlock RobotSpeed ModCenter EndlessGuard PlanPick PreDisasterSpeed; do ./tools/gh/bin/gh.exe release view v1.7.0 --repo survivallog-mods/$m --json assets --jq '([.assets[].name] | join(\",\"))'; done
```

期望：12 行全部 `SurvivalLog.<Mod>_v1.7.0.zip,update.json`。

- [ ] **Step 3: 同步幂等（全量重跑零提交）**

```bash
cd "E:/Game/Mod/mod_stack" && powershell -NoProfile -ExecutionPolicy Bypass -File tools/sync_repos.ps1 2>&1 | grep -c "up to date"
```

期望：`13`（12 mod + profile 全部 up to date，无 pushed 行）。

- [ ] **Step 4: 版本一致性终验**

```bash
cd "E:/Game/Mod/mod_stack" && node -e "const v=require('./version.json');console.log('suite',v.suite,'| mods',Object.keys(v.mods).length,'entries')"
```

期望：`suite 1.7.0 | mods 12 entries`。

- [ ] **Step 5: 向作者汇报**

汇总：13 仓清单 + 12 Release 链接 + 试点链路验证结果（md5 match / dll 版本 1.7.0 / 幂等）+ 套件通道冒烟结果 + 遗留事项（作者 WIP 未入镜像属预期；旧仓弃用等作者触发；ModCenter 在线更新功能为下一个立项，契约已冻结于 spec §7）。
