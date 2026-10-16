// CabinetExpand ViewportFit 守卫 JS 三关校验之一：提取 Bootstrap 过 node --check
const fs = require('fs');
const src = fs.readFileSync('E:/Game/Mod/mod_stack/SurvivalLog.CabinetExpand/ViewportFit.cs', 'utf8');
const m = src.match(/internal const string Bootstrap = @"\r?\n([\s\S]*?)\r?\n";/);
if (!m) { console.error('BOOTSTRAP_NOT_FOUND'); process.exit(1); }
let js = m[1];
if (js.includes('"')) { console.error('HAS_DOUBLE_QUOTE'); process.exit(1); }
fs.writeFileSync('E:/Game/Mod/mod_stack/tools/cevf_bootstrap.js', js);
console.log('OK len=' + js.length);
