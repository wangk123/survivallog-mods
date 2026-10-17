// dd_verify_fix.js —— wave-6 修复的活体验证（不重启游戏：新 page.js 注入接管 window.__SLTF__）
// 段1：旧代码复现「点一下跳走」+ 污染源取证；段2：新代码端到端（尺寸/稳定/贴身）
const pw = require('C:/temp/baidu_up/node_modules/playwright-core');
const fs = require('fs');
const SRC = fs.readFileSync(__dirname + '/../SurvivalLog.CabinetEverywhere/tagfilter.page.js', 'utf8');
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const b = await pw.chromium.connectOverCDP('http://localhost:9222');
  const ctx = b.contexts()[0];
  let root = null; const frames = {};
  for (const p of ctx.pages()) {
    for (const f of p.frames()) {
      if (f === p.mainFrame()) { const has = await f.evaluate(() => !!document.getElementById('iframe-container')).catch(() => false); if (has) { root = f; rootPage = p; } }
      const id = await f.evaluate(() => (window.frameElement && window.frameElement.id) || '').catch(() => '');
      if (id) frames[id] = { page: p, frame: f };
    }
  }
  console.log('驻留:', Object.keys(frames).join(','), '| root:', !!root);
  if (!frames.Trade || !frames.DroneHub) { console.log('!! Trade/DroneHub 未全部驻留'); process.exit(4); }

  const rectOf = (f, sel) => f.evaluate((s) => { const el = document.querySelector(s); if (!el) return null; const r = el.getBoundingClientRect(); return { l: Math.round(r.left), t: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }; }, sel);
  const fmt = r => r ? `${r.l},${r.t} ${r.w}x${r.h}` : 'null';

  // ===== 段1：旧代码复现（Trade）=====
  const t = frames.Trade.frame;
  const b0 = await rectOf(t, '.sltf-dd-btn');
  if (!b0) { console.log('[段1] Trade 无按钮'); process.exit(5); }
  await t.evaluate(() => document.querySelector('.sltf-dd-btn').click());
  await sleep(7000);
  const b1 = await rectOf(t, '.sltf-dd-btn');
  const scan = await t.evaluate(() => {
    const dd = document.querySelector('.sltf-dd-btn');
    let c2 = dd.parentElement; while (c2 && c2.getBoundingClientRect().height < 300) c2 = c2.parentElement;
    const colR = c2.getBoundingClientRect(); const hits = [];
    c2.querySelectorAll('button, [class*=cap], [id*=cap]').forEach(el => {
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.top <= colR.bottom - 30) return;
      hits.push((el.className || el.id).toString().slice(0, 16) + '@' + Math.round(r.left) + 'w' + Math.round(r.width));
    });
    return hits.join(' | ');
  });
  console.log(`[段1·复现] 点击前: ${fmt(b0)} → 弹层开7秒后: ${fmt(b1)} ${JSON.stringify(b0) === JSON.stringify(b1) ? '（未跳）' : '<<< 跳了·复现成功'}`);
  console.log(`[段1·污染源] 旧扫描底带命中: ${scan}`);
  await t.evaluate(() => document.querySelector('.sltf-dd-btn') && document.querySelector('.sltf-dd-btn').click());

  // ===== 段2：注入新代码接管 =====
  await root.evaluate(src => { try { window.__SLTF__ = undefined; } catch (e) {} eval(src); }, SRC);
  console.log('[段2] 新 page.js 已注入 Root，等 C# setData 喂数据…');
  await sleep(8000);

  for (const name of ['Trade', 'DroneHub']) {
    const fr = frames[name].frame, pg = frames[name].page;
    const dd = await rectOf(fr, '.sltf-dd-btn');
    if (!dd) { console.log(`[${name}] !! 新按钮未出现（setData 未到？）`); continue; }
    const nbSel = name === 'Trade' ? '.bt-view-btn' : '#sortBagBtn2';
    const nb = await rectOf(fr, nbSel);
    const kInfo = await fr.evaluate(() => { const d = document.querySelector('.sltf-dd-btn'); const cs = getComputedStyle(d); return { cssH: cs.height, font: cs.fontSize, k: d.__sltfK, visH: d.__sltfVisH, recH: d.__sltfCssH }; });
    console.log(`[${name}] 新按钮: ${fmt(dd)} | css高=${kInfo.cssH} 字号=${kInfo.font} k=${kInfo.k && kInfo.k.toFixed(3)} visH=${kInfo.visH}`);
    if (nb) console.log(`[${name}] 邻居(${nbSel}): ${fmt(nb)} | 高差=${Math.abs(dd.h - nb.h)}px ${Math.abs(dd.h - nb.h) <= 1 ? '✓齐平' : '✗不齐'}`);

    // 点击开弹层 → 跨 2 个刷新周期量稳定性
    await fr.evaluate(() => document.querySelector('.sltf-dd-btn').click());
    await sleep(400);
    const pop = await rectOf(fr, '.sltf-dd-pop');
    const ddOpen = await rectOf(fr, '.sltf-dd-btn');
    const gap = pop && ddOpen ? ddOpen.t - (pop.t + pop.h) : null;
    console.log(`[${name}] 弹层: ${fmt(pop)} | 按钮顶-弹层底=${gap}px ${gap !== null && gap >= 0 && gap <= 8 ? '✓贴身' : '?检查'}`);
    await sleep(12000); // ≥2 个 5s 周期
    const ddAfter = await rectOf(fr, '.sltf-dd-btn');
    console.log(`[${name}] 弹层开着 12 秒后按钮: ${fmt(ddAfter)} ${JSON.stringify(ddOpen) === JSON.stringify(ddAfter) ? '✓稳定未跳' : '✗仍会跳!'}`);
    await fr.evaluate(() => document.querySelector('.sltf-dd-btn') && document.querySelector('.sltf-dd-btn').click());
    await sleep(400);
    const ddClosed = await rectOf(fr, '.sltf-dd-btn');
    console.log(`[${name}] 关弹层后按钮: ${fmt(ddClosed)} ${JSON.stringify(ddClosed) === JSON.stringify(ddAfter) ? '✓关了也不跳' : '✗关弹层跳了!'}`);
    const shot = `dd_verify_${name}.png`;
    await pg.screenshot({ path: shot }).catch(() => {});
    console.log(`[${name}] 截图: ${shot}`);
  }
  await b.close();
  console.log('== 验证完成 ==');
})().catch(e => { console.error('ERR:', e.message); process.exit(3); });
