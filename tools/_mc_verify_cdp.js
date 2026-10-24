// ModCenter Task 5 verification (live game, CDP). Connection pattern copied from
// tools/_probe_menu_cdp.js (playwright-core connectOverCDP, Root page via
// #iframe-container). All DOM work runs inside the ROOT page via page.evaluate --
// the same execution context the C# ExecuteJavaScript bootstrap lands in.
//
// Phase: MAIN MENU ONLY (author save safety: no save load; OutSetting in-game
// assertion deferred to Task 7 first round per task-5 brief).
//
// Checks:
//   A entry injected: #__mc_entry in MainUI doc, <button.menu-item>, tail of .menu,
//     single instance (idempotence across 2s ticks), official class visual applied
//   B click opens panel: #__mc_overlay + #__mc_panel in the SAME iframe doc,
//     title/close button present
//   C outbox contract: __mcDrain patched to record -> MC_OPEN then MC_CLOSE must
//     appear in __mcSeen within 8s each -- proves the C# ticker really polls
//   D close button removes overlay
//   E Escape keydown on host doc removes overlay
//   F after all: exactly one entry, panel closed (no residue beyond the entry+style)
//
// Usage: node tools/_mc_verify_cdp.js
// Exit:  0 all PASS / 1 assertion FAIL / 2 CDP unreachable or no Root/MainUI
// ASCII-only file (Chinese via \u escapes).

const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const ENTRY_TEXT = 'MOD \u7ba1\u7406';            // "MOD 管理"
const PANEL_TITLE = 'MOD \u7ba1\u7406\u4e2d\u5fc3'; // "MOD 管理中心"
const CONNECT_BUDGET_MS = 180000;
const MENU_WAIT_MS = 150000;
const POLL_MS = 1500;
const SHOT_DIR = 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// executed in ROOT page context -----------------------------------------------
function rootProbe(page, expr) {
  return page.evaluate('(' + expr + ')()');
}

(async () => {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

  // ---- connect ----
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; } catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) {
        log('FAIL: CDP ' + CDP + ' unreachable: ' + e.message);
        process.exit(2);
      }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected');

  // ---- find Root page ----
  let page = null;
  const pageDeadline = Date.now() + MENU_WAIT_MS;
  for (;;) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); }
        catch (e) { has = false; }
        if (has) { page = p; break; }
      }
      if (page) break;
    }
    if (page) break;
    if (Date.now() > pageDeadline) { log('FAIL: no Root page'); await browser.close(); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('Root page: ' + page.url().split('?')[0]);

  // ---- helpers executed in Root ----
  const findMainDoc = () => rootProbe(page, `function(){
    var fs = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < fs.length; i++) {
      try {
        var d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf('${MAIN_HTML}') !== -1) return d;
      } catch (e) {}
      if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
    }
    return null;
  }`);

  // wait for menu + entry (C# ticker injects within ~2s of menu ready)
  let doc = null, entry = null;
  const entryDeadline = Date.now() + MENU_WAIT_MS;
  for (;;) {
    doc = await findMainDoc();
    if (doc) {
      entry = await rootProbe(page, `function(){
        var d = (function(){
          var fs = document.querySelectorAll('#iframe-container iframe');
          for (var i = 0; i < fs.length; i++) {
            try { var x = fs[i].contentDocument;
              if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
            if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
          }
          return null;
        })();
        if (!d) return null;
        return d.getElementById('__mc_entry') || null;
      }`);
      if (entry) break;
    }
    if (Date.now() > entryDeadline) break;
    await sleep(POLL_MS);
  }

  const V = {}; // verdicts
  const F = {}; // facts

  // ---- A: entry injection facts ----
  if (entry) {
    F.entry = await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      var e = d && d.getElementById('__mc_entry');
      if (!e) return { found: false };
      var menu = d.querySelector('#app .container .menu') || d.querySelector('.menu');
      var cs = e.ownerDocument.defaultView.getComputedStyle(e);
      var tx = e.querySelector('.menu-text');
      return {
        found: true,
        tag: e.tagName,
        cls: e.className,
        interactive: e.hasAttribute('data-interactive'),
        text: (tx && tx.textContent) || e.textContent,
        inMenu: !!(menu && e.parentNode === menu),
        isLast: !!(menu && menu.lastElementChild === e),
        menuChildren: menu ? menu.children.length : -1,
        dupCount: d.querySelectorAll('#__mc_entry').length,
        styleEl: !!d.getElementById('__mc_entry_style'),
        display: cs.display,
        barBg: (function(){ var b = e.querySelector('.bar'); return b ? b.ownerDocument.defaultView.getComputedStyle(b).backgroundImage.slice(0,60) : null; })(),
        panelStyleInMainDoc: !!d.getElementById('__mc_panel_style'),
        panelInstalled: !!window.__McInstalled,
        outboxReady: !!window.__mcDrain
      };
    }`);
  }
  V.entryPresent = !!(F.entry && F.entry.found);
  V.entryIsButtonMenuItem = !!(F.entry && F.entry.tag === 'BUTTON' && /menu-item/.test(F.entry.cls));
  V.entryInteractive = !!(F.entry && F.entry.interactive);
  V.entryTextOk = !!(F.entry && F.entry.text === ENTRY_TEXT);
  V.entryInMenuTail = !!(F.entry && F.entry.inMenu && F.entry.isLast);
  V.entryNoDup = !!(F.entry && F.entry.dupCount === 1);
  V.entryStyleApplied = !!(F.entry && F.entry.display === 'flex' && F.entry.barBg && F.entry.barBg.indexOf('linear-gradient') === 0);
  V.panelContractInRoot = !!(F.entry && F.entry.panelInstalled && F.entry.outboxReady);

  // wait one extra tick pair (>4s) then re-count to prove idempotence across ticks
  await sleep(4600);
  F.idem = await rootProbe(page, `function(){
    var d = (function(){
      var fs = document.querySelectorAll('#iframe-container iframe');
      for (var i = 0; i < fs.length; i++) {
        try { var x = fs[i].contentDocument;
          if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
        if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
      }
      return null;
    })();
    return d ? d.querySelectorAll('#__mc_entry').length : -1;
  }`);
  V.idempotentAcrossTicks = F.idem === 1;

  // ---- C pre: patch __mcDrain to record what C# polls away ----
  F.patched = await rootProbe(page, `function(){
    if (!window.__mcDrain || window.__mcPatched) return !!window.__mcPatched;
    window.__mcPatched = true;
    window.__mcSeen = [];
    var orig = window.__mcDrain;
    window.__mcDrain = function () {
      var m = orig.call(window) || [];
      for (var i = 0; i < m.length; i++) window.__mcSeen.push(m[i]);
      return m;
    };
    return true;
  }`);

  // ---- B: click entry -> panel opens in SAME iframe doc ----
  if (V.entryPresent) {
    await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      var e = d && d.getElementById('__mc_entry');
      if (e) e.click();
      return !!e;
    }`);
    await sleep(400);
    F.panel = await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      var ov = d && d.getElementById('__mc_overlay');
      var pn = d && d.getElementById('__mc_panel');
      if (!ov) return { open: false };
      return {
        open: true,
        inMainDoc: true,
        interactive: ov.hasAttribute('data-interactive'),
        panel: !!pn,
        title: pn ? (pn.querySelector('.__mc-title') || {}).textContent || '' : '',
        closeBtn: !!(pn && pn.querySelector('.__mc-close')),
        styleEl: !!d.getElementById('__mc_panel_style')
      };
    }`);
    V.panelOpens = !!(F.panel && F.panel.open && F.panel.panel);
    V.panelTitleOk = !!(F.panel && F.panel.title === PANEL_TITLE);
    V.panelCloseBtn = !!(F.panel && F.panel.closeBtn);

    // screenshot: panel open over main menu
    try { await page.screenshot({ path: SHOT_DIR + '/task-5-shot-panel-open.png' }); log('shot: panel open saved'); }
    catch (e) { log('shot fail (non-fatal): ' + e.message); }
  } else { V.panelOpens = V.panelTitleOk = V.panelCloseBtn = false; }

  // ---- C: wait for C# to poll MC_OPEN away ----
  async function seenType(type, budget) {
    const dl = Date.now() + budget;
    for (;;) {
      const s = await rootProbe(page, 'function(){return (window.__mcSeen||[]).map(function(m){return m&&m.type;})}');
      if (s && s.indexOf(type) !== -1) return true;
      if (Date.now() > dl) return false;
      await sleep(POLL_MS);
    }
  }
  V.mcOpenDrained = V.panelOpens ? await seenType('MC_OPEN', 10000) : false;

  // ---- D: close button ----
  if (V.panelOpens) {
    await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      var x = d && d.querySelector('#__mc_panel .__mc-close');
      if (x) x.click();
      return !!x;
    }`);
    await sleep(400);
    F.closed = await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      return d ? !d.getElementById('__mc_overlay') : null;
    }`);
    V.closeBtnRemoves = F.closed === true;
  } else V.closeBtnRemoves = false;

  V.mcCloseDrained = V.closeBtnRemoves ? await seenType('MC_CLOSE', 10000) : false;

  // ---- E: reopen, ESC keydown on host doc closes ----
  if (V.panelOpens) {
    await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      var e = d && d.getElementById('__mc_entry');
      if (e) e.click();
      return !!e;
    }`);
    await sleep(400);
    F.reopened = await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      return d ? !!d.getElementById('__mc_overlay') : false;
    }`);
    await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      if (!d) return false;
      d.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      return true;
    }`);
    await sleep(400);
    F.escClosed = await rootProbe(page, `function(){
      var d = (function(){
        var fs = document.querySelectorAll('#iframe-container iframe');
        for (var i = 0; i < fs.length; i++) {
          try { var x = fs[i].contentDocument;
            if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
          if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
        }
        return null;
      })();
      return d ? !d.getElementById('__mc_overlay') : null;
    }`);
    V.escCloses = F.reopened === true && F.escClosed === true;
  } else V.escCloses = false;

  // ---- F: final state: entry present once, panel closed ----
  F.final = await rootProbe(page, `function(){
    var d = (function(){
      var fs = document.querySelectorAll('#iframe-container iframe');
      for (var i = 0; i < fs.length; i++) {
        try { var x = fs[i].contentDocument;
          if (x && x.location.pathname.indexOf('${MAIN_HTML}') !== -1) return x; } catch (e) {}
        if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
      }
      return null;
    })();
    return {
      entries: d ? d.querySelectorAll('#__mc_entry').length : -1,
      overlayGone: d ? !d.getElementById('__mc_overlay') : null
    };
  }`);
  V.finalClean = F.final.entries === 1 && F.final.overlayGone === true;

  // screenshot: menu with entry (panel closed)
  try { await page.screenshot({ path: SHOT_DIR + '/task-5-shot-menu.png' }); log('shot: menu saved'); }
  catch (e) { log('shot fail (non-fatal): ' + e.message); }

  await browser.close(); // disconnect only, game unaffected

  console.log('---- facts ----');
  console.log(JSON.stringify({ entry: F.entry, idem: F.idem, panel: F.panel, closed: F.closed, reopened: F.reopened, escClosed: F.escClosed, final: F.final, drainPatched: F.patched }, null, 1));
  console.log('---- verdicts ----');
  const checks = [
    ['CDP connect + Root page', true],
    ['A entry #__mc_entry present', V.entryPresent],
    ['A <button class="menu-item">', V.entryIsButtonMenuItem],
    ['A data-interactive attr', V.entryInteractive],
    ['A text == ' + ENTRY_TEXT, V.entryTextOk],
    ['A appended at .menu tail', V.entryInMenuTail],
    ['A single instance (no dup)', V.entryNoDup],
    ['A official class visuals applied (flex + gradient bar)', V.entryStyleApplied],
    ['A panel.js contract in Root (__McInstalled/__mcDrain)', V.panelContractInRoot],
    ['A idempotent across ticks (still 1 after >4s)', V.idempotentAcrossTicks],
    ['B click -> overlay+panel in same doc', V.panelOpens],
    ['B panel title ok', V.panelTitleOk],
    ['B close button present', V.panelCloseBtn],
    ['C MC_OPEN drained by C# within 10s', V.mcOpenDrained],
    ['C MC_CLOSE drained by C# within 10s', V.mcCloseDrained],
    ['D close btn removes overlay', V.closeBtnRemoves],
    ['E Escape keydown closes', V.escCloses],
    ['F final clean (1 entry, panel closed)', V.finalClean],
  ];
  let allPass = true;
  for (const [name, ok] of checks) {
    console.log((ok ? 'PASS' : 'FAIL') + '  ' + name);
    if (!ok) allPass = false;
  }
  log(allPass ? 'RESULT: ALL PASS' : 'RESULT: FAIL (see above)');
  process.exit(allPass ? 0 : 1);
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
