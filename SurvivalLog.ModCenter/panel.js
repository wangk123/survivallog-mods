/* ModCenter 面板 JS（内嵌资源 SurvivalLog.ModCenter.panel.js）。
 * 任务 7 = 完整面板（原型 v4 移植：docs/superpowers/specs/2026-10-24-modcenter-prototype.html
 * 的 <style> 面板段 + renderSide/renderCfg/ctl/toast/toggle/ovr 逻辑；场景模拟与 demo-bar 不迁）。
 *
 * 注入落点：Root 文档（C# WebBridge.ExecuteJavaScript 上下文）。__McInstalled 守卫幂等。
 * 全局契约（C# Ticker / MenuEntryInjector 对齐）：__McInstalled / __mcOutbox / __mcDrain /
 * __mcOpen(hostDoc) / __mcClose / __mcEsc(e) / __mcRender(snapshot) / __mcEvent(event)。
 * 调试镜像：window.__mcLastSnapshot / __mcLastEvent（T6 存根语义保留，CDP 断言用）。
 *
 * 面板 DOM 挂「入口按钮所在 iframe 文档」（host doc，T5 结论：Root 无 clickThrough）。
 * 点击可达性：宿主页 webui-core clickThrough 清单 = .unity-interactive, button, input,
 * select, a, [data-interactive]——因此左列 mod 行（div）显式带 data-interactive，其余
 * 可点元素一律 button/input/select（原型里的 span 开关改为 button，样式全量自带）。
 *
 * 数据流（T6 契约）：收 __mcRender(snapshot) 全量重绘（无 diff，官方模式）；
 * snapshot = {self:{ver}, mods:[ModInfo], tp:[ModInfo]}，
 * ModInfo={guid,name,en,desc,enabled,groups:[{title,items:[Item]}],ovr?:[{id,ms}]}，
 * Item={k,l,t,v,min?,max?,st?,b,note?,opts?}。发消息 push __mcOutbox：
 * MC_SET{guid,section,key,value}（section/key 从展示键 k 按首个 '.' 拆分——C# Match
 * 接受真实节名+主键/组合展示键/任一实写键）/ MC_TOGGLE_MOD{guid,enabled} / MC_OPEN{host}
 * / MC_CLOSE{host?,why?}（why='host-doc-gone' 为任务 8 孤儿看门狗补发）。ovr 清单序列化
 * （"id:ms,id:ms" 整串写 其他.Overrides）由本文件承担。
 *
 * 全部元素 id/class 与样式 __mc 前缀且选择器锚定 #__mc_overlay（零泄漏）；不碰官方
 * tabs 数组与页面数据（CabinetEverywhere C6 决策同款）。
 */
(function () {
  if (window.__McInstalled) return;
  window.__McInstalled = true;

  /* ---- outbox：JS → C# 单向信箱（C# 每 2s __mcDrain 取走） ---- */
  window.__mcOutbox = [];
  window.__mcDrain = function () { return window.__mcOutbox.splice(0); };

  var BADGE = {
    now:    { t: '\u26a1 \u7acb\u5373\u751f\u6548', cls: '__mc-b-now' },      // ⚡ 立即生效
    reload: { t: '\ud83d\udcc1 \u8bfb\u6863\u540e', cls: '__mc-b-reload' },   // 📁 读档后
    reboot: { t: '\ud83d\udd04 \u91cd\u542f\u540e', cls: '__mc-b-reboot' },   // 🔄 重启后
    cfg:    { t: '\u2699 \u5df2\u5199\u5165 cfg',  cls: '__mc-b-cfg' },       // ⚙ 已写入 cfg
  };
  var TOAST_SMALL = {
    now:    '\u5df2\u5199\u5165 cfg \u00b7 \u26a1 \u7acb\u5373\u751f\u6548\uff08\u7ea65\u79d2\u5185\uff09',
    reload: '\u5df2\u5199\u5165 cfg \u00b7 \u8868\u503c\u5373\u65f6\u6539 \u00b7 \ud83d\udcc1 \u8bfb\u6863\u540e\u5bf9\u65e2\u6709\u5bb9\u5668\u4f53\u73b0',
    cfg:    '\u5df2\u5199\u5165\u8be5 mod cfg \u00b7 \u751f\u6548\u65f6\u673a\u53d6\u51b3\u4e8e\u8be5 mod \u662f\u5426\u76d1\u542c\u914d\u7f6e\u53d8\u66f4',
    reboot: '\u5df2\u5199\u5165 \u00b7 \ud83d\udd04 \u91cd\u542f\u540e\u751f\u6548',
  };
  var SELF_NAME = 'Mod \u7ba1\u7406\u4e2d\u5fc3'; // Mod 管理中心
  var SELF_EN = 'ModCenter\uff08\u672c\u9762\u677f\uff09';
  var OVR_SECTION = '\u5176\u4ed6', OVR_KEY = 'Overrides'; // 与 C# Meta.OvrSection/OvrKey 同源

  /* ---- 状态：host/root=宿主文档与面板根；snap=最近快照；sel='__self'|guid ---- */
  var S = { host: null, root: null, side: null, cfg: null, sub: null, toastEl: null };
  var ST = { snap: null, sel: null };
  var TH = Object.create(null); // 滑条 100ms 节流句柄表

  var CSS = ''
    + '#__mc_overlay{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(5,6,10,.8);}'
    + '#__mc_overlay *{box-sizing:border-box;margin:0;padding:0;}'
    /* 面板框 */
    + '#__mc_overlay #__mc_panel{width:92%;height:88%;background:#171a23;border:1px solid #2a2f3d;border-radius:14px;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 18px 70px rgba(0,0,0,.75);font-family:"Segoe UI","Microsoft YaHei",system-ui,sans-serif;color:#e8e6e0;user-select:none;}'
    + '#__mc_overlay .__mc-head{display:flex;align-items:center;gap:12px;padding:13px 18px;border-bottom:1px solid #2a2f3d;background:#1d212c;}'
    + '#__mc_overlay .__mc-title{font-size:17px;letter-spacing:2px;font-weight:600;}'
    + '#__mc_overlay .__mc-sub{font-size:12px;color:#6b7180;}'
    + '#__mc_overlay .__mc-close{margin-left:auto;width:32px;height:32px;border-radius:8px;border:1px solid #2a2f3d;background:transparent;color:#9aa0ad;font-size:15px;cursor:pointer;line-height:1;font-family:inherit;}'
    + '#__mc_overlay .__mc-close:hover{border-color:#f87171;color:#f87171;}'
    /* 主体：左列 + 右区 */
    + '#__mc_overlay .__mc-body{flex:1;display:flex;min-height:0;}'
    + '#__mc_overlay .__mc-side{width:230px;flex-shrink:0;border-right:1px solid #2a2f3d;overflow-y:auto;padding:8px;}'
    + '#__mc_overlay .__mc-side .__mc-cat{font-size:11px;color:#6b7180;letter-spacing:2px;padding:8px 10px 4px;}'
    + '#__mc_overlay .__mc-mod-row{display:flex;align-items:center;gap:9px;padding:9px 10px;border-radius:8px;cursor:pointer;border:1px solid transparent;}'
    + '#__mc_overlay .__mc-mod-row:hover{background:rgba(255,255,255,.03);}'
    + '#__mc_overlay .__mc-mod-row.__mc-sel{background:rgba(232,163,61,.13);border-color:rgba(232,163,61,.27);}'
    + '#__mc_overlay .__mc-mod-row .__mc-st{width:8px;height:8px;border-radius:50%;background:#4ade80;flex-shrink:0;}'
    + '#__mc_overlay .__mc-mod-row.__mc-off .__mc-st{background:#565d6d;}'
    + '#__mc_overlay .__mc-mod-row .__mc-nm{font-size:13.5px;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}'
    + '#__mc_overlay .__mc-mod-row.__mc-off .__mc-nm,#__mc_overlay .__mc-mod-row.__mc-lock .__mc-nm{color:#6b7180;}'
    + '#__mc_overlay .__mc-tp-empty{padding:6px 10px;font-size:12px;color:#6b7180;}'
    /* 迷你开关（button；原型 span 开关改为 button 以进 clickThrough 清单） */
    + '#__mc_overlay .__mc-mini-sw{width:30px;height:17px;border-radius:9px;background:#565d6d;position:relative;cursor:pointer;flex-shrink:0;transition:.15s;border:0;appearance:none;outline:none;}'
    + '#__mc_overlay .__mc-mini-sw::after{content:"";position:absolute;left:2px;top:2px;width:13px;height:13px;border-radius:50%;background:#cfd3dc;transition:.15s;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-on{background:#4ade80;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-on::after{left:15px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big{width:46px;height:25px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big::after{width:20px;height:20px;top:2px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-big.__mc-on::after{left:23px;}'
    + '#__mc_overlay .__mc-mini-sw.__mc-lock{opacity:.35;cursor:not-allowed;pointer-events:none;}'
    /* 右区 */
    + '#__mc_overlay .__mc-cfg{flex:1;overflow-y:auto;padding:16px 22px 30px;min-width:0;}'
    + '#__mc_overlay .__mc-mod-title{display:flex;align-items:center;gap:12px;}'
    + '#__mc_overlay .__mc-mod-title h2{font-size:18px;font-weight:600;}'
    + '#__mc_overlay .__mc-mod-title .__mc-en{font-size:12px;color:#6b7180;font-weight:400;}'
    + '#__mc_overlay .__mc-mod-desc{margin:8px 0 4px;font-size:13px;color:#9aa0ad;line-height:1.6;}'
    + '#__mc_overlay .__mc-stop-banner{margin:10px 0 0;padding:9px 14px;border-radius:8px;font-size:12.5px;line-height:1.6;background:rgba(127,29,29,.2);border:1px solid rgba(248,113,113,.27);color:#fca5a5;display:none;}'
    + '#__mc_overlay .__mc-stop-banner.__mc-show{display:block;}'
    + '#__mc_overlay .__mc-grp{margin-top:20px;}'
    + '#__mc_overlay .__mc-grp>h3{font-size:12.5px;color:#e8a33d;letter-spacing:2px;padding-bottom:7px;border-bottom:1px solid #2a2f3d;margin-bottom:4px;font-weight:500;}'
    + '#__mc_overlay .__mc-item{display:flex;align-items:center;gap:14px;padding:10px 4px;border-bottom:1px dashed rgba(255,255,255,.06);}'
    + '#__mc_overlay .__mc-item .__mc-info{flex:1;min-width:0;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-lb{font-size:13.5px;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-ck{font-size:11px;color:#6b7180;font-family:Consolas,monospace;margin-top:2px;}'
    + '#__mc_overlay .__mc-item .__mc-info .__mc-ck.__mc-note{color:#8a90a0;}'
    + '#__mc_overlay .__mc-item .__mc-ctl{width:min(300px,42%);flex-shrink:0;display:flex;align-items:center;gap:10px;justify-content:flex-end;}'
    + '#__mc_overlay .__mc-item .__mc-badge{width:96px;flex-shrink:0;text-align:right;font-size:11.5px;}'
    + '#__mc_overlay .__mc-b-now{color:#4ade80;}#__mc_overlay .__mc-b-reload{color:#60a5fa;}'
    + '#__mc_overlay .__mc-b-reboot{color:#f87171;}#__mc_overlay .__mc-b-cfg{color:#b8a1e3;}'
    /* 覆盖清单 */
    + '#__mc_overlay .__mc-ovr-add{display:flex;gap:8px;align-items:center;padding:10px 4px 6px;}'
    + '#__mc_overlay .__mc-ovr-add input{width:120px;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:5px 9px;font-size:13px;font-family:Consolas,monospace;}'
    + '#__mc_overlay .__mc-ovr-add button{background:#0f1219;border:1px solid #e8a33d;color:#e8a33d;border-radius:6px;padding:5px 14px;font-size:13px;cursor:pointer;font-family:inherit;}'
    + '#__mc_overlay .__mc-ovr-add button:hover{background:rgba(232,163,61,.13);}'
    + '#__mc_overlay .__mc-del{width:26px;height:26px;border-radius:6px;border:1px solid #2a2f3d;background:transparent;color:#6b7180;font-size:12px;cursor:pointer;flex-shrink:0;font-family:inherit;line-height:1;}'
    + '#__mc_overlay .__mc-del:hover{border-color:#f87171;color:#f87171;}'
    + '#__mc_overlay .__mc-ovr-empty{padding:12px 4px;font-size:12.5px;color:#6b7180;}'
    /* 控件 */
    + '#__mc_overlay input[type=range]{flex:1;accent-color:#e8a33d;height:4px;}'
    + '#__mc_overlay input[type=number]{width:62px;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:4px 6px;font-size:13px;text-align:center;font-family:inherit;}'
    + '#__mc_overlay input[type=text]{flex:1;min-width:0;background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:4px 9px;font-size:13px;text-align:left;font-family:inherit;}'
    + '#__mc_overlay input:focus,#__mc_overlay select:focus{outline:1px solid #e8a33d;}'
    + '#__mc_overlay select{background:#0f1219;border:1px solid #2a2f3d;color:#e8e6e0;border-radius:6px;padding:5px 8px;font-size:12.5px;font-family:inherit;max-width:170px;}'
    /* 底部图例 */
    + '#__mc_overlay .__mc-foot{display:flex;align-items:center;gap:18px;padding:9px 18px;border-top:1px solid #2a2f3d;background:#1d212c;font-size:12px;color:#6b7180;}'
    + '#__mc_overlay .__mc-foot .__mc-lg{display:flex;gap:14px;}'
    + '#__mc_overlay .__mc-foot .__mc-foot-note{margin-left:auto;}'
    /* toast（挂在 overlay 内右下角） */
    + '#__mc_overlay .__mc-toast{position:absolute;right:18px;bottom:16px;background:rgba(15,18,25,.94);border:1px solid #4ade80;color:#e8e6e0;border-radius:10px;padding:10px 16px;font-size:13px;display:none;box-shadow:0 8px 30px rgba(0,0,0,.63);line-height:1.5;max-width:60%;}'
    + '#__mc_overlay .__mc-toast.__mc-show{display:block;}'
    + '#__mc_overlay .__mc-toast.__mc-err{border-color:#f87171;}'
    + '#__mc_overlay .__mc-toast small{color:#9aa0ad;display:block;font-size:11.5px;}'
    + '#__mc_overlay .__mc-loading{padding:40px 0;text-align:center;color:#6b7180;font-size:13px;letter-spacing:1px;}'
    + '#__mc_overlay .__mc-side::-webkit-scrollbar,#__mc_overlay .__mc-cfg::-webkit-scrollbar{width:8px;}'
    + '#__mc_overlay .__mc-side::-webkit-scrollbar-thumb,#__mc_overlay .__mc-cfg::-webkit-scrollbar-thumb{background:#2a2f3d;border-radius:4px;}';

  function ensureStyle(doc) {
    if (doc.getElementById('__mc_panel_style')) return;
    var st = doc.createElement('style');
    st.id = '__mc_panel_style';
    st.textContent = CSS;
    (doc.head || doc.documentElement).appendChild(st);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function fmt(v) { var n = +v; return isNaN(n) ? String(v) : String(Math.round(n * 1000) / 1000); }
  function badgeOf(b) { return BADGE[b] || BADGE.cfg; }

  /* ================= 面板骨架 ================= */

  function build(doc) {
    ensureStyle(doc);
    var ov = doc.createElement('div');
    ov.id = '__mc_overlay';
    ov.setAttribute('data-interactive', ''); // 全屏矩形进 clickThrough 同步 → 点击落 WebView 且不穿透
    var pn = doc.createElement('div');
    pn.id = '__mc_panel';
    pn.style.position = 'relative'; // toast 锚点

    var head = doc.createElement('div'); head.className = '__mc-head';
    var t = doc.createElement('div'); t.className = '__mc-title'; t.textContent = 'MOD \u7ba1\u7406\u4e2d\u5fc3';
    var sub = doc.createElement('div'); sub.className = '__mc-sub'; sub.textContent = '\u6539\u52a8\u5b9e\u65f6\u5199\u5165 cfg \u00b7 \u65e0\u9700\u91cd\u542f';
    var x = doc.createElement('button');
    x.type = 'button'; x.className = '__mc-close'; x.setAttribute('aria-label', '\u5173\u95ed\u9762\u677f'); x.textContent = '\u2715';
    x.addEventListener('click', function (e) { e.stopPropagation(); window.__mcClose(); });
    head.appendChild(t); head.appendChild(sub); head.appendChild(x);

    var body = doc.createElement('div'); body.className = '__mc-body';
    var side = doc.createElement('div'); side.className = '__mc-side'; side.id = '__mc_side';
    var cfg = doc.createElement('div'); cfg.className = '__mc-cfg'; cfg.id = '__mc_cfg';
    body.appendChild(side); body.appendChild(cfg);

    var foot = doc.createElement('div'); foot.className = '__mc-foot';
    foot.innerHTML = '<span class="__mc-lg">'
      + '<span class="__mc-b-now">\u26a1 \u7acb\u5373\u751f\u6548\uff08\u22645\u79d2\uff09</span>'
      + '<span class="__mc-b-reload">\ud83d\udcc1 \u8bfb\u6863\u540e\u751f\u6548</span>'
      + '<span class="__mc-b-reboot">\ud83d\udd04 \u91cd\u542f\u540e\u751f\u6548</span>'
      + '<span class="__mc-b-cfg">\u2699 \u5df2\u5199\u5165 cfg\uff08\u751f\u6548\u89c6\u8be5 mod\uff09</span>'
      + '</span><span class="__mc-foot-note">\u5957\u4ef6\u505c\u7528 = \u7acb\u5373\u8f6f\u505c + \u4e0b\u6b21\u542f\u52a8\u4e0d\u52a0\u8f7d\uff1b\u7b2c\u4e09\u65b9\u505c\u7528 = \u91cd\u542f\u7ea7</span>';

    var toastEl = doc.createElement('div');
    toastEl.className = '__mc-toast'; toastEl.id = '__mc_toast';

    pn.appendChild(head); pn.appendChild(body); pn.appendChild(foot); pn.appendChild(toastEl);
    ov.appendChild(pn);
    /* 点遮罩空白处关闭（面板本体不关） */
    ov.addEventListener('click', function (e) { if (e.target === ov) window.__mcClose(); });

    wireSide(side);
    wireCfg(cfg);

    S.side = side; S.cfg = cfg; S.sub = sub; S.toastEl = toastEl;
    return ov;
  }

  /* ================= 数据访问 ================= */

  function mods() { return ST.snap ? ST.snap.mods : []; }
  function tps() { return ST.snap ? ST.snap.tp : []; }
  function findMod(guid) {
    var i, ms = mods();
    for (i = 0; i < ms.length; i++) if (ms[i].guid === guid) return ms[i];
    var ts = tps();
    for (i = 0; i < ts.length; i++) if (ts[i].guid === guid) return ts[i];
    return null;
  }
  function isTpMod(m) { return tps().indexOf(m) !== -1; }
  function curMod() {
    if (ST.sel === '__self') return null;
    var m = ST.sel ? findMod(ST.sel) : null;
    return m || mods()[0] || null;
  }
  function itemOf(m, g, i) { return (m.groups[g] && m.groups[g].items[i]) || null; }
  function clampItem(it, v) {
    if (typeof it.min === 'number' && v < it.min) v = it.min;
    if (typeof it.max === 'number' && v > it.max) v = it.max;
    return v;
  }

  /* ================= 左列 ================= */

  function swBtn(on, attrs, big) {
    return '<button type="button" class="__mc-mini-sw' + (big ? ' __mc-big' : '') + (on ? ' __mc-on' : '') + '"'
      + attrs + ' aria-checked="' + !!on + '"></button>';
  }

  function renderSide() {
    if (!S.side) return;
    var ms = mods(), ts = tps();
    var h = '<div class="__mc-cat">\u5957\u4ef6 MOD\uff08' + ms.length + '\uff09</div>';
    for (var i = 0; i < ms.length; i++) {
      var m = ms[i];
      h += '<div class="__mc-mod-row' + (m.enabled ? '' : ' __mc-off')
        + (ST.sel === m.guid ? ' __mc-sel' : '') + '" data-interactive data-guid="' + esc(m.guid) + '">'
        + '<span class="__mc-st"></span><span class="__mc-nm">' + esc(m.name) + '</span>'
        + swBtn(m.enabled, ' data-act="toggle" data-guid="' + esc(m.guid) + '"') + '</div>';
    }
    h += '<div class="__mc-cat">\u9762\u677f\u81ea\u8eab</div>';
    h += '<div class="__mc-mod-row __mc-lock' + (ST.sel === '__self' ? ' __mc-sel' : '')
      + '" data-interactive data-guid="__self">'
      + '<span class="__mc-st" style="background:#e8a33d;"></span><span class="__mc-nm">' + esc(SELF_NAME) + '</span>'
      + '<span class="__mc-mini-sw __mc-lock" title="\u9632\u5446\uff1a\u9762\u677f\u4e0d\u53ef\u505c\u7528\u81ea\u8eab"></span></div>';
    h += '<div class="__mc-cat">\u7b2c\u4e09\u65b9 MOD\uff08\u901a\u7528 / \u58f0\u660e\u5f0f\uff09</div>';
    if (ts.length === 0) {
      h += '<div class="__mc-tp-empty">\uff08\u6682\u65e0\u7b2c\u4e09\u65b9 mod \u63a5\u5165\uff09</div>';
    } else {
      for (var j = 0; j < ts.length; j++) {
        var t = ts[j];
        h += '<div class="__mc-mod-row' + (t.enabled ? '' : ' __mc-off')
          + (ST.sel === t.guid ? ' __mc-sel' : '') + '" data-interactive data-guid="' + esc(t.guid) + '">'
          + '<span class="__mc-st"></span><span class="__mc-nm">' + esc(t.name) + '</span>'
          + swBtn(t.enabled, ' data-act="toggle" data-guid="' + esc(t.guid) + '"') + '</div>';
      }
    }
    S.side.innerHTML = h;
  }

  function wireSide(side) {
    side.addEventListener('click', function (e) {
      var sw = e.target.closest ? e.target.closest('[data-act="toggle"]') : null;
      if (sw) { e.stopPropagation(); toggleMod(sw.getAttribute('data-guid')); return; }
      var row = e.target.closest ? e.target.closest('.__mc-mod-row') : null;
      if (row) { ST.sel = row.getAttribute('data-guid'); renderSide(); renderCfg(); }
    });
  }

  /* ================= 右区 ================= */

  function ctlHtml(it, gi, ii) {
    if (it.t === 'bool') {
      return swBtn(!!it.v, ' data-act="set" data-g="' + gi + '" data-i="' + ii + '"', true);
    }
    if (it.t === 'sel') {
      var opts = (it.opts && it.opts.length) ? it.opts : [it.v];
      var o = '';
      for (var i = 0; i < opts.length; i++)
        o += '<option value="' + esc(opts[i]) + '"' + (opts[i] === it.v ? ' selected' : '') + '>' + esc(opts[i]) + '</option>';
      return '<select data-act="set" data-g="' + gi + '" data-i="' + ii + '">' + o + '</select>';
    }
    if (it.t === 'str') { // 任务 14：无选项 string 项（Tier 0 第三方）→ 文本框，change 提交
      return '<input type="text" value="' + esc(it.v) + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">';
    }
    var step = (it.st != null) ? it.st : 0.1;
    var mn = (it.min != null) ? it.min : 0, mx = (it.max != null) ? it.max : 100;
    return '<input type="range" min="' + mn + '" max="' + mx + '" step="' + step + '" value="' + fmt(it.v)
      + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">'
      + '<input type="number" min="' + mn + '" max="' + mx + '" step="' + step + '" value="' + fmt(it.v)
      + '" data-act="set" data-g="' + gi + '" data-i="' + ii + '">';
  }

  function itemHtml(m, it, gi, ii) {
    var bd = badgeOf(it.b);
    return '<div class="__mc-item">'
      + '<div class="__mc-info">'
      + '<div class="__mc-lb">' + esc(it.l) + '</div>'
      + '<div class="__mc-ck">' + esc(it.k) + '</div>'
      + (it.note ? '<div class="__mc-ck __mc-note">' + esc(it.note) + '</div>' : '')
      + '</div>'
      + '<div class="__mc-ctl">' + ctlHtml(it, gi, ii) + '</div>'
      + '<div class="__mc-badge ' + bd.cls + '">' + esc(bd.t) + '</div>'
      + '</div>';
  }

  function ovrHtml(m) {
    var h = '<div class="__mc-grp"><h3>\u52a8\u4f5c\u8986\u76d6\uff08\u5355\u4e2a\u52a8\u4f5c\u5355\u72ec\u8c03\uff09</h3>'
      + '<div class="__mc-ovr-add">'
      + '<input type="number" id="__mc_ovr_new" placeholder="\u52a8\u4f5c ID" min="1">'
      + '<button type="button" data-act="ovr-add">\uff0b \u65b0\u589e\u8986\u76d6</button>'
      + '</div>';
    var ovr = m.ovr || [];
    if (!ovr.length) {
      h += '<div class="__mc-ovr-empty">\u6682\u65e0\u8986\u76d6\u3002\u65b0\u589e\u540e\u5728\u6b64\u5217\u51fa\uff0c\u6ed1\u6761\u5373\u65f6\u8c03\u6574\u3002</div>';
    } else {
      for (var idx = 0; idx < ovr.length; idx++) {
        h += '<div class="__mc-item">'
          + '<div class="__mc-info">'
          + '<div class="__mc-lb">\u52a8\u4f5c #' + ovr[idx].id + '</div>'
          + '<div class="__mc-ck">' + OVR_SECTION + '.' + OVR_KEY + ' \u00b7 ' + ovr[idx].id + ':' + ovr[idx].ms + '</div>'
          + '</div>'
          + '<div class="__mc-ctl">'
          + '<input type="range" min="0" max="5000" step="50" value="' + ovr[idx].ms + '" data-act="ovr-set" data-idx="' + idx + '">'
          + '<input type="number" min="0" max="5000" step="50" value="' + ovr[idx].ms + '" data-act="ovr-set" data-idx="' + idx + '">'
          + '</div>'
          + '<div class="__mc-badge __mc-b-now">\u26a1 \u7acb\u5373\u751f\u6548</div>'
          + '<button type="button" class="__mc-del" data-act="ovr-del" data-idx="' + idx + '" title="\u5220\u9664\u6b64\u8986\u76d6">\u2715</button>'
          + '</div>';
      }
    }
    return h + '</div>';
  }

  function renderCfg() {
    if (!S.cfg) return;
    if (!ST.snap) {
      S.cfg.innerHTML = '<div class="__mc-loading">\u6b63\u5728\u8bfb\u53d6 mod \u914d\u7f6e\u2026</div>';
      return;
    }
    if (ST.sel === '__self' || !mods().length) {
      S.cfg.innerHTML = '<div class="__mc-mod-title"><h2>' + esc(SELF_NAME) + '</h2>'
        + '<span class="__mc-en">' + esc(SELF_EN) + '</span></div>'
        + '<div class="__mc-mod-desc">\u7ba1\u7406\u9762\u677f\u81ea\u8eab\u63d2\u4ef6\u3002\u63d0\u4f9b\u672c\u754c\u9762\u3001\u914d\u7f6e\u6865\uff08\u8bfb\u5199\u5404 mod cfg\uff09\u4e0e\u5143\u6570\u636e\u8868\u3002<br>'
        + '\u9632\u5446\u8bbe\u8ba1\uff1a\u9762\u677f\u5185\u4e0d\u53ef\u505c\u7528\u81ea\u8eab\u2014\u2014\u505c\u7528\u540e\u4f60\u5c06\u6ca1\u6709\u5165\u53e3\u6062\u590d\uff0c\u9700\u624b\u7f16 cfg \u6587\u4ef6\uff1b\u5982\u9700\u5f7b\u5e95\u79fb\u9664\u8bf7\u8d70\u5b89\u88c5\u5668\u5378\u8f7d\u7ec4\u4ef6\u3002</div>';
      return;
    }
    var m = curMod();
    if (!m) return;
    var tp = isTpMod(m);
    var h = '<div class="__mc-mod-title"><h2>' + esc(m.name) + '</h2><span class="__mc-en">' + esc(m.en) + '</span></div>'
      + '<div class="__mc-mod-desc">' + esc(m.desc) + '</div>'
      + '<div class="__mc-stop-banner' + (m.enabled ? '' : ' __mc-show') + '">'
      + (tp
        ? '\u26d4 \u5df2\u6807\u8bb0\u505c\u7528\uff1a\u672c\u6b21\u4f1a\u8bdd\u8be5 mod \u4ecd\u5728\u8fd0\u884c\uff0c<b>\u4e0b\u6b21\u542f\u52a8\u540e</b>\u4e0d\u518d\u52a0\u8f7d\u3002'
        : '\u26d4 \u8be5 mod \u5f53\u524d\u5df2\u505c\u7528\u3002\u6b64\u5904\u4fee\u6539\u4ecd\u4f1a\u5199\u5165 cfg \u4fdd\u5b58\uff0c\u4f46\u9700<b>\u91cd\u65b0\u542f\u7528\u540e</b>\u624d\u4f1a\u751f\u6548\uff08\u5de6\u5217\u5f00\u5173\uff09\u3002')
      + '</div>';
    for (var gi = 0; gi < m.groups.length; gi++) {
      h += '<div class="__mc-grp"><h3>' + esc(m.groups[gi].title) + '</h3>';
      for (var ii = 0; ii < m.groups[gi].items.length; ii++)
        h += itemHtml(m, m.groups[gi].items[ii], gi, ii);
      h += '</div>';
    }
    if (m.groups.length === 0) {
      h += '<div class="__mc-grp"><h3>\u914d\u7f6e</h3>'
        + '<div class="__mc-ovr-empty">\u8be5 mod \u4ec5\u652f\u6301\u542f\u7528 / \u505c\u7528\uff0c\u65e0\u66b4\u9732\u7684\u914d\u7f6e\u9879\u3002</div></div>';
    }
    if (m.ovr) h += ovrHtml(m);
    S.cfg.innerHTML = h;
  }

  /* ================= toast ================= */

  function toast(main, badge, err, errDetail) {
    if (!S.toastEl || !S.toastEl.isConnected) return;
    var small = err
      ? '\u5199\u5165\u88ab\u62d2\u7edd' + (errDetail ? '\uff1a' + errDetail : '') + '\uff08\u8be6\u89c1 LogOutput\uff09'
      : (TOAST_SMALL[badge] || TOAST_SMALL.reboot);
    S.toastEl.className = '__mc-toast __mc-show' + (err ? ' __mc-err' : '');
    S.toastEl.innerHTML = '<span>' + esc(main) + '</span><small>' + esc(small) + '</small>';
    clearTimeout(toast._h);
    toast._h = setTimeout(function () {
      if (S.toastEl) S.toastEl.className = '__mc-toast';
    }, 2600);
  }

  /* ================= 消息发送 ================= */

  function routeOf(k) {
    var i = String(k).indexOf('.');
    return i === -1 ? ['', k] : [String(k).slice(0, i), String(k).slice(i + 1)];
  }
  /* C1 修复（评审 Fix Round 1）：key 一律发**完整展示键 k 原串**（"节.键" / 联动"节.键A + 键B"），
   * section 仍发解析出的节前缀（供 C# Match 的节名前置校验）。此前剥前缀后的联动 key
   * "WidthMult + HeightMult" 在 C# Match 三候选（Key/WKeys/组合 K）全不命中——背包+柜子
   * 4 个联动滑条 MC_SET 必败。整串发后命中 it.K 分支（组合展示键逐字相等）；
   * 非联动项整串 = it.K 同样命中；ovr 走 ovrCommit 固定 section='其他'/key='Overrides'（it.Key 分支）。 */
  function sendSet(m, it, value) {
    var r = routeOf(it.k);
    window.__mcOutbox.push({ type: 'MC_SET', guid: m.guid, section: r[0], key: it.k, value: value });
  }
  function serializeOvr(m) {
    return (m.ovr || []).slice().sort(function (a, b) { return a.id - b.id; })
      .map(function (o) { return o.id + ':' + o.ms; }).join(',');
  }
  function ovrCommit(m) {
    window.__mcOutbox.push({ type: 'MC_SET', guid: m.guid, section: OVR_SECTION, key: OVR_KEY, value: serializeOvr(m) });
  }

  /* 100ms 节流（滑条连发防消息风暴）：立即发首条，100ms 窗口内后续合并为末值补发 */
  function throttleSend(id, fn) {
    var now = Date.now();
    var t = TH[id] || (TH[id] = { last: 0, timer: null, pending: null });
    if (now - t.last >= 100) {
      t.last = now; t.pending = null; fn();
    } else {
      t.pending = fn;
      if (t.timer == null) {
        t.timer = setTimeout(function () {
          var p = TH[id]; if (!p) return;
          p.timer = null; p.last = Date.now();
          var f = p.pending; p.pending = null;
          if (f) f();
        }, 100 - (now - t.last));
      }
    }
  }

  function toggleMod(guid) {
    if (guid === '__self') return; // 防呆：面板不可停用自身
    var m = findMod(guid);
    if (!m) return;
    m.enabled = !m.enabled;
    renderSide(); renderCfg();
    window.__mcOutbox.push({ type: 'MC_TOGGLE_MOD', guid: guid, enabled: m.enabled });
    var tp = isTpMod(m);
    toast(m.name + (m.enabled
      ? (tp ? ' \u5df2\u6062\u590d\u542f\u7528\uff1a\u4e0b\u6b21\u542f\u52a8\u6b63\u5e38\u52a0\u8f7d' : ' \u5df2\u542f\u7528\uff1a\u6062\u590d\u751f\u6548\uff08\u22645\u79d2\uff09')
      : (tp ? ' \u5df2\u6807\u8bb0\u505c\u7528\uff1a\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d\uff08\u91cd\u542f\u7ea7\uff09' : ' \u5df2\u505c\u7528\uff1a\u884c\u4e3a\u7acb\u5373\u8fd8\u539f\uff0c\u4e0b\u6b21\u542f\u52a8\u4e0d\u518d\u52a0\u8f7d')),
      tp ? 'reboot' : 'now');
  }

  /* ================= 右区交互 ================= */

  function syncSibling(el, v) {
    var ctl = el.closest('.__mc-ctl');
    if (!ctl) return;
    var all = ctl.querySelectorAll('input');
    for (var i = 0; i < all.length; i++) if (all[i] !== el) all[i].value = fmt(v);
  }

  function wireCfg(cfg) {
    cfg.addEventListener('click', function (e) {
      var t = e.target;
      if (!t.closest) return;
      var m = curMod();
      if (!m) return;
      /* bool 开关 */
      var sw = t.closest('.__mc-mini-sw[data-act="set"]');
      if (sw) {
        var it = itemOf(m, +sw.getAttribute('data-g'), +sw.getAttribute('data-i'));
        if (!it) return;
        it.v = !it.v;
        sw.className = '__mc-mini-sw __mc-big' + (it.v ? ' __mc-on' : '');
        sw.setAttribute('aria-checked', String(!!it.v));
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + (it.v ? '\u5f00' : '\u5173'), it.b);
        return;
      }
      /* 覆盖清单：新增 */
      var add = t.closest('[data-act="ovr-add"]');
      if (add) {
        if (!m.ovr) return;
        var inp = S.cfg.querySelector('#__mc_ovr_new');
        var id = inp ? parseInt(inp.value, 10) : NaN;
        if (!isNaN(id) && id > 0 && !m.ovr.some(function (o) { return o.id === id; })) {
          m.ovr.push({ id: id, ms: 1000 });
          renderCfg();
          ovrCommit(m);
          toast(m.name + ' \u00b7 \u65b0\u589e\u8986\u76d6 #' + id + '\uff08\u9ed8\u8ba4 1000ms\uff09', 'now');
        }
        return;
      }
      /* 覆盖清单：删除 */
      var del = t.closest('[data-act="ovr-del"]');
      if (del) {
        if (!m.ovr) return;
        var idx = +del.getAttribute('data-idx');
        if (idx >= 0 && idx < m.ovr.length) {
          var gone = m.ovr[idx].id;
          m.ovr.splice(idx, 1);
          renderCfg();
          ovrCommit(m);
          toast(m.name + ' \u00b7 \u5df2\u5220\u9664\u8986\u76d6 #' + gone, 'now');
        }
      }
    });

    cfg.addEventListener('input', function (e) {
      var el = e.target;
      if (!el.getAttribute) return;
      var act = el.getAttribute('data-act');
      if (!act) return;
      var m = curMod();
      if (!m) return;
      /* 覆盖清单滑条/数值 */
      if (act === 'ovr-set') {
        if (!m.ovr) return;
        var o = m.ovr[+el.getAttribute('data-idx')];
        if (!o) return;
        var ov = parseFloat(el.value);
        if (isNaN(ov)) return;
        o.ms = Math.round(Math.max(0, Math.min(5000, ov)));
        syncSibling(el, o.ms);
        var ck = el.closest('.__mc-item');
        if (ck) {
          var ckEl = ck.querySelector('.__mc-ck');
          if (ckEl) ckEl.textContent = OVR_SECTION + '.' + OVR_KEY + ' \u00b7 ' + o.id + ':' + o.ms;
        }
        throttleSend('ovr' + el.getAttribute('data-idx'), function () { ovrCommit(m); });
        toast(m.name + ' \u00b7 \u8986\u76d6 #' + o.id + ' \u2192 ' + o.ms + 'ms', 'now');
        return;
      }
      if (act !== 'set') return;
      if (el.tagName === 'SELECT') return; // 下拉在 change 提交
      if (el.type !== 'range' && el.type !== 'number') return;
      var it = itemOf(m, +el.getAttribute('data-g'), +el.getAttribute('data-i'));
      if (!it) return;
      var v = parseFloat(el.value);
      if (isNaN(v)) return; // 数值框清空中途不发
      v = clampItem(it, v);
      if (it.t === 'int') v = Math.round(v);
      it.v = v;
      syncSibling(el, v);
      throttleSend('g' + el.getAttribute('data-g') + 'i' + el.getAttribute('data-i'), function () { sendSet(m, it, it.v); });
      toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + fmt(v), it.b);
    });

    cfg.addEventListener('change', function (e) {
      var el = e.target;
      if (!el.getAttribute) return;
      var act = el.getAttribute('data-act');
      if (act !== 'set') return;
      var m = curMod();
      if (!m) return;
      var it = itemOf(m, +el.getAttribute('data-g'), +el.getAttribute('data-i'));
      if (!it) return;
      if (el.tagName === 'SELECT') {
        it.v = el.value;
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + el.value, it.b);
      } else if (el.tagName === 'INPUT' && el.type === 'text') {
        // 任务 14：str 文本框——change（失焦/回车）提交整串，不在逐键 input 发送
        it.v = el.value;
        sendSet(m, it, it.v);
        toast(m.name + ' \u00b7 ' + it.l + ' \u2192 ' + el.value, it.b);
      } else if (el.type === 'number') {
        el.value = fmt(it.v); // 提交时归位显示值（input 阶段已按钳制值发送）
      }
    });
  }

  /* ================= C# → JS 推送 ================= */

  function renderAll() {
    if (!S.root || !S.root.isConnected) return;
    if (S.sub && ST.snap && ST.snap.self) S.sub.textContent = 'v' + ST.snap.self.ver + ' \u00b7 \u6539\u52a8\u5b9e\u65f6\u5199\u5165 cfg \u00b7 \u65e0\u9700\u91cd\u542f';
    renderSide();
    renderCfg();
  }

  window.__mcRender = function (snapshot) {
    window.__mcLastSnapshot = snapshot; // 调试/CDP 断言镜像（T6 存根语义保留）
    window.__mcSnapSeq = (window.__mcSnapSeq || 0) + 1; // 推送序号（CDP 判"新快照到达"用）
    ST.snap = snapshot;
    if (ST.sel == null && snapshot && snapshot.mods && snapshot.mods.length) ST.sel = snapshot.mods[0].guid;
    renderAll();
  };

  window.__mcEvent = function (event) {
    window.__mcLastEvent = event;
    if (event && event.type === 'MC_SET_FAIL') {
      toast('\u2715 ' + (event.k || '?'), null, true, event.err);
    }
    /* MC_SET_OK 不二次 toast：改值瞬间已按徽标 toast，OK 回执仅落 __mcLastEvent 供断言 */
  };

  /* ================= 开关 ================= */

  window.__mcOpen = function (hostDoc) {
    if (S.root && S.root.isConnected) return; // 已开
    var doc = (hostDoc && hostDoc.body) ? hostDoc : S.host;
    if (!doc || !doc.body) return;
    S.host = doc;
    S.root = build(doc);
    doc.body.appendChild(S.root);
    renderAll(); // 已有快照则立即出内容，否则"正在读取…"占位
    window.__mcOutbox.push({ type: 'MC_OPEN', host: doc.location.pathname });
  };
  window.__mcClose = function () {
    if (!S.root) return;
    var el = S.root;
    S.root = null; S.side = S.cfg = S.sub = S.toastEl = null;
    if (el.isConnected && el.parentNode) el.parentNode.removeChild(el);
    /* 关闭即清节流句柄（评审备忘 3）：撤销未触发的 trailing 补发，面板关闭后不再发消息 */
    for (var id in TH) if (TH[id] && TH[id].timer != null) clearTimeout(TH[id].timer);
    TH = Object.create(null);
    window.__mcOutbox.push({ type: 'MC_CLOSE' });
  };
  window.__mcEsc = function (e) {
    if (e && e.key !== 'Escape') return;
    if (S.root) window.__mcClose();
  };

  /* Root 文档自身的 ESC（焦点在 Root 时兜底；host doc 的 ESC 由 MenuEntryInjector wireKey 转发；
   * OutSetting 场景 ESC 会被 Unity 截走——✕/点遮罩是保底关闭路径（T5 结论），ESC 仅增强 */
  document.addEventListener('keydown', function (e) { window.__mcEsc(e); });

  /* ---- 面板孤儿看门狗（任务 8）：本脚本活在 Root 上下文，宿主 iframe 消亡不影响本定时器。
   * 宿主文档被游戏重建/导航（如暂停菜单被 Unity 侧 ESC 收起）时面板 DOM 随之消亡，但 iframe
   * 内外都发不出 MC_CLOSE——由 Root 侧每 1s 检查：面板开着的根突然失连 ⇒ 补发 MC_CLOSE
   * （C# 键盘门双还原 + 脏 cfg 落盘收口，FocusTick.cs 消费；层整体消亡另有 C# 侧兜底）。 */
  setInterval(function () {
    if (!S.root) return;
    var alive = false;
    try { alive = S.root.isConnected; } catch (e) {}
    if (alive) return;
    S.root = null; S.side = S.cfg = S.sub = S.toastEl = null;
    for (var id in TH) if (TH[id] && TH[id].timer != null) clearTimeout(TH[id].timer);
    TH = Object.create(null);
    window.__mcOutbox.push({ type: 'MC_CLOSE', why: 'host-doc-gone' });
  }, 1000);
})();
