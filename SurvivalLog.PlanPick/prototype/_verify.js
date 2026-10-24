// 原型功能断言：node prototype/_verify.js  （serve.js 需在跑）
// 断言：官方三卡渲染/弹层打开/tab 计数/锁定原因弹泡/翻倍数学/确认扣点/今日候选原价
const assert = require('assert');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = process.argv[2] || '8612';

(async () => {
    const browser = await chromium.launch({ executablePath: CHROME, headless: true });
    const page = await browser.newPage({ viewport: { width: 1536, height: 864 } });
    await page.goto('http://127.0.0.1:' + PORT + '/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(300);

    // 1) 官方三连卡（今日候选样例，纯背景复刻）+ 入口按钮存在 + 不模拟锁定态
    assert.equal(await page.locator('#officialGrid .card-slot').count(), 3, 'official 3 cards');
    assert.equal(await page.locator('#officialGrid .card.locked').count(), 0, 'official area never shows locked');
    assert.ok(await page.locator('#ppEntry').isVisible(), 'entry button visible');

    // 1b) 确认弹窗无提示句（作者 2026-10-24 反馈）
    await page.click('#ppEntry');
    await page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive)').first().click();
    await page.waitForTimeout(150);
    assert.equal(await page.locator('#cfHint').count(), 0, 'hint line removed');
    await page.click('#cfCancel');
    await page.waitForTimeout(150);

    // 2) tab 计数 = 1(全部) + 4 分类；全部 = 465
    const tabCnt = await page.locator('.pp-tab').count();
    assert.equal(tabCnt, 5, 'tabs = all+4');
    const allTabText = await page.locator('.pp-tab[data-tab="all"]').innerText();
    assert.ok(allTabText.includes('465'), 'all tab count 465: ' + allTabText);

    // 2b) 二级筛选：全部 tab 无 chips；属性 tab chips 换行无横向滚动、溢出出现"更多"可展开
    assert.equal(await page.locator('.pp-sub').count(), 0, 'no sub chips on all-tab');
    assert.equal(await page.locator('.footer-right #ppEntry').count(), 1, 'entry button in footer-right');
    await page.locator('.pp-tab[data-tab="attribute"]').click();
    await page.waitForTimeout(250);
    const subChips = await page.locator('.pp-sub').count();
    assert.ok(subChips > 5, 'sub chips exist on category tab: ' + subChips);
    const scrollable = await page.evaluate(() => { const r = document.getElementById('ppSubRow'); return r ? r.scrollWidth - r.clientWidth : 0; });
    assert.ok(scrollable <= 0, 'no horizontal overflow on sub row: ' + scrollable);
    const beforeSub = await page.locator('.pp-card').count();
    await page.locator('.pp-sub[data-sub="__solo"]').click();
    await page.waitForTimeout(200);
    const afterSub = await page.locator('.pp-card').count();
    assert.ok(afterSub > 0 && afterSub < beforeSub, 'solo filter narrows: ' + beforeSub + ' -> ' + afterSub);
    await page.locator('.pp-sub[data-sub="__all"]').click();
    await page.waitForTimeout(150);
    // 功能强化 tab（64 系列）默认一行收起 + 更多展开可用
    await page.locator('.pp-tab[data-tab="function"]').click();
    await page.waitForTimeout(250);
    const moreVisible = await page.locator('#ppSubMore').isVisible();
    if (moreVisible) {
        await page.click('#ppSubMore');
        await page.waitForTimeout(300);
        assert.ok(await page.evaluate(() => document.getElementById('ppSubRow').classList.contains('expanded')), 'expand works');
        await page.click('#ppSubMore');
        await page.waitForTimeout(300);
    }
    await page.locator('.pp-tab[data-tab="attribute"]').click();
    await page.waitForTimeout(150);

    // 2c) 状态筛选"未解锁"：列表全为 locked；且一二级计数联动（tab 数下降、无 0 计数 chip）
    const attrTabFull = parseInt((await page.locator('.pp-tab[data-tab="attribute"] .cnt').innerText()));
    await page.locator('.pp-state[data-state="locked"]').click();
    await page.waitForTimeout(250);
    const stTotal = await page.locator('.pp-card').count();
    const stLocked = await page.locator('.pp-card.locked').count();
    assert.ok(stTotal > 0 && stTotal === stLocked, 'state filter locked-only: ' + stLocked + '/' + stTotal);
    const attrTabLocked = parseInt((await page.locator('.pp-tab[data-tab="attribute"] .cnt').innerText()));
    assert.ok(attrTabLocked < attrTabFull, 'tab count reacts to state: ' + attrTabFull + ' -> ' + attrTabLocked);
    const zeroChips = await page.evaluate(() => [...document.querySelectorAll('.pp-sub .cnt')].filter(e => parseInt(e.textContent) === 0).length);
    assert.equal(zeroChips, 0, 'no zero-count sub chips');
    const attrSumSeries = await page.evaluate(() => {
        const chips = [...document.querySelectorAll('.pp-sub')].filter(c => c.dataset.sub !== '__all');
        return chips.reduce((s, c) => s + parseInt(c.querySelector('.cnt').textContent), 0);
    });
    assert.ok(attrSumSeries > 0 && attrSumSeries === attrTabLocked, 'series chips sum == state-filtered tab count: ' + attrSumSeries + ' vs ' + attrTabLocked);
    await page.locator('.pp-state[data-state="all"]').click();
    await page.waitForTimeout(150);

    // 3) 第20天：存在 locked 卡与可购卡
    await page.waitForTimeout(200);
    const lockedCnt = await page.locator('.pp-card.locked').count();
    assert.ok(lockedCnt > 0, 'locked cards exist');
    const okCnt = await page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive)').count();
    assert.ok(okCnt > 0, 'affordable cards exist');

    // 4) 点 locked 卡 → 原因弹泡可见且含"解锁"字样
    await page.locator('.pp-card.locked').nth(5).click();
    await page.waitForTimeout(200);
    assert.ok(await page.locator('#ppWhy').isVisible(), 'why popup visible');
    const whyText = await page.locator('#ppWhy').innerText();
    assert.ok(/解锁|激活|专属|需要/.test(whyText), 'why text meaningful: ' + whyText.slice(0, 60));

    // 4b) 角色锁因用官方角色名（专属规划集中在家具扩建/功能强化；Config_PlayerSelect.Name_Local）
    await page.locator('.pp-tab[data-tab="expand"]').click();
    await page.locator('#roleSel').selectOption('0');
    await page.waitForTimeout(250);
    const roleWhy = await page.evaluate(() => {
        const cards = [...document.querySelectorAll('.pp-card.locked')];
        for (const c of cards) { c.click(); const t = document.getElementById('ppWhy'); if (t.className.includes('open') && /专属规划/.test(t.innerText)) return t.innerText; }
        return '';
    });
    assert.ok(/仅限「(打工仔|大学生|仓库管理员|退伍军人)( \/ .+)?」的专属规划/.test(roleWhy),
        'role lock reason uses official names: ' + roleWhy.replace(/\n/g, ' ').slice(0, 80));
    await page.locator('#ppWhy').click(); // 关掉（超时也会关）
    await page.locator('#roleSel').selectOption('0');
    await page.locator('.pp-card:not(.locked):not(.bought):not(.tooexpensive)').first().click();
    await page.waitForTimeout(200);
    assert.ok(await page.locator('#ppConfirm.open').isVisible(), 'confirm open');
    const base = parseInt(await page.locator('#cfBase').innerText());
    const pay = parseInt(await page.locator('#cfPay').innerText());
    assert.equal(pay, base * 2, 'pay = base x2 (' + base + ' -> ' + pay + ')');

    // 6) 确认 → 扣点 + 已激活
    const before = await page.locator('#ppPoints').innerText();
    await page.click('#cfOk');
    await page.waitForTimeout(200);
    const after = await page.locator('#ppPoints').innerText();
    assert.equal(parseInt(after), parseInt(before) - pay, 'points deducted');
    assert.ok((await page.locator('.pp-card.bought').count()) >= 1, 'bought stamp appears');

    // 7) 天数滑到 50：未解锁计数下降（D49 解锁）
    await page.locator('#daySlider').fill('50');
    await page.locator('#devbar').click(); // 触发渲染
    await page.waitForTimeout(200);
    const devStats50 = await page.locator('#devStats').innerText();
    await page.locator('#daySlider').fill('20');
    await page.waitForTimeout(200);
    const devStats20 = await page.locator('#devStats').innerText();
    const un = t => parseInt(t.match(/未解锁 (\d+)/)[1]);
    assert.ok(un(devStats50) < un(devStats20), 'day50 unlocks more: ' + devStats50 + ' vs ' + devStats20);

    // 8) 搜索"扩建"有结果（先回全部 tab，避免分类 tab 内搜不到）
    await page.locator('.pp-tab[data-tab="all"]').click();
    await page.waitForTimeout(150);
    await page.locator('#ppSearch').fill('扩建');
    await page.waitForTimeout(200);
    assert.ok((await page.locator('.pp-card').count()) > 0, 'search hits');

    console.log('ALL ASSERTIONS PASSED');
    await browser.close();
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
