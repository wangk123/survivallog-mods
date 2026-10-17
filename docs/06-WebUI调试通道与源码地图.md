# WebUI 调试通道与源码地图（2026-10-17 全链路实证）

> 结论先行：**游戏 UI 的 JS 渲染层可以从黑盒变白盒**。两条已验证的路：
> ① Vuplex 官方远程调试通道（Chrome DevTools 直连游戏内 WebView）；
> ② UI 源码本来就是明文，就在游戏目录 `StreamingAssets\WebUI\`（此前一直误判为"藏在加密 bundle 里"）。
> 本文是 UI 类 mod 的开发手册：改 UI 前先读这里，再读源码，最后才动手。

## 一、远程调试通道（DevTools）

### 1.1 怎么开

`SurvivalLog.ViewportProbe`（不发布的开发探针）带 cfg 开关：

```
BepInEx\config\com.local.survivallog.viewportprobe.cfg
[DevTools]
Enabled = true
```

开启后游戏启动，Chrome 打开 `chrome://inspect` → Devices → localhost:9222 → 对
"Root Orchestrator" 点 inspect，即得到游戏 UI 的完整 DevTools（Elements/Sources/
Console/Network/断点/Performance 全可用）。

### 1.2 原理与时机窗口（实测三态，勿凭感觉调）

API：`Vuplex.WebView.Web.EnableRemoteDebugging()`（静态；另有 `EnableRemoteDebugging(int port)`
端口重载，默认端口 9222）。interop 程序集 `BepInEx\interop\Vuplex.WebView.dll` 实证存在。

| 调用时机 | 结果（均实测） |
|---|---|
| `BasePlugin.Load()`（BepInEx chainloader） | ❌ `too early, prior to Awake()` |
| Ticker 每 0.5s 静默重试（Awake 后、Chromium 首次启动前） | ✅ **窗口命中，本项目采用** |
| WebView 已创建（Chromium 已跑）后 | ❌ `already running`；需先 `StandaloneWebView.TerminateBrowserProcess()` 杀进程再开（此补救路径**未实测**，代码保留在 ProbeTicker.TryEnableDevTools） |

实测：抢跑路径无副作用，游戏 UI 正常（MainUI 1536x864 渲染正常，探针注入照常）。
游戏启动约 55 秒后 9222 可用（Chrome/137.0.7151.104）。

### 1.3 编程化连接（CDP，可做自动化断言）

`tools\devtools_cdp_test.js`（node + playwright-core，依赖在 `C:\temp\baidu_up\node_modules`）：

```
node tools/devtools_cdp_test.js
```

已验证：`chromium.connectOverCDP('http://localhost:9222')` → 枚举 page/frame →
`page.evaluate()` 读运行时状态全部通过。**注意**：这与红线 13（屏幕坐标模拟点击不可靠）
不冲突——CDP 直连 WebView 不经过屏幕，可靠性高一个量级，可用于 DOM 断言/console 收集/
布局测量（替代反复开游戏肉眼验证）。

### 1.4 红线

- **发布版 / 分享机禁开此开关**：9222 端口对本机任意进程开放（等于游戏 UI 可被任意
  本地程序读写），仅限开发机使用。
- ViewportProbe 本身不发布，开关默认 false。

## 二、UI 源码地图（明文，无需逆向）

### 2.1 位置与结构

游戏 UI 从文件系统直读（CDP 目标 URL 实证）：
`file:///E:/.../SurvivalLog_Data/StreamingAssets/WebUI/Root.html?t=1.1.18902`

```
StreamingAssets\WebUI\
├── Root.html                  ← Root Orchestrator（Vue，所有界面的宿主）
├── webui-bag.js / webui-core.js / webui-common.css ...  ← 共享脚本（背包/消息桥等）
├── vue.global.prod.js 等      ← 第三方库
├── UI\<页面名>\               ← 91 个界面目录：BackpackUI / Cooking / TradeUI /
│                                 DroneHub / Compressor / MainUI ...（每页 html+js）
└── Res\ fonts\                ← 图片/字体资源（约 480MB，与源码无关）
```

### 2.2 归档快照

`tools\webui_src\`（111 文件 / 6.9MB，仅 html/js/css）= 2026-10-17 游戏版本
（t=1.1.18902）的基线快照，供 grep/diff/离线研究。**不入库**（游戏本体资源导出，
同 `tools/il2cpp_out/` 约定，见 .gitignore）。**游戏目录永远为准**；
游戏更新后用同命令重新归档并 diff，即可精确圈出 UI 层变更面。

```bash
WEBUI="/e/SteamLibrary/steamapps/common/Survival Log/SurvivalLog_Data/StreamingAssets/WebUI"
(cd "$WEBUI" && find . -type f \( -iname "*.html" -o -iname "*.js" -o -iname "*.css" \)) \
  | tar -cf - -T - | (cd tools/webui_src && tar -xf -)
```

### 2.3 修正记录（防再犯）

2026-10-17 之前项目内一直认为"JS 打包在 hash 命名的加密 bundle 里，只能读片段"——
**错误**。真相：448 个 bundle 是游戏资源分包（Res 等），而 WebUI 源码一直明文躺在
StreamingAssets。误判成因：`find | head -40` 输出截断，WebUI 目录恰好排在被截掉的
部分，此后无人再看。教训：**目录结论必须完整枚举后再下**。

## 三、这条通道解决什么（对应历史痛点）

| 历史痛点（十几轮验证的来源） | 现在的手段 |
|---|---|
| JS 行为靠猜（消息消费顺序/渲染时机） | Sources 断点单步 + Console 直接看；CDP evaluate 随时问 |
| JS 侧日志只有 iframe 中继+title 可靠（v1.3.2 教训） | DevTools Console 原生，console.log 全收 |
| 闪帧/竞态/焦点拉扯等时序现象黑盒 | Network/Performance 时间线；断点停帧看调用栈 |
| 视口/布局问题反复试（v15 才收敛） | Elements 实时 computed style + getBoundingClientRect |
| 每轮验证要开游戏进存档人工看 | CDP 脚本断言（DOM/状态一键检查），人只做最终验收 |

新 UI 类需求（非增强官方页）优先走"整页自带"路线（N 网三件套模式：mod 内嵌
HTML+JS，LoadHtml/ExecuteJavaScript + window.vuplex 消息桥），不再寄生官方 DOM——
见 docs\02 与记忆《N 网三件套 mod 架构学习》。

## 四、验证记录（2026-10-17）

1. interop 元数据：`NativeMethodInfoPtr_EnableRemoteDebugging_Public_Static_Void_0`
   与 `_Void_Int32_0` 两重载存在，声明类 `Vuplex.WebView.Web`（编译实证）。
2. 首试 Load() 时机 → 日志实锤 too early；改 Ticker 抢跑 → 命中窗口，9222 起服务。
3. `curl localhost:9222/json/list` → 目标 "Root Orchestrator"（file:// URL）。
4. `node tools/devtools_cdp_test.js` → pages/frames 枚举 + evaluate 全过，
   实时读到 MainUI iframe 1536x864（与视口适配战役结论一致）。
5. 全程游戏 UI 无异常；验证后游戏关闭，现场已清理（游戏机 cfg 保留 true＝开发机）。
