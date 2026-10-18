// 通用横幅渲染：node http 服务器（playwright 禁 file://）+ playwright-core + 系统 Chrome
// 用法：node tools/render_banner.js <html绝对路径> <输出png绝对路径> [视口宽=1080] [视口高=1920] [DPR=2]
// 对页面内第一个 section（或 .cover）做元素截图，输出为 视口尺寸×DPR 的 PNG
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const htmlPath = path.resolve(process.argv[2]);
const outPng = process.argv[3];
const W = parseInt(process.argv[4] || '1080', 10);
const H = parseInt(process.argv[5] || '1920', 10);
const DPR = parseFloat(process.argv[6] || '2');
if (!htmlPath || !outPng) { console.error('usage: node render_banner.js <in.html> <out.png> [w] [h] [dpr]'); process.exit(1); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const dir = path.dirname(htmlPath);
const name = path.basename(htmlPath);

const server = http.createServer((req, res) => {
  const f = path.join(dir, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, '') || name);
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
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: DPR });
  await page.goto('http://127.0.0.1:' + port + '/' + encodeURIComponent(name), { waitUntil: 'networkidle' });
  await page.waitForTimeout(600);
  const el = await page.$('.cover') || await page.$('section') || await page.$('body');
  await el.screenshot({ path: outPng });
  await browser.close();
  server.close();
  const b = fs.readFileSync(outPng);
  console.log('OK ' + outPng + '  ' + b.readUInt32BE(16) + 'x' + b.readUInt32BE(20) + '  ' + (fs.statSync(outPng).size / 1024).toFixed(0) + 'KB');
})().catch(e => { console.error(e); process.exit(1); });
