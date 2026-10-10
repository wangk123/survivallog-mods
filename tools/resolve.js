// Resolve decimal-RVA list to method names from script.json. Args: hex RVAs.
const fs = require('fs');
const j = JSON.parse(fs.readFileSync('E:/Game/Mod/mod_stack/tools/il2cppdumper/script.json', 'utf8'));
const map = new Map();
for (const m of j.ScriptMethod) map.set(parseInt(m.Address), m.Name);
for (const a of process.argv.slice(2)) {
  const rva = parseInt(a, 16);
  console.log('0x' + rva.toString(16), '->', map.get(rva) || '(no exact match)');
}
