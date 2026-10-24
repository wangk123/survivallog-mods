// 焦点桥三问诊断：node SurvivalLog.PlanPick/tests/_cdp_focus.js
// ① 真实点击到不到 #ppSearch（mousedown/click/pointerdown 全监听）② 程序化 focus 后探针脚本返回值
// ③ C# 侧探针是否在跑（改写 window 上探针同款函数对照）。跑 3 分钟流式输出。
const sleep = ms => new Promise(r => setTimeout(r, ms));
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const t = () => new Date().toTimeString().slice(0, 8);

(async () => {
    let browser = null;
    for (let i = 0; i < 40 && !browser; i++) { try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); } catch { await sleep(3000); } }
    if (!browser) { console.log(t() + ' NO_CDP'); process.exit(1); }
    const ctx = browser.contexts()[0];
    let page = null, frame = null;
    for (let i = 0; i < 60 && !frame; i++) {
        for (const p of ctx.pages()) { const f = p.frames().find(x => x.url().includes('RogueSelect')); if (f) { page = p; frame = f; break; } }
        if (!frame) await sleep(3000);
    }
    if (!frame) { console.log(t() + ' NO_FRAME'); process.exit(1); }
    console.log(t() + ' LOCKED。现在请你【真实点击一次搜索框】（30 秒内）…');

    await frame.evaluate(() => {
        window.__diag = [];
        ['pointerdown', 'mousedown', 'mouseup', 'click'].forEach(ev =>
            document.addEventListener(ev, e => window.__diag.push(ev + '@' + (e.target.id || e.target.className || e.target.tagName)), true));
        document.addEventListener('focusin', e => window.__diag.push('focusin@' + (e.target.id || e.target.tagName)), true);
    });

    for (let i = 0; i < 36; i++) {          // 3 分钟
        await sleep(5000);
        try {
            const r = await Promise.race([
                frame.evaluate(() => ({
                    diag: (window.__diag || []).slice(-12),
                    ae: document.activeElement ? (document.activeElement.id || document.activeElement.tagName) : 'none',
                    hasInput: !!document.getElementById('ppSearch'),
                })),
                sleep(2500).then(() => { throw new Error('TIMEOUT'); }),
            ]);
            if (r.diag && r.diag.length) { console.log(t() + ' EVENTS ' + JSON.stringify(r.diag)); await frame.evaluate(() => { window.__diag = []; }); }
            if (i % 6 === 5) console.log(t() + ' state ae=' + r.ae + ' hasInput=' + r.hasInput);
        } catch (e) { console.log(t() + ' DEAD ' + String(e).split('\n')[0]); break; }
    }

    // 程序化聚焦 + 探针脚本对照
    const probe = await frame.evaluate(() => {
        const i = document.getElementById('ppSearch'); if (!i) return { err: 'no input' };
        i.focus();
        const d = document, ae = d.activeElement;
        const inp = (ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA')) ? 1 : 0;
        const realKeys = window.__ppRealKeys ? 1 : 0;
        return { focused: document.hasFocus(), ae: ae.id || ae.tagName, bits: inp + (realKeys ? 2 : 0) };
    }).catch(e => ({ err: String(e) }));
    console.log(t() + ' PROBE_FROM_CDP ' + JSON.stringify(probe));
    await browser.close();
})().catch(e => { console.error(t() + ' ERR', e); process.exit(1); });
