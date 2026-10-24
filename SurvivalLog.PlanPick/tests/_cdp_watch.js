// PlanPick 输入法矩阵取证（watch 模式）：node SurvivalLog.PlanPick/tests/_cdp_watch.js
// 连上后只插桩：composition/keydown/focus/DOM移除/postMessage/console + 1s 心跳；
// 页面死亡（CDP evaluate 失败/断连）时打出死亡时间戳。全程不动页面。
const sleep = ms => new Promise(r => setTimeout(r, ms));
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }
const t = () => new Date().toTimeString().slice(0, 8);

(async () => {
    let browser = null;
    for (let i = 0; i < 60 && !browser; i++) {
        try { browser = await chromium.connectOverCDP('http://127.0.0.1:9222'); } catch { await sleep(3000); }
    }
    if (!browser) { console.log(t() + ' CDP_9222_NEVER_OPENED'); process.exit(1); }
    console.log(t() + ' CDP_CONNECTED');
    const ctx = browser.contexts()[0];

    let page = null, frame = null;
    for (let i = 0; i < 120 && !frame; i++) {
        for (const p of ctx.pages()) {
            const f = p.frames().find(x => x.url().includes('RogueSelect'));
            if (f) { page = p; frame = f; break; }
        }
        if (!frame) await sleep(3000);
    }
    if (!frame) { console.log(t() + ' NO_ROGUESELECT_FRAME pages=' + ctx.pages().map(p => p.url()).join('|')); process.exit(1); }
    console.log(t() + ' FRAME_LOCKED ' + frame.url().slice(-40));

    page.on('console', m => console.log(t() + ' [console.' + m.type() + '] ' + m.text().slice(0, 200)));
    page.on('pageerror', e => console.log(t() + ' [pageerror] ' + String(e).slice(0, 200)));

    // 插桩（幂等）：组合输入/键盘/焦点/输入值/页面移除
    await frame.evaluate(() => {
        if (window.__w) return; window.__w = { t: 0, log: [] };
        const L = (tag, v) => { try { window.__w.log.push(Date.now() + ' ' + tag + ' ' + (v || '')); if (window.__w.log.length > 200) window.__w.log.shift(); } catch (e) { } };
        document.addEventListener('compositionstart', e => L('comp.start', e.data), true);
        document.addEventListener('compositionupdate', e => L('comp.update', e.data), true);
        document.addEventListener('compositionend', e => L('comp.end', e.data), true);
        document.addEventListener('keydown', e => L('key.down', e.key + '/' + e.keyCode + '/isComp=' + e.isComposing), true);
        document.addEventListener('textInput', e => L('text.input', e.data), true);
        document.addEventListener('focusin', e => L('focusin', e.target.id || e.target.className || e.target.tagName), true);
        document.addEventListener('input', e => L('input', (e.target.value || '').slice(0, 20) + '/comp=' + (e.isComposing ? 1 : 0)), true);
        new MutationObserver(ms => { for (const m of ms) for (const n of m.removedNodes) L('dom-remove', n.id || n.className || n.nodeName); })
            .observe(document.documentElement, { childList: true });
        window.addEventListener('beforeunload', () => L('beforeunload'));
    }).catch(e => console.log(t() + ' INSTRUMENT_ERR ' + e));

    console.log(t() + ' INSTRUMENTED —— 等你操作（真实点击/打字），每 1s 心跳，页面死亡会打出 DEAD');

    let dead = 0;
    for (let i = 0; i < 900; i++) {          // 观察 15 分钟
        await sleep(1000);
        try {
            const r = await Promise.race([
                frame.evaluate(() => ({ a: 1, log: window.__w ? window.__w.log.slice(-15) : [], v: (document.getElementById('ppSearch') || {}).value || '', ov: !!document.querySelector('.pp-overlay.open') })),
                sleep(2500).then(() => { throw new Error('EVAL_TIMEOUT'); }),
            ]);
            dead = 0;
            if (r.log && r.log.length) { console.log(t() + ' EVENTS ' + JSON.stringify(r.log)); window.__wFlushed = true; }
            try { await frame.evaluate(() => { if (window.__w) window.__w.log = []; }); } catch (e) { }
            if (i % 15 === 0) console.log(t() + ' HEARTBEAT alive val="' + r.v + '" overlay=' + r.ov);
        } catch (e) {
            dead++;
            console.log(t() + ' DEAD(' + dead + ') ' + String(e).split('\n')[0]);
            if (dead >= 3) { console.log(t() + ' PAGE_GONE —— 判定死亡（evaluate 连续失败）'); break; }
            await sleep(1500);
        }
    }
    console.log(t() + ' WATCH_END');
    await browser.close().catch(() => { });
})().catch(e => { console.error(t() + ' PROBE_ERR', e); process.exit(1); });
