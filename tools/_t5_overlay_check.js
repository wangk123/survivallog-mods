// Task 5 follow-up: sweep the RogueSelect page (already open in-game) for the
// injected card. Re-sends PLAN_TALENT if the page is not up yet. Read-only + open/close overlay.
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');
const CDP = 'http://localhost:9222';
const CARD_NAME = '\u8d85\u5e02\u62fe\u9057\u00b7\u516d';   // 超市拾遗·六
const CARD_NAME7 = '\u8d85\u5e02\u62fe\u9057\u00b7\u4e03';  // 超市拾遗·七
const CARD_NAME10 = '\u8d85\u5e02\u62fe\u9057\u00b7\u5341'; // 超市拾遗·十
const HOSP = '\u533b\u9662\u8e0f\u52d8\u00b7\u4e09';        // 医院踏勘·三
const LOCK100 = '\u751f\u5b58\u81f3\u7b2c100\u5929';        // 生存至第100天
const LOCK160 = '\u751f\u5b58\u81f3\u7b2c160\u5929';        // 生存至第160天
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const browser = await chromium.connectOverCDP(CDP);
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
  }
  if (!page) { console.log('no root page'); process.exit(2); }

  // ensure the planning page is open (resend is harmless if already open)
  for (const f of page.frames()) {
    try {
      if (f.url().indexOf('/UI/CoreUI1/CoreUI1.html') !== -1) {
        await f.evaluate(() => { core.UnitySendEvent('PLAN_TALENT'); });
        console.log('PLAN_TALENT resent');
      }
    } catch (e) {}
  }

  const deadline = Date.now() + 120000;
  for (;;) {
    let rs = null;
    for (const f of page.frames()) { try { if (f.url().indexOf('/UI/RogueSelect/RogueSelect.html') !== -1) { rs = f; break; } } catch (e) {} }
    if (!rs) { if (Date.now() > deadline) { console.log('RogueSelect frame not found'); process.exit(1); } await sleep(2000); continue; }
    try {
      const res = await rs.evaluate(async (names, lock) => {
        const out = { entry: false, opened: false };
        for (let i = 0; i < 25; i++) {
          if (document.getElementById('ppEntry')) { out.entry = true; break; }
          await new Promise((r) => setTimeout(r, 500));
        }
        if (!out.entry) return out;
        document.getElementById('ppEntry').click();
        let ov = null;
        for (let i = 0; i < 40; i++) {
          await new Promise((r) => setTimeout(r, 500));
          ov = document.getElementById('ppOverlay');
          if (ov && ov.classList.contains('open') && (ov.textContent || '').length > 200) break;
          ov = null;
        }
        if (!ov) return out;
        out.opened = true;
        const txt = ov.textContent || '';
        for (const n of names) out[n] = txt.indexOf(n) !== -1;
        out.lock100 = txt.indexOf(lock[0]) !== -1;
        out.lock160 = txt.indexOf(lock[1]) !== -1;
        const close = document.getElementById('ppClose');
        if (close) close.click();
        return out;
      }, [CARD_NAME, CARD_NAME7, CARD_NAME10, HOSP], [LOCK100, LOCK160]);
      await browser.close();
      console.log(JSON.stringify(res, null, 1));
      process.exit(0);
    } catch (e) { console.log('err: ' + e.message); await sleep(2000); }
  }
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
