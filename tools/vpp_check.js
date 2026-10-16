// ViewportProbe v3 校验：提取 Bootstrap，node --check
const fs = require('fs');
const src = fs.readFileSync('E:/Game/Mod/mod_stack/SurvivalLog.ViewportProbe/ViewportProbePlugin.cs', 'utf8');
const m = src.match(/internal const string Bootstrap = @"\r?\n([\s\S]*?)\r?\n";/);
if (!m) { console.error('BOOTSTRAP_NOT_FOUND'); process.exit(1); }
let js = m[1];
if (js.includes('"')) { console.error('HAS_DOUBLE_QUOTE'); process.exit(1); }
fs.writeFileSync('E:/Game/Mod/mod_stack/tools/vpp_bootstrap_v3.js', js);
console.log('OK len=' + js.length);
