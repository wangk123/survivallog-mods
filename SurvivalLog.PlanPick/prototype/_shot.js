// 原型截图验证：node prototype/_shot.js  （需 serve.js 已在跑，默认 127.0.0.1:8612）
// 输出 _shot_1page.png（官方页融合）/ _shot_2overlay.png（自选弹层）/ _shot_3locked.png（未解锁原因）
// 同时收集 console error，非空即退出码 1。
const path = require('path');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = process.argv[2] || '8612';

(async () => {
    const browser = await chromium.launch({ executablePath: CHROME, headless: true });
    const page = await browser.newPage({ viewport: { width: 1536, height: 864 }, deviceScaleFactor: 1 });
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push(String(e)));
    page.on('response', r => { if (r.status() >= 400) errors.push('HTTP ' + r.status() + ' ' + r.url()); });

    await page.goto('http://127.0.0.1:' + PORT + '/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(500);
    await page.screenshot({ path: path.join(__dirname, '_shot_1page.png') });

    // 打开弹层（全部 tab）
    await page.click('#ppEntry');
    await page.waitForTimeout(400);
    await page.screenshot({ path: path.join(__dirname, '_shot_2overlay.png') });

    // 找一张 locked 卡点击看原因（第 49 天的 D49 规划在 day=20 必锁）
    const locked = await page.$$('.pp-card.locked');
    if (locked.length) {
        await locked[locked.length > 5 ? 5 : 0].click();
        await page.waitForTimeout(400);
        await page.screenshot({ path: path.join(__dirname, '_shot_3locked.png') });
    }

    // 家具扩建 tab
    const tab = await page.$('[data-tab="expand"]');
    if (tab) { await tab.click(); await page.waitForTimeout(400); await page.screenshot({ path: path.join(__dirname, '_shot_4expand.png') }); }

    await browser.close();
    if (errors.length) { console.log('CONSOLE ERRORS:\n' + errors.join('\n')); process.exit(1); }
    console.log('OK screenshots (no console errors)');
})().catch(e => { console.error(e); process.exit(1); });
