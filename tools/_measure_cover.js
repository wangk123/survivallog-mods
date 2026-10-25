// 量封面 HTML 关键元素包围盒（abs 坐标），裁决重叠/间隙争议
// 用法：node tools/_measure_cover.js <name.html> <sel1> <sel2> ...
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const SRC = path.join(__dirname, '..', 'assets', 'banners', 'nexus-ce');
const htmlName = process.argv[2];
const sels = process.argv.slice(3);
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
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  await page.goto('http://127.0.0.1:' + port + '/' + htmlName, { waitUntil: 'networkidle' });
  for (const sel of sels) {
    const r = await page.evaluate((s) => {
      const out = [];
      document.querySelectorAll(s).forEach((el, i) => {
        const b = el.getBoundingClientRect();
        out.push({ i, tag: el.tagName, cls: el.className || '', text: (el.textContent || '').trim().slice(0, 16),
          x: Math.round(b.x), y: Math.round(b.y), r: Math.round(b.right), b: Math.round(b.bottom) });
      });
      return out;
    }, sel);
    console.log(sel, JSON.stringify(r));
  }
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
