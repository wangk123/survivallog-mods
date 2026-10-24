// 官方工作台搜索 IME 机制对照：node SurvivalLog.PlanPick/tests/_cdp_official.js
// 监听 ToolTable 页的 composition/keydown/input/value —— 官方中文输入到底是
// Chromium 原生 IME（compositionstart 等）还是 C# 侧投喂（无组合事件、value 突变）。
const sleep = ms => new Promise(r => setTimeout(r, ms));
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const t = () => new Date().toTimeString().slice(0, 8);

(async () => {
    let browser = null;
    for (let i = 0; i < 60 && !browser; i++) { try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); } catch { await sleep(3000); } }
    if (!browser) { console.log(t() + ' NO_CDP'); process.exit(1); }
    console.log(t() + ' CDP_OK —— 请打开工作台/烹饪台（带配方搜索的界面）…');
    const ctx = browser.contexts()[0];

    let page = null, frame = null;
    for (let i = 0; i < 120 && !frame; i++) {
        for (const p of ctx.pages()) {
            const f = p.frames().find(x => /ToolTable|Cooking/i.test(x.url()));
            if (f) { page = p; frame = f; break; }
        }
        if (!frame) await sleep(3000);
    }
    if (!frame) { console.log(t() + ' NO_TOOLTABLE_FRAME pages=' + ctx.pages().map(p => p.url()).join('|')); process.exit(1); }
    console.log(t() + ' FRAME_LOCKED ' + frame.url().slice(-45) + ' —— 请点配方搜索框用搜狗打两个汉字…');

    await frame.evaluate(() => {
        window.__o = [];
        const L = (a, b) => window.__o.push(a + ':' + (b || ''));
        document.addEventListener('compositionstart', e => L('compStart', e.data), true);
        document.addEventListener('compositionupdate', e => L('compUpd', e.data), true);
        document.addEventListener('compositionend', e => L('compEnd', e.data), true);
        document.addEventListener('keydown', e => L('key', e.key + (e.isComposing ? '(comp)' : '')), true);
        document.addEventListener('input', e => {
            const el = e.target;
            L('input', (el.tagName === 'INPUT' ? 'val=[' + el.value + ']' : 'non-input') + (e.isComposing ? '(comp)' : ''));
        }, true);
        document.addEventListener('focusin', e => L('focusin', e.target.id || e.target.className || e.target.tagName), true);
    });

    let lastVal = '';
    for (let i = 0; i < 60; i++) {          // 5 分钟观察
        await sleep(5000);
        try {
            const r = await Promise.race([
                frame.evaluate(() => {
                    const i = document.getElementById('notes-search-input') || document.querySelector('input[type=text]');
                    return { ev: (window.__o || []).slice(-20), val: i ? i.value : '(no-input)', ae: document.activeElement ? (document.activeElement.id || document.activeElement.tagName) : '' };
                }),
                sleep(2500).then(() => { throw new Error('TO'); }),
            ]);
            if (r.ev && r.ev.length) { console.log(t() + ' EV ' + JSON.stringify(r.ev)); await frame.evaluate(() => { window.__o = []; }); }
            if (i % 12 === 11) console.log(t() + ' st val=[' + r.val + '] ae=' + r.ae);
            lastVal = r.val;
        } catch (e) { console.log(t() + ' DEAD ' + String(e).split('\n')[0]); break; }
    }
    console.log(t() + ' FINAL_VAL=[' + lastVal + ']  WATCH_END');
    await browser.close();
})().catch(e => { console.error(t() + ' ERR', e); process.exit(1); });
