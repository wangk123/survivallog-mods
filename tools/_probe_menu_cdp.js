// ModCenter Task 2: main-menu WebView reachability probe (live game, CDP).
// Answers spec 2026-10-24-modcenter-design.md #9.1: can we inject into the
// MainUI main-menu .menu while sitting on the main menu? -> GO / NO-GO.
//
// Prereqs:
//   - ViewportProbe deployed, BepInEx cfg [DevTools] Enabled = true (port 9222)
//   - game launched via: powershell Start-Process 'steam://rungameid/4164790'
//   - STAY ON MAIN MENU (do not load a save)
// Usage:  node tools/_probe_menu_cdp.js
// Exit:   0 = all checks PASS (GO)
//         1 = assertion FAIL (NO-GO evidence)
//         2 = CDP unreachable / no Root page (inconclusive, env problem)
//
// Connection pattern copied from tools/devtools_cdp_test.js (playwright-core
// connectOverCDP). DOM work runs entirely inside the ROOT page context via one
// page.evaluate -- the exact path Task 5 will use (C# ExecuteJavaScript lands
// in the Root document, then walks #iframe-container -> MainUI iframe ->
// contentDocument -> .menu). Frame matching follows ANCHORS.md sec.3:
// pathname match first, iframe id ('MainUI') as fallback.
// Probe element <span id="__mc_probe"> is injected, verified, then removed
// immediately (leave no trace). File kept ASCII-only (Chinese via \u escapes).

const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const PROBE_ID = '__mc_probe';
const PROBE_TEXT = 'MOD \u7ba1\u7406'; // "MOD " + guan li
const CONNECT_BUDGET_MS = 180000; // covers launch -> 9222-ready window (~55s)
const MENU_WAIT_MS = 120000;      // wait for main menu DOM after connect
const POLL_MS = 2000;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---- probe body: executed inside the ROOT page, single round trip ----------
// Returns a facts object; every field is evidence, verdicts computed outside.
async function runProbe(page) {
  return page.evaluate(
    ({ mainHtml, probeId, probeText }) => {
      const out = {
        rootHref: location.href.split('?')[0],
        rootHasApp: !!document.querySelector('#app'),
        hasIframeContainer: false,
        frameCount: 0,
        frameInfo: [],
        matchHow: null,
        mainDocAccessible: false,
        menuFound: false,
        menuChain: null,
        menuChildCount: null,
        menuItems: [],
        injectFound: false,
        injectInMenu: false,
        injectTextOk: false,
        removed: false,
        menuCountRestored: false,
      };
      const container = document.querySelector('#iframe-container');
      if (!container) return out;
      out.hasIframeContainer = true;

      // frame scan: pathname match first, id fallback (ANCHORS.md sec.3)
      const frames = container.querySelectorAll('iframe');
      out.frameCount = frames.length;
      let byPath = null;
      let byId = null;
      for (let i = 0; i < frames.length; i++) {
        const f = frames[i];
        const rec = { id: f.id, docAccessible: false, pathMatch: false, url: '' };
        try {
          if (f.contentDocument) {
            rec.docAccessible = true;
            rec.url = String(f.contentDocument.location.href).split('?')[0];
            rec.pathMatch = rec.url.indexOf(mainHtml) !== -1;
          }
        } catch (e) {
          rec.docAccessible = false;
          rec.err = String((e && e.message) || e);
        }
        out.frameInfo.push(rec);
        if (rec.pathMatch && !byPath) byPath = f;
        if (f.id === 'MainUI' && !byId) byId = f;
      }
      const hit = byPath || byId;
      out.matchHow = byPath ? 'pathname' : byId ? 'id-fallback' : null;
      if (!hit) return out;

      let doc = null;
      try { doc = hit.contentDocument; } catch (e) { /* cross-origin */ }
      out.mainDocAccessible = !!doc;
      if (!doc) return out;

      const chainHit = doc.querySelector('#app .container .menu');
      const menu = chainHit || doc.querySelector('.menu');
      out.menuFound = !!menu;
      out.menuChain = chainHit ? '#app .container .menu' : menu ? '.menu (fallback)' : null;
      if (!menu) return out;

      out.menuChildCount = menu.children.length;
      out.menuItems = Array.prototype.map.call(
        doc.querySelectorAll('.menu .menu-item .menu-text'),
        (t) => t.getAttribute('data-i18n') || t.textContent.trim()
      );

      // Task 5 simulation: append probe span, verify, remove immediately
      const before = menu.children.length;
      const span = doc.createElement('span');
      span.id = probeId;
      span.textContent = probeText;
      menu.appendChild(span);
      const check = doc.getElementById(probeId);
      out.injectFound = !!check;
      out.injectInMenu = !!check && check.parentNode === menu;
      out.injectTextOk = !!check && check.textContent === probeText;
      if (check && check.parentNode) check.parentNode.removeChild(check);
      out.removed = !doc.getElementById(probeId);
      out.menuCountRestored = menu.children.length === before;
      return out;
    },
    { mainHtml: MAIN_HTML, probeId: PROBE_ID, probeText: PROBE_TEXT }
  );
}

// final sweep: probe id must not exist anywhere (root or any iframe doc)
async function residueSweep(page) {
  return page.evaluate((probeId) => {
    const hits = [];
    if (document.getElementById(probeId)) hits.push('root');
    const frames = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < frames.length; i++) {
      try {
        if (frames[i].contentDocument && frames[i].contentDocument.getElementById(probeId)) {
          hits.push(frames[i].id || ('frame' + i));
        }
      } catch (e) { /* ignore */ }
    }
    return hits;
  }, PROBE_ID);
}

(async () => {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

  // phase 1: connect (retry through the launch->9222 window)
  let browser = null;
  for (;;) {
    try {
      browser = await chromium.connectOverCDP(CDP);
      break;
    } catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) {
        log('FAIL: CDP ' + CDP + ' unreachable: ' + e.message);
        process.exit(2);
      }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected: ' + CDP);

  // phase 2: find the Root page (the one hosting #iframe-container)
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
    if (Date.now() > pageDeadline) {
      log('Pages seen:');
      for (const ctx of browser.contexts()) {
        for (const p of ctx.pages()) {
          let u = '?', t = '?';
          try { u = p.url(); t = await p.title(); } catch (e) { /* detached */ }
          log('  url=' + u + ' title=' + t);
        }
      }
      log('FAIL: no page with #iframe-container (Root Orchestrator) found');
      await browser.close();
      process.exit(2);
    }
    await sleep(POLL_MS);
  }
  const rootUrl = page.url();
  const rootTitle = await page.title();
  log('Root page: url=' + rootUrl.split('?')[0] + ' title=' + rootTitle);

  // phase 3: wait for main menu, then run the inject->verify->remove probe
  let facts = null;
  const menuDeadline = Date.now() + MENU_WAIT_MS;
  for (;;) {
    facts = await runProbe(page);
    if (facts.menuFound) break;                    // menu present -> single shot
    if (Date.now() > menuDeadline) break;
    await sleep(POLL_MS);
  }

  const residue = await residueSweep(page);
  await browser.close(); // connectOverCDP: disconnects only, game unaffected

  // report
  console.log('---- facts ----');
  console.log(JSON.stringify(facts, null, 1));
  console.log('---- verdicts ----');
  const checks = [
    ['CDP connect', true],
    ['Root page with #iframe-container', facts.hasIframeContainer],
    ['MainUI iframe matched (' + facts.matchHow + ')', !!facts.matchHow],
    ['MainUI contentDocument accessible', facts.mainDocAccessible],
    ['main menu .menu container present (' + facts.menuChain + ')', facts.menuFound],
    ['inject span#' + PROBE_ID + ' into .menu', facts.injectFound && facts.injectInMenu],
    ['probe text intact', facts.injectTextOk],
    ['probe removed', facts.removed && facts.menuCountRestored],
    ['no residue anywhere', residue.length === 0],
  ];
  let allPass = true;
  for (const [name, ok] of checks) {
    console.log((ok ? 'PASS' : 'FAIL') + '  ' + name);
    if (!ok) allPass = false;
  }
  log(allPass ? 'RESULT: GO (all checks pass)' : 'RESULT: NO-GO (see FAIL lines above)');
  process.exit(allPass ? 0 : 1);
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
