// 渲染 Nexus mod 页图件（横幅/海报）：node http 服务器（playwright 禁 file://）+ playwright-core + 系统 Chrome
// 用法：node tools/render_nexus_img.js <name.html> <out.png> <width> <height>
// HTML 放在 assets/banners/nexus-ce/ 下，按 CSS 实际像素 1:1 截图。
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const SRC = path.join(__dirname, '..', 'assets', 'banners', 'nexus-ce');
const htmlName = process.argv[2];
const outPng = process.argv[3];
const W = parseInt(process.argv[4], 10);
const H = parseInt(process.argv[5], 10);
if (!htmlName || !outPng || !W || !H) {
  console.error('usage: node render_nexus_img.js <name.html> <out.png> <width> <height>');
  process.exit(1);
}
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
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  await page.goto('http://127.0.0.1:' + port + '/' + htmlName, { waitUntil: 'networkidle' });
  await page.waitForTimeout(600);
  await page.screenshot({ path: outPng, clip: { x: 0, y: 0, width: W, height: H } });
  await browser.close();
  server.close();
  const sz = fs.statSync(outPng).size;
  console.log('OK ' + outPng + '  ' + (sz / 1024).toFixed(0) + 'KB');
})().catch(e => { console.error(e); process.exit(1); });
