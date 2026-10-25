// 采集 PreDisasterSpeed 封面素材（N网 R4 前后对比）：CoreUI0（灾前 HUD）
//   vanilla：官方 HUD 原样（无倍速条）——灾前 1 倍干等的痛点
//   mod    ：真 page.js（SurvivalLog.PreDisasterSpeed/page.js 源文件）经 addScriptTag 注入，
//            __PdsBoot({s1:2,s2:3}) 推真实倍率 → mount() 落官方位（.action-row 尾部），
//            再走官方消息 WebUI_CoreUI0_TimeScaleMsg{timeScale:3} 触发 page.js 高亮 ×3 档（金色）。
// HUD 状态：两侧同一组官方消息（BtnActiveMsg/CountdownMsg/GoldMsg/ProgressMsg）把按钮/倒计时/
//   金钱/进度条推到真实可见态——与实机同一条数据通路，两侧同体系，唯一差异=五键条。
// 宿主用 iframe 包装页（对齐实机 CoreUI0 为 Root 内 iframe；body 透明，wrapper 出暗色场景底）。
// 用法：node tools/_shot_pds.js <vanilla|mod> <outPrefix>
//   产出 <outPrefix>_full.png；mod 额外 <outPrefix>_zoom.png（#pdsSpeed 五键条+×3 高亮+悬停 tooltip）
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const MODDIR = path.join(__dirname, '..', 'SurvivalLog.PreDisasterSpeed');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const variant = process.argv[2] || 'vanilla';
const outPrefix = process.argv[3] || path.join(__dirname, '..', 'gamekb', 'webui_shots', 'pds_' + variant);
const PAGE = 'UI/CoreUI0/CoreUI0.html';
// 暗色场景底（HUD 背后 3D 场景的离线替身，vanilla/mod 两侧同一张）
const WRAP_BG = 'radial-gradient(1100px 750px at 55% 45%, #2d3328 0%, #1c211a 55%, #0e110c 100%)';

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

// HUD 推送：官方消息通路（webui-core registerHandlers 的 message 分发），两侧同推
async function pushHud(frame) {
  await frame.evaluate(() => {
    const post = (type, data) => window.postMessage({ type: type, data: data }, '*');
    post('WebUI_CoreUI0_BtnActiveMsg', {
      mapActive: true, bagActive: true, survivalLogActive: true, goHomeActive: true,
      finishedPreparingActive: true, phoneActive: true, checklistEntryActive: true,
      cameraModeActive: true, planModeBtnActive: true, isPlanMode: false
    });
    post('WebUI_CoreUI0_CountdownMsg', { displaySeconds: 9300, decreasePerSecond: 1 });
    post('WebUI_CoreUI0_GoldMsg', { gold: 1280 });
    post('WebUI_CoreUI0_ProgressMsg', { progress: 63, progressSpeed: 0.1, color: 'green' });
  });
}

(async () => {
  const pageJs = fs.readFileSync(path.join(MODDIR, 'page.js'), 'utf8');
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const context = await browser.newContext({ viewport: { width: 1536, height: 864 }, deviceScaleFactor: 2 });
  const page = await context.newPage();
  const errs = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 140)); });
  page.on('pageerror', e => errs.push('PAGEERR ' + String(e).slice(0, 140)));
  await page.goto('http://127.0.0.1:' + port + '/__wrap.html', { waitUntil: 'load', timeout: 20000 });

  const frame = page.frames().find(f => f.url().indexOf('CoreUI0.html') !== -1);
  if (!frame) throw new Error('CoreUI0 frame not found');
  await frame.waitForSelector('.bottom-left .action-row', { state: 'attached', timeout: 12000 });
  await frame.waitForTimeout(600); // Vue mount 收尾（handler 已注册，再推 HUD）
  await pushHud(frame);
  // HUD 推送后按钮 v-show 全开，action-row 才有可见内容（此前空行 0 高判 hidden）
  await frame.waitForSelector('.action-row .text-btn', { state: 'visible', timeout: 8000 });
  await frame.waitForTimeout(1200); // 倒计时引擎走 1-2 拍 + 布局稳定

  let report;
  if (variant === 'mod') {
    await frame.addScriptTag({ content: pageJs });           // 真注入：安装守卫+mount 官方位
    await frame.evaluate(() => {
      window.__PdsBoot({ s1: 2, s2: 3 });                     // C# 推真实倍率（二倍=2，真三倍=3）
      window.postMessage({ type: 'WebUI_CoreUI0_TimeScaleMsg', data: { timeScale: 3 } }, '*'); // 官方高亮通路 → ×3 档
    });
    await frame.waitForSelector('#pdsSpeed', { timeout: 6000 });
    await frame.waitForTimeout(400);
    report = await frame.evaluate(() => {
      const d = document;
      const strip = d.getElementById('pdsSpeed');
      const active = strip ? strip.querySelector('.speed-btn.active') : null;
      const host = strip ? strip.parentElement : null;
      return {
        installed: !!window.__PdsInstalled,
        booted: true,
        strip: !!strip,
        hostClass: host ? host.className : null,
        keys: strip ? strip.querySelectorAll('.speed-btn').length : 0,
        gearClasses: strip ? [...strip.querySelectorAll('.speed-btn')].map(b => b.className.replace('speed-btn ', '')) : [],
        activeGear: active ? active.className.replace('speed-btn ', '') : null,
        actionRowBtns: d.querySelectorAll('.action-row > *').length,
        hud: {
          timer: (d.querySelector('.timer-box span:last-child') || {}).textContent,
          gold: (d.querySelector('.money-display span') || {}).textContent,
          finishBtn: (d.querySelector('.text-btn') || {}).textContent,
          verticalTools: d.querySelectorAll('.vertical-tools > div').length
        }
      };
    });
  } else {
    report = await frame.evaluate(() => {
      const d = document;
      return {
        variant: 'vanilla',
        noStrip: !d.getElementById('pdsSpeed'),
        actionRow: !!d.querySelector('.action-row'),
        actionRowBtns: d.querySelectorAll('.action-row > div').length,
        hud: {
          timer: (d.querySelector('.timer-box span:last-child') || {}).textContent,
          gold: (d.querySelector('.money-display span') || {}).textContent,
          finishBtn: (d.querySelector('.text-btn') || {}).textContent,
          verticalTools: d.querySelectorAll('.vertical-tools > div').length
        }
      };
    });
  }

  await page.screenshot({ path: outPrefix + '_full.png' });

  let zoomInfo = null;
  if (variant === 'mod') {
    // 特写：#pdsSpeed 五键条——悬停 ×3 键带出 tooltip「三倍」，clip 含 tooltip 与边距
    const gear3 = frame.locator('#pdsSpeed .pds-gear-3');
    await gear3.hover();
    await frame.waitForTimeout(350);
    const box = await frame.evaluate(() => {
      const r = document.getElementById('pdsSpeed').getBoundingClientRect();
      const tip = document.querySelector('#pdsSpeed .speed-tooltip');
      const tr = tip ? tip.getBoundingClientRect() : null;
      const x0 = Math.min(r.left, tr ? tr.left : r.left) - 14;
      const y0 = Math.min(r.top, tr ? tr.top : r.top) - 12;
      const x1 = Math.max(r.right, tr ? tr.right : r.right) + 14;
      const y1 = r.bottom + 12;
      return { x: Math.round(x0), y: Math.round(y0), w: Math.round(x1 - x0), h: Math.round(y1 - y0) };
    });
    await page.screenshot({ path: outPrefix + '_zoom.png', clip: { x: box.x, y: box.y, width: box.w, height: box.h } });
    zoomInfo = { stripBox: box };
  }
  await browser.close(); server.close();
  console.log('OK ' + outPrefix + '_full.png  report=' + JSON.stringify(report)
    + (zoomInfo ? '  zoom=' + JSON.stringify(zoomInfo) : '')
    + (missing.length ? '\n404:\n' + missing.join('\n') : '  (no 404)')
    + (errs.length ? '\nERRS:\n' + errs.slice(0, 6).join('\n') : '  (no console errors)'));
})().catch(e => { console.error(e); process.exit(1); });
