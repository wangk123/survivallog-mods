// PlanPick page.js 浏览器回归：node tests/run_tests.js（仓库根或本目录均可）
// 自带 http 服务：/fixture.html /page.js /official.css(→prototype) /golden.json(按 query 重算)
//               /Res/*(→gamekb webui 镜像，官方 WebIcon 图标)
// 断言集移植自 prototype/_verify.js 并按夹具口径调整（官方区无卡→零接触断言；
// 天数/点数变化经 __fixtureSet 服务端重算 state/reasons 再走 __PlanPickBoot）。
const http = require('http');
const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const assert = require('assert');

const ROOT = path.resolve(__dirname, '..');            // SurvivalLog.PlanPick
const PROTO = path.join(ROOT, 'prototype');
const WEBUI = path.join(ROOT, '../gamekb/2026-10-23_26157732/webui');
const PORT = parseInt(process.env.PP_PORT || process.argv[2] || '8613', 10);

// ---------- golden 合成（C# Task 4 将产出同构 JSON；此处为 plan_data.json 转换） ----------
// payload 契约：{ plans:[{id,name,desc,icon,cost,isReget,cat,sub,unlockDay,today,exclusive,state,reasons}],
//                 categories:[4], today:[id 数组], points, mult, day, role, roleNames:{1..4} }
// state/reasons 由服务端（实装为 C#）按原型 evalLock 语义算好；JS 侧不推导条件。
// 注：activated（已激活）夹具恒为 ∅；点数不足不算 locked（state 仍 'ok'，JS 按显示逻辑标 too-expensive）。
const PD_PATH = path.join(PROTO, 'plan_data.json');
if (!fs.existsSync(PD_PATH)) cp.execSync('node gen_data.js', { cwd: PROTO, stdio: 'inherit' });
const PD = JSON.parse(fs.readFileSync(PD_PATH, 'utf8'));
const ROLE_NAMES = { 1: '打工仔', 2: '大学生', 3: '仓库管理员', 4: '退伍军人' };
const roleName = ids => ids.map(i => ROLE_NAMES[i] || ('角色' + i)).join(' / ');

function buildGolden(q) {
  const day = +q.day || 20, role = +q.role || 0;
  const points = q.points !== undefined ? +q.points : 150;
  const mult = +q.mult || 2;
  const todayIds = PD.todaySample;                     // [30011, 30661, 31442]（今日候选样例）
  const plans = PD.plans.map(p => {
    const reasons = [];
    if (p.unlockDay !== null && day < p.unlockDay) reasons.push('第 ' + p.unlockDay + ' 天解锁（当前第 ' + day + ' 天）');
    (p.prereq || []).forEach(pr => reasons.push('需先激活「' + pr.name + '」'));
    if ((p.role || []).length && !p.role.includes(role)) reasons.push('仅限「' + roleName(p.role) + '」的专属规划');
    (p.special || []).forEach(sp => reasons.push(sp));
    return {
      id: p.id, name: p.name, desc: p.desc,
      icon: p.icon ? p.icon.replace(/^\//, '../../') : null,   // 还原官方 WebIcon 原值（../../Res/...）
      cost: p.cost, isReget: p.isReget, cat: p.cat, sub: p.sub, unlockDay: p.unlockDay,
      today: todayIds.includes(p.id), exclusive: (p.role || []).length > 0,   // 与 C# payload 同构：bool（RoleIds.Count>0）
      state: reasons.length ? 'locked' : 'ok', reasons,
    };
  });
  return { plans, categories: PD.categories, today: todayIds, points, mult, day, role, roleNames: ROLE_NAMES };
}
const GOLD0 = buildGolden({});                          // 基准（day20/role0/points150/mult2）
const COST_BY_ID = new Map(GOLD0.plans.map(p => [p.id, p.cost]));

// ---------- http 服务 ----------
const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = http.createServer((req, res) => {
  const u = decodeURIComponent(req.url.split('?')[0]);
    const qs = {};
    (req.url.split('?')[1] || '').split('&').filter(Boolean).forEach(kv => { const [k, v] = kv.split('='); qs[k] = decodeURIComponent(v || ''); });
    const send = (code, type, body) => { res.writeHead(code, { 'Content-Type': type }); res.end(body); };
    const sendFile = f => fs.readFile(f, (err, data) => {
        if (err) send(404, 'text/plain; charset=utf-8', '404 ' + u);
        else send(200, MIME[path.extname(f).toLowerCase()] || 'application/octet-stream', data);
    });
    if (u === '/favicon.ico') return send(204, 'text/plain', '');
    if (u === '/' || u === '/fixture.html') return sendFile(path.join(__dirname, 'fixture.html'));
    if (u === '/page.js') return sendFile(path.join(ROOT, 'page.js'));
    if (u === '/official.css') return sendFile(path.join(PROTO, 'official.css'));       // 只读复用官方页复刻样式
    if (u === '/golden.json') return send(200, MIME['.json'], JSON.stringify(buildGolden(qs)));
    if (u.startsWith('/Res/')) return sendFile(path.join(WEBUI, u));                    // 官方图标镜像
    send(404, 'text/plain; charset=utf-8', '404 ' + u);
});

// ---------- 断言 ----------
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
let PASSED = 0;
const ok = (cond, msg) => { PASSED++; assert.ok(cond, '#' + PASSED + ' ' + msg); };
const eq = (a, b, msg) => { PASSED++; assert.strictEqual(a, b, '#' + PASSED + ' ' + msg + ' (' + JSON.stringify(a) + ' vs ' + JSON.stringify(b) + ')'); };
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    await new Promise(r => server.listen(PORT, '127.0.0.1', r));
    const browser = await chromium.launch({ executablePath: CHROME, headless: true });
    const page = await browser.newPage({ viewport: { width: 1536, height: 864 } });
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push(String(e)));

    await page.goto('http://127.0.0.1:' + PORT + '/fixture.html', { waitUntil: 'networkidle' });
    await page.waitForFunction(() => window.__fixtureError !== undefined || document.querySelector('.pp-entry-btn'), null, { timeout: 8000 });
    ok(!(await page.evaluate(() => window.__fixtureError)), 'fixture boot 无错: ' + (await page.evaluate(() => window.__fixtureError)));

    // A) 入口按钮：存在 / 在 footer-right 内 / 首位（"离开"之前）/ 文案含倍率
    await page.waitForSelector('.pp-entry-btn', { timeout: 4000 });
    eq(await page.locator('.footer-right .pp-entry-btn').count(), 1, '入口按钮在 .footer-right 内');
    const frInfo = await page.evaluate(() => {
        const fr = document.querySelector('.footer-right');
        return { first: fr.firstElementChild && fr.firstElementChild.id, n: fr.children.length,
            secondIsLeave: fr.children[1] && fr.children[1].className === 'primary-btn' && fr.children[1].textContent.trim() === '离 开',
            entryText: fr.firstElementChild.innerText.trim() };
    });
    eq(frInfo.first, 'ppEntry', '入口按钮是 .footer-right 第一个子元素');
    ok(frInfo.secondIsLeave, '入口按钮插在"离开"按钮之前');
    ok(/自选规划/.test(frInfo.entryText) && /×2/.test(frInfo.entryText), '入口文案=自选规划×2: ' + frInfo.entryText);

    // B) 幂等安装：单弹层/单确认/官方区零接触
    eq(await page.locator('.pp-overlay').count(), 1, '恰好一个 .pp-overlay');
    eq(await page.locator('.pp-confirm').count(), 1, '恰好一个 .pp-confirm');
    eq(await page.locator('.pp-why').count(), 1, '恰好一个 .pp-why');
    eq(await page.locator('#pp-toast').count(), 1, '恰好一个 #pp-toast');
    eq(await page.evaluate(() => document.getElementById('officialGrid').children.length), 0, '官方卡区保持空（零接触）');
    eq(await page.evaluate(() => document.querySelectorAll('style#pp-style').length), 1, '样式只注入一份');

    // C) 打开弹层：初始点数/tab 计数/全部 tab 无二级 chips
    await page.click('.pp-entry-btn');
    await sleep(300);
    eq(await page.locator('#ppPoints').innerText(), '150', '初始剩余生存点 150');
    ok((await page.locator('#ppMultHint').innerText()).includes('×2'), '倍率提示 ×2');
    eq(await page.locator('.pp-tab').count(), 5, 'tab = 全部+4 分类');
    ok((await page.locator('.pp-tab[data-tab="all"]').innerText()).includes('465'), '全部 tab 计数 465');
    eq(await page.locator('.pp-sub').count(), 0, '全部 tab 无二级 chips');

    // D) 属性 tab：chips 换行无横向溢出 / 独立规划筛选收窄
    await page.click('.pp-tab[data-tab="attribute"]');
    await sleep(250);
    const subChips = await page.locator('.pp-sub').count();
    ok(subChips > 5, '分类 tab 有二级 chips: ' + subChips);
    const overflow = await page.evaluate(() => { const r = document.getElementById('ppSubRow'); return r ? r.scrollWidth - r.clientWidth : 1; });
    ok(overflow <= 0, '二级行无横向溢出: ' + overflow);
    const beforeSub = await page.locator('.pp-card').count();
    await page.click('.pp-sub[data-sub="__solo"]');
    await sleep(200);
    const afterSub = await page.locator('.pp-card').count();
    ok(afterSub > 0 && afterSub < beforeSub, '独立规划筛选收窄: ' + beforeSub + ' -> ' + afterSub);
    await page.click('.pp-sub[data-sub="__all"]');
    await sleep(150);

    // E) 功能强化 tab（64 系列）：默认收起一行 + "更多"展开/收起
    await page.click('.pp-tab[data-tab="function"]');
    await sleep(250);
    ok(await page.locator('#ppSubMore').isVisible(), '溢出时显示"更多"按钮');
    await page.click('#ppSubMore');
    await sleep(300);
    ok(await page.evaluate(() => document.getElementById('ppSubRow').classList.contains('expanded')), '展开生效');
    await page.click('#ppSubMore');
    await sleep(300);
    ok(!(await page.evaluate(() => document.getElementById('ppSubRow').classList.contains('expanded'))), '收起生效');

    // F) 状态筛选"未解锁"：仅锁定卡 / tab 计数联动 / 无 0 计数 chip / 系列求和一致
    await page.click('.pp-tab[data-tab="attribute"]');
    await sleep(250);
    const attrFull = parseInt(await page.locator('.pp-tab[data-tab="attribute"] .cnt').innerText());
    await page.click('.pp-state[data-state="locked"]');
    await sleep(250);
    const stTotal = await page.locator('.pp-card').count();
    const stLocked = await page.locator('.pp-card.locked').count();
    ok(stTotal > 0 && stTotal === stLocked, '锁定筛选后列表全为 locked: ' + stLocked + '/' + stTotal);
    const attrLocked = parseInt(await page.locator('.pp-tab[data-tab="attribute"] .cnt').innerText());
    ok(attrLocked < attrFull, 'tab 计数随状态联动: ' + attrFull + ' -> ' + attrLocked);
    eq(await page.evaluate(() => [...document.querySelectorAll('.pp-sub .cnt')].filter(e => parseInt(e.textContent) === 0).length), 0, '无 0 计数 chip');
    const seriesSum = await page.evaluate(() => [...document.querySelectorAll('.pp-sub')].filter(c => c.dataset.sub !== '__all').reduce((s, c) => s + parseInt(c.querySelector('.cnt').textContent), 0));
    ok(seriesSum > 0 && seriesSum === attrLocked, '系列 chips 求和=筛选后 tab 计数: ' + seriesSum + ' vs ' + attrLocked);
    await page.click('.pp-state[data-state="all"]');
    await sleep(150);

    // G) 第 20 天：锁定/可购并存；锁定卡点击 → 原因弹泡 + 自动隐藏
    const lockedCnt = await page.locator('.pp-card.locked').count();
    const okCnt = await page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive)').count();
    ok(lockedCnt > 0, '存在锁定卡: ' + lockedCnt);
    ok(okCnt > 0, '存在可购卡: ' + okCnt);
    await page.locator('.pp-card.locked').nth(5).click();
    await sleep(200);
    ok(await page.locator('#ppWhy').isVisible(), '原因弹泡可见');
    const whyText = await page.locator('#ppWhy').innerText();
    ok(/解锁|激活|专属|需要/.test(whyText), '原因文案有意义: ' + whyText.slice(0, 60));
    await sleep(4200);
    ok(!(await page.locator('#ppWhy').isVisible()), '原因弹泡 3.8s 自动隐藏');

    // H) 家具扩建 tab：角色锁因用官方角色名（role=0 全部角色专属锁定）
    await page.click('.pp-tab[data-tab="expand"]');
    await sleep(250);
    const roleWhy = await page.evaluate(() => {
        const cards = [...document.querySelectorAll('.pp-card.locked')];
        for (const c of cards) { c.click(); const t = document.getElementById('ppWhy'); if (t.className.includes('open') && /专属规划/.test(t.innerText)) return t.innerText; }
        return '';
    });
    ok(/仅限「(打工仔|大学生|仓库管理员|退伍军人)( \/ .+)?」的专属规划/.test(roleWhy),
        '角色锁因用官方角色名: ' + roleWhy.replace(/\n/g, ' ').slice(0, 80));
    await page.locator('#ppWhy').click();
    // H2) exclusive=true（payload 布尔口径）→ 卡片渲染「专」徽章
    const exCnt = await page.evaluate(() => [...document.querySelectorAll('.pp-tag')].filter(e => e.textContent === '专').length);
    ok(exCnt > 0, 'exclusive=true 规划渲染「专」徽章: ' + exCnt);

    // I) 确认弹窗：无提示句（仅三行）/ 非今日卡 ×2 数学 / 今日卡原价
    await page.click('.pp-tab[data-tab="all"]');
    await sleep(200);
    const buyCard = page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive):not(:has(.pp-tag.today))').first();
    await buyCard.click();
    await sleep(200);
    ok(await page.locator('#ppConfirm.open').isVisible(), '确认弹窗打开');
    eq(await page.locator('.pp-confirm-box .hint').count(), 0, '确认弹窗无提示句元素');
    eq(await page.locator('.pp-confirm-box .row').count(), 3, '确认弹窗三行（原价/自选价/剩余）');
    const base = parseInt(await page.locator('#cfBase').innerText());
    const pay = parseInt(await page.locator('#cfPay').innerText());
    eq(pay, base * 2, '非今日卡 pay = base × 2 (' + base + ' -> ' + pay + ')');
    await page.click('#cfCancel');
    await sleep(150);
    await page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive):has(.pp-tag.today)').first().click();
    await sleep(200);
    const tBase = parseInt(await page.locator('#cfBase').innerText());
    const tPay = parseInt(await page.locator('#cfPay').innerText());
    eq(tPay, tBase, '今日候选卡原价购买 (' + tBase + ' -> ' + tPay + ')');
    await page.click('#cfCancel');
    await sleep(150);

    // J) 确认购买：postMessage CLICK_BUY + 乐观扣点 + 已激活印章
    await buyCard.click();
    await sleep(150);
    const before = parseInt(await page.locator('#ppPoints').innerText());
    const buyId = await buyCard.getAttribute('data-id');
    await page.click('#cfOk');
    await page.waitForFunction(() => window.__ppMessages.length >= 1, null, { timeout: 3000 });
    const msg = await page.evaluate(() => window.__ppMessages[window.__ppMessages.length - 1]);
    eq(msg.type, 'CLICK_BUY', '上行消息 type=CLICK_BUY');
    eq(msg.sourcePageId, 'RogueSelect', '上行消息 sourcePageId=RogueSelect');
    eq(typeof msg.data.id, 'number', '上行消息 data.id 为数字');
    eq(String(msg.data.id), buyId, '上行消息 id=点击卡 id');
    const after = parseInt(await page.locator('#ppPoints').innerText());
    eq(after, before - pay, '乐观扣点: ' + before + ' -> ' + after + ' (pay=' + pay + ')');
    ok((await page.locator('.pp-card.bought').count()) >= 1, '已激活印章出现');

    // K) 幂等重推：反复 __PlanPickBoot 仍单实例
    await page.evaluate(() => fetch('golden.json').then(r => r.json()).then(g => { __PlanPickBoot(g); __PlanPickBoot(g); }));
    await sleep(250);
    eq(await page.locator('.pp-overlay').count(), 1, '重复 boot 后仍恰好一个弹层');
    eq(await page.locator('#pp-style').count(), 1, '重复 boot 后样式仍只一份');
    eq(await page.locator('.pp-entry-btn').count(), 1, '重复 boot 后仍一个入口按钮');
    ok((await page.locator('.pp-card').count()) > 0, '重复 boot 后列表仍渲染');

    // L) 天数 20→50：全量加载后锁定卡减少；回推 20 恢复（服务端重算 state）
    await page.click('.pp-tab[data-tab="all"]');
    await page.click('.pp-state[data-state="all"]');
    await page.locator('#ppSearch').fill('');
    await page.locator('#ppSort').selectOption('id');
    await sleep(200);
    for (let i = 0; i < 8 && await page.locator('#ppMore').isVisible().catch(() => false); i++) await page.click('#ppMore');
    await sleep(200);
    const allRendered = await page.locator('.pp-card').count();
    eq(allRendered, 465, '加载更多后全量 465 张: ' + allRendered);
    const lock20 = await page.locator('.pp-card.locked').count();
    await page.evaluate(() => window.__fixtureSet({ day: 50 }));
    await sleep(350);
    const lock50 = await page.locator('.pp-card.locked').count();
    ok(lock50 < lock20, 'day50 锁定减少: ' + lock20 + ' -> ' + lock50);
    await page.evaluate(() => window.__fixtureSet({ day: 20 }));
    await sleep(350);
    const lock20b = await page.locator('.pp-card.locked').count();
    eq(lock20b, lock20, '回推 day20 锁定恢复: ' + lock20b + ' vs ' + lock20);

    // M) 点数不足（显示层逻辑）：points=15 → too-expensive 卡出现，恢复后消失
    await page.evaluate(() => window.__fixtureSet({ points: 15 }));
    await sleep(350);
    const poor15 = await page.locator('.pp-card.tooexpensive').count();
    ok(poor15 > 0, 'points=15 时出现点数不足卡: ' + poor15);
    await page.evaluate(() => window.__fixtureSet({ points: 150 }));
    await sleep(350);

    // N) 搜索"扩建"有结果
    await page.locator('#ppSearch').fill('扩建');
    await sleep(250);
    ok((await page.locator('.pp-card').count()) > 0, '搜索"扩建"有结果');
    await page.locator('#ppSearch').fill('');
    await sleep(150);

    // O) 价格升序：首卡成本 ≤ 末卡成本
    await page.locator('#ppSort').selectOption('costAsc');
    await sleep(250);
    const ids = await page.$$eval('.pp-card', els => els.map(e => +e.dataset.id));
    ok(ids.length > 1 && COST_BY_ID.get(ids[0]) <= COST_BY_ID.get(ids[ids.length - 1]),
        'costAsc 首卡 ≤ 末卡: ' + COST_BY_ID.get(ids[0]) + ' vs ' + COST_BY_ID.get(ids[ids.length - 1]));

    // P) 图标全量加载无破图（滚动触发 lazy）+ 无 console error
    for (let i = 0; i < 8 && await page.locator('#ppMore').isVisible().catch(() => false); i++) await page.click('#ppMore');
    await page.evaluate(async () => {
        const L = document.getElementById('ppList');
        for (let y = 0; y < L.scrollHeight; y += 700) { L.scrollTop = y; await new Promise(r => setTimeout(r, 40)); }
        L.scrollTop = L.scrollHeight;
    });
    await sleep(800);
    const broken = await page.$$eval('.pp-card img', ims => ims.filter(im => !(im.complete && im.naturalWidth > 0)).length);
    eq(broken, 0, '无破图');
    eq(await page.evaluate(() => document.getElementById('officialGrid').children.length), 0, '全程官方卡区零接触');
    eq(await page.locator('.pp-overlay').count(), 1, '结束仍单弹层');
    eq(errors.length, 0, '无 console error / pageerror: ' + errors.slice(0, 3).join(' | '));

    console.log('ALL ' + PASSED + ' ASSERTIONS PASSED / console errors ' + errors.length);
    await browser.close();
    server.close();
    process.exit(0);
})().catch(e => {
    console.error('FAIL @#' + (PASSED + 1) + ':', e.message);
    process.exit(1);
});
