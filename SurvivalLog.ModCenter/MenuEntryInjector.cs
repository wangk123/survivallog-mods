using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 菜单入口注入器（任务 5）。每 2s 由 Ticker 无条件重跑（TagFilter.cs:46-50 免维护自愈模式：
/// WebView/页面 iframe 重建后下一轮自动补挂），脚本自身幂等（__mc_entry 已在目标 DOM 则跳过）。
/// 两锚点（单源 ANCHORS.md，改挂钩点须同步该文件与 tools/gamekb/moddeps.json）：
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

    /// <summary>Ticker 心跳入口：面板引导（幂等）→ 双锚点入口注入（幂等）→ 面板 outbox 轮询（任务 6 起由 ConfigBridge 消费）。</summary>
    internal static void Tick(Vuplex.WebView.IWebView web)
    {
        try
        {
            if (!_readyLogged)
            {
                _readyLogged = true;
                ModCenterPlugin.Log("WebView 通道就绪，注入巡检启动（每 2s：入口+面板幂等安装 / outbox 轮询）");
            }
            _panelJs ??= ReadPanelJs();
            if (!string.IsNullOrEmpty(_panelJs))
                WebBridge.Exec(web, _panelJs);
            WebBridge.Exec(web, EntryJs);
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
    /// Root 上下文引导脚本：扫 #iframe-container iframe（pathname 优先 / id 兜底，ANCHORS.md §三，
    /// NodeList 无 .find 用 for 遍历），双锚点幂等插入口按钮 + 每页一次性 style（蓝色 accent 区分
    /// 官方金色）+ ESC 转发（doc.__mcKeyWired 防重复挂监听）。点击闭包在 Root realm 执行，
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
})();
";
}
