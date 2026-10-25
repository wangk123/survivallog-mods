// Direct watchdog exercise: open pause -> open panel -> CDP-click the official
// "back to game" button (no ESC keydown anywhere) -> pause iframe removed ->
// panel orphan watchdog must fire MC_CLOSE{why:'host-doc-gone'} and FocusTick
// must restore the keyboard. Complements fixture outsetting whose ESC path
// raced the keydown closer this run.
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const { execFile } = require('child_process');
const FS = require('fs');
const KB = 'E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/ModCenter_kb.txt';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function esc() {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', 'E:/Game/Mod/mod_stack/tools/_mc_sendkey.ps1'], { timeout: 20000 }, () => resolve(true));
  });
}
const tail = '/UI/OutSetting/OutSetting.html';
(async () => {
  const browser = await chromium.connectOverCDP('http://localhost:9222');
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
  }
  if (!page) { console.log('no root'); process.exit(2); }
  const ids = () => page.evaluate(() => Array.from(document.querySelectorAll('#iframe-container iframe')).map((f) => f.id));

  // 1. open pause (adaptive)
  for (let i = 0; i < 10 && (await ids()).indexOf('OutSetting') === -1; i++) {
    await esc();
    for (let w = 0; w < 5 && (await ids()).indexOf('OutSetting') === -1; w++) await sleep(400);
  }
  if ((await ids()).indexOf('OutSetting') === -1) { console.log('pause would not open'); process.exit(2); }
  console.log('pause open');

  // 2. install sent spy + open panel via entry click
  await page.evaluate(() => {
    if (!window.__wdSpy) {
      window.__wdSpy = true; window.__wdLog = [];
      const orig = window.__mcDrain;
      window.__mcDrain = function () { const a = orig(); if (a && a.length) window.__wdLog.push.apply(window.__wdLog, a); return a; };
    }
    return true;
  });
  const opened = await page.evaluate((tail) => {
    for (const f of document.querySelectorAll('#iframe-container iframe')) {
      if (f.id !== 'OutSetting') continue;
      try { const d = f.contentDocument; const e = d.getElementById('__mc_entry'); if (e) { e.click(); return true; } } catch (e) {}
    }
    return false;
  }, tail);
  if (!opened) { console.log('panel did not open'); process.exit(1); }
  await sleep(3000); // drain beat: MC_OPEN consumed; wait overlay rows
  const rows = await page.evaluate((tail) => {
    for (const f of document.querySelectorAll('#iframe-container iframe')) {
      if (f.id !== 'OutSetting') continue;
      try { return f.contentDocument.querySelectorAll('#__mc_side .__mc-mod-row').length; } catch (e) {}
    }
    return -1;
  }, tail);
  console.log('panel rows=' + rows);
  if (rows < 5) { console.log('panel snapshot missing'); process.exit(1); }

  const mark = await page.evaluate(() => (window.__wdLog || []).length);
  const kbBefore = FS.existsSync(KB) ? FS.readFileSync(KB, 'utf8') : '';

  // 3. click the OFFICIAL "back to game" button (RA_Back) -- no keydown path
  const clickedBack = await page.evaluate((tail) => {
    for (const f of document.querySelectorAll('#iframe-container iframe')) {
      if (f.id !== 'OutSetting') continue;
      try {
        const d = f.contentDocument;
        const btn = d.querySelector('.menu-list .main-btn'); // first = back-to-game
        if (btn) { btn.click(); return 'clicked:' + (btn.textContent || '').trim(); }
      } catch (e) {}
    }
    return null;
  }, tail);
  console.log('back button: ' + clickedBack);

  // 4. OutSetting iframe must disappear
  let outGone = false;
  for (let i = 0; i < 20; i++) { if ((await ids()).indexOf('OutSetting') === -1) { outGone = true; break; } await sleep(500); }
  console.log('outGone=' + outGone);

  // 5. watchdog MC_CLOSE{why} within ~4s (1s watchdog + 2s drain + slack)
  let wdMsg = null;
  for (let i = 0; i < 16; i++) {
    const sent = await page.evaluate((mark) => (window.__wdLog || []).slice(mark), mark);
    wdMsg = sent.find((m) => m && m.type === 'MC_CLOSE') || null;
    if (wdMsg) break;
    await sleep(500);
  }
  console.log('watchdog msg: ' + JSON.stringify(wdMsg));

  // 6. kb CLOSE line after
  let kbClose = null;
  for (let i = 0; i < 20; i++) {
    const txt = FS.existsSync(KB) ? FS.readFileSync(KB, 'utf8') : '';
    if (txt.length > kbBefore.length && /CLOSE kb=off/.test(txt.slice(kbBefore.length))) { kbClose = true; break; }
    await sleep(500);
  }
  console.log('kbCloseRestored=' + !!kbClose);

  const pass = outGone && wdMsg && wdMsg.why === 'host-doc-gone' && kbClose;
  console.log(pass ? 'WATCHDOG-DIRECT PASS' : 'WATCHDOG-DIRECT FAIL');
  await browser.close();
  process.exit(pass ? 0 : 1);
})().catch((e) => { console.log('FATAL ' + e.message); process.exit(2); });
