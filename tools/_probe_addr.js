// 反查 ChargeOneTickPower 等方法真实地址（script.json），并打印结构键名
const fs = require('fs');
const j = JSON.parse(fs.readFileSync(__dirname + '/il2cppdumper/dump_out/script.json', 'utf8'));
console.log('keys:', Object.keys(j));
const list = j.ScriptMethod || [];
console.log('methods:', list.length);
const targets = process.argv.slice(2);
for (const m of list) {
  const n = m.Name || m.name || '';
  if (targets.some(t => n.includes(t))) console.log(n, '0x' + Number(m.Address).toString(16));
}
