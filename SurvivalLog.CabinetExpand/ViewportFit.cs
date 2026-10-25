using System;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetExpand;

/// <summary>
/// 柜子界面视口适配守卫 v15（2026-10-23 滚动范式（v16~v17.5b）经作者实测否决，全线回退本版）。
///
/// - A 页（12 个自带官方适配的页面）：注入『根容器 flex-shrink:0』修复官方测量的封顶盲区
///   （大柜子页签时容器被 flex 挤压、右栏顶出屏幕而测量看不见）；欠缩时按官方同公式桥接补写
///   （按页照抄官方 margin/heightProp，页面下次重算自然收敛，无争夺）。
/// - B 页（BrewPanel/DyePanel：官方完全没接适配）：往页面注入官方 createViewportFit 的调用
///   （页面上下文，webui-bag.js 全局函数）——与 12 个稳定页面同一机制。
/// - LOCKFIT（BrewPanel/DyePanel/RatCage）：官方锁纯累计复刻 + ManagedMax 初始（打开即最大）。
/// - TrapUI 跳过（#app 是 display:contents 无可测根容器）；BackpackUI/TradeUI 永不触碰。
///
/// 滚动范式历史与教训（宽度轴遗漏/多列污染/官方锁占位/跑飞收缩五轮翻车）见 DESIGN.md §3.2-3.4
/// 与记忆 cabinet-ui-viewport-fit——结论：整页缩放（官方机制）与扩容数据的组合以官方行为为准。
/// 触发：每 2s C# ExecuteJavaScript 巡检 + #iframe-container MutationObserver + iframe load 钩子。
/// 动作日志 [CEVF] 走 Root console → Player.log（2026-10-16 游戏更新后 Root 转/iframe 不转）。
/// </summary>
internal static class ViewportFit
{
    internal static ConfigEntry<bool> Enabled;

    private static bool _announced;
    private static bool _hadWeb;

    internal static void Install(ConfigFile config)
    {
        Enabled = config.Bind("UiFit", "Enabled", true,
            "柜子界面视口适配守卫：修复大柜子页签把界面右栏挤出屏幕" +
            "（12 页让官方自测真实宽度，酿酒桶/染色台补上官方同款整体缩放）。");
    }

    internal static void Attach()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<FitTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<FitTicker>();
        var go = new GameObject("CabinetExpand.ViewportFit");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<FitTicker>();
    }

    internal sealed class FitTicker : MonoBehaviour
    {
        public FitTicker(IntPtr ptr) : base(ptr) { }
        public FitTicker() { }

        private float _check;

        private void Update()
        {
            // General 总门（2026-10-24，ModCenter 启停位）：主开关关闭即停注（优先于 UiFit.Enabled
            // ——主门关=整个 mod 停用，视口脚本立即停；重开后 2s 内恢复巡检）。
            if (CabinetExpandPlugin.Enabled != null && !CabinetExpandPlugin.Enabled.Value) return;
            if (Enabled != null && !Enabled.Value) return;
            _check += Time.deltaTime;
            if (_check < 2f) return;
            _check = 0f;
            var web = GetWebView();
            if (web == null) { _hadWeb = false; return; }
            try
            {
                web.ExecuteJavaScript(BootstrapJs);
                if (!_announced)
                {
                    _announced = true;
                    CabinetExpandPlugin.Log.LogInfo("[CabinetExpand][ViewportFit] 守卫已就绪（每 2s 巡检）");
                }
                _hadWeb = true;
            }
            catch (Exception e)
            {
                if (_hadWeb) return;
                _hadWeb = true;
                CabinetExpandPlugin.Log.LogWarning("[CabinetExpand][ViewportFit] 注入异常: " + e.Message);
            }
        }
    }

    private static Vuplex.WebView.IWebView GetWebView()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return null;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var layer = rus?.GetWebUILayer();
            var prefab = layer?.canvasWebViewPrefab;
            return prefab?.WebView;
        }
        catch { return null; }
    }

    /// <summary>
    /// <summary>
    /// 运行期 Bootstrap 属性：把受管包最大宽高（封顶后）填进注入脚本——
    /// LockFit 页的锁以它为初始值（seenC/seenR 起步于最大），实现『打开即最大柜子尺寸』
    /// （烹饪等页靠服务端真 maxCols 提示原生做到；酿酒桶/老鼠笼的提示不给力，由本 mod 供给）。
    /// Root 上下文巡检脚本 v14/v15（每 2s 由 C# ExecuteJavaScript 执行）。
    /// A 页：flex-shrink:0 样式 + 欠缩桥接；B 页：注入官方 createViewportFit 调用脚本（页面上下文）。
    /// 只用单引号、无反斜杠、注释禁半角双引号。
    /// </summary>
    internal static string BootstrapJs
    {
        get
        {
            var w = CabinetExpandPlugin.ManagedMaxW > 0 ? CabinetExpandPlugin.ManagedMaxW : 23;
            var h = CabinetExpandPlugin.ManagedMaxH > 0 ? CabinetExpandPlugin.ManagedMaxH : 18;
            return Bootstrap.Replace("var seenC=0,seenR=0;", "var seenC=" + w + ",seenR=" + h + ";");
        }
    }

    /// LockFit 页的锁以它为初始值（seenC/seenR 起步于最大），实现『打开即最大柜子尺寸』
    /// （烹饪等页靠服务端真 maxCols 提示原生做到；酿酒桶/老鼠笼的提示不给力，由本 mod 供给）。
    /// A 页：flex-shrink:0 样式 + 欠缩桥接；B 页：注入官方 createViewportFit 调用脚本（页面上下文）。
    /// </summary>

    internal const string Bootstrap = @"
(function () {
  try {
    if (!window.__CEVF) { window.__CEVF = 1; console.log('[CEVF] guard armed v15'); observeContainer(); }
    pass();
  } catch (e) {}
  function observeContainer() {
    try {
      var host = document.getElementById('iframe-container');
      if (!host || host.__CEVF_OBS) return;
      host.__CEVF_OBS = 1;
      var ob = new MutationObserver(function (muts) {
        for (var i = 0; i < muts.length; i++) {
          var add = muts[i].addedNodes;
          for (var j = 0; j < add.length; j++) {
            var n = add[j];
            if (n && n.tagName === 'IFRAME') hookLoad(n);
          }
        }
      });
      ob.observe(host, { childList: true });
    } catch (e) {}
  }
  function pass() {
    try {
      var fr = document.querySelectorAll('#iframe-container iframe');
      var acts = [];
      for (var i = 0; i < fr.length; i++) {
        hookLoad(fr[i]);
        runFrame(fr[i], acts);
      }
      if (acts.length) console.log('[CEVF] ' + acts.join(' | '));
    } catch (e) {}
  }
  function hookLoad(f) {
    if (f.__CEVF_LOAD) return;
    f.__CEVF_LOAD = 1;
    f.addEventListener('load', function () {
      setTimeout(function () {
        var a = [];
        runFrame(f, a);
        if (a.length) console.log('[CEVF] ' + a.join(' | '));
      }, 120);
    });
  }
  function runFrame(f, acts) {
    try {
      var w = f.contentWindow, doc = f.contentDocument;
      if (!w || !doc || !doc.body) return;
      var m = (doc.location.pathname || '').match(/WebUI\/UI\/([A-Za-z0-9_]+)\//);
      if (!m) return;
      var page = m[1];
      var sel = ROOTS[page];
      if (!sel) return;
      if (OWNED[page]) { ensureLockFit(page, doc, acts); return; }
      ensureLockFit(page, doc, acts);
      var el = doc.querySelector(sel);
      if (!el) return;
      var st = doc.getElementById('__CEVF_STYLE');
      if (!st) {
        st = doc.createElement('style');
        st.id = '__CEVF_STYLE';
        (doc.head || doc.documentElement).appendChild(st);
      }
      var css = sel + '{flex-shrink:0 !important;}';
      if (st.textContent !== css) { st.textContent = css; acts.push(page + ':shrink0'); }
      var lw = el.offsetWidth, lh = el.offsetHeight;
      if (!lw || !lh) return;
      var vw = w.innerWidth, vh = w.innerHeight;
      if (!vw || !vh) return;
      var margin = MARGINS[page] || 48;
      var z = Math.min(1, (vw - margin) / lw, (vh - margin) / lh);
      var cz = 1;
      try { cz = parseFloat(w.getComputedStyle(doc.body).zoom) || 1; } catch (e2) {}
      if (z >= cz - 0.003 || z >= 1) return;
      doc.body.style.zoom = String(z);
      doc.body.style[HPROPS[page] || 'minHeight'] = Math.round(vh / z) + 'px';
      acts.push(page + ' z ' + cz.toFixed(3) + '>' + z.toFixed(3));
    } catch (e) {}
  }
  // LockFit（v14）：往页面注入【官方锁的纯累计复刻 + 官方 fit 调用】脚本（页面上下文）。
  // 背景（2026-10-17 用户验收标准=官方页行为）：官方 createGridSizeLock 的 seenCols 在
  // 服务端送 maxCols 提示时无视历史累计（hintC ? max(hintC,cols) : ...）——酿酒桶/老鼠笼
  // 实测界面随柜子变（疑提示随页签变），烹饪/工作台稳定。复刻版只做纯累计（max(seen,cols)），
  // 消息监听里【同步】重写外边距（页面自己的 listener 先注册先执行，我们后写=生效且无闪帧），
  // 再调官方 createViewportFit 缩放。幂等（__CEVF_LF 标记 + script 元素去重）。
  function ensureLockFit(page, doc, acts) {
    try {
      if (doc.getElementById('__CEVF_LF')) return;
      var cfg = LOCKFIT[page];
      if (!cfg) return;
      var s = doc.createElement('script');
      s.id = '__CEVF_LF';
      s.textContent = '(function(){'
        + 'if(window.__CEVF_LF_ON)return;window.__CEVF_LF_ON=1;'
        + 'var GID=' + q(cfg.g) + ',SEL=' + q(cfg.s) + ',M=' + cfg.m + ',HP=' + q(cfg.p) + ';'
        + 'var seenC=0,seenR=0;'
        + 'var fit=window.createViewportFit?window.createViewportFit({measureSelector:SEL,margin:M,bodyHeightProp:HP}):null;'
        + 'function num(t){var x=(t||\'\').match(/repeat\\((\\d+)/);return x?parseInt(x[1],10):0;}'
        + 'function step(el,ax){var tpl=ax===\'x\'?el.style.gridTemplateColumns:el.style.gridTemplateRows;'
        + 'var c=(tpl||\'\').indexOf(\',\');if(c<0)return 0;var sz=parseFloat(tpl.slice(c+1));if(!(sz>0))return 0;'
        + 'var cs=window.getComputedStyle(el);var gp=parseFloat(ax===\'x\'?cs.columnGap:cs.rowGap)||0;return sz+gp;}'
        + 'function run(){try{var g=document.getElementById(GID);if(!g)return;'
        + 'var c=num(g.style.gridTemplateColumns),r=num(g.style.gridTemplateRows);if(!c||!r)return;'
        + 'if(c>seenC)seenC=c;if(r>seenR)seenR=r;'
        + 'var dx=Math.round(Math.max(0,seenC-c)*step(g,\'x\'));'
        + 'var dy=Math.round(Math.max(0,seenR-r)*step(g,\'y\'));'
        + 'var l=Math.floor(dx/2);'
        + 'g.style.marginLeft=l+\'px\';g.style.marginRight=(dx-l)+\'px\';'
        + 'g.style.marginTop=\'0px\';g.style.marginBottom=dy+\'px\';'
        + 'if(fit)fit.apply();}catch(e){}}'
        + 'window.addEventListener(\'message\',function(){try{run();}catch(e){}});'
        + 'setInterval(run,1000);'
        + 'run();'
        + '})();';
      (doc.head || doc.documentElement).appendChild(s);
      acts.push(page + ':lockfit');
    } catch (e) {}
  }
  function q(s) { return String.fromCharCode(39) + s + String.fromCharCode(39); }
  var ROOTS = { AidDepot: '.hub-root', Compressor: '#app', Cooking: '.cooking-system', DailyFert: '.df-root',
    DroneHub: '.hub-root', FuelGenerator: '.generator-system', GreenhouseBuild: '#ghRoot',
    Heater: '.heater-system', MaterialRack: '.rack-system', RatCage: '.generator-system',
    Shredder: '#app', ToolTable: '.ui-container',
    BrewPanel: '.brew-system', DyePanel: '.dye-system' };
  var OWNED = { BrewPanel: 1, DyePanel: 1 };
  var LOCKFIT = {
    BrewPanel: { g: 'bagGrid', s: '.brew-system', m: 24, p: 'minHeight' },
    DyePanel: { g: 'bagGrid', s: '.dye-system', m: 24, p: 'minHeight' },
    RatCage: { g: 'backpackGrid', s: '.generator-system', m: 48, p: 'minHeight' } };
  var MARGINS = { AidDepot: 48, Compressor: 24, Cooking: 48, DailyFert: 48, DroneHub: 48,
    FuelGenerator: 48, GreenhouseBuild: 40, Heater: 48, MaterialRack: 24, RatCage: 48,
    Shredder: 24, ToolTable: 24 };
  var HPROPS = { Compressor: 'height', MaterialRack: 'height', Shredder: 'height', ToolTable: 'height' };
})();
";
}
