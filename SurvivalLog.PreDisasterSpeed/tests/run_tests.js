// PreDisasterSpeed page.js 浏览器回归：node tests/run_tests.js（仓库根或本目录均可）
// http 服务 /fixture.html /page.js；playwright-core + 系统 Chrome（PlanPick 同款驱动解析）。
const http = require('http');
const fs = require('fs');
const path = require('path');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '..');
const PORT = parseInt(process.env.PDS_PORT || process.argv[2] || '8617', 10);
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
const server = http.createServer((req, res) => {
    const u = req.url.split('?')[0];
    // [Task3 偏差] Chrome 132+ 无头模式自动请求 /favicon.ico，404 会被记为 console error 打挂 F 组断言；
    // 回 204 抑制该浏览器固有请求（不影响被测文档与断言语义）。详见 task-3-report.md 自审。
    if (u === '/favicon.ico') { res.writeHead(204); res.end(); return; }
    const file = u === '/fixture.html' ? path.join(ROOT, 'tests', 'fixture.html')
        : u === '/page.js' ? path.join(ROOT, 'page.js') : null;
    if (!file) { res.writeHead(404); res.end('404'); return; }
    fs.readFile(file, (err, data) => {
        if (err) { res.writeHead(500); res.end(err.message); return; }
        res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] });
        res.end(data);
    });
});

const sleep = ms => new Promise(r => setTimeout(r, ms));
let PASSED = 0;
const ok = (cond, name) => { assert.ok(cond, name); PASSED++; console.log('  ok - ' + name); };
const activeSpeed = () => {
    const b = document.querySelector('#pdsSpeed .speed-btn.active');
    return b ? +(b.className.match(/pds-gear-(\d)/) || [0, -1])[1] : -1;
};

(async () => {
    await new Promise(r => server.listen(PORT, r));
    const browser = await chromium.launch({ executablePath: CHROME, headless: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push(String(e)));

    await page.goto('http://127.0.0.1:' + PORT + '/fixture.html');
    await sleep(500);   // 等 mount 重试周期（250ms）

    // A) 注入与结构
    ok(await page.locator('#pdsSpeed').count() === 1, '五键条已挂载且唯一');
    ok(await page.evaluate(() => document.querySelector('#pdsSpeed').parentElement.classList.contains('top-center')),
        '挂在 .top-center 内');
    const gears = await page.$$eval('#pdsSpeed .speed-btn', els => els.map(e => +(e.className.match(/pds-gear-(\d)/) || [0, -1])[1]));
    assert.deepStrictEqual(gears, [4, 1, 2, 3, 0], '五键顺序 慢速/原速/二倍/三倍/暂停'); PASSED++; console.log('  ok - 五键顺序');

    // B) 点击上报（postMessage 契约）
    await page.click('#pdsSpeed .pds-gear-2');
    await sleep(100);
    let sent = await page.evaluate(() => window.__sent[window.__sent.length - 1]);
    assert.deepStrictEqual(sent, { type: 'PDS_SPEED', data: { speed: 2 }, sourcePageId: 'CoreUI0' }, 'gear-2 上报 speed=2'); PASSED++; console.log('  ok - gear-2 上报');
    await page.click('#pdsSpeed .pds-gear-0');
    await page.click('#pdsSpeed .pds-gear-4');
    await sleep(100);
    sent = await page.evaluate(() => window.__sent.slice(-2));
    assert.deepStrictEqual(sent.map(m => m.data.speed), [0, 4], '暂停/慢速上报 0/4'); PASSED++; console.log('  ok - 暂停/慢速上报');

    // C) 官方时标回显映射（payload s1=3/s2=33；夹具故意用非默认值，验证高亮由 payload 驱动而非内置默认 2/32）
    const cases = [[1, 1], [3, 2], [33, 3], [0.0001, 0], [0.3, -1]];
    for (const [ts, gear] of cases) {
        await page.evaluate(v => window.__fixtureTimeScale(v), ts);
        await sleep(80);
        const a = await page.evaluate(activeSpeed);
        assert.strictEqual(a, gear, 'timeScale=' + ts + ' 高亮 gear-' + gear); PASSED++;
        console.log('  ok - timeScale=' + ts + ' → gear-' + gear);
    }

    // D) 慢速本地高亮被官方同步重算
    await page.click('#pdsSpeed .pds-gear-4');
    await sleep(80);
    assert.strictEqual(await page.evaluate(activeSpeed), 4, '点击慢速本地高亮'); PASSED++;
    await page.evaluate(() => window.__fixtureTimeScale(1));
    await sleep(80);
    assert.strictEqual(await page.evaluate(activeSpeed), 1, '官方 scale=1 同步后慢速高亮被重算'); PASSED++;

    // E) 幂等：重跑 page.js 不重复挂条
    const src = fs.readFileSync(path.join(ROOT, 'page.js'), 'utf8');
    await page.evaluate(s => window.eval(s), src);
    await sleep(300);
    ok(await page.locator('#pdsSpeed').count() === 1, '重复 eval 不重复挂载');

    // F) 无 console error
    ok(errors.length === 0, '无 console error / pageerror: ' + errors.slice(0, 3).join(' | '));

    console.log('ALL ' + PASSED + ' ASSERTIONS PASSED / console errors ' + errors.length);
    await browser.close();
    server.close();
    process.exit(0);
})().catch(e => { console.error('FAIL @#' + (PASSED + 1) + ':', e.message); process.exit(1); });
