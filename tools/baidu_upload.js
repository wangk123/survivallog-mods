// 百度网盘自动上传（发版流水线第 8 步之后的网盘代传）
// 用法：node tools/baidu_upload.js   （在仓库根目录执行）
// 依赖：C:\temp\baidu_up\node_modules 的 playwright-core + 系统 Chrome
//   （首次：mkdir C:\temp\baidu_up && cd /d C:\temp\baidu_up && npm i playwright-core）
// 登录态：tools/baidu.session.json（Playwright storageState 格式，失效则从浏览器重新导出）
// 流程（2026-10-15 v1.3.1 二改）：API 批删 /游戏/生存日志 内旧文件 → URL path 参数直达目录
//   （页面 list 请求 dir=/游戏/生存日志 即确认上下文就位）→ 页面上传按钮直传该目录
//   → 轮询目录 API 等全部文件出现 → API 核验。分享链接为文件夹级，换文件不影响。
// 实证坑（2026-10-09 v1.0.5 实战 + 2026-10-15 v1.3.1 网盘改版实录）：
//   ① 删除接口参数是 opera=delete（不是 oper），且需 newVerify=1&clienttype=0&app_id=250528&web=1
//   ②【已过时】"URL path 参数被路由吃掉、进目录必须 DOM 双击"是旧版结论；2026-10-15 改版后
//      DOM 文件列表虚拟化、headless/headed 均不渲染真实行（只剩 drag-copy 模板）——DOM 进目录
//      彻底不可行；同时新版 hash 支持 path 参数：#/index?category=all&path=<URL编码路径>，
//      页面会以该目录调 api/list（监听 request 验证过），上传按钮落点即该目录
//   ③ 行 checkbox 是 hover 才显示（hide-checkbox），DOM 勾选/全选皆不稳，删除走 API 最稳
//   ④ 上传 input 有 3 个同 title，用 [accept="*/*"] 过滤 torrent 版后 .first()；按钮在页面
//      顶部工具栏，上传落点=页面当前目录上下文（见②），无需 DOM 进入
//   ⑤ IAB 内置浏览器不支持文件上传（filechooser 能力缺失），必须本脚本方式
//   ⑥ filemanager 的 move/copy/rename 在 2026-10-15 实测被风控恒 errno=2（delete 当天首个
//      请求可用），"根目录上传再 API 挪运"的路子走不通，别再试
//   ⑦ 上传完成判定不看 DOM 传输列表文本（改版后不稳），轮询目录 API 文件名齐全最可靠
const fs = require('fs');
const path = require('path');
let chromium;
try { ({ chromium } = require('playwright-core')); }
catch { ({ chromium } = require('C:/temp/baidu_up/node_modules/playwright-core')); }

const SESSION = path.join(__dirname, 'baidu.session.json');
const SRC = path.join(__dirname, '..', 'release', 'baidu_upload');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const DIR = '/游戏/生存日志';
const PAGE_URL = 'https://pan.baidu.com/disk/main#/index?category=all&path=' + encodeURIComponent(DIR);

(async () => {
  const files = fs.readdirSync(SRC).filter(f => !fs.statSync(path.join(SRC, f)).isDirectory() && !f.endsWith('.lnk'));
  if (files.length === 0) throw new Error('release\baidu_upload 为空：先跑 tools\publish.ps1');
  for (const f of files) console.log('待上传: ' + f);

  const state = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const ctx = await browser.newContext({ storageState: state, viewport: { width: 1600, height: 900 } });
  const page = await ctx.newPage();
  page.setDefaultTimeout(20000);
  const log = m => console.log('[' + new Date().toISOString().slice(11, 19) + '] ' + m);

  const api = (fn, arg) => page.evaluate(({ fn, arg }) => {
    const F = {
      list: async dir => {
        const r = await fetch('https://pan.baidu.com/api/list?dir=' + encodeURIComponent(dir) + '&num=1000', { credentials: 'include' });
        return await r.json();
      },
      delete: async paths => {
        let tk = '';
        try { tk = (window.yunData && window.yunData.bdstoken) || ''; } catch { }
        if (!tk) { const m = document.documentElement.innerHTML.match(/"bdstoken":"([^"]+)"/); tk = m ? m[1] : ''; }
        const url = 'https://pan.baidu.com/api/filemanager?async=2&onnest=fail&opera=delete&bdstoken=' + tk +
          '&newVerify=1&clienttype=0&app_id=250528&web=1&dp-logid=' + Date.now();
        const r = await fetch(url, {
          method: 'POST', credentials: 'include',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: 'filelist=' + encodeURIComponent(JSON.stringify(paths)),
        });
        return await r.json();
      },
    };
    return F[fn](arg);
  }, { fn, arg });

  // 0. 先落根目录上下文（API 要在 pan.baidu.com 上下文里 fetch）
  await page.goto('https://pan.baidu.com/disk/main', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);

  // 1. API 批删目标目录内全部旧文件
  const old = await api('list', DIR);
  const oldPaths = (old.list || []).map(f => f.path);
  log('目标目录现存旧文件: ' + oldPaths.length);
  if (oldPaths.length > 0) {
    const delResp = await api('delete', oldPaths);
    if (delResp.errno !== 0) { console.error('批删失败: ' + JSON.stringify(delResp)); process.exit(3); }
    log('批删成功 errno=0');
    await page.waitForTimeout(3000);
  }

  // 2. hash 从根目录切到目标目录（SPA 路由变化才会触发 list），监听请求验证目录上下文
  const seenDirs = [];
  const onReq = r => { if (r.url().includes('api/list')) { const m = r.url().match(/dir=([^&]*)/); if (m) seenDirs.push(decodeURIComponent(m[1])); } };
  page.on('request', onReq);
  await page.goto(PAGE_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(6000);
  page.off('request', onReq);
  if (!seenDirs.some(d => d === DIR)) {
    console.error('页面未进入目标目录（list 未见 dir=' + DIR + '，实际: ' + JSON.stringify([...new Set(seenDirs)]) + '）');
    process.exit(4);
  }
  log('页面目录上下文确认: ' + DIR);

  // 3. 顶部工具栏上传按钮直传（落点=当前目录上下文）
  const input = page.locator('input[title="点击选择文件"][accept="*/*"]').first();
  await input.setInputFiles(files.map(f => path.join(SRC, f)));
  log('已提交上传队列 ' + files.length + ' 文件 → ' + DIR);

  // 4. 轮询目录 API 直到全部文件出现
  let names = [];
  for (let i = 0; i < 120; i++) {
    await page.waitForTimeout(5000);
    const cur = await api('list', DIR);
    names = (cur.list || []).map(f => f.server_filename);
    const got = files.filter(f => names.includes(f)).length;
    if (i % 4 === 0) log('目录已到 ' + got + '/' + files.length);
    if (got === files.length) break;
  }
  const missing = files.filter(f => !names.includes(f));
  if (missing.length) { console.error('上传未全部完成，缺: ' + missing.join(', ')); process.exit(5); }
  log('全部 ' + files.length + ' 个文件已就位');

  // 5. 核验输出
  console.log('RESULT ' + JSON.stringify({ dir: DIR, files: names }, null, 1));
  await browser.close();
  log('核验通过');
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
