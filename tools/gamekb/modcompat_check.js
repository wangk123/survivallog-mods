// modcompat_check.js — gamekb 工具：按 moddeps.json 核对某版 gamekb 是否满足全套件依赖
// CLI: node modcompat_check.js <gamekb版本目录> [--moddeps <path>]（默认同目录 moddeps.json）
// 判定：类型/方法/访问器/行类/字段缺失或签名命中异常 = RED；参数个数不符 = WARN（运行时兜底）
// 表 schema（tables/_index.json 存在时）：字段不在 columns = RED（表结构变更）
// exit: 0=全绿 1=有红 2=用法/IO 错误
'use strict';
const fs = require('fs');
const path = require('path');
const { parseDump } = require('./dumpdiff.js');
const { parsePE, rva2Off, hexToSig, scanPattern, readRel32Target } = require('./sigscan.js');

const a = process.argv.slice(2).filter(x => !x.startsWith('--'));
const verDir = a[0];
const mdPath = process.argv.includes('--moddeps')
  ? process.argv[process.argv.indexOf('--moddeps') + 1]
  : path.join(__dirname, 'moddeps.json');
if (!verDir || !fs.existsSync(verDir)) { console.error('usage: node modcompat_check.js <gamekb版本目录> [--moddeps <path>]'); process.exit(2); }

const deps = JSON.parse(fs.readFileSync(mdPath, 'utf8'));
const dumpPath = path.join(verDir, 'il2cpp', 'dump.cs');
if (!fs.existsSync(dumpPath)) { console.error('no dump.cs under ' + dumpPath); process.exit(2); }
console.log(`# modcompat @ ${path.basename(verDir)}`);
const types = parseDump(dumpPath);

// 方法行匹配：<Method>（ 后面数参数（顶层逗号+1，粗对）
function methodHits(body, method) {
  const re = new RegExp(`[ .]${method}\\(`, 'g');
  const out = [];
  for (const l of body) { re.lastIndex = 0; if (re.test(l)) out.push(l); }
  return out;
}
function paramCount(line) {
  const i = line.indexOf('('); if (i < 0) return -1;
  let d = 0, c = 0, seen = false;
  for (let j = i + 1; j < line.length; j++) {
    const ch = line[j];
    if (ch === '(' || ch === '<') d++;
    else if (ch === ')' || ch === '>') { if (d === 0) return seen ? c + 1 : 0; d--; }
    else if (ch === ',' && d === 0) c++;
    if (ch !== ' ' && ch !== '\t') seen = true;
  }
  return c + 1;
}
// 字段/属性行匹配：词边界 + 行尾 ; 或 { }
function fieldHit(body, field) {
  const re = new RegExp(`[ .]${field}\\s*[;{]`);
  return body.some(l => re.test(l));
}

let red = 0, warn = 0;
const R = m => { red++; console.log(`  [RED]   ${m}`); };
const W = m => { warn++; console.log(`  [WARN]  ${m}`); };
const G = m => console.log(`  [ok]    ${m}`);

// tables/_index.json（可选）：行类 -> columns
const idxPath = path.join(verDir, 'tables', '_index.json');
const tableCols = new Map();
let tablesLoaded = false;
if (fs.existsSync(idxPath)) {
  tablesLoaded = true;
  const idx = JSON.parse(fs.readFileSync(idxPath, 'utf8'));
  for (const t of idx.tables || []) tableCols.set(t.name, t.columns || []);
}

const cfgMgr = types.get('GameCore.HotUpdate.ConfigManager');

for (const [mod, dep] of Object.entries(deps)) {
  console.log(`## ${mod}`);
  for (const h of dep.harmony || []) {
    const t = types.get(h.type);
    if (!t) { R(`${h.type} 类型缺失`); continue; }
    const hits = methodHits(t.body, h.method);
    if (hits.length === 0) { R(`${h.type}.${h.method} 方法缺失`); continue; }
    if (h.params != null) {
      const ok = hits.some(l => paramCount(l) === h.params);
      ok ? G(`${h.type}.${h.method}(${h.params}参)`) : W(`${h.type}.${h.method} 参数个数不符：期望 ${h.params}，实际 ${hits.map(paramCount).join('/')}`);
    } else G(`${h.type}.${h.method}`);
  }
  for (const c of dep.configFields || []) {
    if (!cfgMgr) { R('ConfigManager 类型缺失'); continue; }
    if (!cfgMgr.body.some(l => l.includes(c.accessor))) { R(`ConfigManager.${c.accessor} 访问器缺失`); continue; }
    const row = types.get('GameCore.HotUpdate.' + c.rowType);
    if (!row) { R(`${c.rowType} 行类缺失`); continue; }
    G(`${c.accessor} ✓`);
    for (const f of c.fields) {
      if (!fieldHit(row.body, f)) { R(`${c.rowType}.${f} 字段缺失`); continue; }
      if (tablesLoaded) {
        const cols = tableCols.get(c.rowType);
        if (cols && cols.length && !cols.includes(f)) { R(`${c.rowType}.${f} 不在 tables/_index.json columns（表结构变更）`); continue; }
      }
      G(`${c.rowType}.${f}`);
    }
  }
  for (const s of dep.nativeSigs || []) {
    const pePath = path.join(verDir, 'binaries', 'GameAssembly.dll');
    if (!fs.existsSync(pePath)) { W(`${s.name}: binaries/GameAssembly.dll 缺失（partial 基线），跳过`); continue; }
    const pe = fs.readFileSync(pePath);
    const { sections, il2cpp } = parsePE(pe);
    const buf = pe.subarray(il2cpp.rawPtr, il2cpp.rawPtr + il2cpp.rawSize);
    const hits = scanPattern(buf, hexToSig(s.sigHex));
    if (hits.length === 0) { R(`${s.name} 签名 0 命中（游戏实现已变，运行时会安全关闭）`); continue; }
    if (s.expectHits != null && hits.length !== s.expectHits) W(`${s.name} 命中 ${hits.length} 处（期望 ${s.expectHits}，运行时取第一处，请人工确认语义）`);
    if (s.expectOldSlot != null) {
      const { slotRva } = readRel32Target(pe, sections, il2cpp, buf, hits[0], s.rel32ByteOff);
      const off = rva2Off(sections, slotRva);
      const val = off >= 0 ? pe.readFloatLE(off) : NaN;
      if (Math.abs(val - s.expectOldSlot) > 1e-4) R(`${s.name} 旧槽值 ${val} ≠ ${s.expectOldSlot}`); else G(`${s.name} 签名 ${hits.length} 命中，旧槽 ${val}`);
    } else G(`${s.name} 签名 ${hits.length} 命中`);
  }
}

console.log(`\n== 汇总：RED=${red} WARN=${warn} ${red === 0 ? '(ALL GREEN)' : '(HAS RED)'} ==`);
process.exit(red === 0 ? 0 : 1);
