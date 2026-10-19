// findreset2.js — register-form writes to [reg+0x38] (deviceScanDone) in TowerCombatManager region
const fs = require('fs');
const buf = fs.readFileSync('E:/SteamLibrary/steamapps/common/Survival Log/GameAssembly.dll');
const peOff = buf.readUInt32LE(0x3c), n = buf.readUInt16LE(peOff + 6), opt = buf.readUInt16LE(peOff + 20);
const secs = [];
for (let i = 0; i < n; i++) { const o = peOff + 24 + opt + i * 40; secs.push({ name: buf.toString('ascii', o, o + 8).replace(/\0+$/g, ''), va: buf.readUInt32LE(o + 12), raw: buf.readUInt32LE(o + 16), ptr: buf.readUInt32LE(o + 20) }); }
const il = secs.find(s => s.name === 'il2cpp');
const sj = JSON.parse(fs.readFileSync('tools/il2cppdumper/dump_out/script.json', 'utf8'));
const arr = sj.ScriptMethod.map(m => ({ a: parseInt(m.Address, 10), n: m.Name })).sort((x, y) => x.a - y.a);
function mo(rva) { let lo = 0, hi = arr.length - 1, b = -1; while (lo <= hi) { const mid = (lo + hi) >> 1; if (arr[mid].a <= rva) { b = mid; lo = mid + 1; } else hi = mid - 1; } return b >= 0 ? arr[b].n : '?'; }
const S = 0x2806f00, E = 0x2811000;
const off = il.ptr + (S - il.va), end = il.ptr + (E - il.va);
for (let i = off; i < end - 3; i++) {
  const rva = S + (i - off);
  if (buf[i] === 0x88 && (buf[i + 1] & 0xC0) === 0x40 && buf[i + 2] === 0x38 && (buf[i + 1] & 0x07) !== 0x04) {
    console.log('mov [reg+38h],r8 @0x' + rva.toString(16) + ' in ' + mo(rva));
  }
}
console.log('done');
