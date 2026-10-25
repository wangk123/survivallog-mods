// EndlessGuard Task 8: enter an endless save via CDP white-box (pattern: tools/_t5_verify.js).
// Usage: node _t8_enter.js [saveNameSubstr] [--wait-coreui-ms N]
//   - no substr: first endless non-ending save (same pick as _t5_verify.js)
//   - prints saveList facts (turn/diff text) then waits until CoreUI1 frame is up (in-scene)
// Output: JSON facts; exit 0 = entered save + HUD up.
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const CONNECT_BUDGET_MS = 240000;
const POLL_MS = 2000;
const substr = process.argv.slice(2).filter(x => !x.startsWith('--'))[0] || '';
const coreuiWaitMs = parseInt((process.argv.find(x => x.startsWith('--wait-coreui-ms')) || '').split('=')[1] || '240000', 10);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

async function findFrame(page, sub) {
  for (const f of page.frames()) {
    try { if (f.url().indexOf(sub) !== -1) return f; } catch (e) { /* detached */ }
  }
  return null;
}

(async () => {
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; }
    catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) { log('FAIL: CDP unreachable: ' + e.message); process.exit(2); }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected');

  let page = null;
  const pageDeadline = Date.now() + 180000;
  for (;;) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); } catch (e) {}
        if (has) { page = p; break; }
      }
      if (page) break;
    }
    if (page) break;
    if (Date.now() > pageDeadline) { log('FAIL: no Root page'); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('Root page found');

  const facts = { saveList: null, picked: null, coreUp: false, mapFrames: [] };
  let entered = false;
  const enterDeadline = Date.now() + 300000;
  while (!entered && Date.now() < enterDeadline) {
    let frame = await findFrame(page, '/UI/MainUI/MainUI.html');
    if (!frame) { await sleep(POLL_MS); continue; }
    try {
      const res = await frame.evaluate(async () => {
        if (!document.querySelector('.menu')) return { menu: false };
        core.UnitySendEvent('LOAD_GAME');
        for (let i = 0; i < 30; i++) {
          await new Promise((r) => setTimeout(r, 500));
          const st = (typeof state !== 'undefined') ? state : window.state;
          if (st && st.saveList && st.saveList.length) {
            return { menu: true, list: st.saveList.map((s) => ({
              Index: s.Index, name: s.saveName || s.name || '', turn: s.turnText || '', diff: s.diffText || '',
              endless: !!s.isEndless, ending: !!s.isEndingSave, state: s.stateText || '' }) ) };
          }
        }
        return { menu: true, list: null };
      });
      if (res && res.list) {
        facts.saveList = res.list;
        const pick = (substr && res.list.find((s) => (s.name || '').indexOf(substr) !== -1))
          || res.list.find((s) => s.endless && !s.ending)
          || res.list.find((s) => !s.ending);
        if (pick) {
          facts.picked = pick;
          await frame.evaluate((idx) => { loadSave(idx); }, pick.Index);
          entered = true;
          log('loadSave(' + pick.Index + ') sent: ' + JSON.stringify(pick));
        } else if (substr) {
          log('no save matches substr "' + substr + '"; names: ' + JSON.stringify(res.list.map((s) => s.name)));
        }
      }
    } catch (e) { log('main-menu phase err: ' + e.message); }
    if (!entered) await sleep(POLL_MS);
  }
  if (!entered) { log('FAIL: could not enter a save'); console.log(JSON.stringify(facts, null, 1)); process.exit(1); }

  const coreDeadline = Date.now() + coreuiWaitMs;
  while (Date.now() < coreDeadline) {
    const core = await findFrame(page, '/UI/CoreUI1/CoreUI1.html');
    if (core) { facts.coreUp = true; break; }
    await sleep(3000);
  }
  // passive: which full-screen UI frames exist right now
  for (const f of page.frames()) {
    try { const u = f.url(); if (/UI\/(ExploreMap|TradeUI|DroneHub|RogueSelect)\//.test(u)) facts.mapFrames.push(u.split('?')[0]); } catch (e) {}
  }
  await browser.close();
  log('---- facts ----');
  console.log(JSON.stringify(facts, null, 1));
  process.exit(facts.coreUp ? 0 : 1);
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
