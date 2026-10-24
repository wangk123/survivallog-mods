# AGENTS.md —— 代理操作手册

> 给在本仓库工作的 AI 代理（Codex/Cursor/Claude/ZCode 等）的操作须知。
> 人读入门请走 [README.md](README.md)；机制细节见 `docs/`；本文只放"动手前必须知道的"。

## 一、硬规则（违反即事故，先读这里）

1. **版本号只由作者本人定义**。助手不得自定/自增版本号，一切以 `version.json` 的 `suite` 字段为单源；CHANGELOG 可代写。
2. **工作树常带作者未提交改动**。提交必须外科手术式：`git add <明确文件清单>`，**禁止 `git add -A` / `git add .`**；对作者已脏（有未提交改动）的文件只能改内容、不得擅自 stage 整文件。
3. **游戏单实例红线**：作者正在玩（SurvivalLog.exe 在跑且内存活跃）时，**绝不等待、绝不杀进程**，任务整体中止择时重试。杀游戏后必须回读 tasklist 确认死透再操作文件（`sleep 3`）。注意 `tasklist | grep` 在 GBK 中文控制台输出不可靠，判断进程看原始输出。
4. **游戏启动方式**：`powershell Start-Process 'steam://rungameid/4164790'`（直接 start exe 会被 Steamworks 转拉导致插件哑火）；表就绪约在主菜单后 15-25 秒。
5. **探针 ≠ 玩家 mod**：`SurvivalLog.ActionProbe` / `ViewportProbe` / `ConfigDumpProbe` 是取证探针，**绝不能进** publish.ps1 的 `$suiteMods` 白名单、安装器 [Files] 与 InstallDelete 名单。
6. **版权红线**：`gamekb/`（游戏 dump/表数据/素材镜像）整目录不入库（.gitignore 已锚定 `/gamekb/`）；他人 N 网 mod 禁止转载分发；`StackLimit999` 有未修复缺陷**禁止发布**。
7. **PowerShell 5.1 编码坑**：.ps1 一律 **ASCII-only**（无 BOM 的 ps1 含中文会乱码/替换不命中）；复杂 node 片段写 .js 文件执行，不 inline 进 bash（反斜杠/括号会被吃）。
8. **构建判退出码**：`"sdk\dotnet\dotnet.exe" build <目录> -c Release > log 2>&1; echo $?`——**禁止**用 `| tail` 管道（退出码被 tail 吃掉，构建失败照样部署旧 dll 的实坑）。
9. **BepInEx 日志强杀不落盘**：插件验证一律用自证文件（trace / `_DONE.txt`），别指望 LogOutput.log。
10. **UI 修复必须先实机/浏览器实测再报"修好"**；同题两轮不过换道，不 promises "最后一次"。
11. **改 mod 必须同步该 mod 的 `DESIGN.md`**（每份末尾有迭代维护清单）；**改挂钩点（Harmony 目标/表字段/字节签名）必须同步 `tools/gamekb/moddeps.json`**。
12. **实证优先**：不猜先查（反汇编/读源码/运行时探针），方案确认后再动手，最小侵入。
13. **外部工具装卸可能拆注入链**（2026-10-24 xmod 卸载实锤拆了 winhttp/dotnet/BepInEx core，见 docs/03 环境事故节）：任何 xmod/修改器/loader 安装或卸载后、以及"mod 全部失效"时，先验三件套——根目录 `winhttp.dll`+`doorstop_config.ini`、`dotnet\` 有货、`BepInEx\core` 有 dll；被拆即重跑套件安装器（勿 /SILENT）。

## 二、环境与路径（本机开发环境）

| 项 | 路径 |
|---|---|
| 仓库 | `E:\Game\Mod\mod_stack` |
| 游戏本体 | `E:\SteamLibrary\steamapps\common\Survival Log`（AppID 4164790，Unity 6000.2.7f2，IL2CPP，Steam 注册表 HKCU:\SOFTWARE\Valve\Steam → libraryfolders.vdf 定位） |
| 存档 | `C:\Users\<user>\AppData\LocalLow\LLS\SLGame\Saves\` |
| 注入链 | 游戏根目录 `winhttp.dll`(Doorstop) + `dotnet\` + `BepInEx\`（约 138MB）；Steam 重装/校验不会清除 |
| mod 部署 | 拷 dll 到 `<游戏>\BepInEx\plugins\`（interop 由 BepInEx 在游戏更新后自动重生，重生需一次启动） |
| 游戏资料库 | `gamekb\<日期>_<buildid>\`（本地留存：il2cpp dump 三件套 / binaries / webui 镜像 / tables 表数据 / meta.json / diff 报告） |

## 三、常用命令速查

```bash
# 构建（判退出码，见硬规则 8）
"E:/Game/Mod/mod_stack/sdk/dotnet/dotnet.exe" build SurvivalLog.<Mod名> -c Release > build.log 2>&1; echo $?

# 游戏更新后一键同步（快照+diff+兼容核对+[-AutoProbe]启动采表）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/gamekb/gamekb_sync.ps1 [-AutoProbe]

# 全套件依赖兼容核对（红=缺方法/字段/签名异常；全绿 exit 0）
node tools/gamekb/modcompat_check.js gamekb/<版本目录>

# 两版 dump 结构对比（剥 RVA/TypeDefIndex 噪声；--moddeps 取依赖清单做关注列表）
node tools/gamekb/dumpdiff.js gamekb/<旧>/il2cpp/dump.cs gamekb/<新>/il2cpp/dump.cs [--moddeps tools/gamekb/moddeps.json]

# 原生字节签名静态验证（如 FridgeChill 天赋上限补丁）
node tools/gamekb/sigscan.js "E:/SteamLibrary/steamapps/common/Survival Log/GameAssembly.dll"

# 表数值查询：直接读 gamekb/<版本>/tables/Config_<表名>.json（125 表；5 张非 int 键表仅行数）

# 发版（作者定号后）
powershell -File tools/publish.ps1
```

## 四、两条核心工作流

**游戏更新后**（docs/07 §5/§8）：
`gamekb_sync.ps1` → 过目 `diff_from_*.md`（类型增删/变更 + modcompat 结果）→ modcompat 全绿 → 实机启动一次看各 mod 自证"生效" → 有红灯才改代码 → 走发版。

**发版**（docs/05）：
作者在 version.json 定号 → CHANGELOG 补该版小节 → `publish.ps1`（校验→构建→打包→安装器→回归→打 tag→GitHub Release 仅说明）。

## 五、文档地图

| 文档 | 内容 |
|---|---|
| `docs/00-目录说明.md` | 文档维护规则、mod 清单口径 |
| `docs/01/02` | 需求/技术设计 |
| `docs/03-教训与红线.md` | 历史事故清单（v1.3.2 等） |
| `docs/04/05` | 版本历史 / 版本管理与发版机制（渠道分工） |
| `docs/06-WebUI调试通道与源码地图.md` | DevTools 9222 白盒调试、WebUI 源码位置 |
| `docs/07-游戏资料库gamekb.md` | gamekb 设计与机制（同步流程/探针规格/依赖核对） |
| `SurvivalLog.<Mod>\DESIGN.md` | 各 mod 设计文档（含迭代维护清单） |
| `ModShared\README.md` | 共享源码（防盗署名气泡接线规则） |
| `tools/gamekb/README.md` | gamekb 工具集用法 |

## 六、gamekb 注意事项（2026-10-23 起）

- 游戏侧 `BepInEx\gamekb_tables\<资源版本>\` 是探针的 **_DONE 哨兵目录，勿删**（删了下次启动会重新全量 dump 一遍）；表采集靠 sync 移走整个目录。
- Il2CppDumper 的输出**落在 exe 所在目录**（`tools/il2cppdumper/`，非第三参）；sync 已做防覆盖（exe 目录有未认领产物即拒绝执行）。
- 逆向工具链（Iced 反汇编、E8 调用方扫描、字面量 xref、mdump 等）用法与坑见 docs/06 与工具源码头注释。
