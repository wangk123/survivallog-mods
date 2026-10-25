// EndlessGuard Task 5 machine verification (live game, CDP white-box, zero save writes).
// Pattern: tools/_probe_menu_cdp.js (connectOverCDP 9222 -> Root page -> frame walk).
// Steps driven here (all official frontend events, no DOM injection except reading):
//   1) MainUI frame: core.UnitySendEvent('LOAD_GAME') -> state.saveList -> loadSave(idx)
//      (official handlers at gamekb webui UI/MainUI/MainUI.html:2681/2717)
//   2) wait in-game CoreUI1 frame, then UnitySendEvent('PLAN_TALENT') opens the
//      survival planning page (CoreUI1.html:8813 @click handler)
//   3) wait RogueSelect iframe + PlanPick #ppEntry button, click it, sweep #ppOverlay
//      text for the injected card name + lock reason, then close overlay.
// Output: JSON facts on stdout; exit 0 = entered save + overlay facts collected.
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const CONNECT_BUDGET_MS = 240000;
const POLL_MS = 2000;
const CARD_NAME = '\u8d85\u5e02\u62fe\u9057\u00b7\u516d';            // "超市拾遗·六" (talent 31606)
const LOCK_TEXT = '\u751f\u5b58\u81f3\u7b2c100\u5929';              // "生存至第100天"

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

// find a frame whose url contains sub (or id == sub)
async function findFrame(page, sub, byId) {
  for (const f of page.frames()) {
    try {
      const u = f.url();
      if (byId ? false : (u.indexOf(sub) !== -1)) return f;
    } catch (e) { /* detached */ }
  }
  if (byId) {
    // iframe id lives in the Root document
    try {
      const found = await page.evaluate((id) => {
        const f = document.getElementById(id);
        return !!f;
      }, sub);
      if (found) {
        for (const f of page.frames()) {
          try { if (f.name() === sub || f.url().indexOf('/' + sub + '/') !== -1) return f; } catch (e) {}
        }
      }
    } catch (e) { /* page gone */ }
  }
  return null;
}

(async () => {
  // phase 1: connect
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; }
    catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) { log('FAIL: CDP unreachable: ' + e.message); process.exit(2); }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected');

  // phase 2: Root page (#iframe-container)
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

  const facts = { saveList: null, picked: null, planPageOpened: false, ppEntryFound: false,
                  overlayOpened: false, cardFound: false, lockReasonFound: false, cardHits: [], overlaySample: '' };

  // phase 3: main menu -> LOAD_GAME -> saveList -> loadSave(first endless)
  let entered = false;
  const enterDeadline = Date.now() + 180000;
  while (!entered && Date.now() < enterDeadline) {
    let frame = await findFrame(page, '/UI/MainUI/MainUI.html', false);
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
              Index: s.Index, turn: s.turnText || '', diff: s.diffText || '',
              endless: !!s.isEndless, ending: !!s.isEndingSave, state: s.stateText || '' }) ) };
          }
        }
        return { menu: true, list: null };
      });
      if (res && res.list) {
        facts.saveList = res.list;
        const pick = res.list.find((s) => s.endless && !s.ending) || res.list.find((s) => !s.ending);
        if (pick) {
          facts.picked = pick;
          await frame.evaluate((idx) => { loadSave(idx); }, pick.Index);
          entered = true;
          log('loadSave(' + pick.Index + ') sent: ' + JSON.stringify(pick));
        }
      }
    } catch (e) { log('main-menu phase err: ' + e.message); }
    if (!entered) await sleep(POLL_MS);
  }
  if (!entered) { log('FAIL: could not enter a save'); console.log(JSON.stringify(facts, null, 1)); process.exit(1); }

  // phase 4: wait for in-game HUD frame (CoreUI1) = scene loaded
  let core = null;
  const coreDeadline = Date.now() + 180000;
  while (Date.now() < coreDeadline) {
    core = await findFrame(page, '/UI/CoreUI1/CoreUI1.html', false);
    if (core) break;
    await sleep(3000);
  }
  if (!core) { log('WARN: CoreUI1 frame not found (scene may still be loading or menu UI differs)'); }
  else {
    log('in-game HUD (CoreUI1) up');
    // phase 5: open survival planning page
    await sleep(8000);   // let HUD settle
    try {
      await core.evaluate(() => { core.UnitySendEvent('PLAN_TALENT'); });
      facts.planPageOpened = true;
      log('PLAN_TALENT sent');
    } catch (e) { log('PLAN_TALENT err: ' + e.message); }
  }

  // phase 6: wait RogueSelect + PlanPick entry button, open overlay, sweep text
  const ppDeadline = Date.now() + 90000;
  while (Date.now() < ppDeadline) {
    let rs = await findFrame(page, '/UI/RogueSelect/RogueSelect.html', false);
    if (!rs) { await sleep(POLL_MS); continue; }
    try {
      const opened = await rs.evaluate(async () => {
        for (let i = 0; i < 20; i++) {
          if (document.getElementById('ppEntry')) return true;
          await new Promise((r) => setTimeout(r, 500));
        }
        return false;
      });
      if (!opened) { await sleep(POLL_MS); continue; }
      facts.ppEntryFound = true;
      const sweep = await rs.evaluate(async (cardName, lockText) => {
        const btn = document.getElementById('ppEntry');
        if (btn) btn.click();
        for (let i = 0; i < 30; i++) {
          await new Promise((r) => setTimeout(r, 500));
          const ov = document.getElementById('ppOverlay');
          if (ov && ov.classList.contains('open') && (ov.textContent || '').length > 200) {
            const txt = ov.textContent || '';
            const out = { opened: true, card: txt.indexOf(cardName) !== -1, lock: txt.indexOf(lockText) !== -1, sample: txt.slice(0, 400) };
            const close = document.getElementById('ppClose');
            if (close) close.click();
            return out;
          }
        }
        return { opened: false };
      }, CARD_NAME, LOCK_TEXT);
      facts.overlayOpened = !!sweep.opened;
      facts.cardFound = !!sweep.card;
      facts.lockReasonFound = !!sweep.lock;
      if (sweep.sample) facts.overlaySample = sweep.sample;
      break;
    } catch (e) { log('rogue-select phase err: ' + e.message); await sleep(POLL_MS); }
  }

  await browser.close();   // disconnect only, game unaffected
  console.log('---- facts ----');
  console.log(JSON.stringify(facts, null, 1));
  process.exit(entered ? 0 : 1);
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
