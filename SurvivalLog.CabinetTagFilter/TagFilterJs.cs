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
      if (arr[ai] == null || !shouldHide(String(arr[ai]))) { return false; }
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
      if (!S.sel) return;
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
        if (shouldHide(String(k))) {
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
        // 先清掉本页签栏里遗留的旧下拉（初始化竞态窗口曾插出两个，保此文档内唯一）
        var olds = c.bar.querySelectorAll('.sltf-dd');
        for (var q = 0; q < olds.length; q++) { try { olds[q].remove(); } catch (e) {} }
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
      if (st.frozen && st.frozen.length && Date.now() - (st.freezeAt || 0) > 150) unfreeze(st); // 二次渲染已到且最短冻结满 150ms（防延迟渲染穿透）
      if (!records || subtreeTouched(records, c.list)) applyFrame(st, c);
      if (!records || subtreeTouched(records, c.bar || c.list)) refreshDD(st, c);
    } catch (e) {}
  }

  function hookFrame(el) {
    for (var i = 0; i < S.frames.length; i++) if (S.frames[i].el === el) return;
    var st = { el: el, doc: null, tabs: null, hidden: [], dd: null, mo: null, xorigin: false, lastActiveIdx: null, frozen: [], freezeTimer: 0, pending: null, pendingTimer: 0, skipEcho: null, initTimer: 0 };
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
      st.doc = null; st.tabs = null; st.hidden.length = 0; st.dd = null; st.lastActiveIdx = null; st.skipEcho = null;
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
