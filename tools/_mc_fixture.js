// ModCenter Task 7 verification fixture (live game, CDP).
// Connection pattern copied from tools/_mc_verify_cdp.js / _mc_verify6_cdp.js
// (playwright-core connectOverCDP, Root page via #iframe-container, all DOM work
// in ROOT page via page.evaluate with args).
//
// Modes:
//   node tools/_mc_fixture.js menu       (default) Main-menu phase:
//        - open panel via entry click, wait snapshot + full render
//        - layout: overlay/panel/head/foot/toast, side (9 suite + self lock + tp hint)
//        - page-by-page assertions for all 9 mods (groups/items/controls vs Meta)
//        - PlanPick Cost range min=100/max=300 (debt D: AcceptableValueRange reflection)
//        - interactions through REAL DOM events:
//            slider (RobotSpeed moveMult) incl. 100ms throttle burst shape
//            bool (RoleUnlock flower seeds), select (UiFit.Mode), reload-badge toast
//            side toggle (PreDisasterSpeed off/on, banner + [Disabled] persistence)
//            ovr round-trip: add #8012 -> drag 250 -> fresh snapshot -> delete -> ''
//              (serialized string asserted in outbox AND in quickaction cfg on disk)
//        - close via X button, MC_CLOSE in sent log
//   node tools/_mc_fixture.js loadgame   click "continue" (or "new game") and wait
//        until MainUI iframe is gone (world loading). ESC is sent by the bash driver.
//   node tools/_mc_fixture.js outsetting poll for OutSetting iframe (after driver ESC),
//        assert #__mc_entry insertBefore position (.btn-restart/.btn-exit immediately
//        after), open panel there, assert side rows, close, coexist scan (ppEntry).
//
// ASCII-only file; Chinese via \u escapes (repo tool convention).

const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');
const FS = require('fs');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const OUT_HTML = '/UI/OutSetting/OutSetting.html';
const GAME_ROOT = 'E:/SteamLibrary/steamapps/common/Survival Log';
const MC_CFG = GAME_ROOT + '/BepInEx/config/com.local.survivallog.modcenter.cfg';
const QA_CFG = GAME_ROOT + '/BepInEx/config/com.local.survivallog.quickaction.cfg';
const PD_GUID = 'com.local.survivallog.predisasterspeed';
const RS_GUID = 'com.local.survivallog.robotspeed';
const QA_GUID = 'com.local.survivallog.quickaction';
const SHOT_DIR = 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter';

const MODE = (process.argv[2] || 'menu').toLowerCase();
const CONNECT_BUDGET_MS = 180000;
const POLL_MS = 1500;

// ---- Chinese literals (\u-escaped) ----
const Z = {
  bp: '\u80cc\u5305\u6269\u5bb9', cb: '\u67dc\u5b50\u6269\u5bb9', fc: '\u4fdd\u9c9c\u589e\u5f3a',
  qa: '\u52a8\u4f5c\u52a0\u901f', ce: '\u66f4\u597d\u7684\u50a8\u7269\u67dc', ru: '\u89d2\u8272\u89e3\u9501',
  rs: '\u673a\u5668\u4eba\u63d0\u901f', pp: '\u81ea\u9009\u751f\u5b58\u89c4\u5212', pd: '\u707e\u524d\u52a0\u901f',
  gExpand: '\u6269\u5bb9', gUiFit: '\u89c6\u53e3\u81ea\u9002\u5e94', gChill: '\u4fdd\u9c9c',
  gMs: '\u5206\u7c7b\u8017\u65f6\uff08\u6beb\u79d2\uff0c0=\u5b98\u65b9\u539f\u503c\uff09',
  gVine: '\u85e4\u8513\u89d2\u8272', gPlan: '\u914d\u65b9\u4e0e\u89c4\u5212',
  gCost: '\u82b1\u8d39', gList: '\u5217\u8868', gRs: '\u673a\u5668\u4eba\u63d0\u901f',
  moveKey: '\u79fb\u901f\u500d\u7387', secRs: '\u673a\u5668\u4eba\u63d0\u901f',
  flower: '\u89e3\u9501\u85e4\u8513\u4e13\u5c5e\u82b1\u79cd\u5b50',
  burden: '\u8d1f\u91cd\u500d\u7387',
  curW: '\u5f53\u524d \u5bbd', w15h20: '\u5bbd1.5\u00d7\u9ad82.0',
  afterLoad: '\u8bfb\u6863\u540e', toggleOnly: '\u4ec5\u652f\u6301\u542f\u7528',
  fool: '\u9632\u5446', ovrNew: '\u65b0\u589e\u8986\u76d6',
  cont: '\u7ee7\u7eed\u6e38\u620f', newg: '\u65b0\u6e38\u620f',
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function readCfgSection(path, section, key) {
  try {
    const t = FS.readFileSync(path, 'utf8');
    const m = t.match(new RegExp('\\[' + section + '\\]([\\s\\S]*?)(\\n\\[|$)'));
    if (!m) return null;
    const line = m[1].split('\n').find((l) => l.indexOf(key) !== -1 && l.trim().indexOf(key) === 0);
    return line ? line.trim() : null;
  } catch (e) { return null; }
}

function stripNonCodeLines(line) {
  if (!line) return null;
  const eq = line.indexOf('=');
  return eq === -1 ? null : line.slice(eq + 1).trim();
}

async function connect(t0, log) {
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; } catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) { log('FAIL: CDP unreachable: ' + e.message); process.exit(2); }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected');
  const pageDeadline = Date.now() + 180000;
  for (;;) {
    let page = null;
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); } catch (e) { has = false; }
        if (has) { page = p; break; }
      }
      if (page) break;
    }
    if (page) return { browser, page };
    if (Date.now() > pageDeadline) { log('FAIL: no Root page'); await browser.close(); process.exit(2); }
    await sleep(POLL_MS);
  }
}

// Find an iframe document by pathname tail (searched from Root, like MenuEntryInjector).
function snapEnabledOf(page, guid) {
  return page.evaluate((guid) => {
    const s = window.__mcLastSnapshot;
    const m = s && s.mods && s.mods.find((x) => x.guid === guid);
    return m ? m.enabled : undefined;
  }, guid);
}

// ==========================================================================
//  MODE: menu
// ==========================================================================
async function menuMode() {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const { browser, page } = await connect(t0, log);

  // wait for MainUI + entry + panel bootstrap
  const ready = () => page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1)
          return !!(d.getElementById('__mc_entry') && window.__mcDrain && window.__mcRender);
      } catch (e) {}
    }
    return false;
  }, MAIN_HTML);
  const entryDeadline = Date.now() + 150000;
  for (;;) {
    if (await ready()) break;
    if (Date.now() > entryDeadline) { log('FAIL: entry/panel bootstrap not ready'); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('entry + panel bootstrap ready');

  // install outbox spy (records message shape; still delivers everything to C#)
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

  const V = {}; const F = {};

  // open panel via real entry click
  await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const e = d.getElementById('__mc_entry'); if (e) e.click(); return true;
        }
      } catch (e2) {}
    }
    return false;
  }, MAIN_HTML);

  // wait snapshot + rendered side (10 rows) within 15s
  const renderDeadline = Date.now() + 15000;
  for (;;) {
    const r = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            return {
              snap: window.__mcLastSnapshot || null,
              rows: d.querySelectorAll('#__mc_side .__mc-mod-row').length,
              overlay: !!d.getElementById('__mc_overlay'),
            };
          }
        } catch (e) {}
      }
      return null;
    }, MAIN_HTML);
    if (r && r.snap && r.rows >= 10 && r.overlay) { F.render0 = r; break; }
    if (Date.now() > renderDeadline) { F.render0 = r; break; }
    await sleep(500);
  }
  V.panelOpenedRendered = !!(F.render0 && F.render0.snap && F.render0.rows >= 10 && F.render0.overlay);

  // panel REMEMBERS last selected mod across opens (Root-realm state persists) —
  // normalize to 背包扩容 before layout assertions
  const layoutClickBp = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="com.local.survivallog.backpackexpand"]');
          if (row) { row.click(); return true; }
        }
      } catch (e) {}
    }
    return false;
  }, MAIN_HTML);
  await sleep(60);

  // ---- layout assertions (all inside host doc) ----
  const layout = await page.evaluate(({ tail, curW }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const q = (s) => d.querySelectorAll(s);
          const one = (s) => d.querySelector(s);
          return {
            style: !!d.getElementById('__mc_panel_style'),
            headTitle: one('#__mc_panel .__mc-title') ? one('#__mc_panel .__mc-title').textContent : null,
            headSub: one('#__mc_panel .__mc-sub') ? one('#__mc_panel .__mc-sub').textContent : null,
            closeBtn: !!one('#__mc_panel .__mc-close'),
            footBadges: q('#__mc_panel .__mc-foot .__mc-lg span').length,
            footNote: one('#__mc_panel .__mc-foot-note') ? one('#__mc_panel .__mc-foot-note').textContent : null,
            toastEl: !!one('#__mc_toast'),
            sideRows: q('#__mc_side .__mc-mod-row').length,
            lockRow: !!one('#__mc_side .__mc-mod-row.__mc-lock'),
            lockSw: !!one('#__mc_side .__mc-mod-row.__mc-lock .__mc-mini-sw.__mc-lock'),
            tpHint: one('#__mc_side .__mc-tp-empty') ? one('#__mc_side .__mc-tp-empty').textContent : null,
            cats: Array.from(q('#__mc_side .__mc-cat')).map((c) => c.textContent),
            selRow: one('#__mc_side .__mc-mod-row.__mc-sel') ? one('#__mc_side .__mc-mod-row.__mc-sel').getAttribute('data-guid') : null,
            cfgTitle: one('#__mc_cfg .__mc-mod-title h2') ? one('#__mc_cfg .__mc-mod-title h2').textContent : null,
            cfgGrps: Array.from(q('#__mc_cfg .__mc-grp')).map((g) => ({
              t: g.querySelector('h3') ? g.querySelector('h3').textContent : null,
              items: g.querySelectorAll('.__mc-item').length,
            })),
            curItemNote: one('#__mc_cfg .__mc-item .__mc-note') ? one('#__mc_cfg .__mc-item .__mc-note').textContent : null,
            rangeCount: q('#__mc_cfg input[type=range]').length,
            badgeCount: q('#__mc_cfg .__mc-badge').length,
            ver: window.__mcLastSnapshot && window.__mcLastSnapshot.self ? window.__mcLastSnapshot.self.ver : null,
            curW: curW,
          };
        }
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, curW: Z.curW });
  F.layout = layout;
  if (layout) {
    V.styleInstalled = layout.style;
    V.headTitle = layout.headTitle === 'MOD \u7ba1\u7406\u4e2d\u5fc3';
    V.headSubVer = !!layout.ver && layout.headSub.indexOf('v' + layout.ver) !== -1;
    V.closeBtn = layout.closeBtn;
    V.footFourBadges = layout.footBadges === 4;
    V.toastEl = layout.toastEl;
    V.sideTenRows = layout.sideRows === 10;
    V.selfLock = layout.lockRow && layout.lockSw;
    V.tpEmptyHint = !!layout.tpHint;
    V.threeCats = layout.cats.length === 3
      && layout.cats[0].indexOf('\u5957\u4ef6 MOD') === 0 && layout.cats[1].indexOf('\u9762\u677f\u81ea\u8eab') === 0
      && layout.cats[2].indexOf('\u7b2c\u4e09\u65b9 MOD') === 0;
    V.defaultPageBackpack = layout.cfgTitle === Z.bp && layout.cfgGrps.length === 1
      && layout.cfgGrps[0].t === Z.gExpand && layout.cfgGrps[0].items === 2
      && layout.rangeCount === 2 && layout.badgeCount === 2;
    V.backpackDynNote = !!layout.curItemNote && layout.curItemNote.indexOf(Z.curW) === 0;
  } else {
    ['styleInstalled', 'headTitle', 'headSubVer', 'closeBtn', 'footFourBadges', 'toastEl', 'sideTenRows',
      'selfLock', 'tpEmptyHint', 'threeCats', 'defaultPageBackpack', 'backpackDynNote'].forEach((k) => { V[k] = false; });
  }

  // ---- page-by-page: click each suite row, assert groups/items/controls ----
  // expectations mirror Meta.cs (single source: SurvivalLog.ModCenter/Meta.cs)
  const EXPECT = [
    { name: Z.bp, groups: [{ t: Z.gExpand, items: 2 }] },
    { name: Z.cb, groups: [{ t: Z.gExpand, items: 3 }, { t: Z.gUiFit, items: 2 }], special: 'cabinet' },
    { name: Z.fc, groups: [{ t: Z.gChill, items: 2 }], special: 'fridge' },
    { name: Z.qa, groups: [{ t: Z.gMs, items: 5 }], special: 'ovr' },
    { name: Z.ce, groups: [], special: 'toggleOnly' },
    { name: Z.ru, groups: [{ t: Z.gVine, items: 3 }, { t: Z.gPlan, items: 3 }], special: 'role' },
    { name: Z.rs, groups: [{ t: Z.gRs, items: 7 }], special: 'robot' },
    { name: Z.pp, groups: [{ t: Z.gCost, items: 1 }, { t: Z.gList, items: 2 }], special: 'planpick' },
    { name: Z.pd, groups: [], special: 'toggleOnly' },
  ];

  const clickRow = (guid) => page.evaluate(({ tail, guid }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
          if (row) { row.click(); return true; }
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, guid });

  const readPage = () => page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const q = (s) => d.querySelectorAll('#__mc_cfg ' + s);
          const one = (s) => d.querySelector('#__mc_cfg ' + s);
          const grps = Array.from(q('.__mc-grp')).map((g) => ({
            t: g.querySelector('h3') ? g.querySelector('h3').textContent : null,
            items: g.querySelectorAll('.__mc-item').length,
            ranges: g.querySelectorAll('input[type=range]').length,
            bools: g.querySelectorAll('.__mc-mini-sw.__mc-big[data-act="set"]').length,
            selects: g.querySelectorAll('select').length,
          }));
          return {
            title: one('.__mc-mod-title h2') ? one('.__mc-mod-title h2').textContent : null,
            en: one('.__mc-mod-title .__mc-en') ? one('.__mc-mod-title .__mc-en').textContent : null,
            grps,
            totalItems: q('.__mc-item').length,
            stopBannerShown: one('.__mc-stop-banner') ? one('.__mc-stop-banner').classList.contains('__mc-show') : false,
            placeholder: one('.__mc-ovr-empty') ? one('.__mc-ovr-empty').textContent : null,
            notes: Array.from(q('.__mc-item .__mc-note')).map((n) => n.textContent),
            selOpts: one('select') ? Array.from(one('select').options).map((o) => o.value) : null,
            firstRange: one('input[type=range]')
              ? { min: one('input[type=range]').min, max: one('input[type=range]').max, step: one('input[type=range]').step }
              : null,
            rangeByLabel: {},
            ovrNew: !!one('#__mc_ovr_new'),
            ovrAddBtn: !!one('[data-act="ovr-add"]'),
            ovrRows: q('[data-act="ovr-set"]').length,
          };
        }
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);

  // map name -> guid from snapshot
  const guidOf = {};
  for (const m of (F.render0.snap.mods || [])) guidOf[m.name] = m.guid;
  F.guidOf = guidOf;

  for (const exp of EXPECT) {
    const ok = await clickRow(guidOf[exp.name]);
    await sleep(60);
    const r = await readPage();
    F.pages = F.pages || {}; F.pages[exp.name] = r;
    const key = 'page:' + exp.name;
    if (!ok || !r) { V[key] = false; continue; }
    let pass = r.title === exp.name;
    if (exp.groups.length === 0) {
      pass = pass && r.totalItems === 0 && !!r.placeholder && r.placeholder.indexOf(Z.toggleOnly) !== -1;
    } else {
      // quickaction renders ONE extra DOM group (动作覆盖 ovr block) beyond its Meta groups
      pass = pass && r.grps.length === exp.groups.length + (exp.special === 'ovr' ? 1 : 0);
      for (let i = 0; i < exp.groups.length && i < r.grps.length; i++)
        pass = pass && r.grps[i].t === exp.groups[i].t && r.grps[i].items === exp.groups[i].items;
    }
    if (pass && exp.special === 'cabinet') {
      // linked note is DYNAMIC ("当前 宽w×高h" from live cfg entries) — deployed dll
      // may carry W1.5/H1.5 while repo source defaults H2.0, so assert mechanism +
      // self-consistency with the snapshot's own note instead of hardcoding values
      const snapMod = (F.render0.snap.mods || []).find((m) => m.name === Z.cb);
      const snapNote = snapMod && snapMod.groups[0].items[0] && snapMod.groups[0].items[0].note;
      pass = r.grps[0].ranges === 3 && r.grps[1].bools === 1 && r.grps[1].selects === 1
        && JSON.stringify(r.selOpts) === JSON.stringify(['Scroll', 'Zoom', 'Off'])
        && r.notes.some((n) => n.indexOf(Z.curW) === 0)
        && (!snapNote || r.notes.some((n) => n === snapNote));
    }
    if (pass && exp.special === 'fridge') pass = r.grps[0].ranges === 2;
    if (pass && exp.special === 'ovr') pass = r.grps.length === 2 && r.ovrNew && r.ovrAddBtn && r.ovrRows === 0;
    if (pass && exp.special === 'role') pass = r.totalItems === 6 && r.grps[0].bools === 3 && r.grps[1].bools === 3;
    if (pass && exp.special === 'robot') pass = r.grps[0].bools === 1 && r.grps[0].ranges === 6 && r.totalItems === 7;
    if (pass && exp.special === 'planpick')
      pass = r.grps[0].ranges === 1 && r.grps[0].items === 1 && r.firstRange
        && r.firstRange.min === '100' && r.firstRange.max === '300' && r.firstRange.step === '5';
    V[key] = pass;
  }

  // ---- PlanPick snapshot range (debt D): AcceptableValueRange reflection -> min/max ----
  const snapPP = (F.render0.snap.mods || []).find((m) => m.name === Z.pp);
  const cost = snapPP && snapPP.groups[0] && snapPP.groups[0].items[0];
  F.planpickCost = cost ? { min: cost.min, max: cost.max, v: cost.v, t: cost.t } : null;
  V.planpickReflectedRange = !!cost && cost.t === 'int' && cost.min === 100 && cost.max === 300;

  // ---- self page ----
  await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="__self"]');
          if (row) row.click();
          return true;
        }
      } catch (e) {}
    }
    return false;
  }, MAIN_HTML);
  const selfPage = await readPage();
  V.selfPageFoolProof = !!(selfPage && selfPage.title === 'Mod \u7ba1\u7406\u4e2d\u5fc3'
    && selfPage.totalItems === 0
    && (await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1)
            return (d.querySelector('#__mc_cfg .__mc-mod-desc') || { textContent: '' }).textContent.indexOf('\u9632\u5446') !== -1;
        } catch (e) {}
      }
      return false;
    }, MAIN_HTML)));

  // ---- helper: wait event (clears __mcLastEvent first) ----
  const waitEvent = async (type, budgetMs) => {
    await page.evaluate(() => { window.__mcLastEvent = null; });
    const dl = Date.now() + (budgetMs || 12000);
    for (;;) {
      const ev = await page.evaluate(() => window.__mcLastEvent || null);
      if (ev && ev.type === type) return ev;
      if (Date.now() > dl) return null;
      await sleep(400);
    }
  };
  const waitEventAny = async (types, budgetMs) => {
    await page.evaluate(() => { window.__mcLastEvent = null; });
    const dl = Date.now() + (budgetMs || 12000);
    for (;;) {
      const ev = await page.evaluate(() => window.__mcLastEvent || null);
      if (ev && types.indexOf(ev.type) !== -1) return ev;
      if (Date.now() > dl) return null;
      await sleep(400);
    }
  };
  const sentSince = async (mark) => page.evaluate((mark) => {
    const log = window.__mcSentLog || [];
    return log.slice(mark);
  }, mark);
  const firstOf = (arr, type) => (arr || []).find((m) => m && m.type === type) || null;
  const sentMark = () => page.evaluate(() => (window.__mcSentLog || []).length);

  // ---- interaction 1: slider RobotSpeed moveMult 2 -> 3.5 (real input event) ----
  await clickRow(guidOf[Z.rs]);
  await sleep(60);
  const setRange = (g, i, val) => page.evaluate(({ tail, g, i, val }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const el = d.querySelector('#__mc_cfg input[type=range][data-act="set"][data-g="' + g + '"][data-i="' + i + '"]');
          if (!el) return false;
          el.value = String(val);
          el.dispatchEvent(new Event('input', { bubbles: true }));
          const num = el.parentElement.querySelector('input[type=number]');
          return { ok: true, sibling: num ? num.value : null };
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, g, i, val });

  F.rsBefore = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.robotspeed.cfg', Z.secRs, Z.moveKey));
  const readSnapItem = (name, g, i) => page.evaluate(({ name, g, i }) => {
    const s = window.__mcLastSnapshot;
    const m = s && s.mods && s.mods.find((x) => x.name === name);
    return m && m.groups[g] && m.groups[g].items[i] ? m.groups[g].items[i] : null;
  }, { name, g, i });
  const m0 = await sentMark();
  const r1 = await setRange(0, 1, 3.5);
  const ev1 = await waitEvent('MC_SET_OK');
  const sent1 = await sentSince(m0);
  F.sliderSet = { r1, ev1, sent1 };
  V.sliderMsgShape = !!(firstOf(sent1, 'MC_SET') && sent1.find((s) => s.type === 'MC_SET' && s.guid === RS_GUID && s.section === Z.secRs && s.key === Z.moveKey && s.value === 3.5));
  V.sliderOkReceipt = !!(ev1 && ev1.k === Z.secRs + '.' + Z.moveKey && ev1.badge === 'now');
  V.sliderSiblingSync = !!(r1 && r1.sibling === '3.5');
  const toast1 = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const t = d.getElementById('__mc_toast');
          return t ? { shown: t.classList.contains('__mc-show'), text: t.textContent } : null;
        }
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);
  F.toast1 = toast1;
  V.sliderToast = !!(toast1 && toast1.shown && toast1.text.indexOf(Z.moveKey) !== -1 && toast1.text.indexOf('3.5') !== -1);

  // ---- interaction 2: throttle burst (5 rapid input events < 100ms apart) ----
  const m2 = await sentMark();
  for (const v of [4, 4.1, 4.2, 4.3, 4.5]) await setRange(0, 1, v);
  // C# drains every 2s — poll the spy log until the burst landed (or timeout)
  let sent2 = [];
  const burstDl = Date.now() + 8000;
  for (;;) {
    sent2 = await sentSince(m2);
    if (sent2.filter((s) => s.key === Z.moveKey).length >= 2) break;
    if (Date.now() > burstDl) break;
    await sleep(500);
  }
  F.throttleBurst = sent2.filter((s) => s.key === Z.moveKey);
  const burst = F.throttleBurst;
  V.throttleShape = burst.length >= 2 && burst.length <= 4
    && burst[0].value === 4 && burst[burst.length - 1].value === 4.5;

  // restore 2
  await setRange(0, 1, 2);
  await waitEvent('MC_SET_OK');
  F.rsAfter = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.robotspeed.cfg', Z.secRs, Z.moveKey));

  // ---- interaction 3: bool (RoleUnlock flower seeds off/on) ----
  await clickRow(guidOf[Z.ru]);
  await sleep(60);
  const clickBool = (g, i) => page.evaluate(({ tail, g, i }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const b = d.querySelector('#__mc_cfg .__mc-mini-sw.__mc-big[data-act="set"][data-g="' + g + '"][data-i="' + i + '"]');
          if (!b) return false;
          b.click();
          return { on: b.classList.contains('__mc-on'), aria: b.getAttribute('aria-checked') };
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, g, i });
  F.ruBefore = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.roleunlock.cfg', 'Vine', 'UnlockFlowerSeeds'));
  const flowerSnap = await readSnapItem(Z.ru, 0, 2);
  const flower0 = !!(flowerSnap && flowerSnap.v === true); // current value before click
  const m3 = await sentMark();
  const b1 = await clickBool(0, 2); // Vine.UnlockFlowerSeeds -> toggles to !flower0
  const ev3 = await waitEvent('MC_SET_OK');
  const sent3 = await sentSince(m3);
  const b2 = await clickBool(0, 2); // restore
  await waitEvent('MC_SET_OK');
  const sent3set = firstOf(sent3, 'MC_SET');
  F.boolSet = { flower0, b1, ev3, sent3: sent3set, b2 };
  V.boolMsgShape = !!(sent3set && sent3set.key === 'UnlockFlowerSeeds' && sent3set.value === !flower0);
  V.boolUiToggle = !!(b1 && b1.on === !flower0 && b1.aria === String(!flower0)
    && b2 && b2.on === flower0);
  V.boolOkReceipt = !!(ev3 && ev3.badge === 'now');

  // ---- interaction 4: select (UiFit.Mode -> 'Zoom') ----
  // NOTE: the DEPLOYED CabinetExpand.dll (Oct 20 build, author's live install) does not
  // bind UiFit.Mode yet (repo source does; author's WIP) — so the write is REJECTED by
  // the bridge by design (MC_SET_FAIL) until the entry exists. Assert BOTH the message
  // shape and the designed failure path (error toast + no cfg write).
  await clickRow(guidOf[Z.cb]);
  await sleep(60);
  const setSelect = (val) => page.evaluate(({ tail, val }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const s = d.querySelector('#__mc_cfg select[data-act="set"]');
          if (!s) return false;
          s.value = val;
          s.dispatchEvent(new Event('change', { bubbles: true }));
          return true;
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, val });
  const uifitModeBefore = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetexpand.cfg', 'UiFit', 'Mode'));
  const m4 = await sentMark();
  await setSelect('Zoom');
  const ev4 = await waitEventAny(['MC_SET_OK', 'MC_SET_FAIL']);
  const sent4 = firstOf(await sentSince(m4), 'MC_SET');
  // read toast BEFORE restore so we see the FAIL-corrected toast (not the optimistic one)
  const toast4 = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const t = d.getElementById('__mc_toast');
          return t ? { shown: t.classList.contains('__mc-show'), err: t.classList.contains('__mc-err'), text: t.textContent } : null;
        }
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);
  await setSelect('Scroll');
  await sleep(300);
  const uifitModeAfter = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetexpand.cfg', 'UiFit', 'Mode'));
  F.selSet = { ev4, sent4, toast4, uifitModeBefore, uifitModeAfter };
  V.selMsgShape = !!(sent4 && sent4.section === 'UiFit' && sent4.key === 'Mode' && sent4.value === 'Zoom');
  if (uifitModeBefore == null) {
    // entry absent in deployed install: designed reject path (MC_SET_FAIL + err toast, cfg untouched)
    V.selOkReceipt = !!(ev4 && ev4.type === 'MC_SET_FAIL' && ev4.k === 'UiFit.Mode')
      && !!(toast4 && toast4.shown) && uifitModeAfter == null;
  } else {
    V.selOkReceipt = !!(ev4 && ev4.type === 'MC_SET_OK');
  }

  // ---- interaction 5: reload-badge toast (backpack burden; restore to AUTHOR baseline) ----
  await clickRow(guidOf[Z.bp]);
  await sleep(60);
  F.burdenBefore = parseInt(stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.backpackexpand.cfg', 'Expand', 'BurdenMult')) || '2', 10);
  await setRange(0, 1, Math.min(10, F.burdenBefore + 1)); // Expand.BurdenMult (badge reload)
  const toast5 = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const t = d.getElementById('__mc_toast');
          return t ? { shown: t.classList.contains('__mc-show'), text: t.textContent } : null;
        }
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);
  F.toast5 = toast5;
  V.reloadBadgeToast = !!(toast5 && toast5.shown && toast5.text.indexOf(Z.afterLoad) !== -1);
  await setRange(0, 1, F.burdenBefore); // restore burden to author baseline
  await waitEvent('MC_SET_OK');

  // ---- interaction 6: side toggle (PreDisasterSpeed off -> banner -> cfg -> on) ----
  const clickToggle = (guid) => page.evaluate(({ tail, guid }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const b = d.querySelector('#__mc_side [data-act="toggle"][data-guid="' + guid + '"]');
          if (b) { b.click(); return true; }
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, guid });
  const m6 = await sentMark();
  const tgSeq0 = await page.evaluate(() => window.__mcSnapSeq || 0);
  await clickToggle(PD_GUID);
  // NOTE: toggleMod flips the mod object INSIDE the current snapshot optimistically
  // (__mcLastSnapshot mutates instantly) — so "enabled===false" alone is NOT proof of
  // the C# push-back. Wait for a NEW __mcRender (snapSeq bump) carrying enabled=false.
  const tgDeadline = Date.now() + 15000;
  let tgSnap;
  for (;;) {
    const r = await page.evaluate((guid) => {
      const s = window.__mcLastSnapshot;
      const m = s && s.mods && s.mods.find((x) => x.guid === guid);
      return { enabled: m ? m.enabled : undefined, seq: window.__mcSnapSeq || 0 };
    }, PD_GUID);
    if (r.enabled === false && r.seq > tgSeq0) { tgSnap = false; break; }
    if (Date.now() > tgDeadline) { tgSnap = r.enabled; break; }
    await sleep(400);
  }
  const sent6 = await sentSince(m6);
  // row off + banner shown on its page
  const offRow = await page.evaluate(({ tail, guid }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
          return row ? row.classList.contains('__mc-off') : null;
        }
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, guid: PD_GUID });
  await clickRow(PD_GUID);
  await sleep(60);
  const pdPage = await readPage();
  const sent6t = firstOf(await sentSince(m6), 'MC_TOGGLE_MOD');
  F.toggle = { sent: sent6t, snapEnabled: tgSnap, offRow, banner: pdPage && pdPage.stopBannerShown };
  V.toggleMsgShape = !!(sent6t && sent6t.guid === PD_GUID && sent6t.enabled === false);
  V.toggleSnapshotBack = tgSnap === false;
  V.toggleRowOffBanner = !!offRow && !!(pdPage && pdPage.stopBannerShown);
  // wait [Disabled] persisted on disk (3s debounce + drain margin)
  let pdLine = null;
  const pdDl = Date.now() + 9000;
  for (;;) {
    pdLine = readCfgSection(MC_CFG, 'Disabled', PD_GUID);
    if (pdLine && pdLine.indexOf('true') !== -1) break;
    if (Date.now() > pdDl) break;
    await sleep(500);
  }
  V.togglePersisted = !!(pdLine && pdLine.indexOf('true') !== -1);
  F.pdLine = pdLine;
  await clickToggle(PD_GUID); // restore on
  await sleep(2500);

  // ---- interaction 7: ovr round-trip ----
  await clickRow(guidOf[Z.qa]);
  await sleep(60);
  F.qaOvrBefore = stripNonCodeLines(readCfgSection(QA_CFG, '\u5176\u4ed6', 'Overrides'));
  const ovrAdd = (id) => page.evaluate(({ tail, id }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const inp = d.getElementById('__mc_ovr_new');
          const btn = d.querySelector('#__mc_cfg [data-act="ovr-add"]');
          if (!inp || !btn) return false;
          inp.value = String(id);
          btn.click();
          return true;
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, id });
  const ovrSetRange = (idx, val) => page.evaluate(({ tail, idx, val }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const el = d.querySelector('#__mc_cfg input[type=range][data-act="ovr-set"][data-idx="' + idx + '"]');
          if (!el) return false;
          el.value = String(val);
          el.dispatchEvent(new Event('input', { bubbles: true }));
          const ck = el.closest('.__mc-item').querySelector('.__mc-ck');
          return { ck: ck ? ck.textContent : null };
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, idx, val });
  const ovrDel = (idx) => page.evaluate(({ tail, idx }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const b = d.querySelector('#__mc_cfg [data-act="ovr-del"][data-idx="' + idx + '"]');
          if (!b) return false;
          b.click();
          return true;
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, idx });
  const pushMsg = (obj) => page.evaluate((o) => { window.__mcOutbox.push(o); return true; }, obj);
  // fresh snapshot: MC_OPEN -> C# pushes new snapshot (__mcRender bumps __mcSnapSeq)
  const freshOvr = async () => {
    const seq0 = await page.evaluate(() => window.__mcSnapSeq || 0);
    await pushMsg({ type: 'MC_OPEN' });
    const dl = Date.now() + 12000;
    for (;;) {
      const r = await page.evaluate((guid) => {
        const s = window.__mcLastSnapshot;
        const m = s && s.mods && s.mods.find((x) => x.guid === guid);
        return m ? (m.ovr || []) : undefined;
      }, QA_GUID);
      const seq = await page.evaluate(() => window.__mcSnapSeq || 0);
      if (r !== undefined && seq > seq0) return r;
      if (Date.now() > dl) return undefined;
      await sleep(400);
    }
  };

  const m7 = await sentMark();
  await ovrAdd(8012);
  const evAdd = await waitEvent('MC_SET_OK');
  const sentAdd = await sentSince(m7);
  const sentAddSet = firstOf(await sentSince(m7), 'MC_SET');
  F.ovrAdd = { evAdd, sent: sentAddSet };
  V.ovrAddMsg = !!(sentAddSet && sentAddSet.section === '\u5176\u4ed6' && sentAddSet.key === 'Overrides'
    && sentAddSet.value === '8012:1000');

  const m7b = await sentMark();
  const ckAfterDrag = await ovrSetRange(0, 250);
  let sentDrag = [];
  const dragDl = Date.now() + 8000;
  for (;;) {
    sentDrag = (await sentSince(m7b)).filter((s) => s.type === 'MC_SET' && s.key === 'Overrides');
    if (sentDrag.length >= 1) break;
    if (Date.now() > dragDl) break;
    await sleep(500);
  }
  F.ovrDrag = { ckAfterDrag, sent: sentDrag };
  V.ovrDragMsg = sentDrag.length >= 1 && sentDrag[sentDrag.length - 1].value === '8012:250';
  V.ovrCkText = !!(ckAfterDrag && ckAfterDrag.ck && ckAfterDrag.ck.indexOf('8012:250') !== -1);

  // fresh snapshot (C# re-parses cfg) -> panel re-rendered from serialized string
  const ovrSnap1 = await freshOvr();
  F.ovrSnap1 = ovrSnap1;
  V.ovrRoundTripSnapshot = JSON.stringify(ovrSnap1) === JSON.stringify([{ id: 8012, ms: 250 }]);
  // panel re-rendered row reflects it
  const ovrRowDom = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const ck = d.querySelector('#__mc_cfg [data-act="ovr-set"][data-idx="0"]');
          return ck ? ck.value : null;
        }
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);
  V.ovrPanelResync = ovrRowDom === '250';

  // cfg on disk (debounce 3s + drain 2s)
  let qaLine = null;
  const qaDl = Date.now() + 12000;
  for (;;) {
    qaLine = readCfgSection(QA_CFG, '\u5176\u4ed6', 'Overrides');
    if (qaLine && qaLine.indexOf('8012:250') !== -1) break;
    if (Date.now() > qaDl) break;
    await sleep(600);
  }
  F.qaCfgMid = qaLine;
  V.ovrPersistedDisk = !!(qaLine && qaLine.indexOf('8012:250') !== -1);

  // delete -> ''
  const m7c = await sentMark();
  await ovrDel(0);
  const evDel = await waitEvent('MC_SET_OK');
  const sentDel = await sentSince(m7c);
  const ovrSnap2 = await freshOvr();
  const sentDelSet = firstOf(await sentSince(m7c), 'MC_SET');
  F.ovrDel = { evDel, sent: sentDelSet, snap: ovrSnap2 };
  V.ovrDelMsg = !!(sentDelSet && sentDelSet.value === '');
  V.ovrDelSnapshot = JSON.stringify(ovrSnap2) === JSON.stringify([]);

  // ---- close via X ----
  const m8 = await sentMark();
  await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const x = d.querySelector('#__mc_panel .__mc-close');
          if (x) x.click();
          return true;
        }
      } catch (e) {}
    }
    return false;
  }, MAIN_HTML);
  await sleep(400);
  const closed = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1)
          return { overlay: !!d.getElementById('__mc_overlay'), entry: !!d.getElementById('__mc_entry') };
      } catch (e) {}
    }
    return null;
  }, MAIN_HTML);
  // MC_CLOSE is drained on the 2s tick — poll the spy log for it
  let sentCloseMsg = null;
  const closeMsgDl = Date.now() + 8000;
  for (;;) {
    sentCloseMsg = firstOf(await sentSince(m8), 'MC_CLOSE');
    if (sentCloseMsg) break;
    if (Date.now() > closeMsgDl) break;
    await sleep(500);
  }
  F.close = { dom: closed, sent: sentCloseMsg };
  V.closeByX = !!(closed && !closed.overlay && closed.entry);
  V.closeMsg = !!sentCloseMsg;

  // wait disk settle, then final facts
  await sleep(4000);
  F.final = {
    rs: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.robotspeed.cfg', Z.secRs, Z.moveKey)),
    uifitMode: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetexpand.cfg', 'UiFit', 'Mode')),
    flower: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.roleunlock.cfg', 'Vine', 'UnlockFlowerSeeds')),
    burden: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.backpackexpand.cfg', 'Expand', 'BurdenMult')),
    ovr: stripNonCodeLines(readCfgSection(QA_CFG, '\u5176\u4ed6', 'Overrides')),
    pdDisabled: readCfgSection(MC_CFG, 'Disabled', PD_GUID),
  };
  V.restoredStates = (F.final.rs === (F.rsBefore || '2'))
    && F.final.uifitMode === (F.selSet ? F.selSet.uifitModeBefore : F.final.uifitMode)
    && F.final.flower === (F.ruBefore || 'false')
    && F.final.burden === String(F.burdenBefore)
    && (F.final.ovr === '' || F.final.ovr === null);

  try { await page.screenshot({ path: SHOT_DIR + '/task-7-shot-panel.png' }); } catch (e) {}

  await browser.close();
  console.log('---- facts ----');
  console.log(JSON.stringify({
    layout: F.layout, planpickCost: F.planpickCost,
    pages: F.pages ? Object.keys(F.pages).reduce((acc, k) => {
      const p = F.pages[k];
      acc[k] = p ? { title: p.title, grps: p.grps, totalItems: p.totalItems, selOpts: p.selOpts, notes: p.notes, firstRange: p.firstRange, ovrNew: p.ovrNew, ovrRows: p.ovrRows, placeholder: p.placeholder } : null;
      return acc;
    }, {}) : null,
    sliderSet: F.sliderSet, throttleBurst: F.throttleBurst,
    toast1: F.toast1, boolSet: F.boolSet, selSet: F.selSet, toast5: F.toast5, toggle: F.toggle,
    burdenBefore: F.burdenBefore,
    ovrAdd: F.ovrAdd, ovrDrag: F.ovrDrag, ovrSnap1: F.ovrSnap1, qaCfgMid: F.qaCfgMid,
    ovrDel: F.ovrDel, close: F.close, final: F.final,
    rsBefore: F.rsBefore, ruBefore: F.ruBefore, qaOvrBefore: F.qaOvrBefore,
  }, null, 1));
  console.log('---- verdicts ----');
  const checks = [
    ['0  panel opened + rendered (snapshot + 10 rows)', V.panelOpenedRendered],
    ['0  style installed (scoped #__mc_overlay)', V.styleInstalled],
    ['0  head title/sub(ver)/close', V.headTitle && V.headSubVer && V.closeBtn],
    ['0  foot 4-badge legend', V.footFourBadges],
    ['0  toast element present', V.toastEl],
    ['0  side: 9 suite + self lock rows', V.sideTenRows && V.selfLock],
    ['0  side: tp empty hint handled', V.tpEmptyHint],
    ['0  side: 3 category headers', V.threeCats],
    ['0  default page = backpack 2 items 2 ranges', V.defaultPageBackpack],
    ['0  backpack linked dynamic note (当前 宽..)', V.backpackDynNote],
    ...EXPECT.map((e) => ['1  page ' + e.name, V['page:' + e.name]]),
    ['D  PlanPick Cost min=100/max=300 reflected (snapshot+DOM)', V.planpickReflectedRange
      && (F.pages && F.pages[Z.pp] && F.pages[Z.pp].firstRange ? F.pages[Z.pp].firstRange.min === '100' && F.pages[Z.pp].firstRange.max === '300' : false)],
    ['1  self page fool-proof desc', V.selfPageFoolProof],
    ['2  slider MC_SET message shape', V.sliderMsgShape],
    ['2  slider MC_SET_OK receipt', V.sliderOkReceipt],
    ['2  slider sibling number sync', V.sliderSiblingSync],
    ['2  slider toast shown', V.sliderToast],
    ['2  slider 100ms throttle burst shape', V.throttleShape],
    ['2  bool toggle msg + UI + receipt', V.boolMsgShape && V.boolUiToggle && V.boolOkReceipt],
    ['2  select msg + receipt', V.selMsgShape && V.selOkReceipt],
    ['2  reload-badge toast (读档后)', V.reloadBadgeToast],
    ['2  side toggle msg shape', V.toggleMsgShape],
    ['2  side toggle snapshot push-back', V.toggleSnapshotBack],
    ['2  side toggle row off + stop banner', V.toggleRowOffBanner],
    ['2  side toggle [Disabled] persisted', V.togglePersisted],
    ['2  ovr add -> "8012:1000" MC_SET', V.ovrAddMsg],
    ['2  ovr drag -> "8012:250" + ck text', V.ovrDragMsg && V.ovrCkText],
    ['2  ovr round-trip via fresh snapshot', V.ovrRoundTripSnapshot && V.ovrPanelResync],
    ['2  ovr persisted to quickaction cfg', V.ovrPersistedDisk],
    ['2  ovr delete -> "" + empty snapshot', V.ovrDelMsg && V.ovrDelSnapshot],
    ['3  close by X removes overlay + MC_CLOSE', V.closeByX && V.closeMsg],
    ['4  all touched cfg values restored', V.restoredStates],
  ];
  let allPass = true;
  for (const [name, ok] of checks) {
    console.log((ok ? 'PASS' : 'FAIL') + '  ' + name);
    if (!ok) allPass = false;
  }
  log(allPass ? 'RESULT: ALL PASS' : 'RESULT: FAIL (see above)');
  process.exit(allPass ? 0 : 1);
}

// ==========================================================================
//  MODE: loadgame  (click continue; bash driver sends ESC afterwards)
// ==========================================================================
async function loadGameMode() {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const { browser, page } = await connect(t0, log);
  const clickMenu = (label) => page.evaluate(({ tail, label }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const items = Array.from(d.querySelectorAll('#app .container .menu .menu-item'));
          const hit = items.find((b) => b.textContent.indexOf(label) !== -1 && b.id !== '__mc_entry');
          if (hit) { hit.click(); return hit.textContent.trim(); }
        }
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, label });
  let clicked = await clickMenu('\u7ee7\u7eed'); // 继续…（"继续生存灾后 · 第N天"）
  if (!clicked) clicked = await clickMenu('\u65b0\u751f\u5b58'); // 新生存
  if (!clicked) clicked = await clickMenu('\u8bfb\u53d6\u5b58\u6863'); // 读取存档
  log('menu click: ' + JSON.stringify(clicked));
  if (!clicked) { await browser.close(); log('FAIL: no continue/new-game button'); process.exit(2); }
  // wait until MainUI iframe is gone (world loading / loaded)
  const dl = Date.now() + 180000;
  for (;;) {
    const still = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      let n = 0;
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) n++;
        } catch (e) {}
      }
      return n;
    }, MAIN_HTML);
    if (still === 0) break;
    if (Date.now() > dl) { await browser.close(); log('FAIL: MainUI still present after budget'); process.exit(2); }
    await sleep(2000);
  }
  log('MainUI gone (world stage reached)');
  await browser.close();
  process.exit(0);
}

// ==========================================================================
//  MODE: outsetting  (debt A: in-game pause menu assertion)
// ==========================================================================
async function outsettingMode() {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const { browser, page } = await connect(t0, log);
  const findOut = () => page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) return i;
      } catch (e) {}
    }
    return -1;
  }, OUT_HTML);

  const dl = Date.now() + 240000;
  let idx = -1;
  for (;;) {
    idx = await findOut();
    if (idx !== -1) break;
    if (Date.now() > dl) { log('INFO: OutSetting not seen (driver may retry ESC)'); await browser.close(); process.exit(3); }
    await sleep(1500);
  }
  log('OutSetting iframe seen');
  await sleep(2500); // give injector a tick (2s cadence)

  const A = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const entry = d.getElementById('__mc_entry');
          if (!entry) return { entry: false };
          const next = entry.nextElementSibling;
          const restart = d.querySelector('.menu-list .btn-restart');
          const exit = d.querySelector('.menu-list .btn-exit');
          const list = d.querySelector('#app .left-panel .menu-list') || d.querySelector('.menu-list');
          const kids = list ? Array.from(list.children).map((c) => c.className) : [];
          return {
            entry: true, tag: entry.tagName, cls: entry.className, text: entry.textContent,
            interactive: entry.hasAttribute('data-interactive'),
            nextCls: next ? next.className : null,
            nextIsRestartOrExit: !!(next && (next.classList.contains('btn-restart') || next.classList.contains('btn-exit'))),
            hasRestart: !!restart, hasExit: !!exit,
            entryBeforeRestartOrExit: !!(restart && entry.compareDocumentPosition(restart) & Node.DOCUMENT_POSITION_FOLLOWING)
              || !!(exit && entry.compareDocumentPosition(exit) & Node.DOCUMENT_POSITION_FOLLOWING),
            listKids: kids,
            btnCount: d.querySelectorAll('.menu-list .main-btn').length,
          };
        }
      } catch (e) {}
    }
    return null;
  }, OUT_HTML);
  log('entry assert: ' + JSON.stringify(A));

  // coexistence scan BEFORE opening panel (PlanPick #ppEntry / #pp-root must be untouched after)
  const scanFrames = () => page.evaluate(() => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    const out = [];
    for (let i = 0; i < fs.length; i++) {
      let p = '?'; let mc = false; let pp = false; let pproot = false; let mcOverlay = false;
      try {
        const d = fs[i].contentDocument;
        p = d ? d.location.pathname : '?';
        if (d) {
          mc = !!d.getElementById('__mc_entry');
          pp = !!d.getElementById('ppEntry');
          pproot = !!d.getElementById('pp-root');
          mcOverlay = !!d.getElementById('__mc_overlay');
        }
      } catch (e) { p = 'x-origin'; }
      out.push({ p, mc, pp, pproot, mcOverlay });
    }
    return out;
  });
  const coexBefore = await scanFrames();

  // click entry -> panel opens in OutSetting doc
  const opened = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const e = d.getElementById('__mc_entry');
          if (e) { e.click(); return true; }
        }
      } catch (e2) {}
    }
    return false;
  }, OUT_HTML);
  const dlOpen = Date.now() + 20000;
  let panel = null;
  for (;;) {
    panel = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            return {
              overlay: !!d.getElementById('__mc_overlay'),
              rows: d.querySelectorAll('#__mc_side .__mc-mod-row').length,
              title: (d.querySelector('#__mc_panel .__mc-title') || { textContent: null }).textContent,
              host: window.__mcLastSnapshot ? 'root-snap-ok' : 'no-snap',
            };
          }
        } catch (e) {}
      }
      return null;
    }, OUT_HTML);
    if (panel && panel.overlay && panel.rows >= 10) break;
    if (Date.now() > dlOpen) break;
    await sleep(800);
  }
  log('panel: ' + JSON.stringify(panel));

  // close by X
  await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const x = d.querySelector('#__mc_panel .__mc-close');
          if (x) x.click();
        }
      } catch (e) {}
    }
  }, OUT_HTML);
  await sleep(500);
  const afterClose = await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1)
          return { overlay: !!d.getElementById('__mc_overlay'), entry: !!d.getElementById('__mc_entry') };
      } catch (e) {}
    }
    return null;
  }, OUT_HTML);

  // coexistence scan AFTER panel open+close: PlanPick elements identical, no residual overlay anywhere
  const coex = await scanFrames();
  const ppBefore = coexBefore.filter((c) => c.pp).map((c) => c.p);
  const ppAfter = coex.filter((c) => c.pp).map((c) => c.p);
  const pprootBefore = coexBefore.filter((c) => c.pproot).length;
  const pprootAfter = coex.filter((c) => c.pproot).length;
  log('coexist scan: ' + JSON.stringify(coex));

  try { await page.screenshot({ path: SHOT_DIR + '/task-7-shot-outsetting.png' }); } catch (e) {}
  await browser.close();

  const V = {
    entryInList: !!(A && A.entry && A.cls.indexOf('main-btn') !== -1 && A.interactive),
    insertBeforePos: !!(A && A.nextIsRestartOrExit && A.entryBeforeRestartOrExit),
    panelOpenedInGame: !!(panel && panel.overlay && panel.rows >= 10 && panel.title === 'MOD \u7ba1\u7406\u4e2d\u5fc3'),
    closeCleanInGame: !!(afterClose && !afterClose.overlay && afterClose.entry),
    coexistNoConflict: JSON.stringify(ppBefore) === JSON.stringify(ppAfter)
      && pprootBefore === pprootAfter
      && !coex.some((c) => c.mcOverlay), // our overlay fully removed everywhere
  };
  console.log('---- verdicts (outsetting) ----');
  const checks = [
    ['A  OutSetting entry present (main-btn, insertBefore)', V.entryInList && V.insertBeforePos],
    ['A  panel opens in OutSetting doc (10 rows)', V.panelOpenedInGame],
    ['A  panel close clean (X)', V.closeCleanInGame],
    ['4  coexistence scan clean (ppEntry untouched)', V.coexistNoConflict],
  ];
  let allPass = true;
  for (const [n, ok] of checks) { console.log((ok ? 'PASS' : 'FAIL') + '  ' + n); if (!ok) allPass = false; }
  console.log(JSON.stringify({ A, panel, afterClose, coex }, null, 1));
  log(allPass ? 'RESULT: ALL PASS' : 'RESULT: FAIL');
  process.exit(allPass ? 0 : 1);
}

(async () => {
  if (MODE === 'loadgame') return loadGameMode();
  if (MODE === 'outsetting') return outsettingMode();
  return menuMode();
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
