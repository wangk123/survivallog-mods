// EndlessGuard Task 8: PlanPick overlay sweep — 11 injected cards, lock reasons, series grouping.
// Usage: node _t8_planpick.js [--buy-card 超市拾遗·六]
//   game must be in-save (RogueSelect reachable via PLAN_TALENT). Passive unless --buy-card given:
//   buys via official chain (cfOk → CLICK_BUY) ONLY for the named card, only if not locked.
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const CARD_NAMES = [
  '\u8d85\u5e02\u62fe\u9057\u00b7\u516d', '\u8d85\u5e02\u62fe\u9057\u00b7\u4e03', '\u8d85\u5e02\u62fe\u9057\u00b7\u516b',
  '\u8d85\u5e02\u62fe\u9057\u00b7\u4e5d', '\u8d85\u5e02\u62fe\u9057\u00b7\u5341',
  '\u533b\u9662\u8e0f\u52d8\u00b7\u4e09', '\u533b\u9662\u8e0f\u52d8\u00b7\u56db', '\u533b\u9662\u8e0f\u52d8\u00b7\u4e94',
  '\u5b66\u6821\u6478\u6392\u00b7\u4e09', '\u5b66\u6821\u6478\u6392\u00b7\u56db', '\u5b66\u6821\u6478\u6392\u00b7\u4e94'
];
const SERIES = ['\u8d85\u5e02\u62fe\u9057', '\u533b\u9662\u8e0f\u52d8', '\u5b66\u6821\u6478\u6392'];
const LOCK_TEXTS = ['\u751f\u5b58\u81f3\u7b2c100\u5929', '\u751f\u5b58\u81f3\u7b2c120\u5929', '\u751f\u5b58\u81f3\u7b2c140\u5929',
  '\u751f\u5b58\u81f3\u7b2c160\u5929', '\u751f\u5b58\u81f3\u7b2c300\u5929'];
const buyCard = (process.argv.find(x => x.startsWith('--buy-card=')) || '').split('=').slice(1).join('=') || null;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

async function findFrame(page, sub) {
  for (const f of page.frames()) {
    try { if (f.url().indexOf(sub) !== -1) return f; } catch (e) {}
  }
  return null;
}

(async () => {
  let browser = null;
  for (let i = 0; ; i++) {
    try { browser = await chromium.connectOverCDP(CDP); break; }
    catch (e) { if (i > 60) { log('FAIL: CDP unreachable'); process.exit(2); } await sleep(2000); }
  }
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    let has = false;
    try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); } catch (e) {}
    if (has) { page = p; break; }
  }
  if (!page) { log('FAIL: no Root page'); process.exit(2); }

  // if RogueSelect not open, try opening the plan page from CoreUI1
  let rs = await findFrame(page, '/UI/RogueSelect/RogueSelect.html');
  if (!rs) {
    const core = await findFrame(page, '/UI/CoreUI1/CoreUI1.html');
    if (core) { await core.evaluate(() => { core.UnitySendEvent('PLAN_TALENT'); }); log('PLAN_TALENT sent'); }
    for (let i = 0; i < 30 && !rs; i++) { await sleep(2000); rs = await findFrame(page, '/UI/RogueSelect/RogueSelect.html'); }
  }
  if (!rs) { log('FAIL: RogueSelect frame not reachable'); process.exit(1); }

  // wait ppEntry, open overlay
  let opened = false;
  for (let i = 0; i < 20 && !opened; i++) {
    opened = await rs.evaluate(async () => {
      for (let j = 0; j < 10; j++) {
        if (document.getElementById('ppEntry')) return true;
        await new Promise((r) => setTimeout(r, 500));
      }
      return false;
    });
  }
  if (!opened) { log('FAIL: #ppEntry not found (PlanPick not installed?)'); process.exit(1); }

  const facts = await rs.evaluate(async (args) => {
    const { cardNames, series, lockTexts, buyCard } = args;
    const out = { points: null, mult: null, totalSub: '', tabs: [], subBar: '', cardsFound: {}, locksFound: {},
                  buy: null, overlaySampleLen: 0 };
    document.getElementById('ppEntry').click();
    for (let i = 0; i < 30; i++) {
      const ov = document.getElementById('ppOverlay');
      if (ov && ov.classList.contains('open') && (ov.textContent || '').length > 200) break;
      await new Promise((r) => setTimeout(r, 500));
    }
    // expand all pagination
    for (let i = 0; i < 30; i++) {
      const more = document.getElementById('ppMore');
      if (!more || more.style.display === 'none') break;
      more.click();
      await new Promise((r) => setTimeout(r, 300));
    }
    out.points = document.getElementById('ppPoints') ? document.getElementById('ppPoints').textContent : null;
    out.mult = document.getElementById('ppMultHint') ? document.getElementById('ppMultHint').textContent : null;
    out.totalSub = document.getElementById('ppTitleSub') ? document.getElementById('ppTitleSub').textContent : '';
    out.subBar = (document.getElementById('ppSubBar') || {}).textContent || '';
    out.tabs = Array.from(document.querySelectorAll('#ppTabs .pp-tab')).map((b) => b.textContent);
    const list = document.getElementById('ppList');
    const listText = list ? (list.textContent || '') : '';
    out.overlaySampleLen = listText.length;
    for (const n of cardNames) out.cardsFound[n] = listText.indexOf(n) !== -1;
    for (const t of lockTexts) out.locksFound[t] = listText.indexOf(t) !== -1;
    for (const s of series) out.cardsFound['[series]' + s] = out.subBar.indexOf(s) !== -1;
    if (buyCard) {
      const cards = Array.from(document.querySelectorAll('#ppList .pp-card'));
      const el = cards.find((c) => (c.textContent || '').indexOf(buyCard) !== -1);
      if (!el) out.buy = { attempted: false, why: 'card not in list' };
      else if (el.classList.contains('locked')) out.buy = { attempted: false, why: 'locked', text: el.textContent.slice(0, 200) };
      else if (el.classList.contains('bought')) out.buy = { attempted: false, why: 'already bought' };
      else {
        const before = document.getElementById('ppPoints').textContent;
        el.click();
        for (let i = 0; i < 10; i++) {
          const cf = document.getElementById('ppConfirm');
          if (cf && cf.classList.contains('open')) break;
          await new Promise((r) => setTimeout(r, 300));
        }
        const ok = document.getElementById('cfOk');
        if (ok) ok.click();
        await new Promise((r) => setTimeout(r, 1500));
        const after = document.getElementById('ppPoints').textContent;
        const toast = (document.getElementById('pp-toast') || {}).textContent || '';
        out.buy = { attempted: true, before, after, toast };
      }
    }
    const close = document.getElementById('ppClose');
    if (close) close.click();
    return out;
  }, { cardNames: CARD_NAMES, series: SERIES, lockTexts: LOCK_TEXTS, buyCard });

  await browser.close();
  log('---- facts ----');
  console.log(JSON.stringify(facts, null, 1));
  const allCards = CARD_NAMES.every((n) => facts.cardsFound[n]);
  process.exit(allCards ? 0 : 1);
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
