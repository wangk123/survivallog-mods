// _rev_call.js — reverse E8/E9 caller scan: find all call sites in .text whose target equals a given RVA.
// usage: node _rev_call.js <pe> <scriptjson> <targetRvaHex>
const fs = require('fs');
const [pePath, sjPath, targetS] = process.argv.slice(2);
const target = parseInt(targetS, 16);

const buf = fs.readFileSync(pePath);
const peOff = buf.readUInt32LE(0x3c);
const numSections = buf.readUInt16LE(peOff + 6);
const optSize = buf.readUInt16LE(peOff + 20);
const secOff = peOff + 24 + optSize;
const secs = [];
for (let i = 0; i < numSections; i++) {
  const s = secOff + i * 40;
  secs.push({
    name: buf.toString('ascii', s, s + 8).replace(/\0+$/, ''),
    va: buf.readUInt32LE(s + 12),
    rawSize: buf.readUInt32LE(s + 16),
    rawPtr: buf.readUInt32LE(s + 20),
  });
}
const sj = JSON.parse(fs.readFileSync(sjPath, 'utf8'));
const byAddr = new Map();
for (const m of sj.ScriptMethod) byAddr.set(parseInt(m.Address, 10) >>> 0, m.Name);
// build map: addr -> nearest preceding known method start for caller attribution
const starts = [...byAddr.keys()].sort((a, b) => a - b);
function owner(addr) {
  let lo = 0, hi = starts.length - 1, best = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (starts[mid] <= addr) { best = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return best >= 0 ? byAddr.get(starts[best]) : '?';
}
let hits = 0;
for (const s of secs) {
  if (s.name !== 'il2cpp') continue;
  for (let i = 0; i < s.rawSize - 5; i++) {
    const op = buf[s.rawPtr + i];
    if (op !== 0xe8 && op !== 0xe9) continue;
    const rel = buf.readInt32LE(s.rawPtr + i + 1);
    const srcRva = s.va + i;
    const tgt = (srcRva + 5 + rel) >>> 0;
    if (tgt === target) {
      hits++;
      console.log((op === 0xe8 ? 'call' : 'jmp ') + ' @0x' + srcRva.toString(16) + '  owner: ' + owner(srcRva));
    }
  }
}
console.log('total hits:', hits);
