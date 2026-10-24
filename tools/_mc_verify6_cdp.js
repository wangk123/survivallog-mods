// ModCenter Task 6 verification (live game, CDP). Connection pattern copied from
// tools/_mc_verify_cdp.js (playwright-core connectOverCDP, Root page via
// #iframe-container, all DOM work in ROOT page via page.evaluate).
//
// Phase: MAIN MENU ONLY (author save safety: no save load).
//
// Checks:
//   1 open panel via entry click -> __mcLastSnapshot arrives within 10s (MC_OPEN handled)
//   2 snapshot shape: self.ver truthy, mods.length === 9, name order per Meta,
//     linked keys (Expand.WidthMult + HeightMult etc.), ovr array on quickaction,
//     "better cabinet" enabled === true with empty groups, predisasterspeed empty groups,
//     roleunlock Plan.* fallback items present with v === true, uifit opts = [Scroll,Zoom,Off]
//   3 HOT WRITE: push MC_SET RobotSpeed speedMult -> 4 (value '4') -> MC_SET_OK event
//     within 10s -> poll evidence file via bridge (MC_EVIDENCE) until table MoveSpeed
//     reaches 9.6 (base 2.4 x 4) within budget -> write back 2 -> table back to 4.8
//   4 TOGGLE: MC_TOGGLE_MOD RobotSpeed false -> snapshot refreshed with enabled=false,
//     modcenter cfg [Disabled] persisted -> toggle true -> enabled=true, cfg restored
//   5 MC_CLOSE immediate flush: toggle-marked cfg visible on disk within 1.5s of close
//
// Evidence readback: bash polls E:/.../BepInEx/ModCenter_evidence.txt — this script only
// triggers MC_EVIDENCE and prints what it can see; final pass/fail for table values is
// computed here from a helper endpoint this script CANNOT read (file lives outside the
// WebView). So the table-value verdict is resolved by the outer bash loop reading the
// evidence file. This script exits 0 when phases 1/2/4/5 pass; phase-3 hot-effect verdict
// comes from the bash driver (mc_t6_hot.sh style loop).
//
// Usage: node tools/_mc_verify6_cdp.js   (ASCII-only file; Chinese via \u escapes)

const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

const CDP = 'http://localhost:9222';
const MAIN_HTML = '/UI/MainUI/MainUI.html';
const CONNECT_BUDGET_MS = 180000;
const MENU_WAIT_MS = 150000;
const POLL_MS = 1500;
const NAMES = [
  '\u80cc\u5305\u6269\u5bb9', // 背包扩容
  '\u67dc\u5b50\u6269\u5bb9', // 柜子扩容
  '\u4fdd\u9c9c\u589e\u5f3a', // 保鲜增强
  '\u52a8\u4f5c\u52a0\u901f', // 动作加速
  '\u66f4\u597d\u7684\u50a8\u7269\u67dc', // 更好的储物柜
  '\u89d2\u8272\u89e3\u9501', // 角色解锁
  '\u673a\u5668\u4eba\u63d0\u901f', // 机器人提速
  '\u81ea\u9009\u751f\u5b58\u89c4\u5212', // 自选生存规划
  '\u707e\u524d\u52a0\u901f', // 灾前加速
];
const ROBOT_GUID = 'com.local.survivallog.robotspeed';
const MC_CFG = 'E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/config/com.local.survivallog.modcenter.cfg';
const RS_CFG = 'E:/SteamLibrary/steamapps/common/Survival Log/BepInEx/config/com.local.survivallog.robotspeed.cfg';
const FS = require('fs');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function rootProbe(page, expr) {
  return page.evaluate('(' + expr + ')()');
}

function pushMsg(page, obj) {
  return rootProbe(page, 'function(){ window.__mcOutbox.push(' + JSON.stringify(obj) + '); return true; }');
}

function readCfgDisabled() {
  try {
    const t = FS.readFileSync(MC_CFG, 'utf8');
    const m = t.match(/\[Disabled\]([\s\S]*?)(\n\[|$)/);
    if (!m) return { section: false };
    const line = m[1].split('\n').find((l) => l.indexOf(ROBOT_GUID) !== -1);
    return { section: true, line: line ? line.trim() : null };
  } catch (e) { return { section: false, err: e.message }; }
}

function readRobotMult() {
  try {
    const t = FS.readFileSync(RS_CFG, 'utf8');
    const m = t.match(/\u79fb\u901f\u500d\u7387\s*=\s*([\d.]+)/); // 移速倍率 = x
    return m ? m[1] : null;
  } catch (e) { return null; }
}

(async () => {
  const t0 = Date.now();
  const log = (m) => console.log('[' + Math.round((Date.now() - t0) / 1000) + 's] ' + m);

  // ---- connect ----
  let browser = null;
  for (;;) {
    try { browser = await chromium.connectOverCDP(CDP); break; } catch (e) {
      if (Date.now() - t0 > CONNECT_BUDGET_MS) { log('FAIL: CDP unreachable: ' + e.message); process.exit(2); }
      await sleep(POLL_MS);
    }
  }
  log('CDP connected');

  let page = null;
  const pageDeadline = Date.now() + MENU_WAIT_MS;
  for (;;) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        let has = false;
        try { has = await p.evaluate(() => !!document.querySelector('#iframe-container')); }
        catch (e) { has = false; }
        if (has) { page = p; break; }
      }
      if (page) break;
    }
    if (page) break;
    if (Date.now() > pageDeadline) { log('FAIL: no Root page'); await browser.close(); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('Root page: ' + page.url().split('?')[0]);

  const findMainDoc = () => rootProbe(page, `function(){
    var fs = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < fs.length; i++) {
      try { var d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf('${MAIN_HTML}') !== -1) return d; } catch (e) {}
      if (fs[i].id === 'MainUI') { try { return fs[i].contentDocument; } catch (e) {} }
    }
    return null;
  }`);

  // wait for menu + entry + panel bootstrap installed
  const entryDeadline = Date.now() + MENU_WAIT_MS;
  for (;;) {
    const ok = await rootProbe(page, `function(){
      var fs = document.querySelectorAll('#iframe-container iframe');
      for (var i = 0; i < fs.length; i++) {
        try { var d = fs[i].contentDocument;
          if (d && d.location.pathname.indexOf('${MAIN_HTML}') !== -1)
            return !!(d.getElementById('__mc_entry') && window.__mcDrain && window.__mcRender); } catch (e) {}
      }
      return false;
    }`);
    if (ok) break;
    if (Date.now() > entryDeadline) { log('FAIL: entry/panel bootstrap not ready'); await browser.close(); process.exit(2); }
    await sleep(POLL_MS);
  }
  log('entry + panel bootstrap ready');

  const V = {}; const F = {};

  // ---- 1. open panel -> snapshot ----
  await rootProbe(page, `function(){
    var fs = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < fs.length; i++) {
      try { var d = fs[i].contentDocument;
        if (d && d.location.pathname.indexOf('${MAIN_HTML}') !== -1) {
          var e = d.getElementById('__mc_entry'); if (e) e.click(); return true; } } catch (e2) {}
    }
    return false;
  }`);
  let snap = null;
  const snapDeadline = Date.now() + 12000;
  for (;;) {
    snap = await rootProbe(page, 'function(){ return window.__mcLastSnapshot || null; }');
    if (snap) break;
    if (Date.now() > snapDeadline) break;
    await sleep(POLL_MS);
  }
  V.snapshotArrived = !!snap;
  F.snapshot = snap;

  // ---- 2. snapshot shape ----
  if (snap) {
    const mods = snap.mods || [];
    V.nineMods = mods.length === 9;
    V.nameOrder = NAMES.every((n, i) => mods[i] && mods[i].name === n);
    V.tpEmpty = Array.isArray(snap.tp) && snap.tp.length === 0;
    V.selfVer = !!(snap.self && snap.self.ver);

    const byName = {}; mods.forEach((m) => { byName[m.name] = m; });
    const bp = byName['\u80cc\u5305\u6269\u5bb9']; // 背包扩容
    const cb = byName['\u67dc\u5b50\u6269\u5bb9']; // 柜子扩容
    const qa = byName['\u52a8\u4f5c\u52a0\u901f']; // 动作加速
    const ce = byName['\u66f4\u597d\u7684\u50a8\u7269\u67dc']; // 更好的储物柜
    const ru = byName['\u89d2\u8272\u89e3\u9501']; // 角色解锁
    const rs = byName['\u673a\u5668\u4eba\u63d0\u901f']; // 机器人提速
    const pd = byName['\u707e\u524d\u52a0\u901f']; // 灾前加速
    F.details = {};

    if (bp) {
      const items = bp.groups[0].items;
      F.details.backpack = items.map((i) => i.k);
      V.backpackLinked = items[0] && items[0].k === 'Expand.WidthMult + HeightMult'
        && items[0].t === 'float' && typeof items[0].v === 'number'
        && items[0].min === 1 && items[0].max === 3 && items[0].st === 0.1 && items[0].b === 'now'
        && !!items[0].note && items[0].note.indexOf('\u5f53\u524d \u5bbd') === 0; // 当前 宽
      V.backpackBurden = items[1] && items[1].k === 'Expand.BurdenMult' && items[1].t === 'int' && items[1].b === 'reload';
    } else { V.backpackLinked = V.backpackBurden = false; }

    if (cb) {
      const ks = cb.groups[0].items.map((i) => i.k);
      F.details.cabinet = ks;
      V.cabinetThreeLinked = ks[0] === 'Expand.WidthMult + HeightMult'
        && ks[1] === 'Expand.FridgeWidthMult + FridgeHeightMult'
        && ks[2] === 'Expand.VehicleWidthMult + VehicleHeightMult';
      const fit = cb.groups[1] && cb.groups[1].items;
      V.uifit = !!fit && fit[0].k === 'UiFit.Enabled' && fit[0].t === 'bool'
        && fit[1].k === 'UiFit.Mode' && fit[1].t === 'sel'
        && JSON.stringify(fit[1].opts) === JSON.stringify(['Scroll', 'Zoom', 'Off']);
      F.details.uifitModeV = fit ? fit[1].v : null;
    } else { V.cabinetThreeLinked = V.uifit = false; }

    if (qa) {
      F.details.qaOvr = qa.ovr;
      V.qaOvrArray = Array.isArray(qa.ovr) && qa.groups[0].items.length === 5
        && qa.groups[0].items.every((i) => i.t === 'int' && i.min === 0 && i.max === 5000);
    } else V.qaOvrArray = false;

    V.ceToggleOnly = !!ce && ce.enabled === true && Array.isArray(ce.groups) && ce.groups.length === 0;
    V.pdToggleOnly = !!pd && Array.isArray(pd.groups) && pd.groups.length === 0;
    if (ru) {
      const items = ru.groups.flatMap((g) => g.items);
      const plan = items.find((i) => i.k === 'Plan.ClearPool');
      const vase = items.find((i) => i.k === 'Plan.VaseRecipe');
      F.details.roleItems = items.length;
      V.roleFallback = items.length === 6 && plan && plan.v === true && vase && vase.v === true;
    } else V.roleFallback = false;
    if (rs) {
      const items = rs.groups[0].items;
      F.details.rsCount = items.length;
      const move = items.find((i) => i.k === '\u673a\u5668\u4eba\u63d0\u901f.\u79fb\u901f\u500d\u7387');
      F.details.rsMove = move ? { v: move.v, min: move.min, max: move.max } : null;
      V.robotSeven = items.length === 7 && !!move && move.t === 'float' && move.min === 0.5 && move.max === 10;
    } else V.robotSeven = false;
  } else {
    ['nineMods', 'nameOrder', 'tpEmpty', 'selfVer', 'backpackLinked', 'backpackBurden', 'cabinetThreeLinked',
      'uifit', 'qaOvrArray', 'ceToggleOnly', 'pdToggleOnly', 'roleFallback', 'robotSeven']
      .forEach((k) => { V[k] = false; });
  }

  // ---- 3. HOT WRITE: MC_SET RobotSpeed speedMult -> '4' ----
  F.robotMultBefore = readRobotMult();
  const setT0 = Date.now();
  await pushMsg(page, { type: 'MC_SET', guid: 'RobotSpeed', section: '\u673a\u5668\u4eba\u63d0\u901f', key: '\u79fb\u901f\u500d\u7387', value: '4' });
  let evOk = null; let evFail = null;
  const evDeadline = Date.now() + 12000;
  for (;;) {
    const ev = await rootProbe(page, 'function(){ return window.__mcLastEvent || null; }');
    if (ev && (ev.type === 'MC_SET_OK' || ev.type === 'MC_SET_FAIL')) { evOk = ev; break; }
    if (Date.now() > evDeadline) break;
    await sleep(POLL_MS);
  }
  F.setEvent = evOk;
  V.setOkEvent = !!(evOk && evOk.type === 'MC_SET_OK' && evOk.badge === 'now'
    && evOk.k === '\u673a\u5668\u4eba\u63d0\u901f.\u79fb\u901f\u500d\u7387');
  V.setFailInstead = !!(evOk && evOk.type === 'MC_SET_FAIL');

  // hot-effect polling: MC_EVIDENCE each round, bash-side file check happens after script
  // here we push evidence requests and report what the driver should expect
  log('MC_SET pushed; waiting for table hot-effect window (evidence via MC_EVIDENCE)...');
  for (let i = 0; i < 5; i++) {
    await pushMsg(page, { type: 'MC_EVIDENCE', tag: 'after_set4_' + i });
    await sleep(1500);
  }
  F.robotMultMid = readRobotMult();

  // write back original (clear stale event first so the wait is meaningful)
  const orig = F.robotMultBefore || '2';
  await rootProbe(page, 'function(){ window.__mcLastEvent = null; return true; }');
  await pushMsg(page, { type: 'MC_SET', guid: 'RobotSpeed', section: '\u673a\u5668\u4eba\u63d0\u901f', key: '\u79fb\u901f\u500d\u7387', value: String(orig) });
  const backDeadline = Date.now() + 10000;
  for (;;) {
    const ev = await rootProbe(page, 'function(){ return window.__mcLastEvent || null; }');
    if (ev && ev.type === 'MC_SET_OK') break;
    if (Date.now() > backDeadline) break;
    await sleep(POLL_MS);
  }
  for (let i = 0; i < 5; i++) {
    await pushMsg(page, { type: 'MC_EVIDENCE', tag: 'after_writeback_' + i });
    await sleep(1500);
  }
  F.robotMultAfter = readRobotMult();
  F.setElapsedMs = Date.now() - setT0;

  // ---- 4. TOGGLE round ----
  await pushMsg(page, { type: 'MC_TOGGLE_MOD', guid: 'RobotSpeed', enabled: false });
  let toggled = null;
  const tgDeadline = Date.now() + 12000;
  for (;;) {
    const s = await rootProbe(page, 'function(){ return window.__mcLastSnapshot || null; }');
    const m = s && s.mods && s.mods.find((x) => x.guid === ROBOT_GUID);
    if (m && m.enabled === false) { toggled = m; break; }
    if (Date.now() > tgDeadline) break;
    await sleep(POLL_MS);
  }
  V.toggleOffSnapshot = !!toggled;
  await sleep(4500); // 3s debounce + margin
  F.cfgDisabledAfterOff = readCfgDisabled();
  V.toggleOffPersisted = !!(F.cfgDisabledAfterOff.line
    && F.cfgDisabledAfterOff.line.indexOf(ROBOT_GUID + ' = true') !== -1);

  await pushMsg(page, { type: 'MC_TOGGLE_MOD', guid: 'RobotSpeed', enabled: true });
  let restored = false;
  const rtDeadline = Date.now() + 12000;
  for (;;) {
    const s = await rootProbe(page, 'function(){ return window.__mcLastSnapshot || null; }');
    const m = s && s.mods && s.mods.find((x) => x.guid === ROBOT_GUID);
    if (m && m.enabled === true) { restored = true; break; }
    if (Date.now() > rtDeadline) break;
    await sleep(POLL_MS);
  }
  V.toggleOnSnapshot = restored;

  // ---- 5. MC_CLOSE immediate flush ----
  // first wait until phase-4's toggle-on has flushed to disk (= false), so the
  // flush signal below can only come from the CLOSE batch itself;
  // then push toggle-off AND close in one batch: Pump processes both in the same pass
  // (SetModEnabled marks dirty, MC_CLOSE flushes) — file must flip to true within ~4s,
  // the 3s-throttle path alone would need drain(2s)+3s >= 5s, so <=4.2s proves CLOSE did it.
  const settleDeadline = Date.now() + 10000;
  for (;;) {
    const c = readCfgDisabled();
    if (c.line && c.line.indexOf(ROBOT_GUID + ' = false') !== -1) break;
    if (Date.now() > settleDeadline) break;
    await sleep(300);
  }
  const closeT0 = Date.now();
  await pushMsg(page, { type: 'MC_TOGGLE_MOD', guid: 'RobotSpeed', enabled: false });
  await pushMsg(page, { type: 'MC_CLOSE' });
  let flushed = false; let flushMs = -1;
  const flushDeadline = Date.now() + 8000;
  for (;;) {
    const c = readCfgDisabled();
    if (c.line && c.line.indexOf(ROBOT_GUID + ' = true') !== -1) { flushed = true; flushMs = Date.now() - closeT0; break; }
    if (Date.now() > flushDeadline) break;
    await sleep(300);
  }
  F.closeFlushMs = flushMs;
  V.closeFlushImmediate = flushed && flushMs >= 0 && flushMs <= 4200;

  // restore enabled state for cleanliness
  await pushMsg(page, { type: 'MC_TOGGLE_MOD', guid: 'RobotSpeed', enabled: true });
  await pushMsg(page, { type: 'MC_CLOSE' });
  await sleep(3000);
  F.cfgDisabledFinal = readCfgDisabled();
  F.robotMultFinal = readRobotMult();

  try { await page.screenshot({ path: 'E:/Game/Mod/mod_stack/.superpowers/sdd/2026-10-24-modcenter/task-6-shot-panel.png' }); } catch (e) {}

  await browser.close();

  console.log('---- facts ----');
  console.log(JSON.stringify({
    snapshotSelf: F.snapshot && F.snapshot.self,
    modNames: F.snapshot && F.snapshot.mods && F.snapshot.mods.map((m) => m.name + '/' + m.en + '/' + (m.enabled ? 'on' : 'off')),
    details: F.details,
    robotMult: { before: F.robotMultBefore, mid: F.robotMultMid, after: F.robotMultAfter, final: F.robotMultFinal },
    setEvent: F.setEvent,
    cfgDisabledAfterOff: F.cfgDisabledAfterOff,
    cfgDisabledFinal: F.cfgDisabledFinal,
    closeFlushMs: F.closeFlushMs,
    setElapsedMs: F.setElapsedMs,
  }, null, 1));
  console.log('---- verdicts ----');
  const checks = [
    ['1 MC_OPEN -> __mcLastSnapshot arrived', V.snapshotArrived],
    ['2 self.ver present', V.selfVer],
    ['2 mods.length === 9', V.nineMods],
    ['2 name order matches Meta', V.nameOrder],
    ['2 tp empty placeholder', V.tpEmpty],
    ['2 backpack linked key + float range + dyn note', V.backpackLinked],
    ['2 backpack burden int/reload', V.backpackBurden],
    ['2 cabinet three linked keys', V.cabinetThreeLinked],
    ['2 UiFit bool + Mode sel opts [Scroll,Zoom,Off]', V.uifit],
    ['2 quickaction ovr array + 5 int items', V.qaOvrArray],
    ['2 cabinet-everywhere enabled=true, toggle-only', V.ceToggleOnly],
    ['2 predisasterspeed toggle-only', V.pdToggleOnly],
    ['2 roleunlock 6 items incl Plan.* fallback true', V.roleFallback],
    ['2 robotspeed 7 items, move float 0.5-10', V.robotSeven],
    ['3 MC_SET -> MC_SET_OK(k,badge=now)', V.setOkEvent],
    ['3 (no MC_SET_FAIL)', !V.setFailInstead],
    ['4 toggle off reflected in new snapshot', V.toggleOffSnapshot],
    ['4 [Disabled] persisted after debounce', V.toggleOffPersisted],
    ['4 toggle on reflected in new snapshot', V.toggleOnSnapshot],
    ['5 MC_CLOSE flushes cfg immediately', V.closeFlushImmediate],
  ];
  let allPass = true;
  for (const [name, ok] of checks) {
    console.log((ok ? 'PASS' : 'FAIL') + '  ' + name);
    if (!ok) allPass = false;
  }
  log(allPass ? 'RESULT: ALL PASS (hot-effect table evidence: check ModCenter_evidence.txt via driver)'
    : 'RESULT: FAIL (see above)');
  process.exit(allPass ? 0 : 1);
})().catch((e) => {
  console.error('ERROR: ' + (e && e.stack || e));
  process.exit(1);
});
