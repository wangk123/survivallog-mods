# ModCenter 菜单锚点离线定位结论（Task 1 / 18）

> 全程离线静态分析（未启动游戏）。快照：`tools/webui_src/`（2026-10-17 基线，t=1.1.18902）
> 与 `gamekb/2026-10-23_26157732/webui/`（当前版本镜像）——两处 MainUI.html / OutSetting.html
> 已 md5 核对**逐字节相同**，行号通用。下文行号如无特别说明均指这两份同内容文件。
> 消费方：Task 5（游戏内注入器）。本文件是后续任务的锚点单源，改动挂钩点须同步本文。

## 结论速览

| 入口 | 页面（iframe id / pageId） | 容器选择器 | 按钮类 | 追加方式 |
|---|---|---|---|---|
| 主菜单 | `MainUI` | `#app .container .menu` | `.menu-item`（内层 `.bar` + `.menu-text`） | 尾部 appendChild（推荐）或 insertBefore 设置项 |
| 游戏内 ESC 菜单 | `OutSetting` | `#app .left-panel .menu-list` | `.main-btn` | **insertBefore `.btn-restart`/`.btn-exit`**（禁止插在两者之间） |

游戏内锚点**已找到且结构稳定**（OutSetting 即 ESC 暂停菜单页），无需降级右上角按钮。
spec §4.1 的降级路径仅作为 OutSetting 结构变更时的后备。

## 一、主菜单锚点（MainUI）

源文件：`tools/webui_src/UI/MainUI/MainUI.html`（2941 行）。

- Vue 挂载：`<body class="enter-anim">`(1675) → `<div id="app">`(1677)，`createApp({...}).mount('#app')`(2415-2417)。
  **注意**：页面头部有 `__renderFactory__`(698) + `Vue.createApp` 猴补丁(1670)，挂载时 `#app`
  内容会被 render 函数输出**整体替换**——运行时 DOM 与 body 内模板(1733-1744)结构相同，
  但引导脚本不能假设 body 里的静态模板就是活 DOM，必须等 Vue 挂载后再找 `.menu`。
- 菜单容器链：`.container`(1724) → `.header`(1725-1731, SURVIVAL LOG 标题) + `.menu`(1733)。
  `.menu` 是 flex 纵向列表、`gap:16px`（CSS 第 72 行）。
- 菜单项（`.menu-item`，内层结构固定为 `.bar` + `.menu-text` + 可选 `.menu-sub`/`.menu-cond`），
  逐行证据（模板区 1734-1743，render 区 796-860 同构）：

| 行号 | data-i18n | 中文文案 | 备注 |
|---|---|---|---|
| 1734 | `continue` | 继续生存 | `:class="{primary: state.showContinue}"` |
| 1735 | `newGame` | 新生存 | |
| 1736 | `pureEndless` | 无尽模式 | locked/just-unlocked 状态类 |
| 1739 | `challenge` | 极限挑战 | `v-if="SHOW_CHALLENGE_ENTRY"` 默认不渲染 |
| 1740 | `casual` | 继续生活 | `v-if="state.canCasual"` 条件行 |
| 1741 | `loadGame` | 读取存档 | |
| 1742 | `settings` | 设置 | **推荐插入参照物**（恒存在） |
| 1743 | `exit` | 退出 | 列表末项 |

- 点击模式：官方项均为 `onclick="animateClick(this)"`（行内字符串，C#/JS 侧按内容分发）。
  ModCenter 入口按钮**不要**照抄 animateClick 之外的任何分发假设，自管 onclick 即可。
- **追加方式（二选一，均已核对 CSS 无相邻/序数选择器副作用）**：
  1. 推荐：`.menu` 尾部 `appendChild`（排在「退出」之后）。`.menu-item:nth-child(1..6)` 只设
     入场延迟 `--enter-delay`（CSS 128-135），第 7+ 项落到 136 行的 `var(--enter-delay, .35s)`
     兜底，视觉无害；可选给按钮内联 `style="--enter-delay:.83s"` 对齐节奏。
  2. 备选：`menu.insertBefore(btn, menu.querySelector('.menu-item[data-i18n="settings"]'))`
     （落在「读取存档」与「设置」之间）。settings 项恒在、Vue 不会移动它，插在它前面安全。
- **禁止按文案匹配**：所有文案走 i18n（`data-i18n` + LocalizationMsg 运行时替换，如 MainUI 2267 行），
  跨语言环境文案会变；结构选择器与 `data-i18n` 属性才是稳定身份。
- Vue 安全性：往 vdom 管辖容器追加"非受管节点"在本仓库已有生产先例
  （PlanPick page.js 331-343 往 `.footer-right` 首位 insertBefore，线上无回归）。菜单 v-if
  行（casual/challenge）翻转时 Vue 只按各自锚点插拔自己的节点，尾部/前置的非受管节点不受影响。

## 二、游戏内锚点：ESC 暂停菜单 = OutSetting 页（找到，不降级）

源文件：`tools/webui_src/UI/OutSetting/OutSetting.html`（1417 行）。

- 判定依据（为什么它就是 ESC 菜单）：
  - 菜单头 `<div class="menu-header" data-i18n="menuTitle">PAUSE</div>`（866 行）；
  - 列表项含「返回游戏(869)/设置(873)/图鉴(877)/成就(880)/保存并退出(895)」——标准暂停菜单；
  - C# 侧 `Reducer_Web_OutSetting`（gamekb il2cpp/dump.cs 194322）有
    `RA_Back/RA_OpenSettings/RA_Codex/RA_Achievement/RA_SaveExit/RA_RestartRound` 全套动作；
  - OutSetting.html 1351-1352 注释「ESC 由 Unity 侧处理（页面收不到按键）…设置窗口没开时
    Unity 直接关暂停菜单」——ESC 只负责开/关这页，页面本身即暂停菜单 UI；
  - 91 个 WebUI 页（docs/06 §二）中无其它 PauseMenu/SystemMenu/EscMenu 候选页
    （grep `暂停|PauseMenu|SystemMenu|EscMenu|继续游戏` 全 UI 目录，仅命中文案/注释，无页面）。
- 结构链：`<div id="app">`(863) → `.left-panel`(865) → `.menu-header`(866) + `.menu-list`(868) + `.icon-bar`(900)。
- 列表项 `.main-btn`（869-897，render 区同构）：
  - 869 `data-i18n="back"` 返回游戏；873 `settings` 设置；877 `codex` 图鉴；880 `achievement` 成就；
  - 882-887 `.wishlist-container`（`display:none` 隐藏占位）；
  - 891-893 `.main-btn.btn-restart` 作废这一轮重来，`v-if="state.restartRoundVisible"`
    （由 WebUI_OutSetting_MenuMsg 推送，1093 行）；
  - 895-897 `.main-btn.btn-exit` 保存并退出。
- **追加方式（有硬约束）**：
  - `.btn-exit` 靠 `margin-top:auto` 沉底（CSS 594-598），`.btn-restart` 同为 `margin-top:auto`
    （600-603），且 617 行 `.btn-restart + .btn-exit { margin-top: 0 }` 是**相邻兄弟选择器**——
    **绝对禁止插在 restart 与 exit 两者之间**（会打断该规则、退出键弹回中间）。
  - 正确姿势：`menuList.insertBefore(btn, menuList.querySelector('.btn-restart, .btn-exit'))`
    ——落点在「成就」之后、沉底组之前；restart 行 v-if 消失时锚点自动退化为 `.btn-exit`，仍安全。
- 该页同样是 `__renderFactory__` + `mount('#app')`（10/458/1389 行），运行时 DOM 与 863-897 模板同构。
- 后备（仅当未来版本结构变更）：降级右上角自绘按钮（spec §4.1 允许）。

## 三、Root → iframe 定位手法（按页面名找 document）

源文件：`gamekb/2026-10-23_26157732/webui/Root.html`（533 行；tools/webui_src 版仅差 3 处注释文字
（diff 实测 26/86-87 行，均为注释），代码与行号一致，引用行号两版通用）。

- 注入通道（照抄先例）：C# 侧 `ReduxUISystem.GetWebUILayer().canvasWebViewPrefab.WebView`
  （Vuplex `IWebView`）`ExecuteJavaScript(引导脚本)` ——脚本落在 **Root 文档**里跑。
  先例：`SurvivalLog.CabinetEverywhere/TagFilter.cs` 64-76（GetWebView）、47（ExecuteJavaScript）、
  78-83（同源探针：`document.querySelector('#iframe-container iframe')` + `contentDocument`）。
- iframe 结构（Root.html 实证）：
  - `#app` 内唯一容器 `<div id="iframe-container">`（模板 63 行，render hoisted 54 行）；
  - 每页一个 iframe：`createElement('iframe'); frame.id = pageId; frame.className = 'page-frame';
    iframe-container.appendChild(frame); frame.src = url`（492-496 行，watch(state.pages) 483-523）；
  - 协议 typeId 4=LOAD_PAGE（273-277 → loadPage 134-173）、5=HIDE、6=SHOW、7=REMOVE（278-289）、
    8=向指定页 postMessage（290-299）、11=PAGE_FAILED（243-249）、13=单页淡入淡出（300）；
  - 页面卸载即 iframe 移除（516-521）；加载完成前 `visibility:hidden`（30/216-218/504-506），
    可见性与注入无关，但**注入后需等页面自报 PAGE_READY 才看得见，属正常**。
- **iframe id = C# pageId = WebUIVm.UIKey**（dump.cs：`WebUIVm` 抽象 `get_UIKey`/`get_Url`，
  251086 起；`WebUI_MainUI` 301746、`WebUI_OutSetting` 306279；字符串字面量 `"MainUI"`
  (stringliteral.json 100179)、`"OutSetting"`(30607)、url `"UI/MainUI/MainUI.html"`(135331)、
  `"UI/OutSetting/OutSetting.html"`(135643)）。
- **匹配策略：pathname 优先、id 兜底**。反例实锤：CabinetEverywhere DESIGN.md 36 行
  「无人机交易 | Trade（不是 TradeUI）」——pageId 不总等于目录名。推荐判定：
  ```js
  function isPageDoc(f, tail) {                    // tail 如 '/UI/MainUI/MainUI.html'
    try { return !!(f.contentDocument && f.contentDocument.location.pathname.endsWith(tail)); }
    catch (e) { return false; }                     // 跨域/未加载：contentDocument 抛异常
  }
  document.querySelectorAll('#iframe-container iframe').find(f => f.id === 'MainUI' || isPageDoc(f, '/UI/MainUI/MainUI.html'))
  ```
  （file:// 同源同目录，contentDocument 直通；TagFilter 探针 78-83 已实证，tagfilter.page.js
  还带 xorigin 计数兜底。url 带 `?t=` cache-buster（Root.html GetCacheBuster 引用 + dump.cs
  2D4FB90），故用 pathname 而非 href 全等。）
- **引导脚本骨架（照抄 tagfilter.page.js 858-900 已验证形态）**：
  - `scanFrames()`：`querySelectorAll('#iframe-container iframe')` 全量扫 + 摘除已断链项（889-898）；
  - `MutationObserver(scanFrames).observe(document.body, {childList:true, subtree:true})` 常驻
    （899 行）——OutSetting 这类开关页的 iframe 增删全靠它捕捉；
  - `hookFrame(el)`：`el.contentDocument` 轮询（150ms 直到 `doc.body.firstChild`）+ `el` 的
    `load` 事件重挂（页面 reload 时旧观察链作废重来，862-886 行）；
  - 注入按钮**幂等**：先 `doc.getElementById('mc-entry-main')` 判在再插（PlanPick `entryPending`
    同款，page.js 317/331-343）。
- 页内注入定位到容器后，按钮 onclick 里操作的都是该 iframe 的 `contentDocument`（不是 Root 文档）。

## 四、给 Task 5 的最小检查单

1. Root 注入引导脚本（ExecuteJavaScript → Root 文档），MutationObserver 扫 `#iframe-container iframe`；
2. pathname/id 命中 `MainUI` → 等 `.menu` 出现 → 幂等 appendChild `.menu-item` 风格入口；
3. 命中 `OutSetting` → 等 `.menu-list` → insertBefore `.btn-restart, .btn-exit`（勿插两者之间）；
4. 两处按钮均加自管 onclick + 固定 id（幂等判据），勿按文案/i18n 文本匹配；
5. 结构变更的后备：OutSetting 失稳 → 降级右上角自绘按钮（spec §4.1）。

## 五、证据文件清单

| 文件 | 关键行 |
|---|---|
| `tools/webui_src/UI/MainUI/MainUI.html`（= gamekb 镜像同 md5） | 72/128-136（CSS）、698/1670/2415-2417（Vue 挂载）、1677（#app）、1724/1733-1744（菜单 DOM）、796-860（render 同构） |
| `tools/webui_src/UI/OutSetting/OutSetting.html`（= gamekb 镜像同 md5） | 594-617（沉底 CSS）、863-897（暂停菜单 DOM）、1351-1352（ESC 归属注释）、10/458/1389（Vue 挂载） |
| `gamekb/2026-10-23_26157732/webui/Root.html` | 54/63（iframe-container）、134-173/273-277（LOAD_PAGE）、216-218/483-523（iframe 增删/可见性）、492-496（id=pageId） |
| `gamekb/2026-10-23_26157732/il2cpp/dump.cs` | 194322（Reducer_Web_OutSetting）、251086（WebUIVm.UIKey/Url）、301746（WebUI_MainUI）、306279（WebUI_OutSetting） |
| `gamekb/2026-10-23_26157732/il2cpp/stringliteral.json` | 135331/135643（页面 url）、30651/30607/100179（pageId 字面量） |
| `SurvivalLog.CabinetEverywhere/TagFilter.cs` | 64-76（GetWebView）、47（注入）、78-83（同源探针） |
| `SurvivalLog.CabinetEverywhere/tagfilter.page.js` | 506（iframe#BackpackUI 实测）、858-900（scanFrames/hookFrame/MutationObserver） |
| `SurvivalLog.CabinetEverywhere/DESIGN.md` | 26-36（页面 id 实测表 + Trade≠TradeUI 反例） |
| `SurvivalLog.PlanPick/page.js` | 317/331-343（幂等 insertBefore 先例） |
| `docs/06-WebUI调试通道与源码地图.md` | 64-71（91 页清单与目录结构） |
