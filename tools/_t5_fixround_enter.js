// Task 5 fix-round: enter first endless save once, confirm scene load, exit. Read-only.
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const browser = await chromium.connectOverCDP('http://localhost:9222');
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
  }
  if (!page) { console.log('no root page'); process.exit(2); }
  const deadline = Date.now() + 180000;
  for (;;) {
    let frame = null;
    for (const f of page.frames()) { try { if (f.url().indexOf('/UI/MainUI/MainUI.html') !== -1) { frame = f; break; } } catch (e) {} }
    if (!frame) { if (Date.now() > deadline) { console.log('no MainUI frame'); process.exit(1); } await sleep(2000); continue; }
    try {
      const res = await frame.evaluate(async () => {
        if (!document.querySelector('.menu')) return null;
        core.UnitySendEvent('LOAD_GAME');
        for (let i = 0; i < 30; i++) {
          await new Promise((r) => setTimeout(r, 500));
          const st = (typeof state !== 'undefined') ? state : window.state;
          if (st && st.saveList && st.saveList.length) {
            const pick = st.saveList.find((s) => s.isEndless && !s.isEndingSave) || st.saveList.find((s) => !s.isEndingSave);
            if (pick) { loadSave(pick.Index !== undefined ? pick.Index : 0); return { idx: pick.Index, turn: pick.turnText || '' }; }
          }
        }
        return null;
      });
      if (res) { console.log('loadSave sent: ' + JSON.stringify(res)); break; }
    } catch (e) { console.log('err: ' + e.message); }
    if (Date.now() > deadline) { console.log('timeout'); process.exit(1); }
    await sleep(2000);
  }
  // wait for in-game HUD = scene loaded
  const dl = Date.now() + 180000;
  for (;;) {
    let hit = false;
    for (const f of page.frames()) { try { if (f.url().indexOf('/UI/CoreUI1/CoreUI1.html') !== -1) { hit = true; break; } } catch (e) {} }
    if (hit) { console.log('in-game HUD up'); break; }
    if (Date.now() > dl) { console.log('HUD not seen'); break; }
    await sleep(3000);
  }
  await browser.close();
  process.exit(0);
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
