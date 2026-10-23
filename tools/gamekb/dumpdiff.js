// dumpdiff.js — gamekb 工具：按全限定类型名分块对比两份 Il2CppDumper dump.cs
// 用法: node dumpdiff.js <old_dump.cs> <new_dump.cs> [关注类名子串,...] [--moddeps <moddeps.json>]
// 输出: 汇总 + 新增/删除/变更类型 + 变更类成员级增删 + 关注类逐个报告（stdout，调用方重定向落盘）
// --moddeps: 从 moddeps.json 收集 harmony[].type 与 configFields[].rowType 的短名作为关注列表
// 噪声剥离: 行尾 VA 地址注释 / TypeDefIndex / 独立 RVA 行（地址漂移非语义变化）
// 导出: parseDump(path)（modcompat_check.js 复用）
'use strict';
const fs = require('fs');

function cleanLine(l) {
  l = l.replace(/\r$/, '');
  l = l.replace(/ \/\/ 0x[0-9A-Fa-f]{5,}$/i, '');   // 方法地址注释（VA 级）
  l = l.replace(/ \/\/ TypeDefIndex: \d+$/, '');     // 类型重排噪声
  if (/^\s*\/\/ RVA: 0x/.test(l)) return '';       // 独立 RVA 行：地址漂移非语义
  return l;
}

// 把 dump.cs 切成 { key: 'Ns.Type', header, lines[] } 的 Map
function parse(path) {
  const lines = fs.readFileSync(path, 'utf8').split('\n');
  const types = new Map();
  let ns = '';
  let cur = null; // { key, body: [] }
  let bufNs = null; // 待附着到下一个类型的 Namespace 行
  for (const raw of lines) {
    const l = cleanLine(raw);
    const mNs = raw.match(/^\/\/ Namespace: (.*)/);
    if (mNs) { ns = mNs[1].trim(); bufNs = l; if (cur) cur.body.push(l); continue; }
    const mDecl = l.match(/^(?:public|private|internal|protected)?[\w\s.]*?(class|struct|interface|enum)\s+(\w+)/);
    if (mDecl && !l.includes(' end of ')) {
      const key = (ns ? ns + '.' : '') + mDecl[2];
      cur = { key, decl: l, body: bufNs ? [bufNs] : [] };
      if (types.has(key)) { // 同名类型（嵌套/重载罕见）——加序号
        let i = 2; while (types.has(key + '#' + i)) i++;
        cur.key = key + '#' + i;
      }
      types.set(cur.key, cur);
      bufNs = null;
      cur.body.push(l);
      continue;
    }
    if (cur) cur.body.push(l);
    if (/^\/\/ Image \d+:/.test(raw) || /^\/\/ Typedef created by generator/.test(raw)) cur = null;
  }
  return types;
}

module.exports = { parseDump: parse };

// ---- CLI ----
if (require.main === module) {
  const [,, oldPath, newPath, ...focus] = process.argv;
  if (!oldPath || !newPath) { console.error('usage: node dumpdiff.js <old> <new> [focus substrs]'); process.exit(2); }

  // --moddeps <path>：从 moddeps.json 收集关注类短名（harmony[].type + configFields[].rowType 的最后一段）
  let moddepsFocus = null;
  {
    const i = process.argv.indexOf('--moddeps');
    if (i >= 0) {
      const md = JSON.parse(fs.readFileSync(process.argv[i + 1], 'utf8'));
      const set = new Set();
      for (const mod of Object.values(md))
        for (const h of (mod.harmony || [])) set.add(h.type.split('.').pop());
      for (const mod of Object.values(md))
        for (const c of (mod.configFields || [])) set.add(c.rowType.split('.').pop());
      moddepsFocus = [...set];
    }
  }

  // focus 净化：剔除 --moddeps 开关与它的值；有 moddeps 时把依赖类型短名并入关注列表
  const _mdIdx = process.argv.indexOf('--moddeps');
  const focusList = (moddepsFocus
    ? [...focus.filter(f => f !== '--moddeps' && (_mdIdx < 0 || f !== process.argv[_mdIdx + 1])), ...moddepsFocus]
    : focus.filter(f => !f.startsWith('--')));

  const oldT = parse(oldPath), newT = parse(newPath);
  const added = [], removed = [], changed = [];
  for (const [k, v] of newT) if (!oldT.has(k)) added.push(k);
  for (const [k] of oldT) if (!newT.has(k)) removed.push(k);
  for (const [k, v] of oldT) {
    const n = newT.get(k);
    if (!n) continue;
    if (v.body.join('\n') !== n.body.join('\n')) changed.push(k);
  }

  // 游戏侧优先（GameCore/无命名空间=Assembly-CSharp），库侧靠后
  const gameFirst = k => /^(GameCore|HotUpdate)\./.test(k) ? 0 : 1;
  const srt = (a, b) => gameFirst(a) - gameFirst(b) || a.localeCompare(b);
  added.sort(srt); removed.sort(srt); changed.sort(srt);

  console.log(`OLD types=${oldT.size}  NEW types=${newT.size}`);
  console.log(`ADDED=${added.length}  REMOVED=${removed.length}  CHANGED=${changed.length}`);

  const dump = (title, arr) => {
    console.log(`\n==== ${title} (${arr.length}) ====`);
    for (const k of arr) console.log('  ' + k);
  };
  dump('ADDED (game-side first)', added);
  dump('REMOVED', removed);

  // 变更类：给成员级增删（前 400 个足够）
  console.log(`\n==== CHANGED detail (${changed.length}, first 400) ====`);
  let shown = 0;
  for (const k of changed) {
    if (shown++ >= 400) { console.log('  ... (truncated)'); break; }
    const o = oldT.get(k).body, n = newT.get(k).body;
    const oSet = new Map(); for (const l of o) oSet.set(l, (oSet.get(l) || 0) + 1);
    const nSet = new Map(); for (const l of n) nSet.set(l, (nSet.get(l) || 0) + 1);
    const plus = [], minus = [];
    for (const [l, c] of nSet) { const d = c - (oSet.get(l) || 0); if (d > 0) for (let i = 0; i < d; i++) plus.push(l); }
    for (const [l, c] of oSet) { const d = c - (nSet.get(l) || 0); if (d > 0) for (let i = 0; i < d; i++) minus.push(l); }
    if (!plus.length && !minus.length) continue; // 仅顺序变化
    console.log(`\n-- ${k}`);
    for (const l of minus) console.log('  - ' + l.trim());
    for (const l of plus) console.log('  + ' + l.trim());
  }

  // 关注类：无论是否在 changed 里都单独完整对比
  if (focusList.length) {
    console.log(`\n==== FOCUS ====`);
    for (const sub of focusList) {
      let hits = 0;
      for (const [k] of oldT) {
        if (!k.includes(sub)) continue;
        hits++;
        const n = newT.get(k);
        if (!n) { console.log(`\n## ${k}: REMOVED !!!`); continue; }
        const o = oldT.get(k).body, nB = n.body;
        const oS = new Map(); for (const l of o) oS.set(l, (oS.get(l) || 0) + 1);
        const nS = new Map(); for (const l of nB) nS.set(l, (nS.get(l) || 0) + 1);
        const plus = [], minus = [];
        for (const [l, c] of nS) { const d = c - (oS.get(l) || 0); if (d > 0) for (let i = 0; i < d; i++) plus.push(l); }
        for (const [l, c] of oS) { const d = c - (nS.get(l) || 0); if (d > 0) for (let i = 0; i < d; i++) minus.push(l); }
        console.log(`\n## ${k}: ${plus.length || minus.length ? 'CHANGED' : 'IDENTICAL'}`);
        for (const l of minus) console.log('  - ' + l.trim());
        for (const l of plus) console.log('  + ' + l.trim());
      }
      if (!hits) console.log(`\n## (no old type matches "${sub}")`);
    }
  }
}
