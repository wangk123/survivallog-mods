// 渲染抖音配图：node http 服务器（playwright 禁 file://）+ playwright-core + 系统 Chrome
// 用法：node tools/render_poster.js <html文件名(不带路径)> <输出png绝对路径>
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const PROMO = path.join(__dirname, '..', 'release', 'promo');
const htmlName = process.argv[2];
const outPng = process.argv[3];
if (!htmlName || !outPng) { console.error('usage: node render_poster.js <name.html> <out.png>'); process.exit(1); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const server = http.createServer((req, res) => {
  const f = path.join(PROMO, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, '') || htmlName);
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
  const page = await browser.newPage({ viewport: { width: 1080, height: 1440 }, deviceScaleFactor: 1 });
  await page.goto('http://127.0.0.1:' + port + '/' + htmlName, { waitUntil: 'networkidle' });
  await page.waitForTimeout(600);
  await page.screenshot({ path: outPng, clip: { x: 0, y: 0, width: 1080, height: 1440 } });
  await browser.close();
  server.close();
  const sz = fs.statSync(outPng).size;
  console.log('OK ' + outPng + '  ' + (sz / 1024).toFixed(0) + 'KB');
})().catch(e => { console.error(e); process.exit(1); });
