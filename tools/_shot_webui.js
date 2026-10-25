// 独立渲染游戏 WebUI 页面并截图（探明离线渲染效果 + 控制台报错）
// 用法：node tools/_shot_webui.js <pageRelPath> <out.png> [w] [h] [waitMs]
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const pagePath = process.argv[2];
const out = process.argv[3];
const W = parseInt(process.argv[4] || '1536', 10);
const H = parseInt(process.argv[5] || '864', 10);
const WAIT = parseInt(process.argv[6] || '1200', 10);
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const server = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, ''));
  fs.readFile(f, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    const ext = path.extname(f).toLowerCase();
    const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
      '.png': 'image/png', '.jpg': 'image/jpeg', '.woff2': 'font/woff2', '.json': 'application/json' }[ext] || 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': mime });
    res.end(data);
  });
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: W, height: H } });
  const errs = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 120)); });
  page.on('pageerror', e => errs.push('PAGEERR ' + String(e).slice(0, 120)));
  await page.goto('http://127.0.0.1:' + port + '/' + pagePath, { waitUntil: 'load', timeout: 20000 }).catch(e => errs.push('GOTO ' + e.message.slice(0, 100)));
  await page.waitForTimeout(WAIT);
  const count = await page.evaluate(() => document.querySelectorAll('*').length);
  await page.screenshot({ path: out });
  await browser.close(); server.close();
  console.log('OK ' + out + '  domNodes=' + count + (errs.length ? '\nERRS:\n' + errs.slice(0, 6).join('\n') : '  (no console errors)'));
})().catch(e => { console.error(e); process.exit(1); });
