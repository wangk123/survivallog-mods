// _srv_scroll_test.js —— 滚动模式 Playwright 自测服务器（临时工具，测完可删）
// /WebUI/* → tools/webui_src/*（页面按官方目录结构寻址 ../../webui-*.js）
// /harness.html → 模拟游戏 Root：#iframe-container + 全屏 iframe，iframe src 由 ?page= 指定
const http = require('http');
const fs = require('fs');
const path = require('path');
const ROOT = path.join(__dirname, 'webui_src');
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg' };
const harness = `<!doctype html><html><head><meta charset="utf-8"><style>
  html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#20242a}
  #iframe-container{position:fixed;inset:0}
  #iframe-container iframe{width:100%;height:100%;border:0;display:block}
</style></head><body>
<div id="iframe-container"><iframe id="pageFrame" src="about:blank"></iframe></div>
<script>
  // harness 不自动跑 bootstrap；由 Playwright 注入后按需重复执行（脚本幂等，重跑=手动触发一次 pass）
  window.__ setPage = function (p) { document.getElementById('pageFrame').src = p; };
</script>
</body></html>`;
http.createServer((req, res) => {
  const u = decodeURIComponent((req.url || '').split('?')[0]);
  if (u === '/' || u === '/harness.html') {
    const page = (req.url || '').split('page=')[1];
    const body = page ? harness.replace('src="about:blank"', 'src="/WebUI/UI/' + page + '"') : harness;
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); res.end(body); return;
  }
  if (u.startsWith('/WebUI/')) {
    const p = path.join(ROOT, u.slice('/WebUI/'.length));
    fs.readFile(p, (e, d) => {
      if (e) { res.writeHead(404); res.end('nf'); return; }
      res.writeHead(200, { 'Content-Type': TYPES[path.extname(p).toLowerCase()] || 'application/octet-stream' });
      res.end(d);
    });
    return;
  }
  res.writeHead(404); res.end('nf');
}).listen(8971, '127.0.0.1', () => console.log('scroll-test server on http://127.0.0.1:8971'));
