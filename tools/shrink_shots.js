// 把截图缩到指定最大宽度并重编码 PNG（playwright 禁 file://，用 page.setContent + data URL 内联，canvas 高质量缩放）
// 用法：node tools/shrink_shots.js <目录或单文件> [最大宽度=1920]
const fs = require('fs');
const path = require('path');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const target = process.argv[2];
const maxW = parseInt(process.argv[3], 10) || 1920;
if (!target) { console.error('usage: node shrink_shots.js <dir|file.png> [maxWidth]'); process.exit(1); }

const files = [];
if (fs.statSync(target).isDirectory()) {
  for (const f of fs.readdirSync(target)) if (/\.png$/i.test(f) && /^\{[0-9A-F-]+\}\.png$/i.test(f)) files.push(path.join(target, f));
} else files.push(target);

const OUT_NAMES = ['nexus_ce_shot1_cooking.png', 'nexus_ce_shot2_trade.png', 'nexus_ce_shot3_workbench.png'];
// 目录里按名称排序保证映射稳定
files.sort();

(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
  let i = 0;
  for (const f of files) {
    const buf = fs.readFileSync(f);
    const b64 = buf.toString('base64');
    const dataUrl = 'data:image/png;base64,' + b64;
    const out = path.join(path.dirname(f), OUT_NAMES[i] || ('shrunk_' + path.basename(f)));
    const res = await page.evaluate(async ({ dataUrl, maxW }) => {
      const img = new Image();
      img.src = dataUrl;
      await img.decode();
      const w = Math.min(maxW, img.naturalWidth);
      const h = Math.round(img.naturalHeight * w / img.naturalWidth);
      const c = document.createElement('canvas');
      c.width = w; c.height = h;
      const ctx = c.getContext('2d');
      ctx.imageSmoothingEnabled = true;
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(img, 0, 0, w, h);
      return { dataUrl: c.toDataURL('image/png'), w, h, ow: img.naturalWidth, oh: img.naturalHeight };
    }, { dataUrl, maxW });
    fs.writeFileSync(out, Buffer.from(res.dataUrl.split(',')[1], 'base64'));
    console.log(path.basename(f), res.ow + 'x' + res.oh, (buf.length / 1048576).toFixed(1) + 'MB', '->', path.basename(out), res.w + 'x' + res.h, (fs.statSync(out).length / 1048576).toFixed(2) + 'MB');
    i++;
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
