// Bugfix round 2 -- MAIN MENU phase verification (CDP 9222).
// Checks: (1) slider explicit track/thumb styles + computed webkitAppearance
//         (2) ovr ID input type=text/inputmode/no-min; "8012" adds OK, "abc"
//             rejected, duplicate rejected; panel-side round-trip restores cfg
//         (3) __mcObserver installed; __mcSelfCheck() true
//         (4) slow-tick self-heal: null out __mcObserver -> <=2s guard beat
//             reinstalls it
//         (5) screenshot of the panel with sliders visible
// ASCII-only; Chinese via \u escapes (repo tool convention).
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const FS = require('fs');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const QA_CFG = 'E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/config/com.local.survivallog.quickaction.cfg';
const QA_GUID = 'com.local.survivallog.quickaction';
const RS_GUID = 'com.local.survivallog.robotspeed';
const SHOT = 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-shot-menu.png';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const log = (m) => console.log('[' + ((Date.now() - t0) / 1000).toFixed(1) + 's] ' + m);
const R = {};

function readCfgKey(path, section, key) {
  try {
    const t = FS.readFileSync(path, 'utf8');
    const m = t.match(new RegExp('\\[' + section + '\\]([\\s\\S]*?)(\\n\\[|$)'));
    if (!m) return null;
    const line = m[1].split('\n').find((l) => l.trim().indexOf(key) === 0);
    return line ? line.slice(line.indexOf('=') + 1).trim() : null;
  } catch (e) { return null; }
}

// run a function inside the MainUI host doc (via Root page.evaluate)
function inMain(page, fn, ...args) {
  return page.evaluate(({ tail, src, args }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const f = eval('(' + src + ')');
          return f.apply(null, [d].concat(args));
        }
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, src: fn.toString(), args });
}

(async () => {
  let browser;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; }
    catch (e) { if (Date.now() - t0 > 120000) { log('FAIL CDP unreachable'); process.exit(2); } await sleep(1500); }
  }
  log('CDP connected');
  let page;
  const deadline = Date.now() + 150000;
  for (;;) {
    let got = null;
    for (const ctx of browser.contexts()) for (const p of ctx.pages()) {
      try { if (await p.evaluate(() => !!document.querySelector('#iframe-container'))) { got = p; break; } } catch (e) {}
    }
    if (got) { page = got; break; }
    if (Date.now() > deadline) { log('FAIL no Root page'); process.exit(2); }
    await sleep(1500);
  }

  // wait MainUI + entry + panel bootstrap
  for (;;) {
    const ok = await inMain(page, (d) => !!(d.getElementById('__mc_entry') && window.__mcDrain && window.__mcRender));
    if (ok) break;
    if (Date.now() > deadline) { log('FAIL bootstrap not ready'); process.exit(2); }
    await sleep(1500);
  }
  log('entry + panel bootstrap ready');

  // --- check 3: observer + selfcheck ---
  R.observerInstalled = await page.evaluate(() => !!window.__mcObserver);
  R.selfCheckTrue = await page.evaluate(() => (typeof window.__mcSelfCheck === 'function') ? window.__mcSelfCheck() : null);
  R.loadHooks = await inMain(page, (d) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    let hooked = 0;
    for (const f of fs) if (f.__mcLoadHooked) hooked++;
    return hooked;
  });
  log('observer=' + R.observerInstalled + ' selfCheck=' + R.selfCheckTrue + ' loadHookedFrames=' + R.loadHooks);

  // open panel + go to QuickAction (has ovr block)
  await inMain(page, (d) => { const e = d.getElementById('__mc_entry'); if (e) e.click(); return true; });
  // wait snapshot + side rows (drain is a 2s beat)
  {
    const dlR = Date.now() + 20000;
    for (;;) {
      const rows = await inMain(page, (d) => d.querySelectorAll('#__mc_side .__mc-mod-row').length);
      if (rows >= 10) break;
      if (Date.now() > dlR) { log('FAIL panel rows never arrived: ' + rows); process.exit(1); }
      await sleep(600);
    }
  }
  const clickRow = (guid) => inMain(page, (d, guid) => {
    const row = Array.from(d.querySelectorAll('#__mc_side .__mc-mod-row')).find((r) => r.getAttribute('data-guid') === guid);
    if (row) { row.click(); return true; } return false;
  }, guid);
  if (!await clickRow(QA_GUID)) { log('FAIL cannot click QA row'); process.exit(1); }
  await sleep(300);

  // --- check 2: ovr ID input shape ---
  R.ovrInput = await inMain(page, (d) => {
    const inp = d.getElementById('__mc_ovr_new');
    if (!inp) return null;
    return { type: inp.type, inputmode: inp.getAttribute('inputmode'),
      hasMinAttr: inp.hasAttribute('min'), placeholder: inp.getAttribute('placeholder') };
  });
  log('ovr input: ' + JSON.stringify(R.ovrInput));

  const ovrRows = () => inMain(page, (d) => Array.from(d.querySelectorAll('#__mc_cfg .__mc-item')).filter((it) => it.querySelector('[data-act="ovr-set"]')).length);
  const setOvrVal = (v) => inMain(page, (d, v) => { const i = d.getElementById('__mc_ovr_new'); i.value = v; return true; }, v);
  const clickAdd = () => inMain(page, (d) => { d.querySelector('#__mc_cfg [data-act="ovr-add"]').click(); return true; });
  const waitCfg = async (pred, budget) => {
    const dl = Date.now() + (budget || 12000);
    for (;;) { const v = readCfgKey(QA_CFG, '\u5176\u4ed6', 'Overrides'); if (pred(v)) return v; if (Date.now() > dl) return v; await sleep(500); }
  };

  R.cfgBefore = readCfgKey(QA_CFG, '\u5176\u4ed6', 'Overrides');
  R.rowsBefore = await ovrRows();
  log('cfg Overrides before: "' + R.cfgBefore + '" rows=' + R.rowsBefore);

  // "8012" adds OK
  await setOvrVal('8012');
  await clickAdd();
  const cfgAfter8012 = await waitCfg((v) => v != null && v.indexOf('8012:1000') !== -1);
  R.add8012Rows = await ovrRows();
  R.add8012Cfg = cfgAfter8012;
  R.add8012Ok = R.add8012Rows === R.rowsBefore + 1 && (cfgAfter8012 || '').indexOf('8012:1000') !== -1;
  log('add 8012: rows ' + R.rowsBefore + '->' + R.add8012Rows + ' cfg="' + cfgAfter8012 + '" ok=' + R.add8012Ok);

  // "abc" rejected (no change)
  await setOvrVal('abc');
  await clickAdd();
  await sleep(300);
  R.addAbcRows = await ovrRows();
  R.addAbcRejected = R.addAbcRows === R.add8012Rows;
  log('add abc: rows ' + R.addAbcRows + ' rejected=' + R.addAbcRejected);

  // duplicate "8012" rejected
  await setOvrVal('8012');
  await clickAdd();
  await sleep(300);
  R.addDupRows = await ovrRows();
  R.addDupRejected = R.addDupRows === R.add8012Rows;
  log('add dup 8012: rows ' + R.addDupRows + ' rejected=' + R.addDupRejected);

  // --- check 1: slider explicit styles (ovr slider now exists on this page) ---
  R.sliderCss = await inMain(page, (d) => {
    const el = d.querySelector('#__mc_cfg input[type=range]');
    if (!el) return null;
    const cs = d.defaultView.getComputedStyle(el);
    const trackCs = d.defaultView.getComputedStyle(el, '::-webkit-slider-runnable-track');
    const thumbCs = d.defaultView.getComputedStyle(el, '::-webkit-slider-thumb');
    let rules = [];
    for (const sh of Array.from(d.styleSheets)) {
      let rs; try { rs = sh.cssRules; } catch (e) { continue; }
      if (!rs) continue;
      for (const r of Array.from(rs)) {
        if (r.selectorText && r.selectorText.indexOf('slider-runnable-track') !== -1) rules.push(r.cssText);
        if (r.selectorText && r.selectorText.indexOf('slider-thumb') !== -1) rules.push(r.cssText);
        if (r.selectorText && r.selectorText.indexOf('moz-range') !== -1) rules.push(r.cssText);
      }
    }
    const styleEl = d.getElementById('__mc_panel_style');
    return {
      webkitAppearance: cs.webkitAppearance,
      appearanceNone: cs.appearance === 'none' || cs.webkitAppearance === 'none',
      height: cs.height,
      trackBg: trackCs ? trackCs.backgroundColor : null,
      trackH: trackCs ? trackCs.height : null,
      thumbW: thumbCs ? thumbCs.width : null,
      thumbBg: thumbCs ? thumbCs.backgroundColor : null,
      rules,
      styleText: styleEl ? styleEl.textContent : null,
    };
  });
  log('slider css: ' + JSON.stringify(R.sliderCss && {
    appearanceNone: R.sliderCss.appearanceNone, trackBg: R.sliderCss.trackBg, trackH: R.sliderCss.trackH,
    thumbW: R.sliderCss.thumbW, thumbBg: R.sliderCss.thumbBg, nRules: R.sliderCss.rules.length }));

  // screenshot WITH ovr slider visible (also switch to RobotSpeed for a value slider shot later)
  try { await page.screenshot({ path: SHOT }); log('screenshot saved (qa page): ' + SHOT); } catch (e) { log('shot fail ' + e.message); }

  // restore: delete the 8012 row via panel (official path)
  await inMain(page, (d) => {
    const rows = Array.from(d.querySelectorAll('#__mc_cfg .__mc-item'));
    for (const it of rows) {
      const lb = it.querySelector('.__mc-lb');
      if (lb && lb.textContent.indexOf('#8012') !== -1) { it.querySelector('[data-act="ovr-del"]').click(); return true; }
    }
    return false;
  });
  const cfgRestored = await waitCfg((v) => v === R.cfgBefore);
  R.del8012Cfg = cfgRestored;
  R.del8012Restored = cfgRestored === R.cfgBefore;
  R.rowsAfterDel = await ovrRows();
  log('del 8012: cfg back to "' + cfgRestored + '" ok=' + R.del8012Restored + ' rows=' + R.rowsAfterDel);

  // also assert a VALUE slider (RobotSpeed) renders with explicit styles + shot it
  if (await clickRow(RS_GUID)) {
    await sleep(250);
    R.rsSlider = await inMain(page, (d) => {
      const el = d.querySelector('#__mc_cfg input[type=range]');
      if (!el) return null;
      const cs = d.defaultView.getComputedStyle(el);
      const tc = d.defaultView.getComputedStyle(el, '::-webkit-slider-runnable-track');
      return { appearanceNone: cs.appearance === 'none' || cs.webkitAppearance === 'none', trackBg: tc ? tc.backgroundColor : null };
    });
    log('robot slider: ' + JSON.stringify(R.rsSlider));
  }

  // --- check 4: slow-tick self-heal (null out observer -> guard beat reinstalls) ---
  const healT0 = Date.now();
  await page.evaluate(() => { window.__mcObserver = null; return true; });
  let healed = false; let healedAt = -1;
  const healDl = Date.now() + 8000;
  while (Date.now() < healDl) {
    healed = await page.evaluate(() => !!window.__mcObserver);
    if (healed) { healedAt = Date.now() - healT0; break; }
    await sleep(300);
  }
  R.selfHealMs = healedAt;
  R.entryStillThere = await inMain(page, (d) => !!d.getElementById('__mc_entry'));
  R.panelStillAlive = await page.evaluate(() => !!window.__mcDrain);
  log('self-heal: ' + (healed ? 'reinstalled in ' + healedAt + 'ms' : 'NOT healed within 8s')
    + ' entry=' + R.entryStillThere + ' panel=' + R.panelStillAlive);

  // verdicts
  const V = {
    observerInstalled: R.observerInstalled === true,
    selfCheckTrue: R.selfCheckTrue === true,
    loadHooked: R.loadHooks >= 1,
    ovrTypeText: !!(R.ovrInput && R.ovrInput.type === 'text'),
    ovrInputmode: !!(R.ovrInput && R.ovrInput.inputmode === 'numeric'),
    ovrNoMin: !!(R.ovrInput && R.ovrInput.hasMinAttr === false),
    ovrPlaceholderKept: !!(R.ovrInput && R.ovrInput.placeholder === '\u52a8\u4f5c ID'),
    add8012Ok: R.add8012Ok === true,
    addAbcRejected: R.addAbcRejected === true,
    addDupRejected: R.addDupRejected === true,
    del8012Restored: R.del8012Restored === true,
    sliderAppearanceNone: !!(R.sliderCss && R.sliderCss.appearanceNone),
    // NOTE: this CEF build cannot resolve getComputedStyle pseudo-elements for
    // range pseudos (returns element fallback: trackBg transparent, thumbW=input
    // width) and serializes colors as rgb() -- acceptance = computed
    // webkitAppearance none + RULE PRESENCE with the right colors (task spec),
    // plus visual screenshot check. -moz rules: engine drops them at parse;
    // asserted at INSTALLED-SOURCE level (style element textContent).
    sliderTrackRule: !!(R.sliderCss && R.sliderCss.rules.some((r) => r.indexOf('slider-runnable-track') !== -1
      && (/2a2f3d/i.test(r) || /rgb\(42, ?47, ?61\)/i.test(r)))),
    sliderThumbRule: !!(R.sliderCss && R.sliderCss.rules.some((r) => r.indexOf('slider-thumb') !== -1
      && (/e8a33d/i.test(r) || /rgb\(232, ?163, ?61\)/i.test(r)))),
    sliderMozFallback: !!(R.sliderCss && R.sliderCss.styleText
      && R.sliderCss.styleText.indexOf('-moz-range-track') !== -1
      && R.sliderCss.styleText.indexOf('-moz-range-thumb') !== -1),
    sliderRulesInSheet: !!(R.sliderCss && R.sliderCss.rules.length >= 2),
    rsSliderStyled: !!(R.rsSlider && R.rsSlider.appearanceNone),
    selfHealWithinBeat: typeof R.selfHealMs === 'number' && R.selfHealMs <= 4500,
    entryStillThere: R.entryStillThere === true,
    panelStillAlive: R.panelStillAlive === true,
  };
  let pass = 0, fail = 0;
  for (const [k, v] of Object.entries(V)) { if (v) pass++; else { fail++; log('FAIL ' + k); } }
  console.log('\n==== MENU PHASE: ' + pass + ' PASS / ' + fail + ' FAIL ====');
  FS.writeFileSync('E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter/bugfix-round2-menu-facts.json', JSON.stringify({ V, R }, null, 2));
  await browser.close();
  process.exit(fail ? 1 : 0);
})().catch((e) => { console.log('FATAL ' + e.message); process.exit(2); });
