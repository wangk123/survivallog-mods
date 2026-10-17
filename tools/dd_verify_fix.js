// dd_verify_fix.js —— 下拉按钮全量自动验证（轮询模式：等 9222 + 两页驻留后自动跑，输出报告文件）
// 用法：node tools/dd_verify_fix.js [--wait-min N]（默认 12 分钟内等界面打开）
const pw = require('C:/temp/baidu_up/node_modules/playwright-core');
const fs = require('fs');
const path = require('path');
const WAIT_MIN = parseFloat((process.argv.find(a => a.startsWith('--wait-min')) || '').split('=')[1]) || 12;
const OUT = path.join(__dirname, 'dd_verify_report.txt');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const lines = [];
const log = s => { lines.push(s); console.log(s); };

(async () => {
  // 轮询等游戏与两页驻留
  let ctx = null, deadline = Date.now() + WAIT_MIN * 60000;
  while (Date.now() < deadline) {
    try {
      const b = await pw.chromium.connectOverCDP('http://localhost:9222', { timeout: 3000 });
      ctx = b.contexts()[0];
      const have = {};
      for (const p of ctx.pages()) for (const f of p.frames()) { const id = await f.evaluate(() => (window.frameElement && window.frameElement.id) || '').catch(() => ''); if (id) have[id] = { page: p, frame: f }; }
      if (have.Trade && have.DroneHub) { await runTests(have, b); return; }
      await b.close();
    } catch (e) { /* 游戏未开/9222未起，继续等 */ }
    await sleep(5000);
  }
  log('== 超时：' + WAIT_MIN + ' 分钟内未等到 Trade+DroneHub 驻留 ==');
  fs.writeFileSync(OUT, lines.join('\n'), 'utf8');
  process.exit(4);
})().catch(e => { log('ERR: ' + e.message); fs.writeFileSync(OUT, lines.join('\n'), 'utf8'); process.exit(3); });

async function runTests(F, browser) {
  log('== 两页已驻留，开始验证 ' + new Date().toLocaleTimeString() + ' ==');
  const rectOf = (f, sel) => f.evaluate((s) => { const el = document.querySelector(s); if (!el) return null; const r = el.getBoundingClientRect(); return { l: Math.round(r.left), t: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }; }, sel);
  const fmt = r => r ? `${r.l},${r.t} ${r.w}x${r.h}` : 'null';
  for (const name of ['Trade', 'DroneHub']) {
    try {
      const fr = F[name].frame, pg = F[name].page;
      const dd = await rectOf(fr, '.sltf-dd-btn');
      if (!dd) { log(`[${name}] !! 按钮不存在`); continue; }
      const info = await fr.evaluate(() => {
        const dd = document.querySelector('.sltf-dd-btn');
        const cs = getComputedStyle(dd);
        const st = document.getElementById('sltf-dd-style');
        return { cssH: cs.height, font: cs.fontSize, pad: cs.padding, border: cs.borderRadius + '/' + cs.borderColor, donor: dd.__sltfDonor, styleRule: st ? st.textContent.slice(0, 160) : '无' };
      });
      log(`[${name}] 按钮=${fmt(dd)} cssH=${info.cssH} font=${info.font} donor=${JSON.stringify(info.donor)}`);
      log(`[${name}] 样式规则: ${info.styleRule}`);
      const nbSel = name === 'Trade' ? '.bt-view-btn' : '#sortBagBtn2';
      const nb = await rectOf(fr, nbSel);
      if (nb) { const dh = Math.abs(dd.h - nb.h); log(`[${name}] 邻居=${fmt(nb)} 高差=${dh}px ${dh <= 1 ? '✓齐平' : '✗不齐'} 间距=${dd.l - (nb.l + nb.w)}px ${dd.l >= nb.l + nb.w - 1 ? '✓不重叠' : '✗重叠'}`); }
      else log(`[${name}] 邻居${nbSel}不存在`);
      // 弹层贴身 + 稳定性
      await fr.evaluate(() => document.querySelector('.sltf-dd-btn').click());
      await sleep(400);
      const pop = await rectOf(fr, '.sltf-dd-pop');
      const ddO = await rectOf(fr, '.sltf-dd-btn');
      const gap = pop ? (ddO.t - pop.t - pop.h) : null;
      log(`[${name}] 弹层=${fmt(pop)} 按钮顶-弹层底=${gap}px ${gap !== null && gap >= -1 && gap <= 10 ? '✓贴身' : '✗脱锚'}`);
      await sleep(12000);
      const ddA = await rectOf(fr, '.sltf-dd-btn');
      log(`[${name}] 弹层开12秒后按钮=${fmt(ddA)} ${JSON.stringify(ddO) === JSON.stringify(ddA) ? '✓稳定' : '✗跳了'}`);
      await fr.evaluate(() => document.querySelector('.sltf-dd-btn') && document.querySelector('.sltf-dd-btn').click());
      await sleep(400);
      const ddC = await rectOf(fr, '.sltf-dd-btn');
      log(`[${name}] 关弹层后按钮=${fmt(ddC)} ${JSON.stringify(ddC) === JSON.stringify(ddA) ? '✓稳定' : '✗跳了'}`);
      await pg.screenshot({ path: path.join(__dirname, `dd_verify_${name}.png`) }).catch(() => {});
    } catch (e) { log(`[${name}] 测试异常: ${e.message}`); }
  }
  await browser.close();
  fs.writeFileSync(OUT, lines.join('\n'), 'utf8');
  log('== 报告已写 ' + OUT + ' ==');
  process.exit(0);
}
