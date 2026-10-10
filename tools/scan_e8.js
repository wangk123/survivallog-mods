// Scan GameAssembly il2cpp section for E8 rel32 calls hitting given target RVAs.
// Usage: node scan_e8.js <targetRVA_hex> [moreRVAs...]
const fs = require('fs');
const pe = process.argv[2];
const targets = process.argv.slice(3).map(s => parseInt(s, 16));
const buf = fs.readFileSync(pe);
// PE64 parse
const peOff = buf.readUInt32LE(0x3c);
const numSections = buf.readUInt16LE(peOff + 6);
const optSize = buf.readUInt16LE(peOff + 20);
const secStart = peOff + 24 + optSize;
let il2cpp = null;
for (let i = 0; i < numSections; i++) {
  const s = secStart + i * 40;
  const name = buf.toString('ascii', s, s + 8).replace(/\0+$/, '');
  const vsize = Number(buf.readBigUInt64LE(s + 8));
  const rva = buf.readUInt32LE(s + 12);
  const rawSize = buf.readUInt32LE(s + 16);
  const rawPtr = buf.readUInt32LE(s + 20);
  if (name === 'il2cpp') il2cpp = { vsize, rva, rawSize, rawPtr };
}
if (!il2cpp) { console.error('no il2cpp section'); process.exit(1); }
console.error(`il2cpp section: rva=0x${il2cpp.rva.toString(16)} rawPtr=0x${il2cpp.rawPtr.toString(16)} size=0x${il2cpp.rawSize.toString(16)}`);
const hits = {};
for (let off = il2cpp.rawPtr; off < il2cpp.rawPtr + il2cpp.rawSize - 5; off++) {
  if (buf[off] !== 0xe8) continue;
  const rel = buf.readInt32LE(off + 1);
  const siteRva = il2cpp.rva + (off - il2cpp.rawPtr);
  const dst = siteRva + 5 + rel; if (typeof dst !== "number") throw new Error("bigint");
  for (const t of targets) {
    if (dst === t) { (hits['0x' + t.toString(16)] = hits['0x' + t.toString(16)] || []).push('0x' + siteRva.toString(16)); }
  }
}
for (const k of Object.keys(hits)) console.log(k, '->', hits[k].join(' '));
if (!Object.keys(hits).length) console.log('NO_HITS');
