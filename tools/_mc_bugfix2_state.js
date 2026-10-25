// Tiny state helper: print frame ids (and optional ESC-send loop).
// usage: node tools/_mc_bugfix2_state.js          -> print frames
//        node tools/_mc_bugfix2_state.js openpause -> adaptive-ESC until OutSetting
//        node tools/_mc_bugfix2_state.js closepause-> adaptive-ESC until no OutSetting
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const { execFile } = require('child_process');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const CMD = process.argv[2] || 'frames';
function esc() {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', 'E:/Game/Mod/mod_stack/tools/_mc_sendkey.ps1'], { timeout: 20000 }, () => resolve(true));
  });
}
(async () => {
  const browser = await chromium.connectOverCDP('http://localhost:9222');
  let page = null;
  for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
    try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
  }
  if (!page) { console.log('NO-ROOT'); process.exit(2); }
  const ids = () => page.evaluate(() => Array.from(document.querySelectorAll('#iframe-container iframe')).map((f) => f.id));
  if (CMD === 'frames') {
    console.log(JSON.stringify(await ids()));
  } else {
    const want = CMD === 'openpause';
    for (let i = 0; i < 10; i++) {
      const cur = (await ids()).indexOf('OutSetting') !== -1;
      if (cur === want) { console.log(want ? 'PAUSE-OPEN' : 'PAUSE-CLOSED'); await browser.close(); process.exit(0); }
      await esc();
      for (let w = 0; w < 5; w++) { await sleep(400); const c2 = (await ids()).indexOf('OutSetting') !== -1; if (c2 === want) { console.log(want ? 'PAUSE-OPEN' : 'PAUSE-CLOSED'); await browser.close(); process.exit(0); } }
    }
    console.log('TIMEOUT frames=' + JSON.stringify(await ids()));
    process.exit(1);
  }
  await browser.close();
})().catch((e) => { console.log('FATAL ' + e.message); process.exit(2); });
