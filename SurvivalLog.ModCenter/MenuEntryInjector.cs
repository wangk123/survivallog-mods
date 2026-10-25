using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 菜单入口注入器（任务 5；实机 UI 三修改事件驱动常驻）。Root 上下文一次性安装
/// MutationObserver（观察 body 子树，含 #iframe-container——观察 body 而非容器节点本身：
/// Root.html 的 Vue mount 会用 hoisted 克隆替换 #app 内容，容器节点身份会换，body 不会，
/// tagfilter.page.js:899 同款），回调 + iframe load 钩当场跑幂等 install（找 MainUI/OutSetting
/// iframe、锚点插入口）——菜单 iframe 一插入/一加载完成按钮当场出现（同帧量级），
/// 不再依赖 C# 轮询节奏。C# 侧 2s 慢拍保留但降级为自愈（TagFilter.cs:46-50 免维护语义不变）：
/// 每拍只发极小守卫表达式（observer 在 + 面板已装 + 双锚点入口未被抹除），失守才重发完整
/// 引导（WebView/Root 重建后下一轮自动补挂）。脚本自身幂等（__mc_entry 已在目标 DOM 则跳过；
/// __mcObserver 防重装）。两锚点（单源 ANCHORS.md，改挂钩点须同步该文件与 tools/gamekb/moddeps.json）：
/// - 主菜单 MainUI：`#app .container .menu` 尾部 appendChild，克隆官方 `.menu-item`（内层 .bar+.menu-text）；
/// - 游戏内 ESC 暂停菜单 OutSetting：`.menu-list` insertBefore `.btn-restart,.btn-exit`
///   （617 行相邻兄弟规则 `.btn-restart + .btn-exit`，绝对禁止插在两者之间）。
/// 入口按钮一律 <button> + data-interactive：目标页 webui-core.js clickThrough 清单为
/// `.unity-interactive, button, input, select, a, [data-interactive]`，命中矩形经 Root 以协议 9
/// 同步给 Unity，实机点击才落进 WebView（CabinetEverywhere 自绘下拉同款依赖）。
/// 面板 DOM（panel.js）因此也挂在入口所在 iframe 文档内——Root 文档自身不跑 clickThrough
/// （Root.html 实证：syncInteractiveRects 只收子页 postMessage），挂 Root 的按钮收不到鼠标。
/// </summary>
internal static class MenuEntryInjector
{
    private static readonly HashSet<string> Warned = new();
    private static string _panelJs;
    private static bool _readyLogged;

    /// <summary>Ticker 心跳入口（2s 慢拍，已降级为自愈守卫）：极小表达式校验 Root 侧常驻设施（observer/面板/双锚点入口）在线，失守才重发完整引导（幂等）→ 面板 outbox 轮询（任务 6 起由 ConfigBridge 消费，节奏不变）。入口即时性由 Root 侧 Observer+load 钩承担（EntryJs），与本拍无关。</summary>
    internal static void Tick(Vuplex.WebView.IWebView web)
    {
        try
        {
            if (!_readyLogged)
            {
                _readyLogged = true;
                ModCenterPlugin.Log("WebView 通道就绪，入口注入改事件驱动常驻（Observer+load 钩当场安装），2s 慢拍降级为守卫自愈");
            }
            _panelJs ??= ReadPanelJs();
            // 实机 UI 三修·问题1：不再每拍全量重发注入脚本。守卫三分量任一失守（WebView 重建后
            // 全 false；运行中入口被页面异常抹除则 __mcSelfCheck false）才重发完整引导——
            // panel.js __McInstalled 与 EntryJs __mcObserver 双守卫保证重发本身幂等。
            var guard = "window.__mcObserver && window.__McInstalled && (!window.__mcSelfCheck || window.__mcSelfCheck())";
            WebBridge.Exec(web, string.IsNullOrEmpty(_panelJs)
                ? "if(!(" + guard + ")){" + EntryJs + "}"
                : "if(!(" + guard + ")){" + _panelJs + "\n" + EntryJs + "}");
            DrainOutbox(web);
        }
        catch (Exception e)
        {
            WarnOnce("tick", "注入巡检异常: " + e.Message);
        }
    }

    /// <summary>
    /// 面板 outbox 轮询：取走 JS 侧待发消息数组。任务 5 只 Log；任务 6 起入队 ConfigBridge
    /// （回调线程只入队，分发在 Ticker 主线程 Pump 执行——cfg 读写线程纪律见 DESIGN §4.2）。
    /// </summary>
    internal static void DrainOutbox(Vuplex.WebView.IWebView web)
    {
        WebBridge.Eval(web,
            "JSON.stringify(window.__mcDrain?window.__mcDrain():[])",
            r =>
            {
                if (string.IsNullOrEmpty(r) || r == "[]") return;
                ModCenterPlugin.Log("面板消息: " + r);
                ConfigBridge.EnqueueOutboxJson(r);
            });
    }

    private static string ReadPanelJs()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("SurvivalLog.ModCenter.panel.js");
            if (s == null) { WarnOnce("res", "内嵌资源 panel.js 缺失"); return ""; }
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch (Exception e) { WarnOnce("res", "panel.js 读取失败: " + e.Message); return ""; }
    }

    private static void WarnOnce(string key, string msg)
    {
        if (Warned.Add(key))
            ModCenterPlugin.Log("warn " + msg);
    }

    /// <summary>
    /// Root 上下文引导脚本（实机 UI 三修·问题1 改事件驱动常驻）：
    /// ① install()＝原有幂等双锚点入口安装逻辑（pd 扫 #iframe-container iframe，pathname 优先 /
    /// id 兜底，ANCHORS.md §三，NodeList 无 .find 用 for 遍历；双锚点幂等插入口按钮 + 每页一次性
    /// style + ESC 转发 wireKey，全部照旧）；
    /// ② hookLoads()＝给目标页 iframe 挂 load 事件钩（元素属性防重挂）——iframe 插入时
    /// contentDocument 尚是 about:blank，锚点要等页面加载/挂载后才在；load 一到当场 install；
    /// ③ MutationObserver 观察 document.body 子树（含 #iframe-container 增删；观察 body 而非
    /// 容器节点本身：Root.html 的 Vue mount 会克隆替换 #app 内容，容器节点身份会换、body 不会，
    /// tagfilter.page.js:899 同款）——回调 hookLoads+install，菜单 iframe 一插入当场处理；
    /// ④ __mcObserver 幂等防重装；__mcSelfCheck 供 C# 2s 慢拍守卫（锚点已在而入口被抹 → false，
    /// 触发完整引导重发，保住旧免维护自愈语义）。点击闭包在 Root realm 执行，
    /// window.__mcOpen 即 Root 契约函数；按钮元素归属目标 iframe 文档。
    /// </summary>
    private const string EntryJs = @"
(function () {
  function pd(tail, id) {
    var fs = document.querySelectorAll('#iframe-container iframe');
    var byId = null;
    for (var i = 0; i < fs.length; i++) {
      try {
        var d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) return d;
      } catch (e) {}
      if (!byId && fs[i].id === id) {
        try { if (fs[i].contentDocument) byId = fs[i].contentDocument; } catch (e) {}
      }
    }
    return byId;
  }
  function style(doc, css) {
    if (doc.getElementById('__mc_entry_style')) return;
    var st = doc.createElement('style');
    st.id = '__mc_entry_style';
    st.textContent = css;
    (doc.head || doc.documentElement).appendChild(st);
  }
  function wireKey(doc) {
    if (doc.__mcKeyWired) return;
    doc.__mcKeyWired = true;
    doc.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && window.__mcEsc) window.__mcEsc(e);
    });
  }
  function install() {
  /* 锚点1 主菜单 MainUI：.menu 尾部 appendChild（排在「退出」后，CSS 无相邻/序数副作用，ANCHORS §一） */
  var d1 = pd('/UI/MainUI/MainUI.html', 'MainUI');
  if (d1 && !d1.getElementById('__mc_entry')) {
    var menu = d1.querySelector('#app .container .menu') || d1.querySelector('.menu');
    if (menu) {
      style(d1, '#__mc_entry{background:none;border:0;font:inherit;color:inherit;appearance:none;outline:none;}'
        + '#__mc_entry .bar{background:linear-gradient(180deg,#7fb8e8,#2f6ea8);box-shadow:2px 2px 5px rgba(0,0,30,0.8);}'
        + '#__mc_entry:hover .bar{background:linear-gradient(180deg,#a8d4ff,#5aa8ff);}'
        + '#__mc_entry .menu-text{color:#a9cdf5;}'
        + '#__mc_entry:hover .menu-text{color:#d6ecff;}'
        + '#__mc_entry .menu-text::after{background:linear-gradient(90deg,#5aa8ff,rgba(90,168,255,0.15));}');
      var b1 = d1.createElement('button');
      b1.id = '__mc_entry';
      b1.className = 'menu-item';
      b1.type = 'button';
      b1.setAttribute('data-interactive', '');
      b1.setAttribute('style', '--enter-delay:.83s');
      var bar = d1.createElement('div'); bar.className = 'bar';
      var tx = d1.createElement('div'); tx.className = 'menu-text'; tx.textContent = 'MOD 管理';
      b1.appendChild(bar); b1.appendChild(tx);
      b1.addEventListener('click', function () { if (window.__mcOpen) window.__mcOpen(d1); });
      wireKey(d1);
      menu.appendChild(b1);
    }
  }
  /* 锚点2 ESC 暂停菜单 OutSetting：insertBefore 沉底组（restart 行 v-if 消失时锚点自动退化为 .btn-exit） */
  var d2 = pd('/UI/OutSetting/OutSetting.html', 'OutSetting');
  if (d2 && !d2.getElementById('__mc_entry')) {
    var list = d2.querySelector('#app .left-panel .menu-list') || d2.querySelector('.menu-list');
    var anchor = list ? list.querySelector('.btn-restart, .btn-exit') : null;
    if (list && anchor) {
      style(d2, '#__mc_entry{appearance:none;outline:none;border-top:0;border-right:0;border-bottom:0;'
        + 'font-family:inherit;text-align:inherit;padding-top:0;padding-right:0;padding-bottom:0;}'
        + '#__mc_entry{border-left-color:#4f9fe0;color:#a9cdf5;}'
        + '#__mc_entry:hover{background:linear-gradient(90deg,rgba(79,159,224,0.16) 0%,transparent 100%);'
        + 'border-left-color:#6fb6ff;color:#d6ecff;text-shadow:0 0 10px rgba(111,182,255,0.5);}');
      var b2 = d2.createElement('button');
      b2.id = '__mc_entry';
      b2.className = 'main-btn';
      b2.type = 'button';
      b2.setAttribute('data-interactive', '');
      b2.textContent = 'MOD 管理';
      b2.addEventListener('click', function () { if (window.__mcOpen) window.__mcOpen(d2); });
      wireKey(d2);
      list.insertBefore(b2, anchor);
    }
  }
  }
  /* load 钩：目标页 iframe（pageId 字面量 ANCHORS §三）一加载完成当场 install。
   * MO 回调时 contentDocument 常还是 about:blank（pathname 不匹配、id 兜底拿到空文档，
   * install 扑空无害）——真正入口就位时机=load 事件（页面脚本已跑、Vue 已 mount、锚点已在）。
   * 元素属性防重挂；iframe 每次重建都是新元素，天然重新挂。 */
  function hookLoads() {
    var fs = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < fs.length; i++) {
      var f = fs[i];
      if (f.__mcLoadHooked) continue;
      if (f.id !== 'MainUI' && f.id !== 'OutSetting') continue;
      f.__mcLoadHooked = true;
      f.addEventListener('load', function () { install(); });
    }
  }
  install();
  hookLoads();
  /* 常驻 Observer：body 子树 childList（iframe 插入/移除、Vue 重挂容器全捕捉）→ 当场
   * hookLoads+install。__mcObserver 标志幂等防重装（C# 守卫与重发引导共用该判定）。 */
  if (!window.__mcObserver && document.body) {
    var obs = new MutationObserver(function () { hookLoads(); install(); });
    obs.observe(document.body, { childList: true, subtree: true });
    window.__mcObserver = obs;
  }
  /* 守卫体检（C# 2s 慢拍用）：目标页锚点已挂而入口缺席 = 被页面异常抹除 → false，
   * C# 下一拍重发完整引导重装。页面还在加载（锚点未挂）不算失守（交给 load 钩）。 */
  window.__mcSelfCheck = function () {
    try {
      var fs = document.querySelectorAll('#iframe-container iframe');
      for (var i = 0; i < fs.length; i++) {
        var d = null;
        try { d = fs[i].contentDocument; } catch (e) {}
        if (!d || !d.body) continue;
        var main = false, out = false;
        try {
          var p = d.location.pathname;
          main = p.indexOf('/UI/MainUI/MainUI.html') !== -1 || fs[i].id === 'MainUI';
          out = p.indexOf('/UI/OutSetting/OutSetting.html') !== -1 || fs[i].id === 'OutSetting';
        } catch (e) { main = fs[i].id === 'MainUI'; out = fs[i].id === 'OutSetting'; }
        if (!main && !out) continue;
        if (d.getElementById('__mc_entry')) continue;
        if (main ? d.querySelector('.menu') : d.querySelector('.menu-list')) return false;
      }
    } catch (e) {}
    return true;
  };
})();
";
}
