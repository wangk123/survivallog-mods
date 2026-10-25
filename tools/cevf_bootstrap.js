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