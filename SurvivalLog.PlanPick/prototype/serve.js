// PlanPick 原型本地服务器：node prototype/serve.js [port]
// /            -> prototype 目录（index.html / official.css / plan_data.json）
// /Res/*       -> gamekb webui 镜像（官方 WebIcon 图标，playwright 禁 file:// 的既定绕法）
const http = require('http');
const fs = require('fs');
const path = require('path');

const PROTO = __dirname;
const WEBUI = path.join(__dirname, '../../gamekb/2026-10-23_26157732/webui');
const PORT = parseInt(process.argv[2] || '8612', 10);

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg', '.ttf': 'font/ttf' };

http.createServer((req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    if (url === '/favicon.ico') { res.writeHead(204); res.end(); return; } // 浏览器自动请求，兜掉免 404 噪声
    let f = null;
    if (url.startsWith('/Res/')) f = path.join(WEBUI, url);
    else f = path.join(PROTO, url === '/' ? 'index.html' : url.replace(/^\//, ''));
    fs.readFile(f, (err, data) => {
        if (err) { res.writeHead(404); res.end('404 ' + url); return; }
        res.writeHead(200, { 'Content-Type': MIME[path.extname(f).toLowerCase()] || 'application/octet-stream' });
        res.end(data);
    });
}).listen(PORT, '127.0.0.1', () => {
    console.log('PlanPick prototype -> http://127.0.0.1:' + PORT + '/');
});
