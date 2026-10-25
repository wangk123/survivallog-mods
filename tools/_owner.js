// 找包含某地址的方法：node _owner.js <hexRVA>...
const fs = require('fs');
const path = require('path');
const candidates = [
  __dirname + '/il2cppdumper/script.json',
  ...fs.readdirSync(path.join(__dirname, '..', 'gamekb')).map(d => path.join(__dirname, '..', 'gamekb', d, 'il2cpp', 'script.json')).reverse(),
];
const sj = candidates.find(p => fs.existsSync(p));
const j = JSON.parse(fs.readFileSync(sj, 'utf8'));
const list = (j.ScriptMethod || []).map(m => [Number(m.Address), m.Name]).sort((a, b) => a[0] - b[0]);
for (const arg of process.argv.slice(2)) {
  const a = parseInt(arg, 16);
  let lo = 0, hi = list.length - 1, best = -1;
  while (lo <= hi) { const mid = (lo + hi) >> 1; if (list[mid][0] <= a) { best = mid; lo = mid + 1; } else hi = mid - 1; }
  console.log('0x' + a.toString(16), '=>', best >= 0 ? list[best][1] + ' (0x' + list[best][0].toString(16) + ')' : '(none)');
}
