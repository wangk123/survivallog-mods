// 放大裁剪封面局部区域（裁决视觉争议用）
// 用法：node tools/_clip_cover.js <name.html> <out.png> <x> <y> <w> <h>
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const SRC = path.join(__dirname, '..', 'assets', 'banners', 'nexus-ce');
const htmlName = process.argv[2];
const out = process.argv[3];
const [X, Y, W, H] = process.argv.slice(4).map(Number);
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const server = http.createServer((req, res) => {
  const f = path.join(SRC, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, '') || htmlName);
  fs.readFile(f, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(data);
  });
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  await page.goto('http://127.0.0.1:' + port + '/' + htmlName, { waitUntil: 'networkidle' });
  await page.waitForTimeout(400);
  await page.screenshot({ path: out, clip: { x: X, y: Y, width: W, height: H } });
  await browser.close(); server.close();
  console.log('OK ' + out);
})().catch(e => { console.error(e); process.exit(1); });
