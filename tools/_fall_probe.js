// 查沦陷相关方法地址：node _fall_probe.js <名称子串>...
const fs = require('fs');
const j = JSON.parse(fs.readFileSync(__dirname + '/il2cppdumper/script.json', 'utf8'));
const list = j.ScriptMethod || [];
const targets = process.argv.slice(2);
const hits = [];
for (const m of list) {
  const n = m.Name || m.name || '';
  if (targets.some(t => n.includes(t))) hits.push([Number(m.Address), n]);
}
hits.sort((a, b) => a[0] - b[0]);
for (const [addr, n] of hits) console.log('0x' + addr.toString(16), n);
