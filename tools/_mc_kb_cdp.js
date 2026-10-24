// ModCenter Task 8 verification fixture (live game, CDP): keyboard gate reuse.
// Connection pattern copied from tools/_mc_fixture.js (playwright-core connectOverCDP,
// Root page via #iframe-container, all DOM work in ROOT page via page.evaluate).
//
//   node tools/_mc_kb_cdp.js
//
// Flow (main-menu phase only; saves are NOT touched):
//   - wait MainUI + __mc_entry + panel bootstrap
//   - install outbox spy (message drain evidence)
//   - 3 round-trips: entry click (panel open) -> poll BepInEx/ModCenter_kb.txt for
//       "[ts] OPEN kb=on ime <prev>->On"   (kb readback via get_IsKeyboardEnabled)
//     X click (panel close) -> poll for
//       "[ts] CLOSE kb=off ime-><prev>"    (restore must equal the SAME <prev>)
//     (round 1 only, optional probe: focus ovr ID input #12 -> real keystrokes via
//      tools/_mc_kb_type.ps1 SendKeys -> input.value === '8012' proves physical keys
//      reach the browser through the Vuplex gate; failure is reported but only WARNs,
//      CDP synthetic keys are NOT affected by the gate so the kb file is the authority)
//   - assert: 6 lines total, no ORPHAN lines, per-round restore value matches,
//     spy log drained >=3 MC_OPEN + >=3 MC_CLOSE
//
// ASCII-only file; Chinese via \u escapes (repo tool convention).

const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');
const { execFile } = require('child_process');
const FS = require('fs');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const GAME_ROOT = 'E:/SteamLibrary/steamapps/common/Survival Log';
const KB_TXT = GAME_ROOT + '/BepInEx/ModCenter_kb.txt';
const QA_GUID = 'com.local.survivallog.quickaction';
const SHOT_DIR = 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter';

const ROUNDS = 3;
const CONNECT_BUDGET_MS = 180000;
const POLL_MS = 400;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function kbLines() {
  try { return FS.readFileSync(KB_TXT, 'utf8').split('\n').filter((l) => l.trim()); } catch (e) { return []; }
}
// "[HH:mm:ss.fff] OPEN kb=on ime Auto->On"  -> {act:'OPEN', kb:'on', from:'Auto', to:'On'}
// "[HH:mm:ss.fff] CLOSE kb=off ime->Auto"   -> {act:'CLOSE', kb:'off', from:null, to:'Auto'}
// "[HH:mm:ss.fff] ORPHAN(layer-lost) ime->Auto kb-dies-with-layer" -> {act:'ORPHAN'}
function parseKb(line) {
  const m = line.match(/^\[[^\]]+\]\s+(OPEN|CLOSE)\s+kb=(on|off|\?)\s+ime\s*(\S*)\s*(?:->|→)\s*(\S+)\s*$/);
  if (m) return { act: m[1], kb: m[2], from: m[3] || null, to: m[4], raw: line };
  const o = line.match(/^\[[^\]]+\]\s+ORPHAN\([^)]*\)\s+ime\s*(?:->|→)\s*(\S+)\s+kb-dies-with-layer\s*$/);
  if (o) return { act: 'ORPHAN', kb: null, from: null, to: null, raw: line };
  return null;
}

async function connect(t0, log) {
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; } catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) { log('FAIL: CDP unreachable: ' + e.message); process.exit(2); }
      await sleep(1500);
    }
  }
  log('CDP connected');
  const page = await findRootPage(browser, 180000, log);
  return { browser, page };
}

async function findRootPage(browser, budgetMs, log) {
  const deadline = Date.now() + budgetMs;
  for (;;) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); } catch (e) { has = false; }
        if (has) return p;
      }
    }
    if (Date.now() > deadline) { log('FAIL: no Root page'); await browser.close(); process.exit(2); }
    await sleep(1500);
  }
}

// evaluate inside the MainUI host doc (arg fn serialized into Root realm)
function inMain(page, fn, arg) {
  return page.evaluate(({ tail, fnSrc, arg }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1)
          return eval('(' + fnSrc + ')').call(null, d, arg);
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, fnSrc: fn.toString(), arg });
}

function sendKeysReal(key) {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', 'E:/Game/Mod/mod_stack/tools/_mc_kb_type.ps1', '-Key', key],
      { timeout: 20000 }, (err, stdout, stderr) => {
        resolve({ err: err ? String(err.message) : null, stdout: String(stdout || ''), stderr: String(stderr || '') });
      });
  });
}

(async () => {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const { browser, page } = await connect(t0, log);

  // wait for entry + panel bootstrap
  const ready = () => inMain(page, (d) => !!(d.getElementById('__mc_entry') && window.__mcDrain && window.__mcRender), null);
  const dl0 = Date.now() + 150000;
  for (;;) {
    if (await ready()) break;
    if (Date.now() > dl0) { log('FAIL: entry/panel bootstrap not ready'); process.exit(2); }
    await sleep(1500);
  }
  log('entry + panel bootstrap ready');

  // outbox spy
  await page.evaluate(() => {
    if (window.__mcDrainSpy) return;
    window.__mcDrainSpy = true;
    window.__mcSentLog = [];
    const orig = window.__mcDrain;
    window.__mcDrain = function () {
      const a = orig();
      if (a && a.length) window.__mcSentLog.push.apply(window.__mcSentLog, a);
      return a;
    };
  });

  const V = {};
  const F = { rounds: [] };

  const clickEntry = () => inMain(page, (d) => { const e = d.getElementById('__mc_entry'); if (e) { e.click(); return true; } return false; }, null);
  const clickX = () => inMain(page, (d) => { const x = d.querySelector('#__mc_panel .__mc-close'); if (x) { x.click(); return true; } return false; }, null);
  const overlayPresent = () => inMain(page, (d) => !!d.getElementById('__mc_overlay'), null);

  for (let r = 1; r <= ROUNDS; r++) {
    const rec = { open: null, close: null, prevIme: null, restored: null };
    F.rounds.push(rec);

    // OPEN: entry click -> poll kb file for a new OPEN line (drain <=2s + tick <=0.3s; budget 20s)
    const nBeforeOpen = kbLines().length;
    if (!(await clickEntry())) { log('round ' + r + ': entry click FAILED'); rec.open = 'entry-click-failed'; continue; }
    const dlOpen = Date.now() + 20000;
    for (;;) {
      const lines = kbLines();
      let hit = null;
      for (let i = nBeforeOpen; i < lines.length; i++) {
        const p = parseKb(lines[i]);
        if (p && p.act === 'OPEN') { hit = p; break; }
      }
      if (hit) { rec.open = hit; rec.prevIme = hit.from; break; }
      if (Date.now() > dlOpen) break;
      await sleep(POLL_MS);
    }

    // round 1 only: physical typing probe (best effort, WARN on miss)
    if (r === 1 && rec.open) {
      await sleep(600); // let snapshot render settle
      const clicked = await inMain(page, (d, guid) => {
        const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
        if (row) { row.click(); return true; }
        return false;
      }, QA_GUID);
      await sleep(250);
      const focused = await inMain(page, (d) => {
        const inp = d.getElementById('__mc_ovr_new');
        if (!inp) return false;
        inp.focus();
        return d.activeElement === inp;
      }, null);
      const ks = await sendKeysReal('8012');
      await sleep(900);
      const val = await inMain(page, (d) => {
        const inp = d.getElementById('__mc_ovr_new');
        return inp ? inp.value : null;
      }, null);
      F.typeProbe = { clicked, focused, ps: ks, value: val };
      V.typeProbeReached = !!(clicked && focused && val === '8012');
      // clean the probe residue (no events -> no message; value never committed)
      await inMain(page, (d) => {
        const inp = d.getElementById('__mc_ovr_new');
        if (inp) inp.value = '';
        return true;
      }, null);
    }

    // CLOSE: X click -> poll for CLOSE line
    const nBeforeClose = kbLines().length;
    if (!(await clickX())) { log('round ' + r + ': X click FAILED'); rec.close = 'x-click-failed'; continue; }
    const dlClose = Date.now() + 20000;
    for (;;) {
      const lines = kbLines();
      let hit = null;
      for (let i = nBeforeClose; i < lines.length; i++) {
        const p = parseKb(lines[i]);
        if (p && p.act === 'CLOSE') { hit = p; break; }
      }
      if (hit) { rec.close = hit; rec.restored = hit.to; break; }
      if (Date.now() > dlClose) break;
      await sleep(POLL_MS);
    }
    await sleep(500);
  }

  // spy log drain evidence
  const sent = await page.evaluate(() => window.__mcSentLog || []);
  F.sentCounts = {
    open: sent.filter((m) => m && m.type === 'MC_OPEN').length,
    close: sent.filter((m) => m && m.type === 'MC_CLOSE').length,
  };

  const finalLines = kbLines();
  F.finalLines = finalLines;

  // panel must be closed at the end
  V.finalPanelClosed = (await overlayPresent()) === false;

  // verdicts
  V.roundTrips = F.rounds.every((r) => r.open && r.open.kb === 'on' && r.open.to === 'On'
    && r.close && r.close.kb === 'off');
  V.restoreExact = F.rounds.every((r) => r.prevIme != null && r.restored === r.prevIme);
  V.sixLinesNoOrphan = finalLines.length === ROUNDS * 2
    && finalLines.every((l) => parseKb(l) && parseKb(l).act !== 'ORPHAN');
  V.drainEvidence = F.sentCounts.open >= ROUNDS && F.sentCounts.close >= ROUNDS;

  try { await page.screenshot({ path: SHOT_DIR + '/task-8-shot-menu.png' }); } catch (e) {}
  await browser.close();

  console.log('---- facts ----');
  console.log(JSON.stringify({
    kbPath: KB_TXT,
    rounds: F.rounds.map((r) => ({
      open: r.open && r.open.raw, close: r.close && r.close.raw,
      prevIme: r.prevIme, restored: r.restored,
    })),
    typeProbe: F.typeProbe || null,
    sentCounts: F.sentCounts,
    finalLines,
  }, null, 1));
  console.log('---- verdicts ----');
  const checks = [
    ['1  3x OPEN kb=on ime->On / CLOSE kb=off', V.roundTrips],
    ['1  restore exact (restored == prevIme every round)', V.restoreExact],
    ['1  kb file: exactly 6 lines, no ORPHAN', V.sixLinesNoOrphan],
    ['1  outbox drain: >=3 MC_OPEN + >=3 MC_CLOSE', V.drainEvidence],
    ['1  panel closed at end', V.finalPanelClosed],
    ['2  typing probe (physical keys -> ovr ID input) [optional]', V.typeProbeReached !== false],
  ];
  let allPass = true;
  for (const [n, ok] of checks) { console.log((ok ? 'PASS' : (n.indexOf('[optional]') !== -1 ? 'WARN' : 'FAIL')) + '  ' + n); if (!ok && n.indexOf('[optional]') === -1) allPass = false; }
  log(allPass ? 'RESULT: ALL PASS' : 'RESULT: FAIL (see above)');
  process.exit(allPass ? 0 : 1);
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
