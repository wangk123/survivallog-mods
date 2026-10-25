// 封面素材采集（N网封面管线）：离线渲染游戏 WebUI 页 + 注入脚本 → 整页截图 + 特写元素截图
// 用法：node tools/_shot_cov.js <inject.js> <pageRelPath> <outPrefix> [variant]
//   inject.js 放 tools/_inject/，返回 {zoom:'#sel'} 时额外截该元素特写 _zoom.png
//   variant 会先写成 window.__COV_VARIANT 供注入脚本分支（vanilla / mod）
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const injectFile = process.argv[2];
const pagePath = process.argv[3];
const outPrefix = process.argv[4];
const variant = process.argv[5] || '';
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const server = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, ''));
  fs.readFile(f, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    const ext = path.extname(f).toLowerCase();
    const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
      '.png': 'image/png', '.jpg': 'image/jpeg', '.json': 'application/json', '.woff2': 'font/woff2' }[ext] || 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': mime });
    res.end(data);
  });
});

(async () => {
  const injectCode = fs.readFileSync(path.join(__dirname, '_inject', injectFile), 'utf8');
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: 1536, height: 864 } });
  const errs = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 140)); });
  page.on('pageerror', e => errs.push('PAGEERR ' + String(e).slice(0, 140)));
  await page.goto('http://127.0.0.1:' + port + '/' + pagePath, { waitUntil: 'load', timeout: 20000 });
  await page.waitForTimeout(900);
  if (variant) await page.evaluate(v => { window.__COV_VARIANT = v; }, variant);
  const report = await page.evaluate(new Function('return async () => {' + injectCode + '}')());
  await page.waitForTimeout(1100); // 等入场动画收尾
  await page.screenshot({ path: outPrefix + '_full.png' });
  let zoomInfo = null;
  if (report && (report.zoom || report.zoomClip)) {
    if (report.zoomClip) {
      const [x, y, w, h] = report.zoomClip;
      await page.screenshot({ path: outPrefix + '_zoom.png', clip: { x, y, width: w, height: h } });
      zoomInfo = { clip: report.zoomClip };
    } else {
      const el = page.locator(report.zoom).first();
      if (await el.count()) {
        const bb = await el.boundingBox();
        await el.screenshot({ path: outPrefix + '_zoom.png' });
        zoomInfo = { sel: report.zoom, box: bb };
      } else { zoomInfo = { sel: report.zoom, missing: true }; }
    }
  }
  await browser.close(); server.close();
  console.log('OK ' + outPrefix + '_full.png  report=' + JSON.stringify(report) +
    (zoomInfo ? '  zoom=' + JSON.stringify(zoomInfo) : '') +
    (errs.length ? '\nERRS:\n' + errs.slice(0, 6).join('\n') : '  (no console errors)'));
})().catch(e => { console.error(e); process.exit(1); });
