// 采集 PlanPick mod 真实界面：离线 RogueSelect 页 + 真 page.js + 真实目录数据(plan_data.json)
// 产出：①面板全开 ②面板+确认弹窗
const path = require('path');
const http = require('http');
const fs = require('fs');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const ROOT = path.join(__dirname, '..', 'gamekb', '2026-10-23_26157732', 'webui');
const MOD = path.join(__dirname, '..', 'SurvivalLog.PlanPick');
const OUTDIR = path.join(__dirname, '..', 'gamekb', 'webui_shots');
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
  const pageJs = fs.readFileSync(path.join(MOD, 'page.js'), 'utf8');
  const raw = JSON.parse(fs.readFileSync(path.join(MOD, 'prototype', 'plan_data.json'), 'utf8'));

  // 构造 boot payload：真实 465 项 + 代表性状态（today/bought/locked/ok）
  const plans = raw.plans.map((p, i) => {
    const q = { id: p.id, name: p.name, desc: p.desc, icon: p.icon, cost: p.cost,
      cat: p.cat, sub: p.sub || null, unlockDay: p.unlockDay, exclusive: !!(p.exclusive), isReget: !!p.isReget };
    if (i < 3) { q.today = true; q.state = 'ok'; }
    else if (i === 6 || i === 21) { q.state = 'bought'; }
    else if (i === 9 || i === 33) { q.state = 'locked'; q.reasons = ['需先激活前置规划', '第 ' + Math.max(2, (p.unlockDay || 2) + 2) + ' 天解锁']; }
    else q.state = 'ok';
    return q;
  });
  const payload = { plans, categories: raw.categories, points: 368, mult: 200, day: 12, role: 0, roleNames: {} };

  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const port = server.address().port;
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const page = await browser.newPage({ viewport: { width: 1536, height: 864 } });
  await page.goto('http://127.0.0.1:' + port + '/UI/RogueSelect/RogueSelect.html', { waitUntil: 'load', timeout: 20000 });
  await page.waitForTimeout(900);

  await page.addScriptTag({ content: pageJs });
  const boot = await page.evaluate((pj) => {
    window.__PlanPickBoot(pj);
    return { installed: !!window.__PlanPickInstalled, entry: !!document.getElementById('ppEntry') };
  }, payload);
  // 打开面板（入口按钮在则点它；不在则手动开 overlay 后重推一次数据触发渲染）
  await page.evaluate(() => {
    const b = document.getElementById('ppEntry');
    if (b) b.click();
    else { document.getElementById('ppOverlay').classList.add('open'); }
  });
  await page.evaluate((pj) => window.__PlanPickBoot(pj), payload);
  await page.waitForTimeout(600);
  const cards = await page.evaluate(() => document.querySelectorAll('#ppList .pp-card').length);
  await page.screenshot({ path: path.join(OUTDIR, 'pp_real_panel.png') });

  // 点一张可购卡 → 确认弹窗（原价/×200%/购买后剩余/确认激活）
  const conf = await page.evaluate(() => {
    const ok = [...document.querySelectorAll('#ppList .pp-card')].find(c => !c.classList.contains('locked') && !c.classList.contains('bought') && !c.classList.contains('tooexpensive'));
    if (!ok) return null;
    ok.click();
    return { name: document.getElementById('cfName').textContent, pay: document.getElementById('cfPay').textContent, open: document.getElementById('ppConfirm').classList.contains('open') };
  });
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUTDIR, 'pp_real_confirm.png') });

  await browser.close(); server.close();
  console.log('OK boot=' + JSON.stringify(boot) + ' cards=' + cards + ' confirm=' + JSON.stringify(conf));
})().catch(e => { console.error(e); process.exit(1); });
