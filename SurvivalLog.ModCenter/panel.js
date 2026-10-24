/* ModCenter 面板 JS（内嵌资源 SurvivalLog.ModCenter.panel.js）。
 * 任务 5 = 空壳骨架（遮罩+标题+关闭+outbox）；任务 7 在此生长完整面板。
 *
 * 注入落点：Root 文档（C# WebBridge.ExecuteJavaScript 上下文）。__McInstalled 守卫幂等。
 * 全局契约（与 C# Ticker / 任务 8 键盘门对齐）：__McInstalled / __mcOutbox / __mcDrain /
 * __mcOpen(hostDoc) / __mcClose / __mcEsc(e)。
 *
 * 面板 DOM 挂在「入口按钮所在的 iframe 文档」（host doc），不挂 Root：各页 webui-core.js
 * clickThrough 只同步 button/[data-interactive] 等清单元素的矩形给 Unity（Root 文档自身不跑
 * clickThrough，Root.html 实证），挂错文档按钮收不到实机点击。面板自带 data-interactive，
 * 打开期间全屏矩形进同步清单（点击不穿透到游戏）；关闭即随元素移除，下一轮同步自动恢复。
 *
 * 全部元素 id/class 与样式 __mc 前缀，不碰官方 tabs 数组与页面数据（CabinetEverywhere C6 决策同款）。
 * 样式自包含暗色主题（移植 docs/superpowers/specs/2026-10-24-modcenter-prototype.html panel 段）。
 */
(function () {
  if (window.__McInstalled) return;
  window.__McInstalled = true;

  /* ---- outbox：JS → C# 单向信箱（C# 每 2s __mcDrain 取走） ---- */
  window.__mcOutbox = [];
  window.__mcDrain = function () { return window.__mcOutbox.splice(0); };

  var S = { host: null, root: null }; // host=面板所在 iframe 文档；root=overlay 根元素

  var CSS = ''
    + '#__mc_overlay{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(5,6,10,.8);}'
    + '#__mc_overlay *{box-sizing:border-box;}'
    + '#__mc_panel{width:92%;height:88%;background:#171a23;border:1px solid #2a2f3d;border-radius:14px;'
    + 'display:flex;flex-direction:column;overflow:hidden;box-shadow:0 18px 70px rgba(0,0,0,.75);'
    + 'font-family:"Segoe UI","Microsoft YaHei",system-ui,sans-serif;color:#e8e6e0;}'
    + '#__mc_panel .__mc-head{display:flex;align-items:center;gap:12px;padding:13px 18px;'
    + 'border-bottom:1px solid #2a2f3d;background:#1d212c;}'
    + '#__mc_panel .__mc-title{font-size:17px;letter-spacing:2px;font-weight:600;}'
    + '#__mc_panel .__mc-sub{font-size:12px;color:#6b7180;}'
    + '#__mc_panel .__mc-close{margin-left:auto;width:32px;height:32px;border-radius:8px;'
    + 'border:1px solid #2a2f3d;background:transparent;color:#9aa0ad;font-size:15px;cursor:pointer;'
    + 'line-height:1;font-family:inherit;}'
    + '#__mc_panel .__mc-close:hover{border-color:#f87171;color:#f87171;}'
    + '#__mc_panel .__mc-body{flex:1;display:flex;align-items:center;justify-content:center;'
    + 'color:#6b7180;font-size:13px;letter-spacing:1px;}';

  function ensureStyle(doc) {
    if (doc.getElementById('__mc_panel_style')) return;
    var st = doc.createElement('style');
    st.id = '__mc_panel_style';
    st.textContent = CSS;
    (doc.head || doc.documentElement).appendChild(st);
  }

  function build(doc) {
    ensureStyle(doc);
    var ov = doc.createElement('div');
    ov.id = '__mc_overlay';
    ov.setAttribute('data-interactive', ''); // 全屏矩形进 clickThrough 同步 → 点击落 WebView 且不穿透
    var pn = doc.createElement('div');
    pn.id = '__mc_panel';
    var head = doc.createElement('div'); head.className = '__mc-head';
    var t = doc.createElement('div'); t.className = '__mc-title'; t.textContent = 'MOD 管理中心';
    var sub = doc.createElement('div'); sub.className = '__mc-sub'; sub.textContent = 'ModCenter';
    var x = doc.createElement('button');
    x.className = '__mc-close'; x.type = 'button'; x.textContent = '\u2715';
    x.setAttribute('data-interactive', '');
    x.addEventListener('click', function () { window.__mcClose(); });
    head.appendChild(t); head.appendChild(sub); head.appendChild(x);
    var body = doc.createElement('div');
    body.className = '__mc-body'; body.textContent = '面板施工中（任务 7 移植完整界面）';
    pn.appendChild(head); pn.appendChild(body);
    ov.appendChild(pn);
    /* 点遮罩空白处关闭（面板本体不关） */
    ov.addEventListener('click', function (e) { if (e.target === ov) window.__mcClose(); });
    return ov;
  }

  /* ---- 开关：任务 5 空壳；开/关各 push 一条状态消息（任务 8 键盘门消费） ---- */
  window.__mcOpen = function (hostDoc) {
    if (S.root && S.root.isConnected) return; // 已开
    var doc = (hostDoc && hostDoc.body) ? hostDoc : S.host;
    if (!doc || !doc.body) return;
    S.host = doc;
    S.root = build(doc);
    doc.body.appendChild(S.root);
    window.__mcOutbox.push({ type: 'MC_OPEN', host: doc.location.pathname });
  };
  window.__mcClose = function () {
    if (!S.root) return;
    var el = S.root;
    S.root = null;
    if (el.isConnected && el.parentNode) el.parentNode.removeChild(el);
    window.__mcOutbox.push({ type: 'MC_CLOSE' });
  };
  window.__mcEsc = function (e) {
    if (e && e.key !== 'Escape') return;
    if (S.root) window.__mcClose();
  };

  /* Root 文档自身的 ESC（焦点在 Root 时兜底；host doc 的 ESC 由 MenuEntryInjector wireKey 转发） */
  document.addEventListener('keydown', function (e) { window.__mcEsc(e); });
})();
