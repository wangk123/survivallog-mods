// 渲染 v2 实机素材封面：http 根=仓库根，封面 HTML 可用 ../../../../gamekb/... 引用素材
// 用法：node tools/_render_cover2.js <pageRelPathFromRepoRoot> <out.png|jpg> <w> <h>
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..');
const pagePath = process.argv[2];
const out = process.argv[3];
const W = parseInt(process.argv[4], 10);
const H = parseInt(process.argv[5], 10);
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const server = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]).replace(/^\//, ''));
  fs.readFile(f, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    const ext = path.extname(f).toLowerCase();
    const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
      '.png': 'image/png', '.jpg': 'image/jpeg', '.woff2': 'font/woff2' }[ext] || 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': mime });
    res.end(data);
  });
});

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  await page.goto('http://127.0.0.1:' + port + '/' + encodeURI(pagePath), { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(700);
  await page.screenshot({ path: out, clip: { x: 0, y: 0, width: W, height: H } });
  await browser.close(); server.close();
  console.log('OK ' + out + '  ' + (fs.statSync(out).size / 1024).toFixed(0) + 'KB');
})().catch(e => { console.error(e); process.exit(1); });
