// 一次性检查：封面内容是否溢出画布/元素是否重叠。用完可删。
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const htmlPath = path.resolve(process.argv[2]);
const dir = path.dirname(htmlPath), name = path.basename(htmlPath);
const server = http.createServer((req, res) => {
  fs.readFile(path.join(dir, name), (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(data);
  });
});
(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const page = await browser.newPage({ viewport: { width: 1080, height: 1920 } });
  await page.goto('http://127.0.0.1:' + server.address().port + '/' + encodeURIComponent(name), { waitUntil: 'networkidle' });
  const report = await page.evaluate(() => {
    const out = [];
    const cover = document.querySelector('.cover');
    const cr = cover.getBoundingClientRect();
    out.push(['cover', cr.width + 'x' + cr.height, 'scroll ' + cover.scrollWidth + 'x' + cover.scrollHeight]);
    document.querySelectorAll('.inner > *').forEach(el => {
      const r = el.getBoundingClientRect();
      out.push([el.className || el.tagName, 'y:' + Math.round(r.top) + '-' + Math.round(r.bottom),
        'x:' + Math.round(r.left) + '-' + Math.round(r.right),
        r.right > cr.right || r.bottom > cr.bottom || r.left < cr.left ? '!!OUT!!' : 'ok']);
    });
    // 盖章与步骤框是否压到正文文字
    const stamp = document.querySelector('.stamp').getBoundingClientRect();
    document.querySelectorAll('.steps .row b, .steps .row i, .install').forEach(el => {
      const r = el.getBoundingClientRect();
      const overlap = !(r.right < stamp.left || r.left > stamp.right || r.bottom < stamp.top || r.top > stamp.bottom);
      if (overlap) out.push(['OVERLAP stamp x', el.className || el.tagName, Math.round(r.left) + '-' + Math.round(r.right)]);
    });
    // 进度条填充块必须真的有尺寸（span inline 会静默失效成空框）
    const fill = document.querySelector('.install .fill');
    if (fill) {
      const fr = fill.getBoundingClientRect();
      out.push(['install .fill', Math.round(fr.width) + 'x' + Math.round(fr.height), fr.width > 300 ? 'filled ok' : '!!EMPTY BAR!!']);
    }
    // foot 是否被推出画布
    const foot = document.querySelector('.foot').getBoundingClientRect();
    out.push(['foot bottom', Math.round(foot.bottom), foot.bottom <= 1920 - 44 ? 'ok(above stripe)' : '!!INTO STRIPE!!']);
    return out;
  });
  console.log(report.map(r => r.join(' | ')).join('\n'));
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
