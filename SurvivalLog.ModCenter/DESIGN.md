# MOD 管理中心 ModCenter — 设计文档

> 本文档随源码存放在 mod 自己的文件夹内；**每次迭代改动必须同步更新本文档**
> （文档维护规则见 `docs\00-目录说明.md`）。
> 版本：跟随套件 version.json（作者定号，硬规则 1）。
> 菜单锚点单源：本目录 `ANCHORS.md`（任务 1 离线定位结论；改挂钩点必须同步该文件与
> `tools/gamekb/moddeps.json`）。当前状态：**任务 3 骨架**（构建 + 装机自证通过）。

## 1. 功能定位

套件内 mod 的统一管理面板：在主菜单（MainUI）与游戏内 ESC 菜单（OutSetting）各注入一个
入口按钮，打开 WebUI 面板，集中查看 / 开关本套件各 mod（各 mod 的 `General.Enabled`
配置 gate），替代逐个手改 cfg 文件。不改存档、不改游戏文件，删 dll 即还原。

主菜单 WebView 可达性已由任务 2 实机实证 GO；锚点选择器（`.menu` 尾部 appendChild /
`.menu-list` insertBefore `.btn-restart`）见 ANCHORS.md，**禁止按文案匹配**（全 i18n）。

## 2. 三层架构

| 层 | 代码 | 职责 |
|---|---|---|
| C# 宿主层 | `ModCenterPlugin` / `ModCenterTicker` /（任务 6 起）`ConfigBridge` | BepInEx 插件宿主。Ticker 每 2s（unscaled）心跳；各 mod Enabled 配置的读写（`SetModEnabled`）；轮询面板 outbox 执行面板命令。统一经 `ModCenterPlugin.Log(msg)` 静态门输出 |
| 注入层 | （任务 5 起）Ticker 注入巡检 | 找到主菜单 / ESC 菜单页的 WebView，注入入口按钮并装载 panel.js（内嵌资源）。锚点与装载方式见 ANCHORS.md |
| 前端面板层 | `panel.js`（EmbeddedResource，`SurvivalLog.ModCenter.panel.js`；任务 7 移植） | 面板 UI 与交互。全局名 `__mcOutbox` / `__mcDrain` / `__McInstalled` 为计划既定契约，全程一致；键盘上报任务 8 |

panel.js 作为内嵌资源随 dll 分发，不落盘、不改游戏资源文件（同 CabinetEverywhere
tagfilter.page.js 模式）。

## 3. 工程与验证约定

- 构建：`dotnet build SurvivalLog.ModCenter -c Release` 判退出码（硬规则 8，禁管道）。
- 装机自证：Load 写 `<BepInEx 根>/ModCenter_loaded.txt`（硬规则 9，不依赖 LogOutput.log；
  本机绝对路径 `E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_loaded.txt`，
  代码用 `Paths.BepInExRootPath` 拼接以兼容发布到其他机器）。
- 引用结构抄 CabinetEverywhere.csproj（BepInEx core + interop 游戏目录引用，`Private=false`
  不拷贝产物）；`ModShared/WelcomeTip.cs` 链接编译（套件防盗署名气泡，publish.ps1 要求）。

## 4. 迭代维护清单

（暂无——任务 3 首版骨架。后续每次迭代在此追加日期 + 要点。）
