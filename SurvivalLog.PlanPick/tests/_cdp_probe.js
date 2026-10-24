// PlanPick 搜索框崩溃现场解剖（CDP 白盒）：node SurvivalLog.PlanPick/tests/_cdp_probe.js
// 前置：游戏已启动（ViewportProbe DevTools.Enabled=true → 9222），RogueSelect 是 Root 页内同源 iframe
//       （非独立 CDP 目标）——按 frame 找，evaluate 走 frame。
// 行为：连 9222 → 等 RogueSelect frame → 开弹层 → 插桩（console/postMessage/DOM移除/焦点）→
//       聚焦+真实点击搜索框 → 汇报 page/Root 两层证据。
const sleep = ms => new Promise(r => setTimeout(r, ms));
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

(async () => {
    let browser = null;
    for (let i = 0; i < 120 && !browser; i++) {          // 等 9222 开（最长 10 分钟）
        try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); }
        catch { await sleep(5000); }
    }
    if (!browser) { console.log('CDP_9222_NEVER_OPENED'); process.exit(1); }
    console.log('CDP_CONNECTED');

    const ctx = browser.contexts()[0];
    if (!ctx) { console.log('NO_CONTEXT'); process.exit(1); }

    // 轮询找 RogueSelect frame（页面打开时才存在；再等 10 分钟）
    let page = null, frame = null;
    for (let i = 0; i < 120 && !frame; i++) {
        for (const p of ctx.pages()) {
            const f = p.frames().find(x => x.url().includes('RogueSelect'));
            if (f) { page = p; frame = f; break; }
        }
        if (!frame) await sleep(5000);
    }
    if (!frame) {
        console.log('ROGUESELECT_FRAME_NOT_FOUND; pages=' + ctx.pages().map(p => p.url()).join(' | '));
        process.exit(1);
    }
    console.log('TARGET_PAGE:', page.url());
    console.log('TARGET_FRAME:', frame.url());

    const log = [];
    page.on('console', m => log.push('[console.' + m.type() + '] ' + m.text().slice(0, 200)));
    page.on('pageerror', e => log.push('[pageerror] ' + String(e).slice(0, 300)));

    const before = await frame.evaluate(() => ({
        readyState: document.readyState,
        hasEntry: !!document.getElementById('ppEntry'),
        hasOverlay: !!document.querySelector('.pp-overlay'),
        rootFrames: Array.from(parent.document.querySelectorAll('iframe')).map(f => f.id + ':display=' + (f.style.display || '')),
    })).catch(e => ({ err: String(e) }));
    console.log('BEFORE:', JSON.stringify(before));

    // 1) 打开弹层
    await frame.evaluate(() => { const b = document.getElementById('ppEntry'); if (b) b.click(); }).catch(e => console.log('OPEN_ERR ' + e));
    await sleep(800);
    const opened = await frame.evaluate(() => ({
        overlayOpen: !!document.querySelector('.pp-overlay.open'),
        inputCount: document.querySelectorAll('input').length,
        searchId: !!document.getElementById('ppSearch'),
    })).catch(e => ({ err: String(e) }));
    console.log('OPENED:', JSON.stringify(opened));

    // 2) 插桩
    await frame.evaluate(() => {
        window.__ppLog = [];
        try {
            const origPost = parent.postMessage.bind(parent);
            parent.postMessage = (msg, org) => { try { window.__ppLog.push('[post] ' + JSON.stringify(msg).slice(0, 140)); } catch (e) { } return origPost(msg, org); };
        } catch (e) { window.__ppLog.push('[patch-post-err] ' + e); }
        new MutationObserver(muts => {
            for (const m of muts) for (const n of m.removedNodes) window.__ppLog.push('[dom-remove] ' + (n.id || n.className || n.nodeName));
        }).observe(document.documentElement, { childList: true });
        window.addEventListener('blur', () => window.__ppLog.push('[win.blur]'));
        window.addEventListener('focus', () => window.__ppLog.push('[win.focus]'));
        document.addEventListener('focusin', e => window.__ppLog.push('[focusin] ' + (e.target.id || e.target.className || e.target.tagName)));
        window.addEventListener('beforeunload', () => window.__ppLog.push('[beforeunload]'));
    }).catch(e => console.log('INSTRUMENT_ERR ' + e));
    // Root 侧插桩（iframe 增删）
    await page.evaluate(() => {
        window.__rootLog = [];
        new MutationObserver(muts => {
            for (const m of muts) for (const n of m.removedNodes) window.__rootLog.push('[root.dom-remove] ' + (n.id || n.nodeName));
            for (const m of muts) for (const n of m.addedNodes) window.__rootLog.push('[root.dom-add] ' + (n.id || n.nodeName));
        }).observe(document.body, { childList: true, subtree: true });
    }).catch(e => console.log('ROOT_INSTRUMENT_ERR ' + e));

    // 3) 程序化聚焦
    console.log('FOCUSING...');
    await frame.evaluate(() => { const i = document.getElementById('ppSearch'); if (i) i.focus(); }).catch(e => console.log('FOCUS_ERR ' + e));
    await sleep(1500);
    let st = await frame.evaluate(() => ({ alive: true, log: (window.__ppLog || []).slice(), focused: document.activeElement && document.activeElement.id, overlayOpen: !!document.querySelector('.pp-overlay.open') })).catch(e => ({ alive: false, err: String(e) }));
    console.log('AFTER_FOCUS:', JSON.stringify(st).slice(0, 1200));

    // 4) 真实鼠标点击（playwright 走 frame locator）
    try { await frame.locator('#ppSearch').click({ timeout: 4000 }); console.log('CLICK_OK'); }
    catch (e) { console.log('CLICK_ERR: ' + String(e).split('\n')[0]); }
    await sleep(2500);
    st = await frame.evaluate(() => ({ alive: true, log: (window.__ppLog || []).slice(), overlayOpen: !!document.querySelector('.pp-overlay.open'), bodyKids: document.body.children.length, vis: document.visibilityState })).catch(e => ({ alive: false, err: String(e) }));
    console.log('AFTER_CLICK:', JSON.stringify(st).slice(0, 1800));
    const rootState = await page.evaluate(() => ({
        log: (window.__rootLog || []).slice(0, 30),
        frames: Array.from(document.querySelectorAll('iframe')).map(f => f.id + ':display=' + (f.style.display || '') + ':w=' + f.getBoundingClientRect().width),
    })).catch(e => ({ err: String(e) }));
    console.log('ROOT_STATE:', JSON.stringify(rootState).slice(0, 1200));
    console.log('CONSOLE_TAIL:', JSON.stringify(log.slice(-20), null, 1));
    await browser.close();   // 仅断开 CDP 连接，不影响游戏
})().catch(e => { console.error('PROBE_ERR', e); process.exit(1); });
