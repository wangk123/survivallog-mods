// 采集 ModCenter 封面素材（N网 R4 前后对比）：MainUI 官方主菜单
//   vanilla：主菜单原样（官方 .menu 体系，离线 Vue 默认态渲染）
//   mod    ：.menu 尾部注入「MOD 管理」入口（MenuEntryInjector.EntryJs 同款克隆+蓝色 accent）
//            + 真 panel.js 面板（SurvivalLog.ModCenter/panel.js 内嵌资源源文件）经 addScriptTag 注入，
//              __mcRender(合成快照) + __mcOpen(document) 打开（面板 overlay 全屏盖在主菜单上）。
// 快照：ver 取 version.json suite；mods 三条真实条目（RobotSpeed/BackpackExpand/PlanPick），
//       节键/展示键/默认值/范围照 Meta.cs 逐字（v=默认 Def，min/max/st/b=Badge 全同源）；tp 空数组。
// 宿主用 iframe 包装页（对齐实机：面板挂在 MainUI iframe 文档、Root 包装），iframe 满幅、
// wrapper 只出暗色场景底（游戏内主菜单 body 透明，实机背后是 3D 场景）。两侧同 wrapper。
// 用法：node tools/_shot_mc.js <vanilla|mod> <outPrefix>
//   产出 <outPrefix>_full.png；mod 额外 <outPrefix>_zoom.png（面板 mod 卡片区=组头+开关项+滑条项）
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const MODDIR = path.join(__dirname, '..', 'SurvivalLog.ModCenter');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const variant = process.argv[2] || 'vanilla';
const outPrefix = process.argv[3] || path.join(__dirname, '..', 'gamekb', 'webui_shots', 'mc_' + variant);
const PAGE = 'UI/MainUI/MainUI.html';
// 暗色场景底（菜单背后 3D 场景的离线替身，vanilla/mod 两侧同一张，保证同体系对比）
const WRAP_BG = 'radial-gradient(1200px 800px at 62% 38%, #3a3226 0%, #241f18 55%, #12100c 100%)';

const missing = [];
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/' || url === '/__wrap.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end('<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;padding:0;'
      + 'width:100%;height:100%;overflow:hidden;background:' + WRAP_BG + ';}'
      + 'iframe{border:0;display:block;width:100%;height:100%;}</style></head><body>'
      + '<iframe id="frm" src="/wui/' + PAGE + '"></iframe></body></html>');
    return;
  }
  if (url === '/favicon.ico') { res.writeHead(204); res.end(); return; }
  if (url.indexOf('/wui/') !== 0) { res.writeHead(404); res.end(); return; }
  let rel = path.normalize(url.slice(5).replace(/\\/g, '/'));
  const f = path.join(ROOT, rel);
  if (f.indexOf(ROOT) !== 0) { res.writeHead(403); res.end(); return; }
  fs.readFile(f, (err, data) => {
    if (err) { missing.push(url); res.writeHead(404); res.end(); return; }
    const ext = path.extname(f).toLowerCase();
    const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
      '.png': 'image/png', '.jpg': 'image/jpeg', '.json': 'application/json', '.woff2': 'font/woff2' }[ext] || 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': mime });
    res.end(data);
  });
});

// ---- 合成快照（照 Meta.cs 逐字：节键/展示键/默认值/范围/步进/徽标/备注；ver=version.json suite） ----
const VER = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'version.json'), 'utf8').replace(/^\uFEFF/, '')).suite;
function num(k, l, v, min, max, st, b, note) {
  const it = { k, l, t: 'float', v, min, max, st, b };
  if (note) it.note = note;
  return it;
}
const MULTS = ['移速倍率', '充电速率倍率', '续航倍率', '耗电降低倍率', '动作时长倍率'];
const SNAPSHOT = {
  self: { ver: VER },
  mods: [
    {
      guid: 'com.local.survivallog.robotspeed',
      name: '机器人提速', en: 'RobotSpeed',
      desc: '机器人移速/充电/续航/耗电/动作时长/货舱六维倍率 + 与手动动作对齐。',
      enabled: true,
      groups: [{
        title: '机器人提速',
        items: [
          { k: '机器人提速.动作速度对齐手动', l: '动作速度对齐手动', t: 'bool', v: true, b: 'now' },
          num('机器人提速.移速倍率', '移速倍率', 2, 0.5, 10, 0.5, 'now'),
          num('机器人提速.充电速率倍率', '充电速率倍率', 2, 0.5, 10, 0.5, 'now'),
          num('机器人提速.续航倍率', '续航倍率', 2, 0.5, 10, 0.5, 'now'),
          num('机器人提速.耗电降低倍率', '耗电降低倍率', 2, 0.5, 10, 0.5, 'now'),
          num('机器人提速.动作时长倍率', '动作时长倍率', 2, 0.5, 10, 0.5, 'now'),
          num('机器人提速.货舱格数倍率', '货舱格数倍率', 2, 0.5, 10, 0.5, 'now', '面板重开即见')
        ]
      }]
    },
    {
      guid: 'com.local.survivallog.backpackexpand',
      name: '背包扩容', en: 'BackpackExpand',
      desc: '宽度/基础高度/负重三个明确数值统一应用全部背包（0=恢复官方原值）；天赋『背包加一行』照常每级+1；热改即时生效。',
      enabled: true,
      groups: [
        {
          title: '扩容数值（0=恢复官方原值）',
          items: [
            { k: 'Expand.Width', l: '背包宽度', t: 'int', v: 11, min: 0, max: 30, st: 1, b: 'now', note: '全部背包统一列数（原生最大 10）' },
            { k: 'Expand.Height', l: '基础高度', t: 'int', v: 10, min: 0, max: 30, st: 1, b: 'now', note: '初始背包与登山包行数；天赋『背包加一行』每级自动 +1，照常生效' },
            { k: 'Expand.Burden', l: '负重上限', t: 'int', v: 48000, min: 0, max: 150000, st: 1000, b: 'reload', note: '全部背包统一基础值，天赋加成照常叠加；读档后体现' }
          ]
        },
        {
          title: '防呆上限（0=不封顶）',
          items: [
            { k: 'Global.MaxWidth', l: '宽度上限', t: 'int', v: 16, min: 0, max: 30, st: 1, b: 'now', note: 'Width 超出即夹到此值；过宽挤压背包页右侧栏' },
            { k: 'Global.MaxHeight', l: '高度上限', t: 'int', v: 18, min: 0, max: 30, st: 1, b: 'now' }
          ]
        }
      ]
    },
    {
      guid: 'com.local.survivallog.planpick',
      name: '自选生存规划', en: 'PlanPick',
      desc: '生存规划改自选：解锁日全表候选 + 搜索 + 键盘；整数百分比加成。',
      enabled: true,
      groups: [
        { title: '花费', items: [{ k: 'Cost.CostMultiplierPercent', l: '加成百分比（100=原值）', t: 'int', v: 200, min: 100, max: 300, st: 5, b: 'now' }] },
        { title: '列表', items: [
          { k: 'List.ShowLocked', l: '展示未解锁规划（置灰+原因）', t: 'bool', v: true, b: 'now' },
          { k: 'List.ShowUnpooled', l: '展示不入池规划', t: 'bool', v: true, b: 'now' }
        ] }
      ]
    }
  ],
  tp: []
};

(async () => {
  const panelJs = fs.readFileSync(path.join(MODDIR, 'panel.js'), 'utf8');
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const context = await browser.newContext({ viewport: { width: 1536, height: 864 }, deviceScaleFactor: 2 });
  const page = await context.newPage();
  const errs = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 140)); });
  page.on('pageerror', e => errs.push('PAGEERR ' + String(e).slice(0, 140)));
  await page.goto('http://127.0.0.1:' + port + '/__wrap.html', { waitUntil: 'load', timeout: 20000 });

  const frame = page.frames().find(f => f.url().indexOf('MainUI.html') !== -1);
  if (!frame) throw new Error('MainUI frame not found');
  await frame.waitForSelector('.menu .menu-item', { timeout: 12000 });
  await frame.waitForTimeout(1800); // 入场动画收尾（enter-delay 至 .83s + 0.45s）

  let report;
  if (variant === 'mod') {
    // 1) 入口按钮：MenuEntryInjector.EntryJs 同款（克隆官方 .menu-item，蓝 accent，.menu 尾部）
    await frame.evaluate(() => {
      const doc = document;
      const menu = doc.querySelector('#app .container .menu') || doc.querySelector('.menu');
      if (!menu || doc.getElementById('__mc_entry')) return;
      const st = doc.createElement('style');
      st.id = '__mc_entry_style';
      st.textContent = '#__mc_entry{background:none;border:0;font:inherit;color:inherit;appearance:none;outline:none;}'
        + '#__mc_entry .bar{background:linear-gradient(180deg,#7fb8e8,#2f6ea8);box-shadow:2px 2px 5px rgba(0,0,30,0.8);}'
        + '#__mc_entry:hover .bar{background:linear-gradient(180deg,#a8d4ff,#5aa8ff);}'
        + '#__mc_entry .menu-text{color:#a9cdf5;}'
        + '#__mc_entry:hover .menu-text{color:#d6ecff;}'
        + '#__mc_entry .menu-text::after{background:linear-gradient(90deg,#5aa8ff,rgba(90,168,255,0.15));}';
      (doc.head || doc.documentElement).appendChild(st);
      const b = doc.createElement('button');
      b.id = '__mc_entry'; b.className = 'menu-item'; b.type = 'button';
      b.setAttribute('data-interactive', '');
      b.setAttribute('style', '--enter-delay:.83s');
      const bar = doc.createElement('div'); bar.className = 'bar';
      const tx = doc.createElement('div'); tx.className = 'menu-text'; tx.textContent = 'MOD 管理';
      b.appendChild(bar); b.appendChild(tx);
      menu.appendChild(b);
    });
    // 2) 真 panel.js（内嵌资源源文件）注入 + 快照推送 + 打开面板
    await frame.addScriptTag({ content: panelJs });
    await frame.evaluate(snap => {
      window.__mcRender(snap);
      window.__mcOpen(document);
    }, SNAPSHOT);
    await frame.waitForTimeout(500);
    report = await frame.evaluate(() => {
      const d = document;
      const grp = d.querySelector('#__mc_cfg .__mc-grp');
      const h3 = grp && grp.querySelector('h3');
      const items = grp ? grp.querySelectorAll('.__mc-item') : [];
      const boxes = [];
      if (h3) { const r = h3.getBoundingClientRect(); boxes.push(r); }
      for (let i = 0; i < Math.min(3, items.length); i++) boxes.push(items[i].getBoundingClientRect());
      const clip = boxes.length ? {
        x: Math.round(Math.min.apply(null, boxes.map(b => b.left)) - 16),
        y: Math.round(Math.min.apply(null, boxes.map(b => b.top)) - 12),
        w: Math.round(Math.max.apply(null, boxes.map(b => b.right)) - Math.min.apply(null, boxes.map(b => b.left)) + 32),
        h: Math.round(Math.max.apply(null, boxes.map(b => b.bottom)) - Math.min.apply(null, boxes.map(b => b.top)) + 24)
      } : null;
      return {
        installed: !!window.__McInstalled,
        entryBtn: !!d.getElementById('__mc_entry'),
        entryText: (d.querySelector('#__mc_entry .menu-text') || {}).textContent || null,
        menuItems: d.querySelectorAll('.menu .menu-item').length,
        overlay: !!d.getElementById('__mc_overlay'),
        panelTitle: (d.querySelector('#__mc_overlay .__mc-title') || {}).textContent || null,
        panelSub: (d.querySelector('#__mc_overlay .__mc-sub') || {}).textContent || null,
        sideRows: d.querySelectorAll('#__mc_side .__mc-mod-row').length,
        sideMods: [...d.querySelectorAll('#__mc_side .__mc-cat')].map(e => e.textContent.trim()),
        cfgMod: (d.querySelector('#__mc_cfg .__mc-mod-title h2') || {}).textContent || null,
        cfgItems: d.querySelectorAll('#__mc_cfg .__mc-item').length,
        sliders: d.querySelectorAll('#__mc_cfg input[type=range]').length,
        switches: d.querySelectorAll('#__mc_cfg .__mc-mini-sw').length,
        badges: d.querySelectorAll('#__mc_cfg .__mc-badge').length,
        zoomClip: clip
      };
    });
  } else {
    report = await frame.evaluate(() => {
      const d = document;
      const texts = [...d.querySelectorAll('.menu .menu-item .menu-text')].map(e => e.textContent.trim());
      return {
        variant: 'vanilla',
        menu: !!d.querySelector('.menu'),
        menuItems: d.querySelectorAll('.menu .menu-item').length,
        menuTexts: texts,
        noEntry: !d.getElementById('__mc_entry') && !d.getElementById('__mc_overlay'),
        mainTitle: (d.querySelector('.main-title') || {}).textContent || null
      };
    });
  }

  await page.screenshot({ path: outPrefix + '_full.png' });
  let zoomInfo = null;
  if (variant === 'mod' && report.zoomClip) {
    const c = report.zoomClip;
    await page.screenshot({ path: outPrefix + '_zoom.png', clip: { x: c.x, y: c.y, width: c.w, height: c.h } });
    zoomInfo = { clip: [c.x, c.y, c.w, c.h] };
  }
  await browser.close(); server.close();
  console.log('OK ' + outPrefix + '_full.png  report=' + JSON.stringify(report)
    + (zoomInfo ? '  zoom=' + JSON.stringify(zoomInfo) : '')
    + (missing.length ? '\n404:\n' + missing.join('\n') : '  (no 404)')
    + (errs.length ? '\nERRS:\n' + errs.slice(0, 6).join('\n') : '  (no console errors)'));
})().catch(e => { console.error(e); process.exit(1); });
