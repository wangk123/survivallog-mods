// Bugfix round 2 -- IN-GAME phase (ESC pause menu entry latency, the problem-1
// acceptance number). Instrument Root with our OWN observer stack (independent
// of the mod's), then for 3 rounds: send ESC (pause opens), measure
// iframe-added -> #__mc_entry-present delta; ESC again to close. Finally open
// the panel in-game, screenshot with sliders visible.
// ASCII-only; Chinese via \u escapes (repo tool convention).
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const FS = require('fs');
const { execFile } = require('child_process');

const CDP = 'http://localhost:9222';
const OUT_HTML = '/UI/OutSetting/OutSetting.html';
const RS_GUID = 'com.local.survivallog.robotspeed';
const SHOT = 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-shot-ingame.png';
const SENDKEY = 'E:/Game/Mod/mod_stack/tools/_mc_sendkey.ps1';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const log = (m) => console.log('[' + ((Date.now() - t0) / 1000).toFixed(1) + 's] ' + m);

function sendEsc() {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', SENDKEY],
      { timeout: 20000 }, (err, stdout) => resolve({ err: err ? String(err.message) : null, stdout: String(stdout || '') }));
  });
}

(async () => {
  const browser = await chromium.connectOverCDP(CDP);
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
  }
  if (!page) { log('FAIL no Root page'); process.exit(2); }
  log('Root page acquired');

  // ---- install our own instrumentation (idempotent) ----
  await page.evaluate(() => {
    if (window.__mcLatTool) return;
    window.__mcLatTool = true;
    window.__mcLat = [];
    var push = function (ev, extra) {
      try { window.__mcLat.push(Object.assign({ ev: ev, t: Date.now() }, extra || {})); } catch (e) {}
    };
    var hookFrame = function (f) {
      if (f.id !== 'OutSetting') return;
      push('iframe-added');
      f.addEventListener('load', function () {
        push('iframe-load');
        try {
          var d = f.contentDocument;
          if (!d) return;
          if (d.getElementById('__mc_entry')) { push('entry-at-load'); return; }
          var inner = new MutationObserver(function () {
            try { if (d.getElementById('__mc_entry')) { push('entry', { via: 'inner-observer' }); inner.disconnect(); } } catch (e) {}
          });
          var startInner = function () {
            if (d.body) inner.observe(d.body, { childList: true, subtree: true });
            else setTimeout(startInner, 20);
          };
          startInner();
        } catch (e) {}
      });
    };
    var scan = function () {
      var fs = document.querySelectorAll('#iframe-container iframe');
      for (var i = 0; i < fs.length; i++) {
        if (fs[i].id === 'OutSetting' && !fs[i].__latSeen) { fs[i].__latSeen = true; hookFrame(fs[i]); }
      }
    };
    var obs = new MutationObserver(function (muts) {
      for (var i = 0; i < muts.length; i++) {
        var add = muts[i].addedNodes;
        for (var j = 0; j < add.length; j++) {
          var n = add[j];
          if (n.nodeType === 1 && n.tagName === 'IFRAME' && n.id === 'OutSetting' && !n.__latSeen) {
            n.__latSeen = true; hookFrame(n);
          }
        }
      }
      // removal detection
      for (var i2 = 0; i2 < muts.length; i2++) {
        var rm = muts[i2].removedNodes;
        for (var j2 = 0; j2 < rm.length; j2++) {
          var n2 = rm[j2];
          if (n2.nodeType === 1 && n2.tagName === 'IFRAME' && n2.id === 'OutSetting') push('iframe-removed');
        }
      }
    });
    obs.observe(document.body, { childList: true, subtree: true });
    scan();
  });
  log('latency instrumentation installed');

  const frameIds = () => page.evaluate(() => Array.from(document.querySelectorAll('#iframe-container iframe')).map((f) => f.id));
  const lat = () => page.evaluate(() => window.__mcLat || []);

  // adaptive helpers: the live sim pops UI layers on its own; ESC peels one
  // layer at a time (modal -> ... -> pause menu). Keep sending until the
  // target state is reached.
  const hasOut = async () => (await frameIds()).indexOf('OutSetting') !== -1;
  const openPause = async function () {
    for (let i = 0; i < 8; i++) {
      if (await hasOut()) return true;
      await sendEsc();
      for (let w = 0; w < 6; w++) { await sleep(400); if (await hasOut()) return true; }
    }
    return await hasOut();
  };
  const closePause = async function () {
    for (let i = 0; i < 6; i++) {
      if (!(await hasOut())) return true;
      await sendEsc();
      for (let w = 0; w < 5; w++) { await sleep(400); if (!(await hasOut())) return true; }
    }
    return !(await hasOut());
  };

  // ---- dismiss the auto-restored EventChoice modal if present ----
  {
    const ids0 = await frameIds();
    log('frames at start: ' + JSON.stringify(ids0));
  }

  // ---- 3 latency rounds ----
  const rounds = [];
  for (let r = 1; r <= 3; r++) {
    if (!(await closePause())) { rounds.push({ r, fail: 'pause-would-not-close' }); continue; }
    const mark = (await lat()).length;
    log('round ' + r + ': opening pause (adaptive ESC)');
    const opened = await openPause();
    if (!opened) { rounds.push({ r, fail: 'pause-would-not-open', evs: (await lat()).slice(mark) }); continue; }
    const dl = Date.now() + 6000;
    let rec = null;
    while (Date.now() < dl) {
      const evs = (await lat()).slice(mark);
      const added = evs.find((e) => e.ev === 'iframe-added');
      const done = evs.find((e) => e.ev === 'entry' || e.ev === 'entry-at-load');
      if (added && done) { rec = { added, done, load: evs.find((e) => e.ev === 'iframe-load') || null, all: evs }; break; }
      await sleep(30);
    }
    if (!rec) { rounds.push({ r, fail: 'no-entry-within-6s', evs: (await lat()).slice(mark) }); }
    else {
      const latency = rec.done.t - rec.added.t;
      const loadMs = rec.load ? rec.load.t - rec.added.t : null;
      const via = rec.done.ev;
      rounds.push({ r, latencyMs: latency, loadMs, via, iframeAddedT: rec.added.t });
      log('round ' + r + ': iframe-added -> entry = ' + latency + 'ms (page load ' + loadMs + 'ms, via ' + via + ')');
    }
    await sleep(500);
  }

  // ---- in-game panel screenshot ----
  let shot = 'skipped';
  if (!(await hasOut())) { await openPause(); }
  const opened = await page.evaluate((tail) => {
    for (const f of document.querySelectorAll('#iframe-container iframe')) {
      if (f.id !== 'OutSetting') continue;
      try {
        const d = f.contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const e = d.getElementById('__mc_entry'); if (e) { e.click(); return true; }
        }
      } catch (e) {}
    }
    return false;
  }, OUT_HTML);
  await sleep(800);
  if (opened) {
    await page.evaluate((guid) => {
      for (const f of document.querySelectorAll('#iframe-container iframe')) {
        if (f.id !== 'OutSetting') continue;
        try {
          const d = f.contentDocument;
          const row = Array.from(d.querySelectorAll('#__mc_side .__mc-mod-row')).find((r) => r.getAttribute('data-guid') === guid);
          if (row) { row.click(); return true; }
        } catch (e) {}
      }
      return false;
    }, RS_GUID);
    await sleep(500);
    const panelOk = await page.evaluate((tail) => {
      for (const f of document.querySelectorAll('#iframe-container iframe')) {
        if (f.id !== 'OutSetting') continue;
        try {
          const d = f.contentDocument;
          const el = d.querySelector('#__mc_overlay input[type=range]');
          return { panel: !!d.getElementById('__mc_overlay'), sliders: el ? d.querySelectorAll('#__mc_overlay input[type=range]').length : 0,
            appearance: el ? getComputedStyle(el).webkitAppearance : null };
        } catch (e) {}
      }
      return null;
    }, OUT_HTML);
    log('in-game panel: ' + JSON.stringify(panelOk));
    try { await page.screenshot({ path: SHOT }); shot = 'saved'; log('screenshot: ' + SHOT); } catch (e) { shot = 'fail:' + e.message; }
    await page.evaluate(() => { if (window.__mcClose) window.__mcClose(); return true; });
  } else { log('FAIL panel did not open in-game'); }
  // close pause menu (leave game in-world for fixture modes)
  await sleep(400);
  await closePause();

  const ok = rounds.filter((x) => typeof x.latencyMs === 'number');
  const max = ok.length ? Math.max(...ok.map((x) => x.latencyMs)) : null;
  const V = {
    threeRounds: rounds.length === 3 && ok.length === 3,
    allWithin300: ok.length === 3 && ok.every((x) => x.latencyMs <= 300),
    maxLatency: max,
    inGamePanelOpened: shot === 'saved',
  };
  console.log('\n==== IN-GAME PHASE ====');
  console.log(JSON.stringify({ rounds, V, shot }, null, 2));
  FS.writeFileSync('E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-ingame-facts.json',
    JSON.stringify({ rounds, V, shot }, null, 2));
  const fail = !(V.threeRounds && V.allWithin300 && V.inGamePanelOpened);
  await browser.close();
  process.exit(fail ? 1 : 0);
})().catch((e) => { console.log('FATAL ' + e.message); process.exit(2); });
