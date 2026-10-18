// 图库截图 PNG→JPEG 压缩（playwright 禁 file://，用 page + data URL 内联，canvas 编码）
// 用法：node tools/shots_to_jpg.js <目录> [质量=0.9]
const fs = require('fs');
const path = require('path');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const dir = process.argv[2];
const q = parseFloat(process.argv[3]) || 0.9;
if (!dir) { console.error('usage: node shots_to_jpg.js <dir> [quality]'); process.exit(1); }

(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
  for (const f of fs.readdirSync(dir).filter(n => /^nexus_ce_shot\d.*\.png$/i.test(n)).sort()) {
    const buf = fs.readFileSync(path.join(dir, f));
    const dataUrl = await page.evaluate(async ({ b64, q }) => {
      const img = new Image();
      img.src = 'data:image/png;base64,' + b64;
      await img.decode();
      const c = document.createElement('canvas');
      c.width = img.naturalWidth; c.height = img.naturalHeight;
      const ctx = c.getContext('2d');
      ctx.fillStyle = '#0c1611';           // 不透明底，避免透明区变黑边缘伪影
      ctx.fillRect(0, 0, c.width, c.height);
      ctx.drawImage(img, 0, 0);
      return c.toDataURL('image/jpeg', q);
    }, { b64: buf.toString('base64'), q });
    const out = path.join(dir, f.replace(/\.png$/i, '.jpg'));
    fs.writeFileSync(out, Buffer.from(dataUrl.split(',')[1], 'base64'));
    console.log(f, (buf.length / 1048576).toFixed(2) + 'MB', '->', path.basename(out), (fs.statSync(out).length / 1024).toFixed(0) + 'KB');
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
