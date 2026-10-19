// xref.js — scan il2cpp section for rip-relative LEA/MOV referencing a target VA.
// usage: node xref.js <pe> <scriptjson> <hexTargetVa>
const fs = require('fs');
const [pePath, sjPath, tgtS] = process.argv.slice(2);
const tgt = parseInt(tgtS, 16);
const buf = fs.readFileSync(pePath);
const peOff = buf.readUInt32LE(0x3c);
const numSections = buf.readUInt16LE(peOff + 6);
const optSize = buf.readUInt16LE(peOff + 20);
const secOff = peOff + 24 + optSize;
const secs = [];
for (let i = 0; i < numSections; i++) {
  const s = secOff + i * 40;
  secs.push({ name: buf.toString('ascii', s, s + 8).replace(/\0+$/, ''), va: buf.readUInt32LE(s + 12), rawSize: buf.readUInt32LE(s + 16), rawPtr: buf.readUInt32LE(s + 20) });
}
let il = secs.find(s => s.name === 'il2cpp');
const sj = JSON.parse(fs.readFileSync(sjPath, 'utf8'));
const byAddr = new Map();
for (const m of sj.ScriptMethod) byAddr.set(parseInt(m.Address, 10) >>> 0, m.Name);
// method starts sorted for enclosing lookup
const starts = [...byAddr.keys()].sort((a, b) => a - b);
function methodOf(rva) {
  let lo = 0, hi = starts.length - 1, best = -1;
  while (lo <= hi) { const mid = (lo + hi) >> 1; if (starts[mid] <= rva) { best = starts[mid]; lo = mid + 1; } else hi = mid - 1; }
  return best >= 0 ? byAddr.get(best) + ' @0x' + best.toString(16) : '?';
}
let found = 0;
const base = il.va, off0 = il.rawPtr, end = il.rawSize - 8;
for (let i = 0; i < end; i++) {
  // REX.W + (8D lea | 8B mov) + ModRM mod=00 rm=101 → [rip+disp32], len 7
  if (buf[off0 + i] === 0x48 && (buf[off0 + i + 1] === 0x8D || buf[off0 + i + 1] === 0x8B) && (buf[off0 + i + 2] & 0xC7) === 0x05) {
    const disp = buf.readInt32LE(off0 + i + 3);
    const insnRva = base + i;
    const target = (insnRva + 7 + disp) >>> 0;
    if (target === tgt) {
      found++;
      console.log('xref 0x' + insnRva.toString(16) + '  in  ' + methodOf(insnRva));
    }
  }
}
console.log('total xrefs: ' + found);
