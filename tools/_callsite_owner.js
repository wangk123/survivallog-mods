// 用法: node _callsite_owner.js <script.json> <hexCallSite...>
// 二分找 <= 调用点的最近方法名（script.json ScriptMethod 地址为十进制 RVA）
const path = process.argv[2];
const sites = process.argv.slice(3).map(s => parseInt(s, 16));
const j = JSON.parse(require('fs').readFileSync(path, 'utf8'));
const ms = j.ScriptMethod.map(m => ({ addr: parseInt(m.Address), name: m.Name }))
  .sort((a, b) => a.addr - b.addr);
for (const s of sites) {
  let lo = 0, hi = ms.length - 1, best = -1;
  while (lo <= hi) { const mid = (lo + hi) >> 1; if (ms[mid].addr <= s) { best = mid; lo = mid + 1; } else hi = mid - 1; }
  console.log('0x' + s.toString(16), '->', best >= 0 ? ms[best].name + ' +0x' + (s - ms[best].addr).toString(16) : '(none)');
}
