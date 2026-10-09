// 百度网盘自动上传（发版流水线第 8 步之后的网盘代传）
// 用法：node tools/baidu_upload.js   （在仓库根目录执行）
// 依赖：C:\temp\baidu_up\node_modules 的 playwright-core + 系统 Chrome
//   （首次：mkdir C:\temp\baidu_up && cd /d C:\temp\baidu_up && npm i playwright-core）
// 登录态：tools/baidu.session.json（Playwright storageState 格式，失效则从浏览器重新导出）
// 流程：进 /游戏/生存日志 → API 批删目录内全部旧文件 → DOM 上传 release\baidu_upload\ 全部文件
//       → 等"上传完成（N/N）"→ API 核验。分享链接为文件夹级，换文件不影响。
// 实证坑（2026-10-09 v1.0.5 实战）：
//   ① 删除接口参数是 opera=delete（不是 oper），且需 newVerify=1&clienttype=0&app_id=250528&web=1
//   ② 网盘 SPA 的 URL path 参数会被路由吃掉——进目录必须 DOM 双击 [title="游戏"]→[title="生存日志"]
//   ③ 行 checkbox 是 hover 才显示（hide-checkbox），DOM 勾选/全选皆不稳，删除走 API 最稳
//   ④ 上传 input 有 3 个同 title，用 [accept="*/*"] 过滤 torrent 版后 .first()
//   ⑤ IAB 内置浏览器不支持文件上传（filechooser 能力缺失），必须本脚本方式
const { chromium } = require('playwright-core');
const fs = require('fs');
const path = require('path');

const SESSION = path.join(__dirname, 'baidu.session.json');
const SRC = path.join(__dirname, '..', 'release', 'baidu_upload');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const DIR = '/游戏/生存日志';

(async () => {
  const files = fs.readdirSync(SRC).filter(f => !fs.statSync(path.join(SRC, f)).isDirectory() && !f.endsWith('.lnk'));
  if (files.length === 0) throw new Error('release\\baidu_upload 为空：先跑 tools\\publish.ps1');
  for (const f of files) console.log('待上传: ' + f);

  const state = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const ctx = await browser.newContext({ storageState: state, viewport: { width: 1600, height: 900 } });
  const page = await ctx.newPage();
  page.setDefaultTimeout(20000);
  const log = m => console.log('[' + new Date().toISOString().slice(11, 19) + '] ' + m);

  await page.goto('https://pan.baidu.com/disk/main', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);

  // 1. API 列出并批删全部旧文件
  const oldPaths = await page.evaluate(async dir => {
    const r = await fetch('https://pan.baidu.com/api/list?dir=' + encodeURIComponent(dir) + '&num=1000', { credentials: 'include' });
    const j = await r.json();
    return (j.list || []).map(f => f.path);
  }, DIR);
  log('网盘现存旧文件: ' + oldPaths.length);
  if (oldPaths.length > 0) {
    const delResp = await page.evaluate(async paths => {
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
    }, oldPaths);
    if (delResp.errno !== 0) { console.error('批删失败: ' + JSON.stringify(delResp)); process.exit(3); }
    log('批删成功 errno=0');
  }

  // 2. DOM 双击进目标目录（上传需页面目录上下文）
  await page.locator('[title="游戏"]').first().dblclick();
  await page.waitForTimeout(4000);
  await page.locator('[title="生存日志"]').first().dblclick();
  await page.waitForTimeout(4000);

  // 3. 上传
  const input = page.locator('input[title="点击选择文件"][accept="*/*"]').first();
  if (await input.count() === 0) throw new Error('未找到上传 input');
  await input.setInputFiles(files.map(f => path.join(SRC, f)));
  log('已提交上传队列 ' + files.length + ' 文件');

  // 4. 等完成
  const n = files.length;
  const doneRe = new RegExp('上传完成\\s*[（(]\\s*' + n + '\\s*/\\s*' + n + '\\s*[)）]');
  let done = false, lastTxt = '';
  for (let i = 0; i < 90; i++) {
    await page.waitForTimeout(5000);
    lastTxt = ((await page.locator('em.select-text').first().textContent().catch(() => '')) || '').trim();
    if (i % 4 === 0) log('传输: ' + lastTxt);
    if (doneRe.test(lastTxt)) { done = true; break; }
    if (/[（(]\s*\d+\s*个?\s*失败/.test(lastTxt)) { log('!! 失败: ' + lastTxt); break; }
  }
  log('传输终态: ' + lastTxt + ' done=' + done);

  // 5. API 核验
  const verify = await page.evaluate(async dir => {
    const r = await fetch('https://pan.baidu.com/api/list?dir=' + encodeURIComponent(dir) + '&num=1000', { credentials: 'include' });
    const j = await r.json();
    return (j.list || []).map(f => f.server_filename);
  }, DIR);
  const missing = files.filter(f => !verify.includes(f));
  console.log('RESULT ' + JSON.stringify({ done, files: verify, missing }, null, 1));
  await browser.close();
  if (missing.length || !done) process.exit(2);
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
