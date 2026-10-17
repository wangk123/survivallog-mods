(function () {
  if (window.__SLTF__) return;
  var LS_KEY = 'sltf_sel_v3'; // v2 全局单值废弃、不迁移（旧键 sltf_sel_v2 残留无害）；v3 按界面记忆
  var S = { sel: {}, owners: {}, lib: [], dim: {}, foodId: 0, kind: {}, scenario: {}, matId: 0, toolId: 0, matFood: false, frames: [] };
  try { S.sel = JSON.parse(localStorage.getItem(LS_KEY) || '{}') || {}; } catch (e) {} // { pageId: sel }，sel = tagId 数字 | 'other' | 0
  // ---- 消息层扣发 + 伪造切换（2026-10-15 七轮，根治到头闪烁）----
  // 官方 A/D 步进切到被藏柜子时会推一条 active=被藏柜 的 BagMsg——放行则页面先渲染它一帧
  // （内容闪现），DOM 层弹回要等 Unity 往返再渲染第二次。根治：截读时发现 active 被藏就
  // **扣下不放行**（页面完全不动=零中间帧），按弹回目标**伪造页面同款切换事件**让 C# 直达
  // 正确柜子，其回包 BagMsg 正常放行；300ms 超时兜底放行原消息（降级为 DOM 弹回，即旧行为）。
  // 事件格式与页面原生点击完全一致（各页实证）：tt=TAB_SWITCH{tab}、bag=SWITCH_BAG{ownerId}、
  // trade=SWITCH_BAG{ownerId:String}、cart/cart0=TAB_SWITCH{tab}。Root 转发只认 sourcePageId
  //（=iframe 元素 id，resolveSourceId 的 claimed 分支），从 Root 帧发即模拟了页面来源。
  function rebuildLib() {
    S.dim = {};
    S.foodId = 0;
    S.matId = 0;
    S.toolId = 0;
    for (var i = 0; i < S.lib.length; i++) {
      var e = S.lib[i];
      S.dim[e.id] = e.dim;
      if (e.dim === 4) S.foodId = e.id; // 全部食物（食物维度的代表标签）
      // v3：lib 条目带 icon——材料/工具维度代表标签由此求（material 视图判定与材料架伪标签消费）
      if (e.icon === 'material') S.matId = e.id;
      if (e.icon === 'tool') S.toolId = e.id;
    }
  }
  rebuildLib();

  // v3 归一化：owners 值从 [tagId...] 数组改为 {t:[...],kind:'c'|'f'|'r'} 对象——读取统一走
  // 本函数，缺省 kind='c'。视图判定（T6）经 shouldHide→viewAllows 直接消费 v3 对象。
  function ownerOf(k) {
    var o = S.owners[k];
    if (o == null) return { t: [], kind: 'c' };
    if (Array.isArray(o)) return { t: o, kind: 'c' }; // 旧 v2 数组形状兜底（C# 已只发 v3，正常不会再来）
    return { t: o.t || [], kind: o.kind || 'c' };
  }

  // ---- 记忆层 v3（按界面）：每页独立选择，键=iframe 元素 id ----
  function pageKey(st) { return st.el && st.el.id ? st.el.id : ('f' + S.frames.indexOf(st)); }
  function pageSel(st) { return S.sel[pageKey(st)] || 0; }
  function frameMode(st) { return S.scenario[pageKey(st)] || 'browser'; } // 'food'|'material'|'browser'（cfg 驱动，C# scenario 段）
  function setSel(st, v) {
    S.sel[pageKey(st)] = v || 0;
    try { localStorage.setItem(LS_KEY, JSON.stringify(S.sel)); } catch (e) {}
    st.lastActiveIdx = null; // 玩家主动换筛选：方向无意义，回到『第一个可见』语义
    applyFrame(st);
    refreshDD(st); // 同步刷触发器文案/图标（__sltfKey 缓存幂等零抖动；不刷则最长等下一次 observer 回调≈5s 陈旧）
  }

  // 食物域命中：任一标签落在 Dim1-4（原 matchesSel 的 foodId 分支抽出，语义不变）
  function matchesFood(t) {
    for (var i = 0; i < t.length; i++) { var d = S.dim[t[i]] || 0; if (d >= 1 && d <= 4) return true; }
    return false;
  }
  // 视图谓词：o = {t:[tagId...], kind:'c'|'f'|'r'}，返回 true=该容器在本界面可见。
  // browser：sel 语义同 v2（0=全部/foodId=食物域/其他=精确命中）+「其他」=未打标签容器 + 材料架伪标签；
  // food：全部冰箱一律显示（无论有无标签），柜子/材料架按食物域（材料架伪标签必然不中→隐藏）；
  // material：材料+工具+未打标签柜、材料架显、冰箱藏（S.matFood=T9 的 cfg 钩子，默认 false）。
  function viewAllows(st, o) {
    var m = frameMode(st);
    if (m === 'browser') {
      var sel = pageSel(st);
      if (!sel) return true;
      if (sel === 'other') return o.t.length === 0 && o.kind !== 'r';   // 「其他」=未打标签容器
      if (sel === S.foodId) return matchesFood(o.t);
      return o.t.indexOf(sel) >= 0 || (o.kind === 'r' && sel === S.matId); // 材料架伪标签
    }
    if (m === 'food') {
      if (o.kind === 'f') return true;                    // 全部冰箱一律显示
      return matchesFood(o.t);                            // 柜子/材料架按食物域（材料架必然不中→隐藏）
    }
    // material：材料+工具+未标签柜；不含冰箱；材料架显
    if (o.kind === 'r') return true;
    if (o.kind === 'f') return false;
    return o.t.length === 0 || o.t.indexOf(S.matId) >= 0 || o.t.indexOf(S.toolId) >= 0
        || (S.matFood && matchesFood(o.t));               // S.matFood：K2 取证结论为「工作台需含食物域」时才加 cfg Scenario.MaterialIncludeFood 并经 scenario JSON 推送（当前无此键，默认 false）
  }
  function shouldHide(k, st) {
    var o = S.owners[k];
    // 不在扫描名单里的容器（owners 无此键）：v2 语义=不藏（宁可不藏也不错藏）
    return o !== undefined && !viewAllows(st, o);
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


  function releasePending(st) {
    if (!st.pending) return;
    var p = st.pending;
    st.pending = null;
    st.skipEcho = null;
    if (st.pendingTimer) { clearTimeout(st.pendingTimer); st.pendingTimer = 0; }
    try { p.orig.apply(p.win, [p.msg]); } catch (e) {}
  }

  function forgeSwitch(c, arr, offset, targetEl, cands, isEdge) {
    // targetEl（DOM 页签按钮）→ 页面同款切换事件；返回 {type, data} 或 null（发送由调用方做）
    try {
      var evType = null, evData = null;
      if (isEdge) {
        // 边界落点（背包 pin/工作台抽屉）：按界面语义直接给值；trade 的 pin 背包按钮
        // 自带 data-owner（Vue 模板实证），同样直读属性不走位置映射
        if (c.kind === 'tt') { evType = 'TAB_SWITCH'; evData = { tab: (targetEl.id === 'tab-drawer') ? 1 : 0 }; }
        else if (c.kind === 'bag') { evType = 'SWITCH_BAG'; evData = { ownerId: parseInt(arr[0], 10) }; }
        else if (c.kind === 'cart') { evType = 'TAB_SWITCH'; evData = { tab: 0 }; }
        else if (c.kind === 'trade') { evType = 'SWITCH_BAG'; evData = { ownerId: String(targetEl.getAttribute('data-owner')) }; }
        else return null;
      } else {
        var j = Array.prototype.indexOf.call(cands, targetEl);
        if (j < 0) return null;
        if (c.kind === 'tt') { evType = 'TAB_SWITCH'; evData = { tab: j + offset + 2 }; }
        else if (c.kind === 'bag') { evType = 'SWITCH_BAG'; evData = { ownerId: parseInt(arr[j + offset], 10) }; }
        // trade 是 attr 制：目标柜号直接读元素的 data-owner——**不可用 arr[j] 位置映射**
        //（数组首位是 pin 背包而 cands 不含 pin，恒错位 1：伪造切到错柜→错柜也被藏→回包
        //  再命中拦截→循环伪造 = 消息风暴卡死，2026-10-15 十轮实锤）
        else if (c.kind === 'trade') { evType = 'SWITCH_BAG'; evData = { ownerId: String(targetEl.getAttribute('data-owner')) }; }
        else { evType = 'TAB_SWITCH'; evData = { tab: j + offset }; }
      }
      if (evType == null || evData == null) return null;
      return { type: evType, data: evData };
    } catch (e) { return null; }
  }

  // 返回 true=消息已被扣下（调用方不得再放行）
  function tryHold(st, msg, arr, orig, win) {
    try {
      // 失败点打点（mis1-9）：拦截没跑成时留下原因码，不再靠推理定位
      if (!S.sel || !st.doc || !st.doc.body) { return false; }
      var c = detectC(st.doc);
      if (!c) { return false; }
      var d = msg && msg.data;
      if (typeof d === 'string') { try { d = JSON.parse(d); } catch (e) { return false; } }
      if (!d) { return false; }
      // active（官方语义）→ 数组下标 ai
      var ai = -1;
      if (c.kind === 'tt') {
        var tab = d.activeLeftTab | 0;
        if (tab < 2) return false;
        ai = tab - 2;
      } else if (c.kind === 'bag') {
        if (d.bagOwnerId == null) return false;
        for (var q = 0; q < arr.length; q++) { if (String(arr[q]) === String(d.bagOwnerId)) { ai = q; break; } }
      } else if (c.kind === 'trade') {
        if (d.bagOwnerKey == null) return false;
        for (var q2 = 0; q2 < arr.length; q2++) { if (String(arr[q2]) === String(d.bagOwnerKey)) { ai = q2; break; } }
      } else {
        var t2 = d.activeTab | 0;
        ai = t2;
      }
      if (ai < 0 || ai >= arr.length) return false;
      if (arr[ai] == null || !shouldHide(String(arr[ai]), st)) { return false; }
      // 弹回目标（DOM 未变，active 仍在旧柜上——方向计算按 DOM 现状）
      var cands = c.list.querySelectorAll(c.sel);
      var offset = computeOffset(c, cands, arr);
      if (offset < -100) { return false; }
      var domIdx = c.attr
        ? (function () { for (var w = 0; w < cands.length; w++) { if (cands[w].getAttribute(c.attr) === String(arr[ai])) return w; } return -1; })()
        : ai - offset;
      if (domIdx < 0) { return false; }
      var r = pickBounceTarget(st, c, cands, domIdx);
      if (!r) { return false; }
      var isEdge = Array.prototype.indexOf.call(cands, r.el) < 0; // 边界落点（pin 背包/抽屉，不在柜子 cands 里）
      var ev = forgeSwitch(c, arr, offset, r.el, cands, isEdge);
      if (!ev) { return false; }
      window.postMessage({ type: ev.type, data: ev.data, sourcePageId: st.el.id }, '*');
      // 到头弹回原地：伪造目标=画面正在显示的柜子，其回包与现状完全相同——记下目标值，
      // 回包到达时直接丢弃（无人机站/烹饪等页签无短路、收到消息就整条重建，放行=白闪一帧）
      st.skipEcho = r.bounced ? { v: String(ev.data.ownerId != null ? ev.data.ownerId : ev.data.tab), until: Date.now() + 250 } : null;
      st.pending = { msg: msg, orig: orig, win: win };
      st.pendingTimer = setTimeout(function () { st.pendingTimer = 0; releasePending(st); }, 300);
      return true;
    } catch (e) { return false; }
  }

  function wrapRelay(el, st) {
    try {
      var win = el.contentWindow;
      if (!win || win.__sltfWrapped) return true;
      var orig = win.postMessage;
      win.__sltfWrapped = 1;
      win.postMessage = function (msg) {
        // 任何 *_BagMsg 类型消息都算『状态已前进』——交易页等界面的回包只带 active 字段
        // 不带页签数组（ReactiveProperty 独立推送），扣发清理不能依赖 extractTabs 命中，
        // 否则回包被无视、被扣的旧消息 300ms 超时乱序放行 → 页面与 C# 状态错位卡死
        //（2026-10-15 七轮交易页卡死根因；九轮实验日志实锤回包 +35ms 到达且伪造生效）。
        var isBag = !!(msg && typeof msg.type === 'string' && msg.type.indexOf('BagMsg') >= 0);
        // **按内容判定（2026-10-16 十七轮）**：存在 type 不含 BagMsg、但 data 里带『当前柜子』
        // 字段的轻量消息——漏过扣发直接渲染被藏柜=玩家看到被过滤柜子闪现（用户描述+日志
        // 隐身模式实锤：闪的时刻 HOLD/PROBE 全无记录）。凡携带 active 字段的消息一律纳入。
        var hasActive = false;
        if (!isBag && msg && msg.data != null) {
          try {
            var da = typeof msg.data === 'string' ? JSON.parse(msg.data) : msg.data;
            hasActive = !!(da && (da.bagOwnerId != null || da.bagOwnerKey != null || da.activeLeftTab != null || da.activeTab != null));
          } catch (e) {}
        }
        var relevant = isBag || hasActive;
        var t = extractTabs(msg);
        if (relevant) {
          // 到头弹回的伪造回声：active 与画面正在显示的状态完全相同——**窗口内持续丢弃**。
          // 一次切换 C# 会推多条消息（字段独立推送：active 一条、tabs 一条），只丢第一条
          // 的话紧跟的第二条照样触发整条重建=白闪（十三轮实锤）。窗口 250ms 覆盖全部回声、
          // 远小于玩家下一次按键间隔；真实切换（active 变化）不匹配即放行，不受影响。
          var dropped = false;
          if (st.skipEcho) {
            var act = readActive(st, msg);
            if (act != null && String(act) === st.skipEcho.v && Date.now() < st.skipEcho.until) {
              dropped = true;
            } else {
              st.skipEcho = null; // 不匹配或窗口过期：结束回声抑制
            }
          }
          if (st.pending) { // 任何 *_BagMsg 都算状态已前进（七轮教训：回包可能只带 active 字段）
            st.pending = null;
            if (st.pendingTimer) { clearTimeout(st.pendingTimer); st.pendingTimer = 0; }
          }
          if (dropped) {
            if (t) st.tabs = t;
            return; // 丢弃：画面保持现状（内容与回声一致，无信息损失；窗口后推送补齐）
          }
        }
        if (t) st.tabs = t;
        // 消息可能只带 active 不带页签数组（ReactiveProperty 独立推送：A/D 步进只改 active，
        // C# 只推该字段）——这种轻量消息同样要走扣发判定，页签数组用内存缓存（st.tabs），
        // 否则间歇漏过=间歇闪（十四轮后零 HOLD 仍闪的根因）
        if (relevant && (t || st.tabs) && tryHold(st, msg, t || st.tabs, orig, win)) return;
        return orig.apply(this, arguments);
      };
      return true;
    } catch (e) { st.xorigin = true; return false; }
  }

  // ---- 页签栏识别（按各页 DOM 实证结构）----
  // tooltable: #leftTabsScroll 里 .cabinet-tab ↔ cabinetTabs（0 起；背包 pin 在栏外，
  //            工作台抽屉 #tab-drawer 是 scroll 内第一个按钮但无 .cabinet-tab 类——不在 cands 里）
  // trade:     .bag-tabs-scroll 里 .bag-tab 自带 data-owner 属性（无人机交易，Vue 页）
  // bagtabs:   #bagTabsScroll 里 .bag-tab ↔ bagTabs（烹饪/老鼠笼/酿酒/无人机站，pin 背包
  //            在 #bagTabPinned 静态分离、不在 cands 里——DroneHub.html 实证）
  // shredder:  #tabsScroll 里 .left-tab ↔ cartTabs（pin=tabs[0] 偏移 1）
  // compressor:#tabs 里 .left-tab ↔ cartTabs（0 起，无 pin 分离）
  function detectC(doc) {
    if (doc.getElementById('leftTabsScroll')) return { kind: 'tt', list: doc.getElementById('leftTabsScroll'), sel: '.cabinet-tab', offset: 0, bar: doc.querySelector('.left-tabs'), pos: true };
    // 顺序即身份：DroneHub/Cooking/RatCage/Brew 的页签容器有 id=bagTabsScroll（bag 族，字段
    // bagOwnerId）——必须先于 trade 分支判定：两者的容器 class 同名（bag-tabs-scroll），若 trade
    // 先判会把 DroneHub 误判成 trade、读错字段名（bagOwnerKey）全部静默放行=漏网闪（2026-10-16
    // 十八轮 misB 日志实锤 frame=DroneHub type=WebUI_DroneHub_BagMsg）；TradeUI 无此 id 落回 trade。
    if (doc.getElementById('bagTabsScroll')) return { kind: 'bag', list: doc.getElementById('bagTabsScroll'), sel: '.bag-tab', offset: -1, bar: doc.getElementById('bagTabs'), pos: true };
    if (doc.querySelector('.bag-tabs-scroll .bag-tab[data-owner]')) return { kind: 'trade', list: doc.querySelector('.bag-tabs-scroll'), sel: '.bag-tab', attr: 'data-owner', bar: doc.querySelector('.bag-tabs'), pos: false };
    if (doc.getElementById('tabsScroll')) return { kind: 'cart', list: doc.getElementById('tabsScroll'), sel: '.left-tab', offset: -1, bar: doc.getElementById('tabsScroll').parentElement, pos: true };
    if (doc.getElementById('tabs')) return { kind: 'cart0', list: doc.getElementById('tabs'), sel: '.left-tab', offset: 0, bar: doc.getElementById('tabs'), pos: true };
    return null;
  }

  // cands 下标 → arr 下标的偏移（pin 背包分离时=1）；对不上返回 -999（宁可不藏也不错藏）
  function computeOffset(c, cands, arr) {
    var offset = c.offset;
    if (!c.pos) return 0;
    if (offset < 0) offset = (cands.length === arr.length) ? 0 : ((cands.length === arr.length - 1) ? 1 : -1);
    return offset < 0 ? -999 : offset;
  }

  // 消息里的 active 字段（各页语义，与 tryHold 的读取一致）
  function readActive(st, msg) {
    try {
      var c = detectC(st.doc);
      if (!c) return null;
      var d = msg && msg.data;
      if (typeof d === 'string') { try { d = JSON.parse(d); } catch (e) { return null; } }
      if (!d) return null;
      if (c.kind === 'tt') return d.activeLeftTab;
      if (c.kind === 'bag') return d.bagOwnerId;
      if (c.kind === 'trade') return d.bagOwnerKey;
      return d.activeTab;
    } catch (e) { return null; }
  }

  // 页签按钮判定（.left-tab/.bag-tab 覆盖全部界面；滚动箭头 .tab-scroll-btn 等不匹配自动排除）
  function isTabBtn(el) {
    return !!el && el.nodeType === 1
      && (el.classList.contains('left-tab') || el.classList.contains('bag-tab'))
      && el.style.display !== 'none' && !el.disabled;
  }

  // 向左穿出整片被藏柜子区后的官方落点（通用，替代按页硬编码 edges）：从第一个柜子按钮起
  // 沿 DOM 向左找最近的可用页签按钮——先在页签容器内走左兄弟链（工作台抽屉 #tab-drawer
  // 就在这里），到头跳出容器后对每个左兄弟块取其内部**最右**的页签按钮（背包 pin 槽：
  // 工作台 .left-tab-pinned、无人机站/烹饪 .bag-tab-pinned-slot）。找 null 则由调用方兜底。
  function leftEdgeTab(cands, c) {
    for (var p = (cands.length ? cands[0].previousElementSibling : null); p; p = p.previousElementSibling) {
      if (isTabBtn(p)) return p;
    }
    for (var b = c.list.previousElementSibling; b; b = b.previousElementSibling) {
      if (isTabBtn(b)) return b;
      var inner = b.querySelectorAll('.left-tab, .bag-tab');
      for (var i = inner.length - 1; i >= 0; i--) if (isTabBtn(inner[i])) return inner[i];
    }
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

  // 弹回原地（方向到头、目标=上一个可见柜）时冻结内容区一拍：第一次 BagMsg 已把被藏柜子
  // 的物品渲染进内容区，弹回的第二次 BagMsg 要等 Unity 往返——中间那一帧会闪现被藏柜子的
  // 内容。冻结让这一帧内容空白（页签栏有预高亮，玩家看到的是『按了没变化』= 到头的正确
  // 反馈），第二次 BagMsg 渲染完成（下一次 applyFrame）即解冻；超时兜底防二次消息不来。
  function unfreeze(st) {
    if (st.freezeTimer) { clearTimeout(st.freezeTimer); st.freezeTimer = 0; }
    for (var i = 0; i < st.frozen.length; i++) { var f = st.frozen[i]; if (f && f.el) f.el.style.visibility = f.prev; }
    st.frozen.length = 0;
  }
  function freezeContent(st, c) {
    unfreeze(st);
    st.freezeAt = Date.now();
    var host = (c.bar && c.bar.parentElement) || st.doc.body;
    if (!host || !host.children) return;
    for (var i = 0; i < host.children.length; i++) {
      var el = host.children[i];
      if (el === c.bar) continue;
      st.frozen.push({ el: el, prev: el.style.visibility });
      el.style.visibility = 'hidden';
    }
    st.freezeTimer = setTimeout(function () { st.freezeTimer = 0; unfreeze(st); }, 250);
  }

  // 弹回目标计算（纯函数，DOM 弹回与消息层扣发共用）：activeHiddenIdx=被藏 active 在 cands
  // 里的下标，方向由 st.lastActiveIdx 对比得出。返回 {el, bounced} 或 null。
  function pickBounceTarget(st, c, cands, activeHiddenIdx) {
    var dirPick = 0;
    if (activeHiddenIdx >= 0 && st.lastActiveIdx != null && st.lastActiveIdx >= 0) {
      if (activeHiddenIdx > st.lastActiveIdx) dirPick = 1;
      else if (activeHiddenIdx < st.lastActiveIdx) dirPick = -1;
    }
    var target = dirPick ? nearestVisible(cands, activeHiddenIdx, dirPick) : null;
    var bounced = false; // 弹回原地（方向到头、目标=上一个 active 可见柜）
    if (!target && dirPick < 0) {
      // 向左穿出整片被藏柜子区：落点=DOM 上紧邻柜子区左侧的可用页签（通用探测——
      // 工作台=抽屉/背包、无人机站/烹饪/粉碎机=pin 背包，各页结构不同但都在左边）
      target = leftEdgeTab(cands, c);
    }
    if (!target && dirPick) {
      target = nearestVisible(cands, activeHiddenIdx, -dirPick);
      if (target && st.lastActiveIdx != null && st.lastActiveIdx >= 0 && st.lastActiveIdx < cands.length && target === cands[st.lastActiveIdx]) bounced = true;
    }
    if (!target) {
      for (var m = 0; m < cands.length; m++) {
        if (cands[m].style.display !== 'none' && !cands[m].disabled) { target = cands[m]; break; }
      }
    }
    return target ? { el: target, bounced: bounced } : null;
  }

  function applyFrame(st, c) {
    try {
      if (!st.doc || !st.doc.body) return;
      c = c || detectC(st.doc);
      if (!c) return;
      unfreeze(st);
      for (var i = 0; i < st.hidden.length; i++) { var h = st.hidden[i]; if (h && h.el) h.el.style.display = h.prev; }
      st.hidden.length = 0;
      if (!S.sel) return; // v3 恒为对象（恒过）：固定视图页 sel=0 也要套隐藏，判定全在 shouldHide/viewAllows
      var cands = c.list.querySelectorAll(c.sel);
      var arr = c.pos ? st.tabs : null;
      var offset = c.pos ? computeOffset(c, cands, arr || []) : 0;
      if (c.pos) {
        if (!arr) return;
        if (offset < 0) return; // 页签数与数据对不上（页面结构变化），宁可不藏也不错藏
      }
      var activeIdx = -1, activeHiddenIdx = -1;
      for (var j = 0; j < cands.length; j++) {
        var el = cands[j];
        var isActive = el.classList.contains('active');
        if (isActive) activeIdx = j;
        var k = c.attr ? el.getAttribute(c.attr) : arr[j + offset];
        if (k == null) continue;
        if (shouldHide(String(k), st)) {
          st.hidden.push({ el: el, prev: el.style.display });
          if (isActive) activeHiddenIdx = j;
          el.style.display = 'none';
        }
      }
      // 当前打开的页签被藏（多为官方 A/D 步进切到被藏柜子——C# 侧下标±1，看不见隐藏）：
      // 按移动方向点同方向最近可见页签（=替玩家跳过被藏柜子）。此为消息层扣发（tryHold）
      // 的兜底路径——正常情况 BagMsg 在 wrapRelay 就被扣下并伪造切换，不会走到这里。
      if (activeHiddenIdx >= 0) {
        var r = pickBounceTarget(st, c, cands, activeHiddenIdx);
        if (r) {
          var target = r.el;
          // 预高亮：弹回要经官方链走第二次 BagMsg 整条重建，两次重建之间有一帧——先把高亮
          // 挪到目标上，页签栏就不会闪『无高亮』帧（第二次重建后官方状态自然接管）
          if (activeHiddenIdx < cands.length && cands[activeHiddenIdx].classList.contains('active')) cands[activeHiddenIdx].classList.remove('active');
          target.classList.add('active');
          // 冻结内容区覆盖全部弹回场景（十七轮前仅 bounced）：消息层拦截失效时这里是唯一防线，
          // 冻结把『闪被藏柜内容』降级为『空白一拍回到原柜』——弹回的第二次渲染到达即解冻
          freezeContent(st, c);
          target.click();
        }
      }
      st.lastActiveIdx = activeIdx;
    } catch (e) {}
  }

  // 下拉选项：「其他」（未打标签容器）+ 食物汇总（Dim4 代表）+ 物资（Dim0）逐个按官方 Order——
  // 全量显示，不按使用裁剪。「全部柜子」由 refreshDD 补在首位，最终排列=全部柜子→其他→食物→物资
  function options() {
    var food = null, supplies = [];
    for (var i = 0; i < S.lib.length; i++) {
      var e = S.lib[i];
      if (e.dim === 4) { food = e; }
      else if (e.dim === 0) { supplies.push(e); }
    }
    supplies.sort(function (a, b) { return (a.order || 0) - (b.order || 0) || a.id - b.id; });
    var out = [{ id: 'other', name: '其他' }];
    if (food) out.push({ id: food.id, name: food.name || '食物' });
    for (var j = 0; j < supplies.length; j++) out.push({ id: supplies[j].id, name: supplies[j].name || ('#' + supplies[j].id) });
    return out;
  }

  // ---- 自绘下拉（T7）：触发器=列容器内绝对定位左下+测量避让（2026-10-17 作者反馈三轮定稿）、popup 防裁切、官方图标/配色 ----
  // 落位由 placeDD 每次 refreshDD 重算，不写死坐标（作者核心诉求）：旁列按钮（外来 mod 注入的
  // 排序/视图按钮）卸载后自动左挪贴边。换位背景（作者实测）：挂页签行末会被 DroneHub 官方
  // 「一键整理」按钮与交易页他 mod 排序按钮挤变形；上一版挂 body+fixed 视口角落又跑出界面面板外。
  // 避让测量阈值（px，集中可调）：band=列底带高度（带内元素才参与避让）、wide=宽底栏阈值
  // （如 DroneHub 负重条 #bagCap 横贯整列→排它上方）、nearLeft=左区阈值（窄按钮贴列左才避让，
  // 交易页 sort-btn 在右远端不挤它、它也不挤我们）。
  var DD_AVOID = { band: 30, wide: 200, nearLeft: 250 };
  // 官方 TAG_ICONS 静态兜底：33 键与 BackpackUI.html 的 const TAG_ICONS 同源（SVG 字符串原样复制）。
  // 官方 svg 无 fill 属性（BackpackUI 用 CSS color 控色），注入侧由 iconSvg 统一补 fill=currentColor。
  // 官方图标库更新时 diff 游戏目录快照同步本副本（保留官方分批注释便于对齐）。
  var TAG_ICONS_FALLBACK = {
    food: '<svg viewBox="0 0 24 24"><path d="M2.6 10.6h18.8a9.4 9.4 0 0 1-18.8 0z"/><rect x="1.4" y="19.8" width="21.2" height="2.3" rx="1.15"/><rect x="7.6" y="2.2" width="1.9" height="5.6" rx=".95" opacity=".6"/><rect x="14.5" y="2.2" width="1.9" height="5.6" rx=".95" opacity=".6"/></svg>',
    dish: '<svg viewBox="0 0 24 24"><path d="M3.4 14.6a8.6 8.6 0 0 1 17.2 0z"/><rect x="1.6" y="15.4" width="20.8" height="2.4" rx="1.2"/><circle cx="12" cy="3.8" r="1.7"/></svg>',
    ingredient: '<svg viewBox="0 0 24 24"><path d="M6.2 7.4h11.6l1.5 12a2.1 2.1 0 0 1-2.1 2.4H6.8a2.1 2.1 0 0 1-2.1-2.4z"/><path d="M6.2 7.4 7.8 3.4h8.4l1.6 4z" opacity=".62"/></svg>',
    veg: '<svg viewBox="0 0 24 24"><path d="M12 21c-4 0-6.4-3.2-6.4-7.2C5.6 10.4 8 8.4 12 8.4s6.4 2 6.4 5.4c0 4-2.4 7.2-6.4 7.2z"/><path d="M12 8.6c0-3 1.8-5 4.6-5.6.3 3-1.4 5.2-4.6 5.6z" opacity=".7"/></svg>',
    prey: '<svg viewBox="0 0 24 24"><ellipse cx="12" cy="16.4" rx="5.4" ry="4.4"/><circle cx="6.2" cy="9.2" r="2.3"/><circle cx="10.3" cy="6.2" r="2.3"/><circle cx="14.6" cy="6.2" r="2.3"/><circle cx="18.2" cy="9.6" r="2.3"/></svg>',
    cold: '<svg viewBox="0 0 24 24"><rect x="10.9" y="1.6" width="2.2" height="20.8" rx="1.1"/><rect x="10.9" y="1.6" width="2.2" height="20.8" rx="1.1" transform="rotate(60 12 12)"/><rect x="10.9" y="1.6" width="2.2" height="20.8" rx="1.1" transform="rotate(120 12 12)"/></svg>',
    seed: '<svg viewBox="0 0 24 24"><rect x="11" y="9.5" width="2" height="11.5"/><path d="M11 11C8 11 5.5 9 5 5.6c3.6-.4 6 1.6 6 5.4z"/><path d="M13 11c3 0 5.5-2 6-5.4-3.6-.4-6 1.6-6 5.4z" opacity=".72"/></svg>',
    drink: '<svg viewBox="0 0 24 24"><path d="M6.5 4h11l-1.6 16.2a1.6 1.6 0 0 1-1.6 1.4h-4.6a1.6 1.6 0 0 1-1.6-1.4z"/><rect x="7.6" y="8.6" width="8.8" height="1.8" opacity=".45"/></svg>',
    med: '<svg viewBox="0 0 24 24"><rect x="3.5" y="5.5" width="17" height="13" rx="2.6"/><rect x="11" y="8.4" width="2" height="7.2" fill="#fff"/><rect x="8.4" y="11" width="7.2" height="2" fill="#fff"/></svg>',
    daily: '<svg viewBox="0 0 24 24"><path d="M9.6 1.8h4.8v3.4l2.6 3.4a4 4 0 0 1 .8 2.4v9a2 2 0 0 1-2 2H8.2a2 2 0 0 1-2-2v-9a4 4 0 0 1 .8-2.4l2.6-3.4z"/><rect x="7.4" y="13" width="9.2" height="1.8" fill="#fff" opacity=".45"/></svg>',
    tool: '<svg viewBox="0 0 24 24"><rect x="3.5" y="3" width="17" height="5.4" rx="1.4"/><rect x="9.6" y="8.4" width="4.8" height="12.6" rx="1"/></svg>',
    material: '<svg viewBox="0 0 24 24"><rect x="1.6" y="13.6" width="9.6" height="7.4" rx="1.2"/><rect x="12.8" y="13.6" width="9.6" height="7.4" rx="1.2"/><rect x="7.2" y="5" width="9.6" height="7.4" rx="1.2" opacity=".78"/></svg>',
    fuel: '<svg viewBox="0 0 24 24"><rect x="6" y="8" width="12" height="13" rx="2"/><rect x="9.5" y="5.5" width="5" height="3"/><path d="M12 1.5c2.4 2 3.4 3.4 3.4 4.7A3.4 3.4 0 0 1 12 9.6 3.4 3.4 0 0 1 8.6 6.2c0-1.3 1-2.7 3.4-4.7z" opacity=".55"/></svg>',
    fertilizer: '<svg viewBox="0 0 24 24"><path d="M2.4 21.5a9.6 6 0 0 1 19.2 0z"/><rect x="11.1" y="11.8" width="1.8" height="7.2"/><path d="M11.1 13.2c-2.4 0-4.2-1.7-4.6-4.3 2.9-.3 4.6 1.3 4.6 4.3z"/><path d="M12.9 13.2c2.4 0 4.2-1.7 4.6-4.3-2.9-.3-4.6 1.3-4.6 4.3z" opacity=".72"/><circle cx="5.2" cy="14.6" r="1.3" opacity=".6"/><circle cx="18.8" cy="14.6" r="1.3" opacity=".6"/></svg>',
    defense: '<svg viewBox="0 0 24 24"><path d="M12 2.2 20 5v7.2c0 4.6-3.2 8.2-8 9.6-4.8-1.4-8-5-8-9.6V5z"/><path d="M12 6.5v11" stroke="#fff" stroke-width="1.6" opacity=".5"/></svg>',
    book: '<svg viewBox="0 0 24 24"><path d="M4 3.5h7.2v17H4z"/><path d="M12.8 3.5H20v17h-7.2z" opacity=".72"/><rect x="11.2" y="3.5" width="1.6" height="17" opacity=".4"/></svg>',
    elec: '<svg viewBox="0 0 24 24"><path d="M13.6 1.5 5 13.4h5.3L9.4 22.5 19 10.2h-5.6z"/></svg>',
    special: '<svg viewBox="0 0 24 24"><path d="M12 1.8l3.1 6.9 7.5.8-5.6 5.1 1.6 7.4L12 18.2l-6.6 3.8 1.6-7.4-5.6-5.1 7.5-.8z"/></svg>',
    misc: '<svg viewBox="0 0 24 24"><circle cx="7" cy="7" r="3.2"/><rect x="13.4" y="3.8" width="6.6" height="6.6" rx="1.4"/><rect x="4" y="13.6" width="6.6" height="6.6" rx="1.4" opacity=".72"/><circle cx="17" cy="17" r="3.2" opacity=".72"/></svg>',
    // 2026-09-14 新增 8 个选项的占位图标（美术出图后替换）
    ready: '<svg viewBox="0 0 24 24"><rect x="5" y="4" width="14" height="17" rx="2.5"/><rect x="5" y="8" width="14" height="1.6" opacity=".45"/><rect x="5" y="15" width="14" height="1.6" opacity=".45"/></svg>',
    meat: '<svg viewBox="0 0 24 24"><path d="M14.5 3.5c3.5 0 6 2.6 6 6 0 4.8-4.6 8.8-9.6 8.8-1.6 0-2.6-.4-3.4-1.1L4.6 20.1l-1.6-1.6 2.9-2.9c-.7-.8-1.1-1.8-1.1-3.4 0-5 4-8.7 9.7-8.7z"/></svg>',
    fish: '<svg viewBox="0 0 24 24"><path d="M2.5 12c3-4.2 7-6 11-5.4 2.6.4 4.6 1.9 6 3.6L22 7v10l-2.5-3.2c-1.4 1.7-3.4 3.2-6 3.6-4 .6-8-1.2-11-5.4z"/></svg>',
    egg: '<svg viewBox="0 0 24 24"><path d="M12 2.5c3.8 0 6.8 6 6.8 10.4a6.8 6.8 0 0 1-13.6 0C5.2 8.5 8.2 2.5 12 2.5z"/></svg>',
    mushroom: '<svg viewBox="0 0 24 24"><path d="M2.5 11.5a9.5 7.5 0 0 1 19 0z"/><rect x="9.4" y="11.5" width="5.2" height="9.5" rx="2"/></svg>',
    grain: '<svg viewBox="0 0 24 24"><rect x="11.1" y="3" width="1.8" height="18.5"/><ellipse cx="9" cy="8" rx="2" ry="3.2" transform="rotate(-25 9 8)"/><ellipse cx="15" cy="8" rx="2" ry="3.2" transform="rotate(25 15 8)"/><ellipse cx="9" cy="13.5" rx="2" ry="3.2" transform="rotate(-25 9 13.5)"/><ellipse cx="15" cy="13.5" rx="2" ry="3.2" transform="rotate(25 15 13.5)"/></svg>',
    spice: '<svg viewBox="0 0 24 24"><rect x="8" y="2.5" width="8" height="3.5" rx="1"/><path d="M7 7h10l1 3v9.5a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V10z"/></svg>',
    snack: '<svg viewBox="0 0 24 24"><path d="M4 6.5 7 3h10l3 3.5-2 1 1.2 12.5H4.8L6 7.5z"/></svg>',
    // 2026-09-23 物品大类重分：剧情道具 / 收藏品 / 唱片 / 装修材料（占位图标，美术出图后替换）
    story: '<svg viewBox="0 0 24 24"><path d="M5 2.5h9.5L19 7v14.5H5z"/><path d="M14.5 2.5V7H19z" opacity=".6"/><rect x="7.6" y="10.6" width="8.8" height="1.7" fill="#fff" opacity=".55"/><rect x="7.6" y="14.2" width="8.8" height="1.7" fill="#fff" opacity=".55"/><rect x="7.6" y="17.8" width="5.4" height="1.7" fill="#fff" opacity=".55"/></svg>',
    collect: '<svg viewBox="0 0 24 24"><path d="M6.8 3.2h10.4L21.8 9 12 21.4 2.2 9z"/><path d="M2.2 9h19.6L12 21.4z" opacity=".72"/><path d="M9.6 3.2 7.8 9 12 21.4 16.2 9l-1.8-5.8z" fill="#fff" opacity=".3"/></svg>',
    record: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10.2"/><circle cx="12" cy="12" r="6.8" fill="none" stroke="#fff" stroke-width="1" opacity=".4"/><circle cx="12" cy="12" r="3.2" fill="#fff" opacity=".6"/><circle cx="12" cy="12" r="1.1"/></svg>',
    renov: '<svg viewBox="0 0 24 24"><rect x="3" y="2.6" width="15.6" height="6.4" rx="1.6"/><path d="M18.6 5h1.6a1.2 1.2 0 0 1 1.2 1.2v4.2a1.2 1.2 0 0 1-1.2 1.2h-8.4v2.6" fill="none" stroke="currentColor" stroke-width="1.8" opacity=".72"/><rect x="10" y="13.6" width="3.6" height="8.2" rx="1.2"/></svg>',
    // 2026-09-29 任务道具（从剧情道具拆出，占位图标）
    task: '<svg viewBox="0 0 24 24"><rect x="4.5" y="4" width="15" height="18" rx="2"/><rect x="8.5" y="2" width="7" height="4" rx="1.2" opacity=".72"/><path d="M8 13.4l2.8 2.8 5.6-5.6" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" opacity=".6"/></svg>',
    // 2026-10-09 防御耗材（门窗修补 / 加固件从材料拆出，占位图标）
    repair: '<svg viewBox="0 0 24 24"><path d="M15.6 2.4a5.2 5.2 0 0 0-4.9 6.9L2.9 17.1 2.7 19.6l1.7 1.7 2.5-.2 7.8-7.8a5.2 5.2 0 0 0 6.9-4.9l-.1-1-3 3-2.6-.6-.6-2.6 3-3z"/></svg>'
  };
  // 「全部柜子」图标：三横线=列表（33 键无通用列表键，控制器裁定用内联 svg）
  var ICON_ALL_SVG = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 5h18v2H3zm0 6h18v2H3zm0 6h18v2H3z"/></svg>';

  // 图标取值：优先 BackpackUI 帧的 TAG_ICONS（同源提取，官方更新自动跟上；BackpackUI 多数时候
  // 未打开→null→走兜底）；未知键兜底 misc（与官方 tagIconSvg 的 || TAG_ICONS.misc 同款）。
  // 从 Root 帧直查 iframe，不需要页面 doc 参数（控制器裁定）。
  function tagIcon(key) {
    try {
      var f = document.querySelector('#iframe-container iframe#BackpackUI');
      var icons = f && f.contentWindow ? f.contentWindow.eval('TAG_ICONS') : null;
      if (icons && icons[key]) return icons[key];
    } catch (e) {}
    return TAG_ICONS_FALLBACK[key] || TAG_ICONS_FALLBACK.misc;
  }
  // 官方 svg 字符串补齐尺寸/填色：默认 14x14、触发器传 13（对齐 gbtn.sm 63×21 配套）；根节点无 fill 时补 fill="currentColor"
  //（由所在元素 style.color 控色=官方 BackpackUI 同款机制；子路径自带 fill="#fff" 等保持不动）
  function iconSvg(html, size) {
    var s = String(html || '');
    if (s.indexOf('<svg') !== 0) return '';
    var head = s.substring(0, s.indexOf('>'));
    var sz = size || 14;
    var add = ' width="' + sz + '" height="' + sz + '" style="display:block"';
    if (!/\sfill\s*=/.test(head)) add += ' fill="currentColor"';
    return '<svg' + add + s.slice(4);
  }
  // 选中值 → lib 条目（sel=lib id 命中；'other'/0 返回 null，由调用方分别给「其他」/「全部柜子」文案）
  function selById(sel) {
    for (var i = 0; i < S.lib.length; i++) if (S.lib[i].id === sel) return S.lib[i];
    return null;
  }
  // 外观态样式表：**逐字克隆 BetterTrade 下拉库 CSS + 跨页视觉归一**。
  // 克隆源=其 dll 内嵌 page.js 提取的 .PFX-btn/.PFX-active/.PFX-menu/.PFX-on 原文（PFX=bt-view，
  // token：fg #fff、accent #fbb034、menu-bg #1d1e22）。**两页缩放不同**（交易≈0.62/调度机队≈0.85），
  // 同一 css 值视觉大小必然不同（作者实测两页不一致根因）——所有计量值按「视觉 px ÷ k」生成：
  // 视觉值=它的 css × DD_VIS（0.647=交易页校准系数），k=按页面查静态表（DD_K_TABLE，零测量）
  // placeDD 会带新 k 重生成。选中某标签→.on 金色激活态（其排序激活同语义）。内联只留布局。
  var DD_VIS = 0.647; // 交易页校准：css×0.647=视觉 px
  // 各页静态缩放表（页面 id → 典型 k，作者机 1536×864 实测）：交易=祖先层 #app 缩放、
  // 调度机队=body zoom。零测量路线的唯一数据源，改分辨率/游戏改版时更新此表。
  var DD_K_TABLE = { Trade: 0.62, DroneHub: 0.845 };
  function ensureDDStyle(doc, k) {
    var kk = (k && k > 0.2 && k < 4) ? k : 1;
    var px = function (btCss) { return (btCss * DD_VIS / kk).toFixed(2) + 'px'; };
    var txt = '.sltf-dd-btn{font-family:inherit;font-size:' + px(12) + ';line-height:1.5;color:#fff;white-space:nowrap;'
      + 'background:rgba(255,255,255,0.06);border:' + px(1) + ' solid rgba(255,255,255,0.15);border-radius:' + px(4) + ';'
      + 'padding:' + px(4) + ' ' + px(10) + ';text-align:left;cursor:pointer;display:flex;align-items:center;gap:' + px(6) + ';}'
      + '.sltf-dd-btn:hover{border-color:rgba(251,176,52,0.6);}'
      + '.sltf-dd-btn.on{color:#fbb034;border-color:rgba(251,176,52,0.75);background:rgba(251,176,52,0.14);}'
      + '.sltf-dd-opt{font-family:inherit;font-size:' + px(12) + ';line-height:1.5;color:#fff;white-space:nowrap;'
      + 'background:rgba(255,255,255,0.06);border:' + px(1) + ' solid rgba(255,255,255,0.15);border-radius:' + px(4) + ';'
      + 'padding:' + px(4) + ' ' + px(10) + ';text-align:left;cursor:pointer;display:flex;align-items:center;gap:' + px(6) + ';}'
      + '.sltf-dd-opt:hover{border-color:rgba(251,176,52,0.6);}'
      + '.sltf-dd-opt.on{color:#fbb034;}'
      + '.sltf-dd-ico svg{display:block;}'
      + '.sltf-dd-pop{display:flex;flex-direction:column;gap:' + px(2) + ';padding:' + px(3) + ';border-radius:' + px(4) + ';'
      + 'background:#1d1e22;border:' + px(1) + ' solid rgba(255,255,255,0.15);}'
      + '.sltf-dd-pop::-webkit-scrollbar{width:6px;}'
      + '.sltf-dd-pop::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:3px;}';
    var el = doc.getElementById('sltf-dd-style');
    if (el) return; // **样式表只写一次**：已在即原样返回，任何路径（含 k 变化/重建分支）都不重写——重写=尺寸跳变之源（采样实锤）
    var st = doc.createElement('style');
    st.id = 'sltf-dd-style';
    st.textContent = txt;
    (doc.head || doc.documentElement).appendChild(st);
  }
  function closePop(st) {
    if (st.pop) { try { st.pop.remove(); } catch (e) {} st.pop = null; }
    if (st.popDown) { try { if (st.doc) st.doc.removeEventListener('mousedown', st.popDown, true); } catch (e) {} st.popDown = null; }
  }

  // 列容器查找（placeDD/togglePop 共用）：从页签行（c.bar）向上取第一个高>300 的父级为列容器
  //（Trade=.col、DroneHub=#paneMine 实测命中），走到 body/html 根则兜底 c.bar.parentElement；
  // col 为 static 时补 relative（仅 computed static 才设，幂等；body 不动——其初始包含块语义
  // 已够，改根布局风险大）。弹层也挂这里（与按钮同空间，锚定见 togglePop）。
  function ddCol(st, c) {
    try {
      if (!c || !c.bar) return null;
      var col = null;
      for (var p = c.bar.parentElement; p && p.nodeType === 1; p = p.parentElement) {
        if (p === st.doc.body || p === st.doc.documentElement) break; // 列容器=面板布局子树，不认 body/html 根（认了就退化回视口角落）
        if (p.getBoundingClientRect().height > 300) { col = p; break; }
      }
      if (!col) col = c.bar.parentElement; // 兜底：结构异常时挂页签行父级（最近的面板子树）
      if (!col || col.nodeType !== 1) return null;
      var view = st.doc.defaultView;
      if (view && col !== st.doc.body && view.getComputedStyle(col).position === 'static') col.style.position = 'relative';
      return col;
    } catch (e) { return null; }
  }


  // 触发器落位（refreshDD 每次调用）：动态锚定列容器底部左侧（ddCol）+测量避让——旁列按钮
  //（外来 mod 注入的排序/视图按钮）卸载后自动左挪贴边。换位背景（作者实测）：挂页签行末会被
  // DroneHub 官方「一键整理」按钮与交易页他 mod 排序按钮挤变形；上一版挂 body+fixed 视口角落
  // 又跑出界面面板外。避让测量阈值（px，集中可调）：band=列底带高度（带内元素才参与避让）、
  // wide=宽底栏阈值（如 DroneHub 负重条 #bagCap 横贯整列→排它上方）、nearLeft=左区阈值
  //（窄按钮贴列左才避让，交易页 sort-btn 在右远端不挤它、它也不挤我们）。
  // 换算与尺寸（fix-wave-5）：避让偏移（rect 差读出→写进 style）÷k；按钮视觉规格（高 21/
  // 字号 12/pad 横 10/icon 13/max-width 110，与官方按钮视觉等大）按 k 反推 css 值
  //（=目标/k）每轮直写（幂等：同值重写浏览器零布局变更，值变才写的门控不值得多一坨状态）。
  // 调度机队侧效果：css 25 → 视觉 21（此前 17.7，更贴官方 21）。详见 DESIGN §2.5.1。
  // 触发器落位（fix-wave-9 终版：行内插入 + BetterTrade 组件级锚点 + 样式硬抄本）。
  // 教训链（w2-w8）：绝对定位自算坐标每轮都在错（视口角落/zoom/避让污染/竖向偏差）→ w8 行内插入
  // 方向对了，但锚进了 BetterTrade 组件（bt-view 根 div）**内部**——它排序激活态的样式连坐我们
  //（作者实测：排序激活→我们也高亮）。终版锚点=组件壳之外：交易页插在 bt-view **组件壳之后**
  //（同在 .bag-toolbar 行内，行内自动对齐、互不连坐）；调度机队插进负重条行（#bagCap 官方 flex
  // 行实证）最左；都没有=col 绝对左下兜底。样式零运行时计算（ensureDDStyle 硬抄本）。
  function placeDD(st, dd, c) {
    try {
      if (!dd) return;
      var col = ddCol(st, c); // 弹层锚定空间；无锚行时兜底挂点
      var view = st.doc.defaultView;
      var refEl = null, anchorRow = null, capEl = null;
      var colR = col ? col.getBoundingClientRect() : null; // 兜底绝对定位偏移用
      // k=**静态查表**（按页面 id），零测量零计算——与 BetterTrade 的静态样式完全同构。
      // 测量路线三轮全灭：动态测→重写震荡；首帧测→闪错；测一次写死→量错成永久错误
      //（作者实测"按钮一直小+弹层飘走"）。查表无任何可出错的运行时路径；表值为
      // 作者机（1536×864）实测典型缩放。未知页面用交易页值兜底。
      var k = DD_K_TABLE[pageKey(st)] || DD_K_TABLE.Trade;
      dd.__sltfK = k;
      ensureDDStyle(st.doc, k);
      // 首选锚=.sort-btn（BetterTrade 自己的稳定锚：它每次重绘都把自己的组件 insertBefore 到
      // sort-btn 紧左边——我们若在它右边就会被它重绘挤到左边去（作者实测"顺序翻转"根因）。
      // 我们插 **sort-btn 右侧**，它的重绘永远碰不到我们，顺序稳定：[bt-view][sort][我们]）
      var sbtn = col ? col.querySelector('.sort-btn') : null;
      if (sbtn && sbtn.getBoundingClientRect().width > 4) { refEl = sbtn; anchorRow = sbtn.parentElement; }
      if (refEl) {
        if (refEl !== sbtn) {
          var host = refEl.parentElement;
          if (host && host !== col && host.children.length <= 2 && host.parentElement) refEl = host; // 组件壳升级
        }
        anchorRow = refEl.parentElement;
      } else {
        if (!capEl && col) {
          var caps = col.querySelectorAll('[class*=cap], [id*=cap]');
          for (var ci = 0; ci < caps.length; ci++) { var cr2 = caps[ci].getBoundingClientRect(); if (cr2.width > 100) { capEl = caps[ci]; break; } }
        }
        if (capEl) anchorRow = capEl; // 负重条行最左
      }
      // 锚行必须是 flex 行（行内对齐的前提；官方 .cap 与 .bag-toolbar 实证满足，守卫防结构变化）
      if (anchorRow && view && ('' + view.getComputedStyle(anchorRow).display).indexOf('flex') < 0) { anchorRow = null; refEl = null; }
      // 落位：有锚行=行内插入（static，跟随行 gap/对齐；行无 gap 再补 margin）；无锚行=col 绝对左下兜底
      var prevPar = dd.parentElement, prevNext = dd.nextElementSibling;
      if (anchorRow) {
        dd.style.position = 'static';
        dd.style.left = ''; dd.style.right = ''; dd.style.top = ''; dd.style.bottom = '';
        dd.style.flex = '0 0 auto';
        dd.style.alignSelf = 'center';
        dd.style.marginLeft = (view && parseFloat(view.getComputedStyle(anchorRow).columnGap || view.getComputedStyle(anchorRow).gap) >= 2) ? '' : '6px';
        var ref = refEl ? refEl.nextSibling : anchorRow.firstChild;
        var wantPrev = ref ? ref.previousElementSibling : (anchorRow.lastElementSibling === dd ? dd.previousElementSibling : null);
        if (dd.parentElement !== anchorRow || dd.previousElementSibling !== wantPrev) anchorRow.insertBefore(dd, ref);
      } else if (col) {
        if (dd.parentElement !== col) col.appendChild(dd);
        dd.style.position = 'absolute';
        dd.style.left = '6px';
        dd.style.bottom = (capEl ? ((colR.bottom - capEl.getBoundingClientRect().top) / k + 8) : 6) + 'px';
        dd.style.top = 'auto'; dd.style.right = 'auto';
        dd.style.flex = ''; dd.style.alignSelf = ''; dd.style.marginLeft = '';
      }
      // 挪位关旧弹层（防脱锚）
      if ((dd.parentElement !== prevPar || dd.nextElementSibling !== prevNext) && st.pop && st.pop.isConnected) closePop(st);
    } catch (e) {}
  }

  // 弹出层（fix-wave-5）：挂**列容器**（ddCol，与触发器同容器同空间——body+fixed 时代要管视口
  // 换算，现 col 内 absolute 一套 rect 差 ÷k 管到底；col 已由 ddCol 补 relative，两页 col 均无
  // overflow 裁切已核 CSS）。锚定：与按钮同空间取 rect 差（视觉空间）再除一次 k 回 css 布局
  // 空间——向上开优先（top=按钮顶-ph-4），放不下（top+ph 超列高）或出顶（top<0）改向下
  //（按钮底+4）；左=对齐按钮左缘，右溢出钳制列宽内。弹层尺寸同步按 k 反推（行高 19/字号 11/
  // icon 12/min 104/max 150/max-height 190≈10 行——视觉目标，css 值=目标/k）；ph/pw（offsetHeight
  // 等）与 col.clientWidth/clientHeight 本就是 css 布局空间，同空间比较无需再除。行多时
  // max-height+滚动（滚动逻辑不变，滚动条值克隆官方 DyePanel）。选项行/触发器都是 <button>
  //（官方 clickThrough selector 收 button/[data-interactive]；选项行再加 data-interactive 双保险，照 brief）
  function togglePop(st) {
    try {
      if (st.pop && st.pop.isConnected) { closePop(st); return; } // 开着再点按钮=关
      closePop(st);                                                 // 清不连通的残留引用（重开由本次继续）
      if (!st.doc || !st.doc.body || !st.dd || !st.dd.isConnected) return;
      var view = st.doc.defaultView;
      ensureDDStyle(st.doc);                                        // 幂等：hover 样式表意外被摘时补回
      var sel = pageSel(st);
      var k = st.dd.__sltfK || DD_K_TABLE[pageKey(st)] || 0.62; // 静态查表（仅弹层自身尺寸用；定位已零换算）
      var pop = st.doc.createElement('div');
      pop.className = 'sltf-dd-pop';
      // 外观（背景/描边/圆角/内衬/gap）全在 ensureDDStyle 的 .sltf-dd-pop（BetterTrade 菜单硬抄本）；
      // 这里只留布局定位与滚动上限（视觉目标 ÷k：宽 104-150、高 ~190≈10 行）
      pop.style.cssText = 'position:absolute;z-index:99999;visibility:hidden;'
        + 'min-width:' + Math.round(104 / k) + 'px;max-width:' + Math.round(150 / k) + 'px;max-height:' + Math.round(190 / k) + 'px;overflow-y:auto;';
      function addRow(id, name, lib) {
        var on = id === sel;
        var b = st.doc.createElement('button');
        b.type = 'button';
        b.className = 'sltf-dd-opt' + (on ? ' on' : '');
        b.setAttribute('data-interactive', '');
        // 外观/字体/内衬全在样式表（.sltf-dd-opt 硬抄本，与按钮同款）；内联只留布局
        b.style.cssText = 'width:100%;margin:0;'; // 布局only——display/gap/padding 全在样式表（归一值）
        var isz = 14; // BetterTrade .PFX-ico 同值
        var ico = st.doc.createElement('span');
        ico.style.cssText = 'display:flex;width:' + isz + 'px;height:' + isz + 'px;flex:0 0 auto;'
          + (lib && lib.color ? 'color:' + lib.color + ';' : ''); // 官方色控 svg（currentColor），空色不设
        ico.innerHTML = iconSvg(lib ? tagIcon(lib.icon) : (id === 'other' ? TAG_ICONS_FALLBACK.misc : ICON_ALL_SVG), isz);
        var txt = st.doc.createElement('span');
        txt.style.cssText = 'flex:1 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;';
        txt.textContent = name;
        b.appendChild(ico);
        b.appendChild(txt);
        if (on) { // 选中标记（BetterTrade .PFX-on 同语义：金色文字，另加 ✓）
          var ok = st.doc.createElement('span');
          ok.style.cssText = 'flex:0 0 auto;';
          ok.textContent = '\u2713';
          b.appendChild(ok);
        }
        b.addEventListener('click', function () { setSel(st, id); closePop(st); });
        pop.appendChild(b);
      }
      addRow(0, '全部柜子', null); // 首位=全部，其后 options()：其他→食物→物资（T6 排列不变）
      var opts = options();
      for (var i = 0; i < opts.length; i++) addRow(opts[i].id, opts[i].name, selById(opts[i].id));
      // 弹层挂**按钮所在的行容器**，用 offsetTop/offsetLeft 定位——布局空间原生值与 css 同一
      // 坐标系，按钮的 offset 就是弹层的坐标，**零换算零除法**（此前 rect÷k 各版换算误差被
      // 距离放大：遮盖/飘远/出屏三个症状同根因。行容器 static 时补 relative——两页行容器
      // 内无依赖定位祖先的官方绝对元素，已核）
      var row = st.dd.parentElement;
      if (!row) return;
      if (view && view.getComputedStyle(row).position === 'static') row.style.position = 'relative';
      row.appendChild(pop); // 先挂行内量尺寸（visibility:hidden 防跳）
      var tCss = st.dd.offsetTop - pop.offsetHeight - 6; // 向上开：弹层底=按钮顶-6（同空间直接算）
      if (tCss < 0) tCss = st.dd.offsetTop + st.dd.offsetHeight + 6; // 放不下转向下开
      var lCss = st.dd.offsetLeft;
      var maxL = row.clientWidth - pop.offsetWidth - 4;
      if (lCss > maxL) lCss = Math.max(4, maxL);
      pop.style.left = lCss + 'px';
      pop.style.top = tCss + 'px';
      pop.style.visibility = '';
      st.pop = pop;
      st.popDown = function (e) { // 任意 mousedown 关闭（capture，先于页面处理）：选项行交给 click、按钮交给 toggle
        var t = e.target;
        if (pop.contains(t) || (st.dd && st.dd.contains(t))) return;
        closePop(st);
      };
      st.doc.addEventListener('mousedown', st.popDown, true);
    } catch (e) {}
  }

  function refreshDD(st, c) {
    try {
      if (frameMode(st) !== 'browser') {
        // 固定视图页（food/material）无任何 UI 注入：摘除遗留下拉/弹出层（如 cfg 把该页从 browser 改走）
        if (st.dd) { try { st.dd.remove(); } catch (e) {} st.dd = null; }
        closePop(st);
        return;
      }
      if (!st.doc || !st.doc.body) return;
      c = c || detectC(st.doc);
      if (!c || !c.bar) return;
      var opts = options();
      var sel = pageSel(st);
      var dd = st.dd;
      if (!opts.length) { if (dd) dd.style.display = 'none'; return; }
      if (!dd || !dd.isConnected || dd.ownerDocument !== st.doc) {
        // 先清本页面遗留的触发器/弹出层（初始化竞态窗口曾插出两个，保此文档内唯一）
        var olds = st.doc.querySelectorAll('.sltf-dd-btn, .sltf-dd-pop');
        for (var q = 0; q < olds.length; q++) { try { olds[q].remove(); } catch (e) {} }
        // 样式表不在此处创建——只允许 placeDD 携有效 k 创建，杜绝任何 kk=1 错误系数帧闪现
        //（作者实测"刚打开闪一下小的"：首帧 col 未量出系数 → 回退 body zoom=1 → 错误系数表闪一帧）
        dd = st.doc.createElement('button');
        dd.type = 'button'; dd.className = 'sltf-dd-btn'; dd.title = '按标签筛选容器页签';
        // 内联只管布局——外观/字体/内衬全部由样式表管理，内联外观会压死 :hover（复审 R1 旧坑）。
        // ▾ 用文字（BetterTrade 同款 "排序 ▾"），弃 CSS 三角。left/bottom 由 placeDD 写入。
        dd.style.cssText = 'z-index:99998;box-sizing:border-box;min-width:0;max-width:128px;margin:0;'
          + 'cursor:pointer;overflow:hidden;'; // display/gap/padding 在样式表（归一值），内联不压
        dd.addEventListener('click', function (e) { e.stopPropagation(); togglePop(st); });
        dd.innerHTML = '<span class="sltf-dd-ico" style="display:flex;flex:0 0 auto"></span>'
          + '<span class="sltf-dd-txt" style="min-width:0;overflow:hidden;text-overflow:ellipsis"></span>';
        st.dd = dd; // 先记引用再 placeDD（锚行查找+落位都在那里）
        // 旁按钮（BetterTrade 组件）可能晚于我们注入——延迟重放三轮消灭"初始落错位"
        var stR = st, cR = c;
        var retries = [500, 1500, 3000];
        for (var ri = 0; ri < retries.length; ri++) {
          (function (ms) {
            setTimeout(function () { try { if (stR.dd && stR.dd.isConnected) placeDD(stR, stR.dd, cR || detectC(stR.doc)); } catch (e) {} }, ms);
          })(retries[ri]);
        }
      }
      dd.style.display = 'inline-flex';
      placeDD(st, dd, c); // 锚行查找+行内落位，每次重算（外来按钮卸载→行自动收拢；须在 __sltfKey 缓存返回之前）
      // 选中态：文案+图标+官方色+按钮激活类（.on=金色系，BetterTrade 排序激活同语义）
      var lib = selById(sel);
      var txt = (sel ? (lib ? lib.name : '其他') : '全部柜子') + ' \u25BE';
      var icoSz = 14; // BetterTrade .PFX-ico 同值
      var ico = iconSvg(sel && lib ? tagIcon(lib.icon) : (sel ? TAG_ICONS_FALLBACK.misc : ICON_ALL_SVG), icoSz);
      var key = sel + '|' + txt + '|' + ico + '|' + (lib && lib.color ? lib.color : '');
      if (dd.__sltfKey === key) return;
      dd.__sltfKey = key;
      dd.classList.toggle('on', !!sel);
      dd.querySelector('.sltf-dd-txt').textContent = txt;
      var icoEl = dd.querySelector('.sltf-dd-ico');
      icoEl.innerHTML = ico;
      icoEl.style.width = icoSz + 'px';
      icoEl.style.height = icoSz + 'px';
      icoEl.style.color = (sel && lib && lib.color) ? lib.color : '';
    } catch (e) {}
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
      // TradeUI 是 Vue 页会整条重渲染，可能把列容器内 popup 一并摘掉：只清引用不重建（重开由按钮
      // click 触发 togglePop），触发器本体走下面 refreshDD 的 isConnected 重插（既有机制不变）
      if (st.pop && !st.pop.isConnected) st.pop = null;
      var c = detectC(st.doc);
      if (!c) return;
      if (st.frozen && st.frozen.length && Date.now() - (st.freezeAt || 0) > 150) unfreeze(st); // 二次渲染已到且最短冻结满 150ms（防延迟渲染穿透）
      if (!records || subtreeTouched(records, c.list)) applyFrame(st, c);
      if (!records || subtreeTouched(records, c.bar || c.list)) refreshDD(st, c);
    } catch (e) {}
  }

  function hookFrame(el) {
    for (var i = 0; i < S.frames.length; i++) if (S.frames[i].el === el) return;
    var st = { el: el, doc: null, tabs: null, hidden: [], dd: null, pop: null, popDown: null, mo: null, xorigin: false, lastActiveIdx: null, frozen: [], freezeTimer: 0, pending: null, pendingTimer: 0, skipEcho: null, initTimer: 0 };
    S.frames.push(st);
    var init = function () {
      var doc = null;
      try { doc = el.contentDocument; } catch (e) { doc = null; }
      if (!doc || !doc.body || !doc.body.firstChild) { st.initTimer = setTimeout(init, 150); return; }
      st.initTimer = 0;
      st.doc = doc;
      wrapRelay(el, st);
      if (!st.mo) st.mo = new MutationObserver(function (rs) { schedule(st, rs); });
      st.mo.observe(doc.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
      schedule(st);
    };
    el.addEventListener('load', function () {
      // 界面（重）载：取消还在轮询的旧 init 链（首次打开期间的竞态窗口曾插出两个下拉，
      // 2026-10-16 实测），再重置状态重启初始化
      if (st.initTimer) { clearTimeout(st.initTimer); st.initTimer = 0; }
      st.doc = null; st.tabs = null; st.hidden.length = 0; st.dd = null; st.pop = null; st.popDown = null; st.lastActiveIdx = null; st.skipEcho = null;
      st.pending = null;
      if (st.pendingTimer) { clearTimeout(st.pendingTimer); st.pendingTimer = 0; }
      unfreeze(st);
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
    ver: 3, // 数据契约 v3：owners 对象化（t+kind）、lib 带 icon/color、scenario 段（T6 起驱动固定视图）
    setData: function (json) {
      S.owners = (json && json.owners) || {};
      S.lib = (json && json.lib) || [];
      S.scenario = (json && json.scenario) || {}; // 页面 id→'food'|'material'|'browser'（frameMode 消费，cfg 改动重推即换视图）
      S.kind = {}; // ownerId→kind 平铺（诊断用；视图判定走 owners 原对象）
      for (var k in S.owners) S.kind[k] = ownerOf(k).kind;
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
