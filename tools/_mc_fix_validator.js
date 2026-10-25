// Temp validator for bugfix-round2: extract EntryJs from MenuEntryInjector.cs,
// check JS syntax; also syntax-check panel.js. ASCII-only (repo tool convention).
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..');

const t = fs.readFileSync(path.join(root, 'SurvivalLog.ModCenter/MenuEntryInjector.cs'), 'utf8');
const m = t.match(/EntryJs = @"([\s\S]*?)";\r?\n\}/);
if (!m) { console.log('NO MATCH for EntryJs'); process.exit(1); }
let js = m[1].replace(/""/g, '"');
try { new Function(js); console.log('EntryJs: JS SYNTAX OK, length', js.length); }
catch (e) { console.log('EntryJs: JS SYNTAX FAIL:', e.message); process.exit(1); }
let bal = 0; for (const c of js) { if (c === '{') bal++; if (c === '}') bal--; }
console.log('EntryJs brace balance:', bal);
if (bal !== 0) process.exit(1);

// sanity: guarded-Tick string concat shape (simulate what C# sends)
const guard = "window.__mcObserver && window.__McInstalled && (!window.__mcSelfCheck || window.__mcSelfCheck())";
const sent = 'if(!(' + guard + ')){' + js + '}';
try { new Function(sent); console.log('guarded payload: JS SYNTAX OK, length', sent.length); }
catch (e) { console.log('guarded payload: JS SYNTAX FAIL:', e.message); process.exit(1); }

const pj = fs.readFileSync(path.join(root, 'SurvivalLog.ModCenter/panel.js'), 'utf8');
try { new Function(pj); console.log('panel.js: JS SYNTAX OK, length', pj.length); }
catch (e) { console.log('panel.js: JS SYNTAX FAIL:', e.message); process.exit(1); }
console.log('ALL OK');
