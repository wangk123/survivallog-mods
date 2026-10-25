// 给离线渲染的游戏 WebUI 注入游戏自家素材（图标/页签/卡片）后截图——忠实还原 mod 效果
// 用法：node tools/_inject_webui.js <inject.js> <pageRelPath> <out.png> [w] [h] [waitMs]
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const injectFile = process.argv[2];
const pagePath = process.argv[3];
const out = process.argv[4];
const W = parseInt(process.argv[5] || '1536', 10);
const H = parseInt(process.argv[6] || '864', 10);
const WAIT = parseInt(process.argv[7] || '900', 10);
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
  const injectCode = fs.readFileSync(path.join(__dirname, '_inject', injectFile), 'utf8');
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: W, height: H } });
  await page.goto('http://127.0.0.1:' + port + '/' + pagePath, { waitUntil: 'load', timeout: 20000 });
  await page.waitForTimeout(WAIT);
  const report = await page.evaluate(new Function('return async () => {' + injectCode + '}')());
  await page.waitForTimeout(500);
  await page.screenshot({ path: out });
  await browser.close(); server.close();
  console.log('OK ' + out + '  report=' + JSON.stringify(report));
})().catch(e => { console.error(e); process.exit(1); });
