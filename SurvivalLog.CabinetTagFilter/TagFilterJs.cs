namespace SurvivalLog.CabinetTagFilter;

/// <summary>
/// 标签筛选的 JS 引导脚本（运行在 Root.html 主帧，经 IWebView.ExecuteJavaScript 注入）。
///
/// 架构（2026-10-14 全程反汇编实证，见 DESIGN.md）：
/// - 游戏 UI = 一个 Vuplex Chromium WebView 装载 Root.html，各界面页是 #iframe-container 下的
///   iframe；C#→页面数据全部经 Root 的 case-10 广播 frame.contentWindow.postMessage({type,data}) 转发，
///   页签数据都在 *_BagMsg 里（cabinetTabs/bagTabs/cartTabs/bagTabsJson，条目带 ownerId）。
/// - 本脚本只做两件事：① 包装每个 iframe 的 contentWindow.postMessage，截读 BagMsg 记住页签
///   顺序与 ownerId（**不改消息内容**——工作台/粉碎机/压缩机切页签发 TAB_SWITCH{tab:数组下标}，
///   C#/JS 共享下标，改数组会切错柜子）；② 按当前选中标签把不匹配的容器页签 DOM display:none
///   （背包等不在名单里的 ownerId 永不隐藏；下标/事件/自动取料全部原样）。
/// - 下拉选项（2026-10-14 第三轮定版）：**完整标签库**默认全量显示——食物维度汇总为一个
///   "食物"选项（Dim 4 的那条做代表，选中时匹配任何 Dim 1-4 食物标签），物资维度（Dim 0）
///   逐个列出；不再按"柜子是否用到"裁剪（空标签存档只剩伪标签一项的困惑）。
/// - MutationObserver 回调内**同步**重挂隐藏（微任务、绘制前执行）——页面每次 BagMsg 整条
///   重建页签 DOM，任何防抖窗口都会让被藏页签闪现（闪烁根因，实修验证）。
/// - 方向感知弹回（2026-10-15）：官方 A/D 切页签是 C# 侧"下标±1"步进
///   （ContainerTabHotKey.SendSwitchTab → 各面板 RA_SwitchTabByKey，反汇编实证），
///   对 DOM 隐藏零感知——按 D 常落在被藏柜子上，若把焦点盲拽回"第一个可见页签"会表现为
///   来回跳跃/无法移动。故被藏 active 页签按移动方向（对比上帧 active 下标）点同方向最近
///   可见页签（=跳过被藏柜子）；方向未知（首帧/玩家换筛选）才点第一个可见页签。
/// - 选中态存 localStorage（sltf_sel_v2），界面间共享；跨会话保持。
/// - 同源前提：包装 contentWindow 与直访 contentDocument 都要求 Root 与 iframe 同源；
///   跨域时包装自然失败（写属性 throw），功能静默不生效，C# 侧探针会在日志留痕。
/// </summary>
internal static class TagFilterJs
{
    internal const string Bootstrap = @"
(function () {
  if (window.__SLTF__) return;
  var LS_KEY = 'sltf_sel_v2';
  var S = { sel: 0, owners: {}, lib: [], dim: {}, foodId: 0, frames: [] };
  try { S.sel = parseInt(localStorage.getItem(LS_KEY) || '0', 10) || 0; } catch (e) {}

  function rebuildLib() {
    S.dim = {};
    S.foodId = 0;
    for (var i = 0; i < S.lib.length; i++) {
      var e = S.lib[i];
      S.dim[e.id] = e.dim;
      if (e.dim === 4) S.foodId = e.id; // 全部食物（食物维度的代表标签）
    }
  }
  rebuildLib();

  // 选中匹配：食物=命中任一 Dim1-4 食物标签；其他=精确命中
  function matchesSel(tags) {
    if (S.sel === S.foodId) {
      for (var i = 0; i < tags.length; i++) {
        var d = S.dim[tags[i]] || 0;
        if (d >= 1 && d <= 4) return true;
      }
      return false;
    }
    return tags.indexOf(S.sel) >= 0;
  }
  function shouldHide(k) {
    if (!S.sel) return false;
    var t = S.owners[k];
    return t !== undefined && !matchesSel(t);
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // ---- 截读 Root→iframe 的 *_BagMsg：记页签数组（含 null 占位，维持下标对齐）----
  function normOwner(t) {
    if (!t) return null;
    var o = (t.ownerId != null) ? t.ownerId : ((t.owner != null) ? t.owner : null);
    return (o == null || o === '') ? null : String(o);
  }
  function extractTabs(msg) {
    try {
      if (!msg || typeof msg.type !== 'string' || msg.type.indexOf('BagMsg') < 0) return null;
      var d = msg.data;
      if (typeof d === 'string') d = JSON.parse(d);
      if (!d) return null;
      var arr = null;
      if (d.bagTabsJson) arr = JSON.parse(d.bagTabsJson);
      else arr = d.cabinetTabs || d.bagTabs || d.cartTabs || null;
      if (!arr) return null;
      var out = [];
      for (var i = 0; i < arr.length; i++) out.push(normOwner(arr[i]));
      return out;
    } catch (e) { return null; }
  }

  function wrapRelay(el, st) {
    try {
      var win = el.contentWindow;
      if (!win || win.__sltfWrapped) return true;
      var orig = win.postMessage;
      win.__sltfWrapped = 1;
      win.postMessage = function (msg) {
        var t = extractTabs(msg);
        if (t) st.tabs = t;
        return orig.apply(this, arguments);
      };
      return true;
    } catch (e) { st.xorigin = true; return false; }
  }

  // ---- 页签栏识别（按各页 DOM 实证结构）----
  // tooltable: #leftTabsScroll 里 .cabinet-tab ↔ cabinetTabs（0 起；背包 pin 在栏外，
  //            工作台抽屉 #tab-drawer 是 scroll 内第一个按钮但无 .cabinet-tab 类——不在 cands 里，
  //            对应官方 tab 1。edges=向左穿越整片被藏柜子后的官方落点（抽屉→背包，tab 1→0 序））
  // trade:     .bag-tabs-scroll 里 .bag-tab 自带 data-owner 属性（无人机交易，Vue 页）
  // bagtabs:   #bagTabsScroll 里 .bag-tab ↔ bagTabs（烹饪/老鼠笼/酿酒/无人机站，pin=tabs[0] 偏移 1）
  // shredder:  #tabsScroll 里 .left-tab ↔ cartTabs（pin=tabs[0] 偏移 1）
  // compressor:#tabs 里 .left-tab ↔ cartTabs（0 起，无 pin 分离）
  function detectC(doc) {
    if (doc.getElementById('leftTabsScroll')) return { list: doc.getElementById('leftTabsScroll'), sel: '.cabinet-tab', offset: 0, bar: doc.querySelector('.left-tabs'), pos: true, edges: [doc.getElementById('tab-drawer'), doc.getElementById('tab-bag')] };
    if (doc.querySelector('.bag-tabs-scroll .bag-tab[data-owner]')) return { list: doc.querySelector('.bag-tabs-scroll'), sel: '.bag-tab', attr: 'data-owner', bar: doc.querySelector('.bag-tabs'), pos: false };
    if (doc.getElementById('bagTabsScroll')) return { list: doc.getElementById('bagTabsScroll'), sel: '.bag-tab', offset: -1, bar: doc.getElementById('bagTabs'), pos: true };
    if (doc.getElementById('tabsScroll')) return { list: doc.getElementById('tabsScroll'), sel: '.left-tab', offset: -1, bar: doc.getElementById('tabsScroll').parentElement, pos: true };
    if (doc.getElementById('tabs')) return { list: doc.getElementById('tabs'), sel: '.left-tab', offset: 0, bar: doc.getElementById('tabs'), pos: true };
    return null;
  }

  // 同方向步进找最近可见页签（越界即该方向到头，返回 null）
  function nearestVisible(cands, from, dir) {
    for (var s = 1; s < cands.length; s++) {
      var p = from + dir * s;
      if (p < 0 || p >= cands.length) return null;
      var el = cands[p];
      if (el.style.display !== 'none' && !el.disabled) return el;
    }
    return null;
  }

  function applyFrame(st, c) {
    try {
      if (!st.doc || !st.doc.body) return;
      c = c || detectC(st.doc);
      if (!c) return;
      for (var i = 0; i < st.hidden.length; i++) { var h = st.hidden[i]; if (h && h.el) h.el.style.display = h.prev; }
      st.hidden.length = 0;
      if (!S.sel) return;
      var cands = c.list.querySelectorAll(c.sel);
      var arr = c.pos ? st.tabs : null;
      var offset = c.offset;
      if (c.pos) {
        if (!arr) return;
        if (offset < 0) offset = (cands.length === arr.length) ? 0 : ((cands.length === arr.length - 1) ? 1 : -1);
        if (offset < 0) return; // 页签数与数据对不上（页面结构变化），宁可不藏也不错藏
      }
      var activeIdx = -1, activeHiddenIdx = -1;
      for (var j = 0; j < cands.length; j++) {
        var el = cands[j];
        var isActive = el.classList.contains('active');
        if (isActive) activeIdx = j;
        var k = c.attr ? el.getAttribute(c.attr) : arr[j + offset];
        if (k == null) continue;
        if (shouldHide(String(k))) {
          st.hidden.push({ el: el, prev: el.style.display });
          if (isActive) activeHiddenIdx = j;
          el.style.display = 'none';
        }
      }
      // 当前打开的页签被藏（多为官方 A/D 步进切到被藏柜子——C# 侧下标±1，看不见隐藏）：
      // 按移动方向点同方向最近可见页签（=替玩家跳过被藏柜子）；向左穿出整片被藏柜子区时
      // 点官方下一个左页签（工作台抽屉→背包，edges）——否则屏障挡路永远到不了 tab1/tab0；
      // 方向未知（首帧/换筛选）点第一个可见页签。下标/owner 语义都安全，事件是页面自己的。
      if (activeHiddenIdx >= 0) {
        var dirPick = 0;
        if (activeIdx >= 0 && st.lastActiveIdx != null && st.lastActiveIdx >= 0) {
          if (activeIdx > st.lastActiveIdx) dirPick = 1;
          else if (activeIdx < st.lastActiveIdx) dirPick = -1;
        }
        var target = dirPick ? nearestVisible(cands, activeIdx, dirPick) : null;
        if (!target && dirPick < 0 && c.edges) {
          for (var q = 0; q < c.edges.length && !target; q++) {
            var eb = c.edges[q];
            if (eb && eb.isConnected && eb.style.display !== 'none' && !eb.disabled) target = eb;
          }
        }
        if (!target && dirPick) target = nearestVisible(cands, activeIdx, -dirPick);
        if (!target) {
          for (var m = 0; m < cands.length; m++) {
            if (cands[m].style.display !== 'none' && !cands[m].disabled) { target = cands[m]; break; }
          }
        }
        if (target) {
          // 预高亮：弹回要经官方链走第二次 BagMsg 整条重建，两次重建之间有一帧——先把高亮
          // 挪到目标上，页签栏就不会闪『无高亮』帧（第二次重建后官方状态自然接管）
          if (activeHiddenIdx < cands.length && cands[activeHiddenIdx].classList.contains('active')) cands[activeHiddenIdx].classList.remove('active');
          target.classList.add('active');
          target.click();
        }
      }
      st.lastActiveIdx = activeIdx;
    } catch (e) {}
  }

  // 下拉选项：食物汇总（Dim4 代表）+ 物资（Dim0）逐个，按官方 Order 排序——全量显示，不按使用裁剪
  function options() {
    var food = null, supplies = [];
    for (var i = 0; i < S.lib.length; i++) {
      var e = S.lib[i];
      if (e.dim === 4) { food = e; }
      else if (e.dim === 0) { supplies.push(e); }
    }
    supplies.sort(function (a, b) { return (a.order || 0) - (b.order || 0) || a.id - b.id; });
    var out = [];
    if (food) out.push({ id: food.id, name: food.name || '食物' });
    for (var j = 0; j < supplies.length; j++) out.push({ id: supplies[j].id, name: supplies[j].name || ('#' + supplies[j].id) });
    return out;
  }

  function refreshDD(st, c) {
    try {
      if (!st.doc || !st.doc.body) return;
      c = c || detectC(st.doc);
      if (!c || !c.bar) return;
      var opts = options();
      var dd = st.dd;
      if (!opts.length) { if (dd) dd.style.display = 'none'; return; }
      if (!dd || !dd.isConnected || dd.ownerDocument !== st.doc) {
        dd = st.doc.createElement('select');
        dd.className = 'sltf-dd';
        dd.title = '按标签筛选容器页签';
        dd.style.cssText = 'height:26px;max-width:130px;margin:2px 4px;padding:0 4px;font-size:12px;flex:0 0 auto;align-self:center;'
          + 'background:rgba(28,24,18,0.96);color:#e8dcc8;border:1px solid rgba(255,193,7,0.35);border-radius:4px;cursor:pointer;outline:none;';
        var ddRef = dd;
        dd.addEventListener('change', function () { setSel(parseInt(ddRef.value, 10) || 0); });
        c.bar.insertBefore(dd, c.bar.firstChild);
        st.dd = dd;
      }
      dd.style.display = '';
      var key = S.sel + '|' + opts.map(function (u) { return u.id + '.' + u.name; }).join(',');
      if (dd.__sltfKey === key) return;
      dd.__sltfKey = key;
      dd.innerHTML = '';
      var doc = st.doc;
      function addOpt(v, label, selected) {
        var o = doc.createElement('option');
        o.value = String(v);
        o.textContent = label;
        if (selected) o.selected = true;
        dd.appendChild(o);
      }
      addOpt(0, '全部柜子', S.sel === 0);
      for (var i = 0; i < opts.length; i++) addOpt(opts[i].id, opts[i].name, opts[i].id === S.sel);
    } catch (e) {}
  }

  function setSel(v) {
    S.sel = v || 0;
    try { localStorage.setItem(LS_KEY, String(S.sel)); } catch (e) {}
    for (var i = 0; i < S.frames.length; i++) {
      S.frames[i].lastActiveIdx = null; // 玩家主动换筛选：方向无意义，回到『第一个可见』语义
      applyFrame(S.frames[i]);
    }
  }

  // 同步重挂（勿改回防抖）：页面每次 BagMsg 会整条重建页签 DOM，MutationObserver 回调是微任务、
  // 在浏览器绘制前执行——这里同步把隐藏重新套上，被滤掉的页签就不会闪现（80ms 防抖窗口=闪烁根因）。
  // records 过滤（2026-10-15 降噪）：观察的是整个 body 子树，物品网格/tooltip 等高频变化都会进
  // 回调——只有页签栏子树真正被触碰才重挂/刷下拉，其余变更直接跳过（卡顿主源）。
  // 必须同时观察 class 属性（attributeFilter 只放 class、排除 style，display 改动不自触发）：
  // 官方 renderCabinetTabs 有 key 短路——A/D 切页签时 cabinetTabs 内容不变，页面不重建页签
  // DOM、只由 syncTabActiveClass 切 active 类；childList-only 观察会漏掉这个唯一的 DOM 变化，
  // 弹回失去触发点，玩家会停在看不见页签的被藏柜子上（2026-10-15 实测回归，勿再犯）。
  function underRoot(n, root) {
    for (var p = n; p; p = p.parentNode) if (p === root) return true;
    return false;
  }
  function subtreeTouched(records, root) {
    if (!root) return true;
    for (var i = 0; i < records.length; i++) {
      var r = records[i];
      if (r.target === root || underRoot(r.target, root)) return true;
      for (var k = 0; k < 2; k++) {
        var ns = k ? r.removedNodes : r.addedNodes;
        if (!ns) continue;
        for (var m = 0; m < ns.length; m++) {
          var x = ns[m];
          if (!x) continue;
          if (x === root || underRoot(x, root)) return true;
          if (x.nodeType === 1 && typeof x.contains === 'function' && x.contains(root)) return true;
        }
      }
    }
    return false;
  }
  function schedule(st, records) {
    try {
      if (!st.doc || !st.doc.body) return;
      var c = detectC(st.doc);
      if (!c) return;
      if (!records || subtreeTouched(records, c.list)) applyFrame(st, c);
      if (!records || subtreeTouched(records, c.bar || c.list)) refreshDD(st, c);
    } catch (e) {}
  }

  function hookFrame(el) {
    for (var i = 0; i < S.frames.length; i++) if (S.frames[i].el === el) return;
    var st = { el: el, doc: null, tabs: null, hidden: [], dd: null, mo: null, xorigin: false, lastActiveIdx: null };
    S.frames.push(st);
    var init = function () {
      var doc = null;
      try { doc = el.contentDocument; } catch (e) { doc = null; }
      if (!doc || !doc.body || !doc.body.firstChild) { setTimeout(init, 150); return; }
      st.doc = doc;
      wrapRelay(el, st);
      if (!st.mo) st.mo = new MutationObserver(function (rs) { schedule(st, rs); });
      st.mo.observe(doc.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
      schedule(st);
    };
    el.addEventListener('load', function () {
      st.doc = null; st.tabs = null; st.hidden.length = 0; st.dd = null; st.lastActiveIdx = null;
      if (st.mo) st.mo.disconnect();
      init();
    });
    init();
  }

  function scanFrames() {
    var els = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < els.length; i++) hookFrame(els[i]);
    for (var j = S.frames.length - 1; j >= 0; j--) {
      if (!S.frames[j].el.isConnected) {
        if (S.frames[j].mo) S.frames[j].mo.disconnect();
        S.frames.splice(j, 1);
      }
    }
  }
  try {
    new MutationObserver(scanFrames).observe(document.body, { childList: true, subtree: true });
  } catch (e) {}
  scanFrames();

  window.__SLTF__ = {
    ver: 2,
    setData: function (json) {
      S.owners = (json && json.owners) || {};
      S.lib = (json && json.lib) || [];
      rebuildLib();
      for (var i = 0; i < S.frames.length; i++) { applyFrame(S.frames[i]); refreshDD(S.frames[i]); }
    },
    state: function () {
      var xs = 0;
      for (var i = 0; i < S.frames.length; i++) if (S.frames[i].xorigin) xs++;
      return { sel: S.sel, owners: Object.keys(S.owners).length, frames: S.frames.length, xorigin: xs };
    }
  };
})();
";
}
