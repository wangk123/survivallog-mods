// dd_placement_live.js —— 筛选按钮落位活体探查（开发机专用，需 ViewportProbe DevTools 9222）
// 用法：
//   node tools/dd_placement_live.js            → 报告两页左栏布局（元素 rect 清单+现有按钮/外来按钮位置）+ 截图
//   node tools/dd_placement_live.js DroneHub <left> <top>   → 把现有 .sltf-dd-btn 活体挪到 (left,top)（fixed 坐标）+ 截图
//   node tools/dd_placement_live.js Trade <left> <top>
// 依赖：playwright-core @ C:/temp/baidu_up/node_modules（同 devtools_cdp_test.js）
const pw = require('C:/temp/baidu_up/node_modules/playwright-core');

const PAGES = ['DroneHub', 'Trade'];

(async () => {
  const args = process.argv.slice(2);
  const browser = await pw.chromium.connectOverCDP('http://localhost:9222');
  const ctx = browser.contexts()[0];

  function frameOf(pageId) {
    for (const p of ctx.pages())
      for (const f of p.frames())
        try { if (f.frameElement && f.frameElement().then) { /* async not sync */ } } catch (e) {}
    return null;
  }
  // frameElement 是 async——改走 evaluate 自报（同 cabinet_views_cdp.js 的路数）
  async function findFrame(pageId) {
    for (const p of ctx.pages()) {
      for (const f of p.frames()) {
        const id = await f.evaluate(() => (window.frameElement && window.frameElement.id) || '').catch(() => '');
        if (id === pageId) return { page: p, frame: f };
      }
    }
    return null;
  }

  const target = args[0] && PAGES.includes(args[0]) ? [args[0]] : PAGES;

  for (const pageId of target) {
    const hit = await findFrame(pageId);
    if (!hit) { console.log(`[${pageId}] iframe 未驻留（先在游戏里打开一次该界面）`); continue; }
    const { page, frame } = hit;

    if (args.length >= 3) {
      const left = parseFloat(args[1]), top = parseFloat(args[2]);
      await frame.evaluate(([l, t]) => {
        const dd = document.querySelector('.sltf-dd-btn');
        if (!dd) return 'NOBTN';
        dd.style.position = 'fixed';
        dd.style.left = l + 'px'; dd.style.top = t + 'px'; dd.style.bottom = 'auto'; dd.style.right = 'auto';
        return 'MOVED';
      }, [left, top]).then(r => console.log(`[${pageId}] 按钮挪到 fixed(${left},${top}) → ${r}`));
    } else {
      const report = await frame.evaluate(() => {
        const out = [];
        const add = (label, el) => {
          if (!el) { out.push(label + ': null'); return; }
          const r = el.getBoundingClientRect();
          out.push(`${label}: class=${el.className || ''} id=${el.id || ''} rect=${Math.round(r.left)},${Math.round(r.top)} ${Math.round(r.width)}x${Math.round(r.height)}`);
        };
        const dd = document.querySelector('.sltf-dd-btn');
        add('现有筛选按钮', dd);
        const bar = document.querySelector('.bag-tabs');
        add('页签行.bag-tabs', bar);
        add('滚动区', document.querySelector('.bag-tabs-scroll'));
        add('标题栏.tb', document.querySelector('.tb'));
        add('负重条#bagCap', document.getElementById('bagCap'));
        add('网格wrap', document.querySelector('.gridwrap') || document.querySelector('[class*=grid]'));
        // 左栏容器：页签行的父级链向上找一个像列容器的
        let col = bar ? bar.parentElement : null;
        for (let i = 0; col && i < 4; i++) {
          const r = col.getBoundingClientRect();
          if (r.height > 300) { add('左栏容器(猜测)', col); break; }
          col = col.parentElement;
        }
        // 外来按钮扫描：左栏内全部 button（官方 sortb/scroll-btn/tab 之外的就是别家 mod 或我们自己的）
        if (bar) {
          let scope = bar.parentElement;
          for (let i = 0; scope && i < 3; i++) scope = scope.parentElement;
          const btns = scope ? scope.querySelectorAll('button') : [];
          const seen = [];
          btns.forEach(b => {
            const r = b.getBoundingClientRect();
            if (r.width <= 0) return;
            seen.push(`btn ${b.id || b.className} @${Math.round(r.left)},${Math.round(r.top)} ${Math.round(r.width)}x${Math.round(r.height)}`);
          });
          out.push('--- 区域内按钮清单 ---'); out.push(...seen.slice(0, 40));
        }
        out.push(`viewport=${innerWidth}x${innerHeight}`);
        return out.join('\n');
      }).catch(e => 'evaluate 失败: ' + e.message);
      console.log(`===== [${pageId}] =====\n${report}\n`);
    }

    const shot = `dd_live_${pageId}.png`;
    await page.screenshot({ path: shot }).catch(e => console.log(`[${pageId}] 截图失败: ${e.message}`));
    if (!args.length || args.length >= 4) console.log(`[${pageId}] 截图: ${shot}`);
  }
  await browser.close();
})().catch(e => { console.error('连接失败:', e.message, '\n（9222 未开？先启动游戏，ViewportProbe DevTools 需为 true）'); process.exit(3); });
