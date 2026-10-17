// CDP 金标准验证：直连游戏 WebView（需 ViewportProbe DevTools 开关已开启，游戏在运行）
// 用法：node tools/devtools_cdp_test.js
const { chromium } = require('C:/temp/baidu_up/node_modules/playwright-core');

(async () => {
  const browser = await chromium.connectOverCDP('http://localhost:9222');
  const ctx = browser.contexts()[0];
  const pages = ctx.pages();
  console.log('connected, pages =', pages.length);
  for (const page of pages) {
    console.log('PAGE url =', page.url());
    console.log('TITLE =', await page.title());
    const frames = page.frames().map(f => f.url().split('?')[0]);
    console.log('FRAMES =', JSON.stringify(frames, null, 1));
    // 证明运行时可注入、可观测：读 Root 上下文实时状态
    const probe = await page.evaluate(() => ({
      vueApp: !!document.querySelector('#app'),
      iframes: Array.from(document.querySelectorAll('iframe')).map(f => ({
        src: (f.src || '').split('?')[0].slice(-48),
        vis: f.clientWidth + 'x' + f.clientHeight,
      })),
      consoleCapture: 'eval-works',
    }));
    console.log('RUNTIME PROBE =', JSON.stringify(probe, null, 1));
  }
  await browser.close(); // connectOverCDP 下仅断开连接，不影响游戏
  console.log('OK: CDP 连接、页面枚举、运行时 evaluate 全部通过');
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
