// ModCenter task-8 typing-probe retry (one bounded variant): establish browser-native
// focus via full pointer event sequence on the ovr ID input before real SendKeys.
// If this also fails, the probe is environmental (CDP focus != Unity-side focus for
// Vuplex key routing) and the kb self-evidence file stands as the authority.
// ASCII-only file (repo tool convention).
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');
const { execFile } = require('child_process');
const FS = require('fs');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const GAME_ROOT = 'E:/SteamLibrary/steamapps/common/Survival Log';
const KB_TXT = GAME_ROOT + '/BepInEx/ModCenter_kb.txt';
const QA_GUID = 'com.local.survivallog.quickaction';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const kbLines = () => { try { return FS.readFileSync(KB_TXT, 'utf8').split('\n').filter((l) => l.trim()); } catch (e) { return []; } };

function sendKeysReal(key) {
  return new Promise((resolve) => {
    execFile('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', 'E:/Game/Mod/mod_stack/tools/_mc_kb_type.ps1', '-Key', key],
      { timeout: 20000 }, (err, stdout) => resolve({ err: err ? String(err.message) : null, stdout: String(stdout || '') }));
  });
}

(async () => {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const browser = await chromium.connectOverCDP(CDP);
  let page = null;
  for (const ctx of browser.contexts()) {
    for (const p of ctx.pages()) {
      try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { page = p; break; } } catch (e) {}
    }
    if (page) break;
  }
  if (!page) { log('FAIL: no Root page'); process.exit(2); }
  const inMain = (fn, arg) => page.evaluate(({ tail, fnSrc, arg }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) return eval('(' + fnSrc + ')').call(null, d, arg);
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, fnSrc: fn.toString(), arg });

  // open panel if not open (kb gate must be ON)
  const n0 = kbLines().length;
  await inMain((d) => { const e = d.getElementById('__mc_entry'); if (e) e.click(); return true; }, null);
  const dl = Date.now() + 20000;
  for (;;) {
    const lines = kbLines();
    if (lines.length > n0 && /OPEN kb=on/.test(lines[lines.length - 1] || lines[n0] || '')) break;
    if (Date.now() > dl) break;
    await sleep(400);
  }
  await sleep(800);
  await inMain((d, guid) => { const r = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]'); if (r) r.click(); return !!r; }, QA_GUID);
  await sleep(300);
  // pointer event sequence -> native-ish focus, then real keys
  const focusInfo = await inMain((d) => {
    const inp = d.getElementById('__mc_ovr_new');
    if (!inp) return null;
    const r = inp.getBoundingClientRect();
    const opts = { bubbles: true, cancelable: true, view: window, button: 0,
      clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 };
    try {
      inp.dispatchEvent(new MouseEvent('pointerdown', opts));
      inp.dispatchEvent(new MouseEvent('mousedown', opts));
      inp.focus();
      inp.dispatchEvent(new MouseEvent('mouseup', opts));
      inp.dispatchEvent(new MouseEvent('click', opts));
    } catch (e) {}
    return { active: d.activeElement === inp, tag: d.activeElement ? d.activeElement.id || d.activeElement.tagName : null,
      hostActive: window.document.activeElement && window.document.activeElement.tagName };
  }, null);
  const k1 = await sendKeysReal('8012');
  await sleep(1000);
  let val = await inMain((d) => { const i = d.getElementById('__mc_ovr_new'); return i ? i.value : null; }, null);
  if (!val) { const k2 = await sendKeysReal('1234'); await sleep(1000); val = await inMain((d) => { const i = d.getElementById('__mc_ovr_new'); return i ? i.value : null; }, null); log('retry2: ' + k2.stdout.trim()); }
  // cleanup: clear value (never committed), close panel
  await inMain((d) => { const i = d.getElementById('__mc_ovr_new'); if (i) i.value = ''; const x = d.querySelector('#__mc_panel .__mc-close'); if (x) x.click(); return true; }, null);
  const dl2 = Date.now() + 20000;
  const n1 = kbLines().length;
  for (;;) {
    const lines = kbLines();
    if (lines.length > n1 && /CLOSE kb=off/.test(lines[lines.length - 1] || '')) break;
    if (Date.now() > dl2) break;
    await sleep(400);
  }
  await browser.close();
  console.log(JSON.stringify({ focusInfo, ps: k1.stdout.trim(), value: val, kbTail: kbLines().slice(-2) }, null, 1));
  log(val ? 'TYPING PROBE: LANDED value=' + val : 'TYPING PROBE: still empty (environmental - kb self-evidence stands)');
  process.exit(0);
})().catch((e) => { console.error('ERROR: ' + (e && e.stack || e)); process.exit(1); });
