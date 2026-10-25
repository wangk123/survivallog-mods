// 查字符串字面量槽的值：node _lit.js <hexVA...>
const fs = require('fs');
const j = JSON.parse(fs.readFileSync(__dirname + '/il2cppdumper/stringliteral.json', 'utf8'));
const map = new Map();
for (const it of j) map.set('0x' + Number(it.address).toString(16).toLowerCase(), it.value);
for (const arg of process.argv.slice(2)) {
  const va = parseInt(arg, 16);
  const rva = '0x' + va.toString(16).toLowerCase().replace(/^18/, '');
  console.log('0x' + va.toString(16), '=>', JSON.stringify(map.get(rva)));
}
