const fs = require('fs');
const path7 = require("path");
// 直接复用 _bundle_scan.js 的 extractPayload：把它 import 进来不优雅，复制调用
const src = fs.readFileSync('tools/_bundle_scan.js','utf8');
const fnStart = src.indexOf('function lz4');
const fnEnd = src.indexOf('const dir =');
eval(src.slice(fnStart, fnEnd));
const dir = process.argv[2];
const files = fs.readdirSync(dir).filter(f=>f.endsWith('.bundle'));
let ok=0, fail=0, withCab=0, cfgNameHits=0;
const cfgRe = /Config_MemoryPack_Default_Config_(\w+)_bytes/;
const tables = {};
for (const f of files) {
  const buf = fs.readFileSync(path7.join(dir,f));
  const p = extractPayload(buf);
  if (!p) { fail++; continue; }
  ok++;
  const s = p.toString('latin1');
  if (s.includes('CAB-')) withCab++;
  let m; const re = new RegExp(cfgRe.source, 'g');
  while ((m = re.exec(s))) { tables[m[1]] = f; cfgNameHits++; }
}
console.log('extract ok', ok, 'fail', fail, 'withCAB', withCab, 'cfgAssetNameHits', cfgNameHits);
const names = Object.keys(tables);
console.log('tables found:', names.length, names.slice(0,20).join(','));
if (tables.DefenseLv) console.log('DefenseLv bundle:', tables.DefenseLv);
