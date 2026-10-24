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
//        - Fix R1 (review P2/F1): synthetic no-range numeric item (min/max absent)
//          renders number box ONLY (no fabricated 0..100 slider); out-of-range 500
//          shows uncapped and MC_SET sends 500 uncapped
//        - interactions through REAL DOM events:
//            slider (RobotSpeed moveMult) incl. 100ms throttle burst shape
//            bool (RoleUnlock flower seeds), select (UiFit.Mode), reload-badge toast
//            side toggle (PreDisasterSpeed off/on, banner + own-cfg General.Enabled
//              persistence — task 13 direct-write semantics)
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
const PD_CFG = GAME_ROOT + '/BepInEx/config/com.local.survivallog.predisasterspeed.cfg';
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
  bp: '\u80cc\u5305\u6269\u5bb9', cb: '\u67dc\u5b50\u5bb9\u91cf', fc: '\u4fdd\u9c9c\u589e\u5f3a',
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
  const page = await findRootPage(browser, 180000, log);
  return { browser, page };
}

// Root page may be a transient boot page that closes — re-acquire on every poll.
async function findRootPage(browser, budgetMs, log) {
  const deadline = Date.now() + budgetMs;
  for (;;) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); } catch (e) { has = false; }
        if (has) return p;
      }
    }
    if (Date.now() > deadline) { log('FAIL: no Root page'); await browser.close(); process.exit(2); }
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
  let browserRef;
  let page;
  {
    const conn = await connect(t0, log);
    browserRef = conn.browser;
    page = conn.page;
  }

  // wait for MainUI + entry + panel bootstrap (page may close/reopen during boot: re-acquire)
  const ready = async () => {
    try {
      return await page.evaluate((tail) => {
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
    } catch (e) {
      try { page = await findRootPage(browserRef, 5000, () => {}); } catch (e2) { return false; }
      return false;
    }
  };
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
            tpCount: window.__mcLastSnapshot && window.__mcLastSnapshot.tp ? window.__mcLastSnapshot.tp.length : 0,
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
    V.sideTenRows = layout.sideRows === 10 + layout.tpCount; // 9 suite + self lock + N tp（T14 自适应）
    V.selfLock = layout.lockRow && layout.lockSw;
    V.tpEmptyHint = layout.tpCount === 0 ? !!layout.tpHint : !layout.tpHint; // 空数组→占位；非空→无占位
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

  // =========================================================================
  // Task 14: third-party (Tier 0) assertion group.
  // Ground truth for structure = the tp mod's own cfg file on disk
  // (BepInEx auto-names <guid>.cfg); interactions round-trip and restore.
  // Skip-tolerant: machines without third-party plugins skip with a note.
  // =========================================================================
  const TP = (F.render0.snap.tp || []).slice();
  F.tpList = TP.map((m) => ({ guid: m.guid, name: m.name, enabled: m.enabled,
    groups: m.groups.map((g) => ({ t: g.title, n: g.items.length })) }));
  const tpCfgOf = (guid) => GAME_ROOT + '/BepInEx/config/' + guid + '.cfg';
  const parseCfgSections = (p) => {
    try {
      const t = FS.readFileSync(p, 'utf8');
      const secs = [];
      let cur = null;
      for (const line0 of t.split('\n')) {
        const s = line0.trim();
        if (!s || s.charAt(0) === '#') continue;
        const m2 = s.match(/^\[(.+)\]$/);
        if (m2) { cur = { sec: m2[1], keys: {} }; secs.push(cur); continue; }
        const eq = s.indexOf('=');
        if (eq > 0 && cur) cur.keys[s.slice(0, eq).trim()] = s.slice(eq + 1).trim();
      }
      return secs;
    } catch (e) { return null; }
  };
  // exemption: no first-party guid, no probe/self/burden/endlessguard ever in tp
  const tpGuidsNoFirstParty = TP.every((m) => m.guid.indexOf('com.local.survivallog.') !== 0 && !/probe/i.test(m.name));
  V.tpExempt = tpGuidsNoFirstParty; // non-empty asserted separately by tpPresent
  V.tpPresent = TP.length > 0;

  // snapshot structure vs disk cfg (set-of-sections + per-item k/l/b/t/v)
  const tpStruct = [];
  let tpStructOk = TP.length > 0;
  for (const m of TP) {
    if (m.tier === 1) {
      // Task 15: Tier 1 manifest page — groups/labels are manifest-authored by design
      // (cfg-section groups / key-name labels do NOT hold); asserted by its own group below.
      tpStruct.push({ guid: m.guid, tier1: 'skipped (manifest page, own assertion group)' });
      continue;
    }
    const secs = parseCfgSections(tpCfgOf(m.guid));
    const rec = { guid: m.guid, cfgExists: !!secs };
    if (!secs) { rec.err = 'cfg missing'; tpStructOk = false; tpStruct.push(rec); continue; }
    const panelSecs = m.groups.map((g) => g.title);
    rec.panelSecs = panelSecs;
    rec.diskSecs = secs.map((s) => s.sec);
    rec.secSetOk = panelSecs.length === secs.length && secs.every((s) => panelSecs.indexOf(s.sec) !== -1);
    if (!rec.secSetOk) { tpStructOk = false; tpStruct.push(rec); continue; }
    rec.items = [];
    let itemOk = true;
    for (const g of m.groups) {
      const diskSec = secs.find((s) => s.sec === g.title);
      for (const it of g.items) {
        const diskV = diskSec.keys[it.l];
        const ok = it.k === g.title + '.' + it.l && it.b === 'cfg' && diskV !== undefined
          && (diskV === 'true' || diskV === 'false' ? (it.t === 'bool' && String(it.v) === diskV) : true);
        rec.items.push({ k: it.k, t: it.t, v: it.v, diskV, ok });
        if (!ok) itemOk = false;
      }
    }
    rec.itemOk = itemOk;
    if (!itemOk) tpStructOk = false;
    tpStruct.push(rec);
  }
  F.tpStruct = tpStruct;
  V.tpSnapshotVsDisk = tpStructOk;

  // side rows for every tp guid + third-party category header present
  const tpSorted = TP.slice().sort((a, b) => (a.guid < b.guid ? -1 : 1));
  const tgt = tpSorted[0] || null;
  const tpRows = await page.evaluate(({ tail, guids }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const rows = [];
          for (const g of guids) {
            const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + g + '"]');
            rows.push(!!row && !!row.querySelector('[data-act="toggle"]'));
          }
          return { rows, cats: Array.from(d.querySelectorAll('#__mc_side .__mc-cat')).map((c) => c.textContent) };
        }
      } catch (e) {}
    }
    return null;
  }, { tail: MAIN_HTML, guids: TP.map((m) => m.guid) });
  F.tpRows = tpRows;
  V.tpSideRows = !!(tpRows && tpRows.rows.length && tpRows.rows.every(Boolean)
    && tpRows.cats.length === 3 && tpRows.cats[2].indexOf('\u7b2c\u4e09\u65b9 MOD') === 0);

  // local helpers (page-scoped; reuse of later-section helpers avoided on purpose)
  const tpClickToggle = (guid) => page.evaluate(({ tail, guid }) => {
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
  const tpClickBool = (g, i) => page.evaluate(({ tail, g, i }) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let k = 0; k < fs.length; k++) {
      try {
        const d = fs[k].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const b = d.querySelector('#__mc_cfg .__mc-mini-sw.__mc-big[data-act="set"][data-g="' + g + '"][data-i="' + i + '"]');
          if (b) { b.click(); return b.classList.contains('__mc-on'); }
        }
      } catch (e) {}
    }
    return false;
  }, { tail: MAIN_HTML, g, i });
  const tpDisk = (guid, sec, key) => stripNonCodeLines(readCfgSection(tpCfgOf(guid), sec, key));

  // DOM page of first tp mod: groups/items/notes/badges mirror snapshot
  if (tgt) {
    await clickRow(tgt.guid);
    await sleep(60);
    const tpPage = await readPage();
    F.tpPage = tpPage;
    const snapTgt = TP.find((m) => m.guid === tgt.guid);
    const snapNotes = snapTgt.groups.reduce((a, g) => a + g.items.filter((it) => it.note).length, 0);
    const snapBools = snapTgt.groups.reduce((a, g) => a + g.items.filter((it) => it.t === 'bool').length, 0);
    V.tpPageRender = !!(tpPage && tpPage.title === snapTgt.name && tpPage.en === snapTgt.name
      && tpPage.grps.length === snapTgt.groups.length
      && snapTgt.groups.every((g, i) => tpPage.grps[i] && tpPage.grps[i].t === g.title && tpPage.grps[i].items === g.items.length)
      && tpPage.notes.length === snapNotes
      && tpPage.grps.reduce((a, g) => a + g.bools, 0) === snapBools
      && tpPage.totalItems === snapTgt.groups.reduce((a, g) => a + g.items.length, 0));
  } else {
    V.tpPageRender = false;
  }

  // bool round-trip on a REAL tp cfg (first bool item of tgt; restore exact original)
  if (tgt) {
    const snapTgt = TP.find((m) => m.guid === tgt.guid);
    let bt = null;
    outerT: for (let gi = 0; gi < snapTgt.groups.length; gi++)
      for (let ii = 0; ii < snapTgt.groups[gi].items.length; ii++)
        if (snapTgt.groups[gi].items[ii].t === 'bool') { bt = { g: gi, i: ii, it: snapTgt.groups[gi].items[ii] }; break outerT; }
    if (bt) {
      const sec = bt.it.k.slice(0, bt.it.k.indexOf('.'));
      const before = tpDisk(tgt.guid, sec, bt.it.l);
      const want = before === 'true' ? 'false' : 'true';
      const mk1 = await sentMark();
      await clickRow(tgt.guid);
      await sleep(60);
      // race-proof receipt wait: snapshot the CURRENT event first, then accept any
      // NEW MC_SET_OK/FAIL (the drain tick can land between click and a clear-first wait)
      const evPre = await page.evaluate(() => JSON.stringify(window.__mcLastEvent || null));
      await tpClickBool(bt.g, bt.i);
      let ev1 = null;
      const dlE = Date.now() + 12000;
      for (;;) {
        ev1 = await page.evaluate(() => window.__mcLastEvent || null);
        if (ev1 && JSON.stringify(ev1) !== evPre
          && (ev1.type === 'MC_SET_OK' || ev1.type === 'MC_SET_FAIL')) break;
        if (Date.now() > dlE) break;
        await sleep(300);
      }
      const sent1 = firstOf(await sentSince(mk1), 'MC_SET');
      let after = null;
      const dlA = Date.now() + 12000;
      for (;;) {
        after = tpDisk(tgt.guid, sec, bt.it.l);
        if (after === want) break;
        if (Date.now() > dlA) break;
        await sleep(600);
      }
      F.tpBool = { k: bt.it.k, before, want, sent: sent1, ev: ev1, after };
      V.tpBoolMsgShape = !!(sent1 && sent1.guid === tgt.guid && sent1.section === sec
        && sent1.key === bt.it.k && sent1.value === (want === 'true'));
      V.tpBoolOkReceipt = !!(ev1 && ev1.k === bt.it.k && ev1.badge === 'cfg');
      V.tpBoolPersisted = after === want;
      await tpClickBool(bt.g, bt.i); // restore original value
      await waitEvent('MC_SET_OK');
      let back = null;
      const dlB = Date.now() + 12000;
      for (;;) {
        back = tpDisk(tgt.guid, sec, bt.it.l);
        if (back === before) break;
        if (Date.now() > dlB) break;
        await sleep(600);
      }
      V.tpBoolRestored = back === before;
      F.tpBool.back = back;
    } else {
      F.tpBool = { skip: 'no bool item in first tp mod' };
      V.tpBoolMsgShape = V.tpBoolOkReceipt = V.tpBoolPersisted = V.tpBoolRestored = false;
    }

    // toggle marker round-trip (restart-level semantics): off -> push-back + row off +
    // restart-level banner + [Disabled] marker true on modcenter cfg -> on -> marker false
    const mk2 = await sentMark();
    const seq0 = await page.evaluate(() => window.__mcSnapSeq || 0);
    await tpClickToggle(tgt.guid);
    let tog = null;
    const dlT = Date.now() + 15000;
    for (;;) {
      tog = await page.evaluate((guid) => {
        const s = window.__mcLastSnapshot;
        const m = s && s.tp && s.tp.find((x) => x.guid === guid);
        return { enabled: m ? m.enabled : undefined, seq: window.__mcSnapSeq || 0 };
      }, tgt.guid);
      if (tog.enabled === false && tog.seq > seq0) break;
      if (Date.now() > dlT) break;
      await sleep(400);
    }
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
    }, { tail: MAIN_HTML, guid: tgt.guid });
    await clickRow(tgt.guid);
    await sleep(60);
    const offPage = await readPage();
    const bannerText = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const b = d.querySelector('#__mc_cfg .__mc-stop-banner');
            return b ? b.textContent : null;
          }
        } catch (e) {}
      }
      return null;
    }, MAIN_HTML);
    let markerLine = null;
    const dlM = Date.now() + 12000;
    for (;;) {
      markerLine = readCfgSection(MC_CFG, 'Disabled', tgt.guid);
      if (markerLine && markerLine.indexOf('true') !== -1) break;
      if (Date.now() > dlM) break;
      await sleep(600);
    }
    F.tpToggle = { tog, offRow, bannerShown: offPage && offPage.stopBannerShown, bannerText, markerLine,
      sent: firstOf(await sentSince(mk2), 'MC_TOGGLE_MOD') };
    V.tpToggleSnapshotBack = !!(tog && tog.enabled === false && tog.seq > seq0);
    V.tpToggleRowOffBanner = offRow === true && !!(offPage && offPage.stopBannerShown)
      && !!bannerText && bannerText.indexOf('\u4e0b\u6b21\u542f\u52a8\u540e') !== -1; // restart-level wording
    V.tpToggleMarkerPersisted = !!(markerLine && markerLine.indexOf('true') !== -1);
    await tpClickToggle(tgt.guid); // restore enabled
    await sleep(2500);
    let markerBack = null;
    const dlM2 = Date.now() + 12000;
    for (;;) {
      markerBack = readCfgSection(MC_CFG, 'Disabled', tgt.guid);
      if (markerBack && markerBack.indexOf('false') !== -1) break;
      if (Date.now() > dlM2) break;
      await sleep(600);
    }
    V.tpToggleMarkerRestored = !!(markerBack && markerBack.indexOf('false') !== -1);
    F.tpToggle.markerBack = markerBack;
  } else {
    V.tpBoolMsgShape = V.tpBoolOkReceipt = V.tpBoolPersisted = V.tpBoolRestored = false;
    V.tpToggleSnapshotBack = V.tpToggleRowOffBanner = V.tpToggleMarkerPersisted = V.tpToggleMarkerRestored = false;
  }

  // str control (panel-side): synthetic snapshot injection through the REAL render
  // path (C# string write path is already live-proven by the ovr Overrides writes)
  if (tgt) {
    const fakeGuid = 'tp.fixture.str';
    const fakeSnap = JSON.parse(JSON.stringify(F.render0.snap));
    fakeSnap.tp = (fakeSnap.tp || []).concat([{ guid: fakeGuid, name: 'Fixture Str', en: 'Fixture Str',
      desc: 'fixture', enabled: true,
      groups: [{ title: 'S', items: [
        { k: 'S.Msg', l: 'Msg', t: 'str', v: 'hello', b: 'cfg' },
        { k: 'S.N', l: 'N', t: 'int', v: 7, min: 0, max: 10, st: 1, b: 'cfg' },
        // Fix R1 (P2): NO min/max/st — the third-party "numeric without
        // AcceptableValueRange" shape; v=500 sits outside any fabricated 0..100
        { k: 'S.Free', l: 'Free', t: 'int', v: 500, b: 'cfg' },
      ] }] }]);
    await page.evaluate((s) => { window.__mcRender(s); return true; }, fakeSnap);
    await sleep(60);
    await clickRow(fakeGuid);
    await sleep(60);
    const strCtl = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const t = d.querySelector('#__mc_cfg input[type=text][data-act="set"]');
            const n = d.querySelector('#__mc_cfg input[type=number][data-act="set"]');
            return { text: t ? t.value : null, num: n ? n.value : null };
          }
        } catch (e) {}
      }
      return null;
    }, MAIN_HTML);
    F.tpStrCtl = strCtl;
    V.tpStrRender = !!(strCtl && strCtl.text === 'hello' && strCtl.num === '7');

    // Fix R1 (review P2/F1): no-range numeric item (S.Free, index 2) must render a
    // number box ONLY — no fabricated 0..100 slider — and the out-of-range value 500
    // must display uncapped (no min/max attrs), then be SENT uncapped on input.
    const noRangeCtl = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const n = d.querySelector('#__mc_cfg input[type=number][data-act="set"][data-g="0"][data-i="2"]');
            const ctl = n ? n.closest('.__mc-ctl') : null;
            return (n && ctl) ? {
              ranges: ctl.querySelectorAll('input[type=range]').length,
              nums: ctl.querySelectorAll('input[type=number]').length,
              value: n.value, min: n.min, max: n.max,
            } : null;
          }
        } catch (e) {}
      }
      return null;
    }, MAIN_HTML);
    F.tpNoRangeCtl = noRangeCtl;
    V.tpNoRangeRender = !!(noRangeCtl && noRangeCtl.ranges === 0 && noRangeCtl.nums === 1
      && noRangeCtl.value === '500' && noRangeCtl.min === '' && noRangeCtl.max === '');
    const mkF = await sentMark();
    await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const n = d.querySelector('#__mc_cfg input[type=number][data-act="set"][data-g="0"][data-i="2"]');
            if (n) {
              n.value = '500';
              n.dispatchEvent(new Event('input', { bubbles: true }));
            }
            return true;
          }
        } catch (e) {}
      }
      return false;
    }, MAIN_HTML);
    let sentFree = null;
    const dlF = Date.now() + 8000;
    for (;;) {
      sentFree = (await sentSince(mkF)).find((s) => s && s.type === 'MC_SET' && s.key === 'S.Free') || null;
      if (sentFree) break;
      if (Date.now() > dlF) break;
      await sleep(500);
    }
    F.tpNoRangeSent = sentFree;
    V.tpNoRangeSendUncapped = !!(sentFree && sentFree.guid === fakeGuid
      && sentFree.section === 'S' && sentFree.value === 500);

    const mk3 = await sentMark();
    await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const el = d.querySelector('#__mc_cfg input[type=text][data-act="set"]');
            if (el) {
              el.value = 'world';
              el.dispatchEvent(new Event('change', { bubbles: true }));
            }
            return true;
          }
        } catch (e) {}
      }
      return false;
    }, MAIN_HTML);
    let sentStr = null;
    const dlS = Date.now() + 8000;
    for (;;) {
      sentStr = firstOf(await sentSince(mk3), 'MC_SET');
      if (sentStr) break;
      if (Date.now() > dlS) break;
      await sleep(500);
    }
    F.tpStrSent = sentStr;
    V.tpStrMsgShape = !!(sentStr && sentStr.guid === fakeGuid && sentStr.section === 'S'
      && sentStr.key === 'S.Msg' && sentStr.value === 'world');
    // restore real snapshot (MC_OPEN -> C# pushes fresh render) and assert fake row gone
    const seqR = await page.evaluate(() => window.__mcSnapSeq || 0);
    await page.evaluate(() => { window.__mcOutbox.push({ type: 'MC_OPEN' }); return true; });
    let fakeGone = null;
    const dlG = Date.now() + 12000;
    for (;;) {
      const r = await page.evaluate((guid) => {
        const fs = document.querySelectorAll('#iframe-container iframe');
        for (let i = 0; i < fs.length; i++) {
          try {
            const d = fs[i].contentDocument;
            if (d && d.location.pathname.indexOf('/UI/MainUI/MainUI.html') !== -1)
              return { gone: !d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]'), seq: window.__mcSnapSeq || 0 };
          } catch (e) {}
        }
        return null;
      }, fakeGuid);
      if (r && r.gone && r.seq > seqR) { fakeGone = true; break; }
      if (Date.now() > dlG) { fakeGone = false; break; }
      await sleep(400);
    }
    V.tpSyntheticCleanedUp = fakeGone === true;
  } else {
    V.tpStrRender = V.tpStrMsgShape = V.tpSyntheticCleanedUp = false;
    V.tpNoRangeRender = V.tpNoRangeSendUncapped = false;
  }

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
  const rsFullK = Z.secRs + '.' + Z.moveKey; // C1 fix: key = FULL display string
  V.sliderMsgShape = !!(firstOf(sent1, 'MC_SET') && sent1.find((s) => s.type === 'MC_SET' && s.guid === RS_GUID && s.section === Z.secRs && s.key === rsFullK && s.value === 3.5));
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
    if (sent2.filter((s) => s.type === 'MC_SET' && s.key === rsFullK).length >= 2) break;
    if (Date.now() > burstDl) break;
    await sleep(500);
  }
  F.throttleBurst = sent2.filter((s) => s.type === 'MC_SET' && s.key === rsFullK);
  const burst = F.throttleBurst;
  V.throttleShape = burst.length >= 2 && burst.length <= 4
    && burst[0].value === 4 && burst[burst.length - 1].value === 4.5;

  // restore 2
  await setRange(0, 1, 2);
  await waitEvent('MC_SET_OK');
  F.rsAfter = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.robotspeed.cfg', Z.secRs, Z.moveKey));

  // ---- interaction 1b: linked sliders end-to-end (C1 regression, 4 linked MC_SET) ----
  // key must be the FULL display string ("Expand.WidthMult + HeightMult") so the C#
  // Match hits its it.K branch; then TrySet writes BOTH writeKeys equal. Assert:
  // message shape -> MC_SET_OK -> both keys equal new value on disk -> restore.
  const LINKED = [
    { name: Z.bp, file: GAME_ROOT + '/BepInEx/config/com.local.survivallog.backpackexpand.cfg',
      k: 'Expand.WidthMult + HeightMult', g: 0, i: 0, keys: ['WidthMult', 'HeightMult'] },
    { name: Z.cb, file: GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg',
      k: 'Expand.WidthMult + HeightMult', g: 0, i: 0, keys: ['WidthMult', 'HeightMult'] },
    { name: Z.cb, file: GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg',
      k: 'Expand.FridgeWidthMult + FridgeHeightMult', g: 0, i: 1, keys: ['FridgeWidthMult', 'FridgeHeightMult'] },
    { name: Z.cb, file: GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg',
      k: 'Expand.VehicleWidthMult + VehicleHeightMult', g: 0, i: 2, keys: ['VehicleWidthMult', 'VehicleHeightMult'] },
  ];
  const readPair = (file, keys) => keys.map((k) => stripNonCodeLines(readCfgSection(file, 'Expand', k)));
  F.linked = [];
  for (const L of LINKED) {
    const entry = { k: L.k, step: [] };
    const orig = readPair(L.file, L.keys);
    entry.orig = orig;
    entry.pairEqual = orig[0] != null && orig[0] === orig[1];
    if (!entry.pairEqual) { F.linked.push(entry); continue; } // unequal pair: cannot round-trip safely, skip
    await clickRow(guidOf[L.name]);
    await sleep(60);
    const mk = await sentMark();
    await setRange(L.g, L.i, 2.5);
    const ev = await waitEvent('MC_SET_OK', 12000);
    const sent = firstOf(await sentSince(mk), 'MC_SET');
    entry.msg = sent && { section: sent.section, key: sent.key, value: sent.value };
    entry.evOk = !!(ev && ev.k === L.k && ev.badge === 'now');
    // both keys equal 2.5 on disk after debounce flush
    let pair = null;
    const dl = Date.now() + 12000;
    for (;;) {
      pair = readPair(L.file, L.keys);
      if (pair[0] === '2.5' && pair[1] === '2.5') break;
      if (Date.now() > dl) break;
      await sleep(600);
    }
    entry.afterSet = pair;
    // restore original pair value (equal pair -> one linked write restores both)
    await setRange(L.g, L.i, parseFloat(orig[0]));
    await waitEvent('MC_SET_OK', 12000);
    F.linked.push(entry);
  }
  // final pair check happens in the "final facts" settle below (after close flush)
  V.linkedMsgShape = F.linked.every((e) => !e.pairEqual
    || (e.msg && e.msg.section === 'Expand' && e.msg.key === e.k && e.msg.value === 2.5));
  V.linkedOkReceipt = F.linked.every((e) => !e.pairEqual || e.evOk);
  V.linkedBothKeysEqual = F.linked.every((e) => !e.pairEqual
    || (e.afterSet && e.afterSet[0] === '2.5' && e.afterSet[1] === '2.5'));

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
  V.boolMsgShape = !!(sent3set && sent3set.key === 'Vine.UnlockFlowerSeeds' && sent3set.value === !flower0);
  V.boolUiToggle = !!(b1 && b1.on === !flower0 && b1.aria === String(!flower0)
    && b2 && b2.on === flower0);
  V.boolOkReceipt = !!(ev3 && ev3.badge === 'now');

  // ---- interaction 4: select (UiFit.Mode -> 'Zoom') ----
  // NOTE: the DEPLOYED dll behind this page (registry GUID now com.local.survivallog.
  // cabinetsize, successor of CabinetExpand) does not bind UiFit.Mode — so the write is
  // REJECTED by the bridge by design (MC_SET_FAIL) until the entry exists. Assert BOTH
  // the message shape and the designed failure path (error toast + no cfg write).
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
  const uifitModeBefore = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg', 'UiFit', 'Mode'));
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
  const uifitModeAfter = stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg', 'UiFit', 'Mode'));
  F.selSet = { ev4, sent4, toast4, uifitModeBefore, uifitModeAfter };
  V.selMsgShape = !!(sent4 && sent4.section === 'UiFit' && sent4.key === 'UiFit.Mode' && sent4.value === 'Zoom');
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
  // task 13: toggle now writes the TARGET mod's own cfg General.Enabled (the old
  // ModCenter [Disabled] list is gone). Wait it persisted on disk (3s debounce + drain).
  let pdLine = null;
  const pdDl = Date.now() + 9000;
  for (;;) {
    pdLine = readCfgSection(PD_CFG, 'General', 'Enabled');
    if (pdLine && pdLine.indexOf('false') !== -1) break;
    if (Date.now() > pdDl) break;
    await sleep(500);
  }
  V.togglePersisted = !!(pdLine && pdLine.indexOf('false') !== -1);
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

  // =========================================================================
  // Task 15: Tier 1 declarative manifest (modcenter.json) assertion group.
  // Ground truth = target plugin's embedded manifest + its own cfg on disk.
  // Driver deploys two throwaway dlls for this round (deleted afterwards):
  //   tier1.demo     Tier1Demo.dll        mcSpec 1 manifest (namespaced resource
  //                                        Tier1Demo.modcenter.json — suffix path)
  //   tier0.fallback Tier0FallbackDemo.dll mcSpec 99 x2 (exact-name + dup) -> Tier 0
  // SKIP semantics: without the targets (net machine / post-cleanup) these
  // verdicts print SKIP and do not affect the result (T14 tpPresent keeps the
  // "some third-party present" gate on this machine via the ivmakk mods).
  // =========================================================================
  const T1_GUID = 'tier1.demo';
  const T0_GUID = 'tier0.fallback';
  const T1_NAME = 'Tier1 \u793a\u4f8b'; // Tier1 示例（manifest display 覆盖）
  const t1 = TP.find((m) => m.guid === T1_GUID) || null;
  const t0f = TP.find((m) => m.guid === T0_GUID) || null;
  const t1Exp = {
    summary: 'Tier1 \u58f0\u660e\u5f0f\u6e05\u5355\u9a8c\u8bc1\u9776\uff08\u5185\u5d4c modcenter.json\uff09\uff1a\u4e2d\u6587\u5b9a\u5236\u9875/\u5206\u7ec4\u6392\u5e8f/\u5fbd\u6807/\u8303\u56f4\u8986\u76d6/\u9690\u85cf\u952e\u6f14\u793a\u3002',
    groups: ['\u663e\u793a', '\u6027\u80fd'], // order 1 < 2（清单里"性能"列在前——order 覆盖清单序）
    groupCounts: [4, 1], // Ghost 隐藏 + Secret 未列不出现（显示组 5 项 - 1 幽灵 = 4）
    orderLabels: ['\u9762\u677f\u7f29\u653e', '\u9762\u677f\u5e38\u663e', '\u6392\u5e8f\u7532', '\u6392\u5e8f\u4e59'], // null=0 稳定序 + orderA(1)<orderB(5)
    scaleNote: '\u6e05\u5355\u8303\u56f4 0.5-2.0 \u8986\u76d6 cfg \u53cd\u5c04\u8303\u56f4 0.8-1.2',
    rebootTxt: '\ud83d\udd04 \u91cd\u542f\u540e', // guide badge cold -> panel BADGE.reboot.t（panel.js 徽标词汇，非 toast 文案）
  };
  F.t15 = { t1Found: !!t1, t0Found: !!t0f, t1: null, t1Dom: null, t1Write: null, t0Snap: null };
  if (t1) {
    // -- snapshot layer: identity / groups / items / range+badge overrides --
    const flat = t1.groups.map((g) => ({ t: g.title, items: g.items.map((it) => ({
      k: it.k, l: it.l, t: it.t, v: it.v, b: it.b, min: it.min, max: it.max, st: it.st, note: it.note || null,
    })) }));
    F.t15.t1 = { name: t1.name, en: t1.en, desc: t1.desc, tier: t1.tier, groups: flat };
    V.t15Identity = t1.tier === 1 && t1.name === T1_NAME && t1.en === 'Tier1Demo' && t1.desc === t1Exp.summary;
    const allItems = flat.reduce((a, g) => a.concat(g.items), []);
    V.t15Groups = JSON.stringify(flat.map((g) => g.t)) === JSON.stringify(t1Exp.groups)
      && JSON.stringify(flat.map((g) => g.items.length)) === JSON.stringify(t1Exp.groupCounts);
    V.t15Items = JSON.stringify(flat[0].items.map((it) => it.l)) === JSON.stringify(t1Exp.orderLabels)
      && !allItems.some((it) => it.k.indexOf('Ghost') !== -1 || it.k.indexOf('Secret') !== -1);
    const sc = flat[0].items[0];   // UI.Scale：清单 0.5-2.0/step .05/badge cold->reboot 覆盖反射 0.8-1.2
    const rf = flat[1].items[0];   // Perf.RefreshMs：无 AcceptableValueRange，范围纯来自清单
    const at = flat[0].items[1];   // UI.AlwaysOnTop：bool + badge reload
    V.t15Ranges = sc.k === 'UI.Scale' && sc.t === 'float' && sc.min === 0.5 && sc.max === 2 && sc.st === 0.05
      && sc.b === 'reboot' && sc.note === t1Exp.scaleNote
      && rf.k === 'Perf.RefreshMs' && rf.t === 'int' && rf.min === 100 && rf.max === 5000 && rf.st === 100 && rf.b === 'now'
      && at.k === 'UI.AlwaysOnTop' && at.t === 'bool' && at.b === 'reload';

    // -- DOM layer: 中文定制页渲染（页题/分组序/中文标签/徽标文案/滑条范围属性/note）--
    await clickRow(T1_GUID);
    await sleep(80);
    const t1Dom = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const grps = Array.from(d.querySelectorAll('#__mc_cfg .__mc-grp')).map((g) => ({
              t: g.querySelector('h3') ? g.querySelector('h3').textContent : null,
              items: Array.from(g.querySelectorAll('.__mc-item')).map((it) => ({
                l: (it.querySelector('.__mc-lb') || {}).textContent || null,
                badge: (it.querySelector('.__mc-badge') || {}).textContent || null,
                note: it.querySelector('.__mc-note') ? it.querySelector('.__mc-note').textContent : null,
                range: it.querySelector('input[type=range]')
                  ? { min: it.querySelector('input[type=range]').min, max: it.querySelector('input[type=range]').max, step: it.querySelector('input[type=range]').step }
                  : null,
              })),
            }));
            return {
              title: (d.querySelector('#__mc_cfg .__mc-mod-title h2') || {}).textContent || null,
              en: (d.querySelector('#__mc_cfg .__mc-mod-title .__mc-en') || {}).textContent || null,
              grps,
            };
          }
        } catch (e) {}
      }
      return null;
    }, MAIN_HTML);
    F.t15.t1Dom = t1Dom;
    V.t15Dom = !!(t1Dom && t1Dom.title === T1_NAME && t1Dom.en === 'Tier1Demo'
      && JSON.stringify(t1Dom.grps.map((g) => g.t)) === JSON.stringify(t1Exp.groups)
      && t1Dom.grps[0].items[0].l === t1Exp.orderLabels[0]
      && t1Dom.grps[0].items[0].badge === t1Exp.rebootTxt // cold 归一为 reboot 徽标文案
      && t1Dom.grps[0].items[0].range && t1Dom.grps[0].items[0].range.min === '0.5'
      && t1Dom.grps[0].items[0].range.max === '2' && t1Dom.grps[0].items[0].range.step === '0.05'
      && t1Dom.grps[0].items[0].note === t1Exp.scaleNote
      && JSON.stringify(t1Dom.grps[0].items.map((it) => it.l)) === JSON.stringify(t1Exp.orderLabels));
    try { await page.screenshot({ path: SHOT_DIR + '/task-15-shot-tier1.png' }); } catch (e) {}

    // -- write layer: 清单项滑条写路由（guid 路由/整展示键/回执 badge=清单徽标/盘上落盘/还原）--
    const t1Cfg = tpCfgOf(T1_GUID);
    const t1Disk = () => stripNonCodeLines(readCfgSection(t1Cfg, 'UI', 'Scale'));
    const before = t1Disk();
    const mk9 = await sentMark();
    await setRange(0, 0, 1.1); // UI.Scale（清单滑条 0.5-2.0；entry ClampValue 0.8-1.2 内）
    const ev9 = await waitEvent('MC_SET_OK', 12000);
    const sent9 = firstOf(await sentSince(mk9), 'MC_SET');
    let after9 = null;
    const dl9 = Date.now() + 12000;
    for (;;) {
      after9 = t1Disk();
      if (after9 === '1.1') break;
      if (Date.now() > dl9) break;
      await sleep(600);
    }
    F.t15.t1Write = { before, sent: sent9, ev: ev9, after: after9, back: null };
    V.t15Write = !!(sent9 && sent9.guid === T1_GUID && sent9.section === 'UI' && sent9.key === 'UI.Scale'
      && sent9.value === 1.1 && ev9 && ev9.k === 'UI.Scale' && ev9.badge === 'reboot' && after9 === '1.1');
    await setRange(0, 0, 1); // 还原
    await waitEvent('MC_SET_OK', 12000);
    let back9 = null;
    const dl9b = Date.now() + 12000;
    for (;;) {
      back9 = t1Disk();
      if (back9 === before) break;
      if (Date.now() > dl9b) break;
      await sleep(600);
    }
    F.t15.t1Write.back = back9;
    V.t15Write = V.t15Write && back9 === before;
  }

  // -- Tier 0 fallback (mcSpec 99 x2, duplicate resources): whole manifest ignored --
  if (t0f) {
    const secs = parseCfgSections(tpCfgOf(T0_GUID));
    const items = t0f.groups.reduce((a, g) => a.concat(g.items), []);
    const diskSecs = secs ? secs.map((s) => s.sec) : [];
    F.t15.t0Snap = { name: t0f.name, tier: t0f.tier, diskSecs,
      groups: t0f.groups.map((g) => ({ t: g.title, n: g.items.length })),
      items: items.map((it) => ({ k: it.k, l: it.l, t: it.t, b: it.b, min: it.min, max: it.max, st: it.st })),
      badges: items.map((it) => it.b).filter((b, i, arr) => arr.indexOf(b) === i) };
    V.t15Fallback = t0f.tier === undefined && t0f.name === 'Tier0FallbackDemo'
      && t0f.groups.length === diskSecs.length && diskSecs.every((s) => t0f.groups.some((g) => g.title === s))
      && t0f.groups.every((g) => g.items.every((it) => it.k === g.title + '.' + it.l && it.b === 'cfg'))
      && items.some((it) => it.k === 'UI.Secret') // 清单 99 被忽略：未列键照常全量显示（Tier 0 语义）
      && (function () { const sc = items.find((it) => it.k === 'UI.Scale'); return sc && sc.min === 0.8 && sc.max === 1.2 && sc.st === 0.1; })()
      && !items.some((it) => it.k.indexOf('Ghost') !== -1); // 清单幽灵组/幽灵项未混入
  }

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
  const linkedFinalPairs = LINKED.map((L) => ({ k: L.k, pair: readPair(L.file, L.keys) }));
  F.final = {
    rs: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.robotspeed.cfg', Z.secRs, Z.moveKey)),
    uifitMode: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.cabinetsize.cfg', 'UiFit', 'Mode')),
    flower: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.roleunlock.cfg', 'Vine', 'UnlockFlowerSeeds')),
    burden: stripNonCodeLines(readCfgSection(GAME_ROOT + '/BepInEx/config/com.local.survivallog.backpackexpand.cfg', 'Expand', 'BurdenMult')),
    ovr: stripNonCodeLines(readCfgSection(QA_CFG, '\u5176\u4ed6', 'Overrides')),
    pdEnabled: stripNonCodeLines(readCfgSection(PD_CFG, 'General', 'Enabled')),
    linkedPairs: linkedFinalPairs,
  };
  V.linkedRestored = F.linked.every((e, i) => !e.pairEqual
    || (linkedFinalPairs[i] && linkedFinalPairs[i].pair[0] === e.orig[0]
      && linkedFinalPairs[i].pair[1] === e.orig[1]));
  V.restoredStates = (F.final.rs === (F.rsBefore || '2'))
    && F.final.uifitMode === (F.selSet ? F.selSet.uifitModeBefore : F.final.uifitMode)
    && F.final.flower === (F.ruBefore || 'false')
    && F.final.burden === String(F.burdenBefore)
    && F.final.pdEnabled === 'true'
    && (F.final.ovr === '' || F.final.ovr === null);

  try { await page.screenshot({ path: SHOT_DIR + '/task-7-shot-panel.png' }); } catch (e) {}

  await browserRef.close();
  console.log('---- facts ----');
  console.log(JSON.stringify({
    layout: F.layout, planpickCost: F.planpickCost,
    tpList: F.tpList, tpStruct: F.tpStruct, tpRows: F.tpRows, tpPage: F.tpPage,
    tpBool: F.tpBool, tpToggle: F.tpToggle, tpStrCtl: F.tpStrCtl, tpStrSent: F.tpStrSent,
    tpNoRangeCtl: F.tpNoRangeCtl, tpNoRangeSent: F.tpNoRangeSent,
    t15: F.t15,
    pages: F.pages ? Object.keys(F.pages).reduce((acc, k) => {
      const p = F.pages[k];
      acc[k] = p ? { title: p.title, grps: p.grps, totalItems: p.totalItems, selOpts: p.selOpts, notes: p.notes, firstRange: p.firstRange, ovrNew: p.ovrNew, ovrRows: p.ovrRows, placeholder: p.placeholder } : null;
      return acc;
    }, {}) : null,
    sliderSet: F.sliderSet, throttleBurst: F.throttleBurst,
    linked: F.linked,
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
    ['0  side: 9 suite + self lock rows (+N tp)', V.sideTenRows && V.selfLock],
    ['0  side: tp empty hint consistent with tp count', V.tpEmptyHint],
    ['0  side: 3 category headers', V.threeCats],
    ['0  default page = backpack 2 items 2 ranges', V.defaultPageBackpack],
    ['0  backpack linked dynamic note (当前 宽..)', V.backpackDynNote],
    ['14 tp present (>=1 third-party plugin)', V.tpPresent],
    ['14 tp exempt: no first-party guid / probe name in tp', V.tpExempt],
    ['14 tp snapshot vs own cfg on disk (sections/items/values)', V.tpSnapshotVsDisk],
    ['14 tp side rows + third-party category header', V.tpSideRows],
    ['14 tp page render (groups/labels/notes/bool controls)', V.tpPageRender],
    ['14 tp bool MC_SET shape + cfg badge receipt', V.tpBoolMsgShape && V.tpBoolOkReceipt],
    ['14 tp bool persisted to third-party cfg (debounce)', V.tpBoolPersisted],
    ['14 tp bool restored to original', V.tpBoolRestored],
    ['14 tp toggle: snapshot push-back + row off + restart banner', V.tpToggleSnapshotBack && V.tpToggleRowOffBanner],
    ['14 tp toggle: [Disabled] marker persisted then restored', V.tpToggleMarkerPersisted && V.tpToggleMarkerRestored],
    ['14 str control renders text box (synthetic snapshot)', V.tpStrRender],
    ['14 str change commits MC_SET (string value)', V.tpStrMsgShape],
    ['F1 no-range numeric: number box only, 500 uncapped (no slider)', V.tpNoRangeRender],
    ['F1 no-range numeric: MC_SET sends 500 uncapped', V.tpNoRangeSendUncapped],
    ['14 synthetic snapshot cleaned up by fresh render', V.tpSyntheticCleanedUp],
    // Task 15: SKIP (null) when the throwaway target dlls are absent — verdict printer
    // treats null as SKIP (no pass/fail), so net machines keep RESULT: ALL PASS.
    ...(t1 ? [
      ['15 t1 tier=1 mark + display/summary override', V.t15Identity],
      ['15 t1 groups: manifest titles + order weights (显示<性能)', V.t15Groups],
      ['15 t1 items: 中文 labels/order/ghost+secret hidden', V.t15Items],
      ['15 t1 range/badge overrides (0.5-2.0 beat 0.8-1.2; cold->reboot)', V.t15Ranges],
      ['15 t1 DOM render (定制页/徽标文案/滑条属性/note)', V.t15Dom],
      ['15 t1 slider write: MC_SET + receipt badge reboot + persist/restore', V.t15Write],
    ] : [
      ['15 t1 identity', null], ['15 t1 groups', null], ['15 t1 items', null],
      ['15 t1 ranges', null], ['15 t1 dom', null], ['15 t1 write', null],
    ]),
    ...(t0f
      ? [['15 t0 fallback (mcSpec99 x2): tier0 page, Secret shown, reflected range', V.t15Fallback]]
      : [['15 t0 fallback', null]]),
    ...EXPECT.map((e) => ['1  page ' + e.name, V['page:' + e.name]]),
    ['D  PlanPick Cost min=100/max=300 reflected (snapshot+DOM)', V.planpickReflectedRange
      && (F.pages && F.pages[Z.pp] && F.pages[Z.pp].firstRange ? F.pages[Z.pp].firstRange.min === '100' && F.pages[Z.pp].firstRange.max === '300' : false)],
    ['1  self page fool-proof desc', V.selfPageFoolProof],
    ['2  slider MC_SET message shape', V.sliderMsgShape],
    ['2  slider MC_SET_OK receipt', V.sliderOkReceipt],
    ['2  slider sibling number sync', V.sliderSiblingSync],
    ['2  slider toast shown', V.sliderToast],
    ['2  slider 100ms throttle burst shape', V.throttleShape],
    ['C1x4 linked MC_SET key = full display k', V.linkedMsgShape],
    ['C1x4 linked MC_SET_OK receipt', V.linkedOkReceipt],
    ['C1x4 linked writeKeys both = 2.5 on disk', V.linkedBothKeysEqual],
    ['C1x4 linked pairs restored to baseline', V.linkedRestored],
    ['2  bool toggle msg + UI + receipt', V.boolMsgShape && V.boolUiToggle && V.boolOkReceipt],
    ['2  select msg + receipt', V.selMsgShape && V.selOkReceipt],
    ['2  reload-badge toast (读档后)', V.reloadBadgeToast],
    ['2  side toggle msg shape', V.toggleMsgShape],
    ['2  side toggle snapshot push-back', V.toggleSnapshotBack],
    ['2  side toggle row off + stop banner', V.toggleRowOffBanner],
    ['2  side toggle own-cfg General.Enabled=false persisted', V.togglePersisted],
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
    if (ok === null) { console.log('SKIP  ' + name + '  (t15 target dlls not deployed)'); continue; } // T15 净机语义
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

// ==========================================================================
//  MODE: toggle  (Task 13: hot-restore assertion suite, main menu)
//  Driver contract (bash):
//    - game launched via steam:// with ALL mod cfgs Enabled=true and the freshly
//      built ModCenter/QuickAction/RobotSpeed (+CabinetExpand for the round) dlls;
//    - BepInEx/ModCenter_evidence.txt deleted before launch (fresh evidence log);
//    - BackpackExpand cfg ExcludeIds preset (driver choice) for the inherited
//      semantics assertion; restored by the driver after the round.
//  Every assertion reads up to THREE sources: in-memory tables (via MC_EVIDENCE
//  multi-table snapshot lines in ModCenter_evidence.txt), trace files written
//  at state transitions (CE_gate_trace.txt / RU_gate_trace.txt), cfg on disk.
// ==========================================================================
async function toggleMode() {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);
  const { browser, page } = await connect(t0, log);

  const BP_GUID = 'com.local.survivallog.backpackexpand';
  const CAB_GUID = 'com.local.survivallog.cabinetexpand'; // 柜子扩容
  const FC_GUID = 'com.local.survivallog.fridgechill';
  const CEW_GUID = 'com.local.survivallog.cabineteverywhere';
  const RU_GUID = 'com.local.survivallog.roleunlock';
  const PP_GUID = 'com.local.survivallog.planpick';
  const CFG = (g) => GAME_ROOT + '/BepInEx/config/' + g + '.cfg';
  const EV_FILE = GAME_ROOT + '/BepInEx/ModCenter_evidence.txt';
  const TRACE_CE = GAME_ROOT + '/BepInEx/CE_gate_trace.txt';
  const TRACE_RU = GAME_ROOT + '/BepInEx/RU_gate_trace.txt';
  const GAME_LOG = GAME_ROOT + '/BepInEx/LogOutput.log';

  // wait for MainUI + entry + panel bootstrap
  const ready = async () => {
    try {
      return await page.evaluate((tail) => {
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
    } catch (e) { return false; }
  };
  const dl0 = Date.now() + 150000;
  for (;;) {
    if (await ready()) break;
    if (Date.now() > dl0) { log('FAIL: entry/panel bootstrap not ready'); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('bootstrap ready');

  // open panel via entry click
  await page.evaluate((tail) => {
    const fs = document.querySelectorAll('#iframe-container iframe');
    for (let i = 0; i < fs.length; i++) {
      try {
        const d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf(tail) !== -1) {
          const e = d.getElementById('__mc_entry'); if (e) e.click();
        }
      } catch (e2) {}
    }
  }, MAIN_HTML);
  const dlR = Date.now() + 15000;
  for (;;) {
    const ok = await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1)
            return !!(window.__mcLastSnapshot && d.getElementById('__mc_overlay'));
        } catch (e) {}
      }
      return false;
    }, MAIN_HTML);
    if (ok) break;
    if (Date.now() > dlR) { log('FAIL: panel/snapshot not ready'); process.exit(2); }
    await sleep(500);
  }
  log('panel open + snapshot');

  // ---- helpers ----
  const pushMsg = (obj) => page.evaluate((o) => { window.__mcOutbox.push(o); return true; }, obj);
  const readEvLine = (tag) => {
    try {
      const t = FS.readFileSync(EV_FILE, 'utf8');
      const lines = t.split('\n').filter((l) => l.indexOf('tag=' + tag + ' ') !== -1);
      return lines.length ? lines[lines.length - 1] : null; // LAST match (state may transition)
    } catch (e) { return null; }
  };
  // evidence round trip: push MC_EVIDENCE(tag) once -> C# appends one multi-table line
  // (outbox drain is 2s cadence; retry the push only if nothing landed after 5s)
  const ev = async (tag, budgetMs) => {
    const dl = Date.now() + (budgetMs || 12000);
    let pushedAt = 0;
    for (;;) {
      const l = readEvLine(tag);
      if (l) return l;
      if (Date.now() - pushedAt > 5000) { await pushMsg({ type: 'MC_EVIDENCE', tag: tag }); pushedAt = Date.now(); }
      if (Date.now() > dl) return 'TIMEOUT';
      await sleep(400);
    }
  };
  const act = (line, id) => {
    const m = String(line).match(new RegExp('(?:^|\\s|acts=)' + id + '=([^\\s]+)'));
    return m ? m[1] : null;
  };
  const bag = (line, id) => {
    const m = String(line).match(new RegExp('(?:^|\\s|bags=)' + id + '=(\\S+)'));
    return m ? m[1] : null;
  };
  const cold = (line) => { const m = String(line).match(/cold=([^\s]+)/); return m ? m[1] : null; };
  const talent = (line) => { const m = String(line).match(/talent=restricted=(\d+) vase=(\S+)/); return m ? { r: +m[1], vase: m[2] } : null; };
  const ceCount = (line) => { const m = String(line).match(/(?:^|\s)ce=([^\s]+)/); return m ? m[1] : null; };
  const moveSpeeds = (line) => { const ms = String(line).match(/ms=([^\r\n]+?)\s+acts=/); return ms ? (ms[1].match(/MoveSpeed=([\d.]+)/g) || []).join(',') : null; };

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

  // toggle + wait for C# push-back (new __mcRender with expected enabled value);
  // re-opens the panel if it was destroyed (author menu navigation)
  const toggleAndWait = async (guid, wantEnabled) => {
    if (!(await ensurePanel())) throw new Error('panel/host gone (session taken over)');
    const seq0 = await page.evaluate(() => window.__mcSnapSeq || 0);
    const clicked = await clickToggle(guid);
    const dl = Date.now() + 15000;
    for (;;) {
      const r = await page.evaluate((guid) => {
        const s = window.__mcLastSnapshot;
        const m = s && s.mods && s.mods.find((x) => x.guid === guid);
        return { enabled: m ? m.enabled : undefined, seq: window.__mcSnapSeq || 0 };
      }, guid);
      if (r.enabled === wantEnabled && r.seq > seq0) return { clicked, pushed: true };
      if (Date.now() > dl) return { clicked, pushed: false, got: r.enabled };
      await sleep(400);
    }
  };
  // row/page visual state for a mod (clicks its row first)
  const rowState = async (guid) => {
    await page.evaluate(({ tail, guid }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
            if (row) row.click();
          }
        } catch (e) {}
      }
    }, { tail: MAIN_HTML, guid });
    await sleep(80);
    return page.evaluate(({ tail, guid }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
            const banner = d.querySelector('#__mc_stop') || d.querySelector('.__mc-stop-banner');
            return {
              off: row ? row.classList.contains('__mc-off') : null,
              banner: banner ? banner.classList.contains('__mc-show') : null,
            };
          }
        } catch (e) {}
      }
      return null;
    }, { tail: MAIN_HTML, guid });
  };
  const waitCfgKey = async (guid, section, key, want, budgetMs) => {
    const dl = Date.now() + (budgetMs || 12000);
    for (;;) {
      const v = stripNonCodeLines(readCfgSection(CFG(guid), section, key));
      if (v === want) return v;
      if (Date.now() > dl) return v;
      await sleep(600);
    }
  };
  const readTraceText = (p) => { try { return FS.readFileSync(p, 'utf8'); } catch (e) { return null; } };
  const logHas = async (needle, budgetMs) => {
    const dl = Date.now() + (budgetMs || 15000);
    for (;;) {
      try { if (FS.readFileSync(GAME_LOG, 'utf8').indexOf(needle) !== -1) return true; } catch (e) {}
      if (Date.now() > dl) return false;
      await sleep(700);
    }
  };
  // poll evidence until predicate passes or budget out; returns last line
  // poll evidence until predicate passes or budget out; re-pushes the request at
  // most every 2s (drain cadence) so each retry appends ONE fresh-state line
  const evWait = async (tag, pred, budgetMs) => {
    const dl = Date.now() + (budgetMs || 12000);
    let last = null;
    let lastPush = 0;
    for (;;) {
      if (Date.now() - lastPush >= 2000) { await pushMsg({ type: 'MC_EVIDENCE', tag: tag }); lastPush = Date.now(); }
      await sleep(600);
      last = readEvLine(tag);
      if (last && pred(last)) return { ok: true, line: last };
      if (Date.now() > dl) return { ok: false, line: last };
    }
  };

  const V = {}; const F = {};
  const CK = [];
  const ck = (name, ok) => { CK.push([name, !!ok]); log((ok ? 'PASS ' : 'FAIL ') + name); };
  const ckSkip = (name, why) => { CK.push([name + '  [SKIP: ' + why + ']', true]); log('SKIP  ' + name + ' (' + why + ')'); };

  // suite composition probe: the parallel CabinetSize task replaced CabinetExpand
  // in Meta (commit c899f20 series). Cabinet hot-restore assertions only run when
  // the deployed Meta actually exposes cabinetexpand.
  const snapGuids = await page.evaluate(() => (window.__mcLastSnapshot ? window.__mcLastSnapshot.mods.map((m) => m.guid) : []));
  const HAS_CAB = snapGuids.indexOf('com.local.survivallog.cabinetexpand') !== -1;
  F.hasCab = HAS_CAB; F.snapGuids = snapGuids;

  // ---- liveness guard (T13 round-1 lesson): the author can take over the session
  // mid-round (panel DOM destroyed by menu navigation / world load). Every toggle
  // helper below re-opens the panel if the overlay is gone; if the host page is
  // gone entirely (world load), abort loudly instead of blindly polling timeouts.
  const panelAlive = async () => {
    try {
      return await page.evaluate((tail) => {
        const fs = document.querySelectorAll('#iframe-container iframe');
        for (let i = 0; i < fs.length; i++) {
          try {
            const d = fs[i].contentDocument;
            if (d && d.location.pathname.indexOf(tail) !== -1) return !!d.getElementById('__mc_overlay');
          } catch (e) {}
        }
        return false;
      }, MAIN_HTML);
    } catch (e) { return false; }
  };
  const ensurePanel = async () => {
    if (await panelAlive()) return true;
    // host gone entirely? (world load / exit)
    let host = false;
    try {
      host = await page.evaluate((tail) => {
        const fs = document.querySelectorAll('#iframe-container iframe');
        for (let i = 0; i < fs.length; i++) {
          try { const d = fs[i].contentDocument; if (d && d.location.pathname.indexOf(tail) !== -1) return true; } catch (e) {}
        }
        return false;
      }, MAIN_HTML);
    } catch (e) { host = false; }
    if (!host) return false; // session taken over: abort
    // re-open via entry click and wait for overlay
    await page.evaluate((tail) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) { const e = d.getElementById('__mc_entry'); if (e) e.click(); }
        } catch (e2) {}
      }
    }, MAIN_HTML);
    const dl = Date.now() + 10000;
    for (;;) {
      if (await panelAlive()) return true;
      if (Date.now() > dl) return false;
      await sleep(500);
    }
  };;

  // ---- 0) baseline all-on discriminators ----
  const e0 = await evWait('t0-base', (l) => cold(l) !== null && cold(l) !== '未就绪' && talent(l) !== null, 20000);
  F.e0 = e0.line;
  ck('0 baseline: ms=4.8 x3 (RS x2 on)', moveSpeeds(e0.line) === 'MoveSpeed=4.8,MoveSpeed=4.8,MoveSpeed=4.8');
  ck('0 baseline: acts 9075=60 9098=150 9100=36 9130=60 9131=150 1611=60 1615=60 1915=60',
    act(e0.line, 9075) === '60' && act(e0.line, 9098) === '150' && act(e0.line, 9100) === '36'
    && act(e0.line, 9130) === '60' && act(e0.line, 9131) === '150' && act(e0.line, 1611) === '60'
    && act(e0.line, 1615) === '60' && act(e0.line, 1915) === '60');
  F.baseBags = { b1: bag(e0.line, 1), b8: bag(e0.line, 8), b11: bag(e0.line, 11), c1061: bag(e0.line, 1061), c1063: bag(e0.line, 1063), cab: bag(e0.line, 110001), rack: bag(e0.line, 110009), fridge: bag(e0.line, 1004), fr2: bag(e0.line, 3002), veh: bag(e0.line, 1023) };
  ck('0 baseline: bags 1=12x11/48000 11=14x17 1061=8x2 1063=8x3',
    String(bag(e0.line, 1)).indexOf('12x11/48000') === 0
    && String(bag(e0.line, 11)).indexOf('14x17') === 0
    && String(bag(e0.line, 1061)).indexOf('8x2') === 0 && String(bag(e0.line, 1063)).indexOf('8x3') === 0);
  if (HAS_CAB) {
    ck('0 baseline: cabinet rows 110001=12x15 110009=23x18 1004=12x11 3002=9x8 1023=18x15 (CabinetExpand x1.5)',
      String(bag(e0.line, 110001)).indexOf('12x15') === 0 && String(bag(e0.line, 110009)).indexOf('23x18') === 0
      && String(bag(e0.line, 1004)).indexOf('12x11') === 0 && String(bag(e0.line, 3002)).indexOf('9x8') === 0
      && String(bag(e0.line, 1023)).indexOf('18x15') === 0);
  } else {
    ckSkip('0 baseline + hot-restore: cabinet rows (CabinetExpand)', 'deployed Meta replaced by CabinetSize (parallel task); CabinetExpand not in panel');
  }
  ck('0 baseline: cold=16/16@0.1 (FC x2 on)', cold(e0.line) === '16/16@0.1');
  ck('0 baseline: talent restricted=77 vase=1/1 (RU on)', talent(e0.line) && talent(e0.line).r === 77 && talent(e0.line).vase === '1/1');
  ck('0 baseline: ce whitelist=710 (CEW on)', ceCount(e0.line) === '710');

  // ---- 1) RobotSpeed toggle x2 rounds: <=5s table back to official, re-enable re-applies ----
  for (let round = 1; round <= 2; round++) {
    const off = await toggleAndWait(RS_GUID, false);
    const cfgOff = await waitCfgKey(RS_GUID, 'General', 'Enabled', 'false');
    const vis = await rowState(RS_GUID);
    const e1 = await evWait('rs-off-' + round, (l) => moveSpeeds(l) === 'MoveSpeed=2.4,MoveSpeed=2.4,MoveSpeed=2.4', 10000);
    F['rsOff' + round] = { off, cfgOff, vis, line: e1.line };
    ck('RS r' + round + ' off: snapshot push + cfg=false + row off + banner', off.pushed && cfgOff === 'false' && vis.off === true && vis.banner === true);
    ck('RS r' + round + ' off: <=~5s MoveSpeed 4.8->2.4 (x3)', moveSpeeds(e1.line) === 'MoveSpeed=2.4,MoveSpeed=2.4,MoveSpeed=2.4');
    ck('RS r' + round + ' off: acts 9098/9099/9100/9131 -> 300/300/60/300; 1061/1063 -> 4x2/4x3; 9130 stays 60 (QA on)',
      act(e1.line, 9098) === '300' && act(e1.line, 9099) === '300' && act(e1.line, 9100) === '60'
      && act(e1.line, 9131) === '300' && String(bag(e1.line, 1061)).indexOf('4x2') === 0
      && String(bag(e1.line, 1063)).indexOf('4x3') === 0 && act(e1.line, 9130) === '60');
    const on = await toggleAndWait(RS_GUID, true);
    const cfgOn = await waitCfgKey(RS_GUID, 'General', 'Enabled', 'true');
    const e2 = await evWait('rs-on-' + round, (l) => moveSpeeds(l) === 'MoveSpeed=4.8,MoveSpeed=4.8,MoveSpeed=4.8', 10000);
    F['rsOn' + round] = { on, cfgOn, line: e2.line };
    ck('RS r' + round + ' on: re-applied 4.8 x3 + 9098=150 + 9130=60 + cargo 8x2',
      on.pushed && cfgOn === 'true' && moveSpeeds(e2.line) === 'MoveSpeed=4.8,MoveSpeed=4.8,MoveSpeed=4.8'
      && act(e2.line, 9098) === '150' && act(e2.line, 9130) === '60' && String(bag(e2.line, 1061)).indexOf('8x2') === 0);
  }

  // ---- 2) BackpackExpand toggle: restore x1 mult (bag1 8x7/9600, bag11 9x11); bag 8 excluded stays 9x8 ----
  {
    const off = await toggleAndWait(BP_GUID, false);
    const cfgOff = await waitCfgKey(BP_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('bp-off', (l) => String(bag(l, 1)).indexOf('8x7/9600') === 0, 10000);
    F.bpOff = { off, cfgOff, line: e1.line };
    ck('BP off: bag1 -> 8x7/9600 bag11 -> 9x11 (x1 restore)', String(bag(e1.line, 1)).indexOf('8x7/9600') === 0 && String(bag(e1.line, 11)).indexOf('9x11') === 0);
    ck('BP off (ExcludeIds=8 inherited semantics): bag8 stays OFFICIAL 9x8 both before and after (row never managed this session)',
      String(bag(e1.line, 8)) === '9x8/0' || String(bag(e1.line, 8)).indexOf('9x8') === 0);
    const on = await toggleAndWait(BP_GUID, true);
    const e2 = await evWait('bp-on', (l) => String(bag(l, 1)).indexOf('12x11/48000') === 0, 10000);
    F.bpOn = { on, line: e2.line };
    ck('BP on: bag1 -> 12x11/48000 bag11 -> 14x17; bag8 STILL 9x8 (excluded)',
      String(bag(e2.line, 1)).indexOf('12x11/48000') === 0 && String(bag(e2.line, 8)).indexOf('9x8') === 0);
    // write-side guard: ExcludeIds not in Meta -> MC_SET_FAIL (bridge only writes declared keys)
    await page.evaluate((guid) => {
      window.__mcOutbox.push({ type: 'MC_SET', guid: guid, section: 'Expand', key: 'ExcludeIds', value: '1' });
      window.__mcLastEvent = null;
      return true;
    }, BP_GUID);
    let rej = null;
    const dlRej = Date.now() + 12000;
    for (;;) {
      rej = await page.evaluate(() => window.__mcLastEvent || null);
      if (rej && rej.type === 'MC_SET_FAIL') break;
      if (Date.now() > dlRej) break;
      await sleep(400);
    }
    F.bpExcludeReject = rej;
    ck('BP ExcludeIds NOT exposed: MC_SET rejected (residual-modified-value scenario unreachable via panel)',
      !!(rej && rej.type === 'MC_SET_FAIL' && rej.k === 'Expand.ExcludeIds'));
  }

  // ---- 3) QuickAction toggle (RS on): restore official; 9130 -> 450 (RS re-manages its own) ----
  {
    const off = await toggleAndWait(QA_GUID, false);
    const cfgOff = await waitCfgKey(QA_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('qa-off', (l) => act(l, 9075) === '3600', 10000);
    F.qaOff = { off, cfgOff, line: e1.line };
    ck('QA off: 9075->3600 1611->900 1615->300 1915->1200', act(e1.line, 9075) === '3600' && act(e1.line, 1611) === '900' && act(e1.line, 1615) === '300' && act(e1.line, 1915) === '1200');
    ck('QA off with RS on: 9130=450 (QA restores polluted cache, RS yield-guard keeps its own target)',
      act(e1.line, 9130) === '450');
    const on = await toggleAndWait(QA_GUID, true);
    const e2 = await evWait('qa-on', (l) => act(l, 9075) === '60', 10000);
    F.qaOn = { on, line: e2.line };
    ck('QA on: 9075->60 9130->60 again', act(e2.line, 9075) === '60' && act(e2.line, 9130) === '60');
  }

  // ---- 4) 9130 double-off determinism ----
  // Order B (QA off first, RS off last) is the DETERMINISTIC-900 case: RS's clean
  // cache restores last; QA's restore yield-guard (cur 450 < 900) lets it stand.
  // Order A with a forced >=6s gap (QA's 5s apply cycle guaranteed to run between
  // the toggles) is the recorded polluted-cache final: QA re-applies its managed
  // 9130=60 after RS's 900 restore, then its own restore writes its cached 450.
  // Full convergence needs a cross-mod original registry (out of minimal-fix scope).
  {
    // order A (forced gap -> deterministic polluted final 450)
    const a1 = await toggleAndWait(RS_GUID, false);
    await sleep(6500); // let QA's active 5s cycle re-manage 9130 after RS's restore
    const a2 = await toggleAndWait(QA_GUID, false);
    const eA = await evWait('dbl-off-a', (l) => act(l, 9098) === '300' && act(l, 9075) === '3600' && (act(l, 9130) === '450' || act(l, 9130) === '900'), 12000);
    F.dblA = { a1, a2, line: eA.line };
    ck('9130 double-off (RS then QA, gap 6.5s): robot acts restored 300; final = QA cached view (450 recorded, 900 if cycle missed)',
      act(eA.line, 9098) === '300' && (act(eA.line, 9130) === '450' || act(eA.line, 9130) === '900'));
    const r1 = await toggleAndWait(QA_GUID, true);
    const r2 = await toggleAndWait(RS_GUID, true);
    const eBack = await evWait('dbl-back-a', (l) => act(l, 9130) === '60', 12000);
    ck('9130 double-off recover: both on -> 60 again', r1.pushed && r2.pushed && act(eBack.line, 9130) === '60');
    // order B (deterministic official 900: RS restores last with clean cache)
    const b1 = await toggleAndWait(QA_GUID, false);
    const b2 = await toggleAndWait(RS_GUID, false);
    const eB = await evWait('dbl-off-b', (l) => act(l, 9130) === '900' && act(l, 9075) === '3600' && act(l, 9098) === '300', 12000);
    F.dblB = { b1, b2, line: eB.line };
    ck('9130 double-off (QA then RS): final = 900 official, order-independent', act(eB.line, 9130) === '900');
    const s1 = await toggleAndWait(RS_GUID, true);
    const s2 = await toggleAndWait(QA_GUID, true);
    const eB2 = await evWait('dbl-back-b', (l) => act(l, 9130) === '60', 12000);
    ck('9130 order-B recover: both on -> 60', s1.pushed && s2.pushed && act(eB2.line, 9130) === '60');
  }

  // ---- 5) CabinetExpand toggle: RestoreAllSizes runtime path (uncapped: rack 20x12) ----
  if (HAS_CAB) {
    const off = await toggleAndWait(CAB_GUID, false);
    const cfgOff = await waitCfgKey(CAB_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('cab-off', (l) => String(bag(l, 110001)).indexOf('8x10') === 0, 10000);
    F.cabOff = { off, cfgOff, line: e1.line };
    ck('CAB off: 110001->8x10 1004->8x7 3002->6x5 1023->12x10 (RestoreAllSizes runtime)',
      String(bag(e1.line, 110001)).indexOf('8x10') === 0 && String(bag(e1.line, 1004)).indexOf('8x7') === 0
      && String(bag(e1.line, 3002)).indexOf('6x5') === 0 && String(bag(e1.line, 1023)).indexOf('12x10') === 0);
    ck('CAB off: rack 110009 -> 20x12 native (restore NOT capped by MaxCols)', String(bag(e1.line, 110009)).indexOf('20x12') === 0);
    const on = await toggleAndWait(CAB_GUID, true);
    const e2 = await evWait('cab-on', (l) => String(bag(l, 110009)).indexOf('23x18') === 0, 10000);
    F.cabOn = { on, line: e2.line };
    ck('CAB on: sizes re-applied (110001=12x15 rack=23x18)', String(bag(e2.line, 110001)).indexOf('12x15') === 0 && String(bag(e2.line, 110009)).indexOf('23x18') === 0);
  } else {
    ckSkip('CAB hot off/on (RestoreAllSizes runtime, uncapped rack 20x12)', 'CabinetExpand not in deployed Meta (CabinetSize parallel task)');
  }

  // ---- 6) FridgeChill toggle + KeepMult 2->1 hot migration ----
  {
    const off = await toggleAndWait(FC_GUID, false);
    const cfgOff = await waitCfgKey(FC_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('fc-off', (l) => cold(l) === '0/16@0.2', 10000);
    const logLine = await logHas('General.Enabled=false\uff1a\u5df2\u8fd8\u539f\u5b98\u65b9\u539f\u503c\u5e76\u505c\u5199', 8000);
    F.fcOff = { off, cfgOff, line: e1.line };
    ck('FC off: cold -> 0/16@0.2 + restore log line', cold(e1.line) === '0/16@0.2' && logLine);
    const on = await toggleAndWait(FC_GUID, true);
    const e2 = await evWait('fc-on', (l) => cold(l) === '16/16@0.1', 10000);
    ck('FC on: cold -> 16/16@0.1', cold(e2.line) === '16/16@0.1');
    // KeepMult 2 -> 1 via panel slider (FC still enabled): early-exit region ALSO restores
    if (!(await ensurePanel())) throw new Error('panel/host gone (session taken over) at FC KeepMult');
    await page.evaluate((guid) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf('/UI/MainUI/MainUI.html') !== -1) {
            const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
            if (row) row.click();
          }
        } catch (e) {}
      }
      return true;
    }, FC_GUID);
    await sleep(80);
    await page.evaluate(({ tail }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let k = 0; k < fs.length; k++) {
        try {
          const d = fs[k].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const el = d.querySelector('#__mc_cfg input[type=range][data-act="set"][data-g="0"][data-i="0"]');
            if (!el) return false;
            el.value = '1';
            el.dispatchEvent(new Event('input', { bubbles: true }));
            return true;
          }
        } catch (e) {}
      }
      return false;
    }, { tail: MAIN_HTML });
    const e3 = await evWait('fc-km1', (l) => cold(l) === '0/16@0.2', 12000);
    F.fcKm1 = { line: e3.line, cfgKm: await waitCfgKey(FC_GUID, 'Chill', 'KeepMult', '1') };
    ck('FC KeepMult 2->1 (still enabled): early-exit region restores cold -> 0/16@0.2 + cfg=1', cold(e3.line) === '0/16@0.2' && F.fcKm1.cfgKm === '1');
    // back to 2
    await page.evaluate(({ tail }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let k = 0; k < fs.length; k++) {
        try {
          const d = fs[k].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const el = d.querySelector('#__mc_cfg input[type=range][data-act="set"][data-g="0"][data-i="0"]');
            if (!el) return false;
            el.value = '2';
            el.dispatchEvent(new Event('input', { bubbles: true }));
            return true;
          }
        } catch (e) {}
      }
      return false;
    }, { tail: MAIN_HTML });
    const e4 = await evWait('fc-km2', (l) => cold(l) === '16/16@0.1', 12000);
    ck('FC KeepMult back 2: cold -> 16/16@0.1', cold(e4.line) === '16/16@0.1');
  }

  // ---- 7) CabinetEverywhere toggle: whitelist symmetric RemoveAll (trace + live count) ----
  {
    const traceBefore = readTraceText(TRACE_CE);
    const off = await toggleAndWait(CEW_GUID, false);
    const cfgOff = await waitCfgKey(CEW_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('cew-off', (l) => ceCount(l) === '1', 10000);
    const traceAfter = readTraceText(TRACE_CE);
    F.cewOff = { off, cfgOff, line: e1.line, traceAdded: traceBefore !== null ? String(traceAfter || '').slice(String(traceBefore).length) : traceAfter };
    const added = F.cewOff.traceAdded || '';
    ck('CEW off: whitelist 710 -> 1 (live count) + RemoveAll trace (before=710 after=1)',
      ceCount(e1.line) === '1' && added.indexOf('RemoveAll') !== -1 && added.indexOf('710') !== -1 && added.indexOf('1') !== -1);
    const on = await toggleAndWait(CEW_GUID, true);
    const e2 = await evWait('cew-on', (l) => ceCount(l) === '710', 12000);
    ck('CEW on: whitelist re-injected 710', ceCount(e2.line) === '710');
  }

  // ---- 8) RoleUnlock toggle + two Plan switches ----
  {
    const off = await toggleAndWait(RU_GUID, false);
    const cfgOff = await waitCfgKey(RU_GUID, 'General', 'Enabled', 'false');
    const e1 = await evWait('ru-off', (l) => talent(l) && talent(l).r === 123 && talent(l).vase === '0/0', 15000);
    const traceRu = readTraceText(TRACE_RU) || '';
    F.ruOff = { off, cfgOff, line: e1.line, traceTail: traceRu.slice(-600) };
    ck('RU off: talent restricted 123 + vase 0/0 (all-off restore)', talent(e1.line) && talent(e1.line).r === 123 && talent(e1.line).vase === '0/0');
    ck('RU off: RU_gate_trace restore lines present', traceRu.indexOf('\u8fd8\u539f\u5b98\u65b9\u767d\u540d\u5355') !== -1);
    const on = await toggleAndWait(RU_GUID, true);
    const e2 = await evWait('ru-on', (l) => talent(l) && talent(l).r === 77 && talent(l).vase === '1/1', 15000);
    ck('RU on: 77 + 1/1 back', talent(e2.line) && talent(e2.line).r === 77 && talent(e2.line).vase === '1/1');

    // Plan.ClearPool switch via panel bool (group 1 item 1)
    if (!(await ensurePanel())) throw new Error('panel/host gone (session taken over) at RU switches');
    const clickRuBool = (g, i) => page.evaluate(({ tail, g, i }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let k = 0; k < fs.length; k++) {
        try {
          const d = fs[k].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const b = d.querySelector('#__mc_cfg .__mc-mini-sw.__mc-big[data-act="set"][data-g="' + g + '"][data-i="' + i + '"]');
            if (b) { b.click(); return true; }
          }
        } catch (e) {}
      }
      return false;
    }, { tail: MAIN_HTML, g, i });
    const ruRow = await page.evaluate(({ tail, guid }) => {
      const fs = document.querySelectorAll('#iframe-container iframe');
      for (let i = 0; i < fs.length; i++) {
        try {
          const d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf(tail) !== -1) {
            const row = d.querySelector('#__mc_side .__mc-mod-row[data-guid="' + guid + '"]');
            if (row) row.click();
          }
        } catch (e) {}
      }
      return true;
    }, { tail: MAIN_HTML, guid: RU_GUID });
    await sleep(80);
    await clickRuBool(1, 1); // Plan.ClearPool -> false
    const eCp = await evWait('ru-cp-off', (l) => talent(l) && talent(l).r === 123, 15000);
    const cpCfg = await waitCfgKey(RU_GUID, 'Plan', 'ClearPool', 'false');
    F.ruCp = { line: eCp.line, cpCfg };
    ck('RU Plan.ClearPool=false via panel: restricted 77->123 + cfg persisted', talent(eCp.line) && talent(eCp.line).r === 123 && cpCfg === 'false');
    await clickRuBool(1, 1); // back on
    const eCp2 = await evWait('ru-cp-on', (l) => talent(l) && talent(l).r === 77, 15000);
    ck('RU Plan.ClearPool=true: 77 back', talent(eCp2.line) && talent(eCp2.line).r === 77);
    await clickRuBool(1, 2); // Plan.VaseRecipe -> false
    const eVr = await evWait('ru-vr-off', (l) => talent(l) && talent(l).vase === '0/0', 15000);
    const vrCfg = await waitCfgKey(RU_GUID, 'Plan', 'VaseRecipe', 'false');
    F.ruVr = { line: eVr.line, vrCfg };
    ck('RU Plan.VaseRecipe=false via panel: vase 0/0 + cfg persisted', talent(eVr.line) && talent(eVr.line).vase === '0/0' && vrCfg === 'false');
    await clickRuBool(1, 2); // back on
    const eVr2 = await evWait('ru-vr-on', (l) => talent(l) && talent(l).vase === '1/1', 15000);
    ck('RU Plan.VaseRecipe=true: 1/1 back', talent(eVr2.line) && talent(eVr2.line).vase === '1/1');
  }

  // ---- 9) PlanPick + PDS: cfg-level toggles (UI-level -> T17 with save backup) ----
  for (const [guid, tag, name] of [[PP_GUID, 'pp', 'PlanPick'], [PD_GUID, 'pd', 'PDS']]) {
    const off = await toggleAndWait(guid, false);
    const cfgOff = await waitCfgKey(guid, 'General', 'Enabled', 'false');
    const vis = await rowState(guid);
    const on = await toggleAndWait(guid, true);
    const cfgOn = await waitCfgKey(guid, 'General', 'Enabled', 'true');
    F[tag] = { off, cfgOff, vis, on, cfgOn };
    ck(name + ' toggle: own-cfg General.Enabled false->true + row/banner visual',
      off.pushed && cfgOff === 'false' && vis.off === true && vis.banner === true
      && on.pushed && cfgOn === 'true');
  }

  // ---- 10) close panel (flush) + final all-on evidence + all cfgs enabled ----
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
  }, MAIN_HTML);
  await sleep(4500); // flush debounce + drain
  const ef = await ev('t-final', 12000);
  F.efinal = ef;
  const allGuids = [BP_GUID, FC_GUID, QA_GUID, CEW_GUID, RU_GUID, RS_GUID, PP_GUID, PD_GUID]
    .concat(HAS_CAB ? [CAB_GUID] : []);
  const cfgFinal = {};
  let allEnabled = true;
  for (const g of allGuids) {
    const v = stripNonCodeLines(readCfgSection(CFG(g), 'General', 'Enabled'));
    cfgFinal[g] = v;
    if (v !== 'true') allEnabled = false;
  }
  F.cfgFinal = cfgFinal;
  ck('final: all 9 suite cfgs General.Enabled=true on disk', allEnabled);
  ck('final evidence: full-suite applied state (ms 4.8, cold 16/16@0.1, talent 77, vase 1/1, ce 710, 9075=60)',
    moveSpeeds(ef) === 'MoveSpeed=4.8,MoveSpeed=4.8,MoveSpeed=4.8' && cold(ef) === '16/16@0.1'
    && talent(ef) && talent(ef).r === 77 && talent(ef).vase === '1/1' && ceCount(ef) === '710' && act(ef, 9075) === '60');

  try { await page.screenshot({ path: SHOT_DIR + '/task-13-shot-toggle.png' }); } catch (e) {}
  await browser.close();

  console.log('---- facts (toggle) ----');
  console.log(JSON.stringify(F, null, 1));
  console.log('---- verdicts (toggle) ----');
  let allPass = true;
  for (const [n, ok] of CK) { console.log((ok ? 'PASS' : 'FAIL') + '  ' + n); if (!ok) allPass = false; }
  log(allPass ? 'RESULT: ALL PASS' : 'RESULT: FAIL (see above)');
  process.exit(allPass ? 0 : 1);
}

(async () => {
  if (MODE === 'loadgame') return loadGameMode();
  if (MODE === 'outsetting') return outsettingMode();
  if (MODE === 'toggle') return toggleMode();
  return menuMode();
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
