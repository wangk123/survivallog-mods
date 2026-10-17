// dd_regression_test.js —— 下拉控件回归断言（本地 Chromium，无游戏依赖）
// 断言：①弹层贴身（按钮顶-弹层底 ∈ [3,10]px）②水平对齐 ③不出视口顶 ④两页按钮视觉等高
// ⑤弹层不遮按钮 ⑥激活态样式只落自身（连坐检查：外来按钮类不变）
const pw = require('C:/temp/baidu_up/node_modules/playwright-core');
const path = require('path');

(async () => {
  let browser;
  try { browser = await pw.chromium.launch({ channel: 'msedge' }); }
  catch (e) { browser = await pw.chromium.launch({ channel: 'chrome' }); }
  const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  await page.goto('file:///' + path.resolve(__dirname, 'dd_regression_harness.html').replace(/\\/g, '/'));
  const fails = [];
  const ok = (cond, msg) => { console.log((cond ? '✓ ' : '✗ ') + msg); if (!cond) fails.push(msg); };

  for (const [pane, wrapId, btnId] of [['A(仿调度机队 body zoom .845)', 'wrapA', 'btnA'], ['B(仿交易页 祖先zoom .62)', 'wrapB', 'btnB']]) {
    const st = await page.evaluate(([w, b]) => {
      const pop = window.__open(w);
      const pr = pop.getBoundingClientRect(), br = document.getElementById(b).getBoundingClientRect(), wr = document.getElementById(w).getBoundingClientRect();
      return { gap: br.top - (pr.top + pr.height), dx: Math.abs(pr.left - br.left), top: pr.top, overlap: pr.bottom - br.top, btnH: br.height, btnW: br.width, wrapPos: getComputedStyle(document.getElementById(w)).position };
    }, [wrapId, btnId]);
    ok(st.gap >= 3 && st.gap <= 10, `${pane} 弹层贴身 gap=${st.gap.toFixed(1)}px`);
    ok(st.dx <= 2, `${pane} 水平对齐差=${st.dx.toFixed(1)}px`);
    ok(st.top >= 0, `${pane} 弹层不出视口顶 top=${st.top.toFixed(0)}`);
    ok(st.overlap <= 0.5, `${pane} 弹层不遮按钮 overlap=${st.overlap.toFixed(1)}px`);
    ok(st.wrapPos === 'relative', `${pane} 包裹器 relative（弹层锚点）`);
    if (pane.startsWith('A')) globalThis.__hA = st.btnH; else ok(Math.abs(st.btnH - globalThis.__hA) <= 1.2, `两页按钮视觉等高 A=${globalThis.__hA.toFixed(1)} B=${st.btnH.toFixed(1)} 差=${Math.abs(st.btnH - globalThis.__hA).toFixed(1)}px`);
  }
  // 连坐检查：我们按钮挂 .on，外来按钮 computed 不变
  const lk = await page.evaluate(() => {
    const fb = document.querySelector('#rowB .foreign');
    const before = getComputedStyle(fb).color + '|' + getComputedStyle(fb).backgroundColor + '|' + fb.className;
    document.getElementById('btnB').classList.add('on');
    const after = getComputedStyle(fb).color + '|' + getComputedStyle(fb).backgroundColor + '|' + fb.className;
    return before === after;
  });
  ok(lk, '激活态不连坐外来按钮');
  await browser.close();
  console.log(fails.length ? `\n== ${fails.length} 项失败 ==` : '\n== 全部通过 ==');
  process.exit(fails.length ? 1 : 0);
})().catch(e => { console.error('ERR:', e.message); process.exit(2); });
