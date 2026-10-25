// 查字符串字面量槽的值（gamekb 永久副本）：node _lit2.js <hexVA...>
const fs = require('fs');
const path = require('path');
const dirs = fs.readdirSync(path.join(__dirname, '..', 'gamekb')).map(d => path.join(__dirname, '..', 'gamekb', d, 'il2cpp', 'stringliteral.json')).filter(p => fs.existsSync(p));
const j = JSON.parse(fs.readFileSync(dirs[dirs.length - 1], 'utf8'));
const map = new Map();
for (const it of j) map.set('0x' + Number(it.address).toString(16).toLowerCase(), it.value);
for (const arg of process.argv.slice(2)) {
  const va = parseInt(arg, 16);
  const rva = '0x' + va.toString(16).toLowerCase().replace(/^18/, '');
  console.log('0x' + va.toString(16), '=>', JSON.stringify(map.get(rva)));
}
