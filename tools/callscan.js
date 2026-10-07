// callscan.js — scan E8/E9 rel32 in [rvaStart, rvaEnd) of GameAssembly.dll,
// resolve targets against Il2CppDumper script.json (addresses are DECIMAL strings).
// usage: node callscan.js <pe> <scriptjson> <rvaStart> <rvaEnd>
const fs = require('fs');

const [pePath, sjPath, rvaStartS, rvaEndS] = process.argv.slice(2);
const rvaStart = parseInt(rvaStartS, 16), rvaEnd = parseInt(rvaEndS, 16);

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
function rvaToOff(rva) {
  for (const s of secs) if (rva >= s.va && rva < s.va + s.rawSize) return s.rawPtr + (rva - s.va);
  throw new Error('rva not in any section: 0x' + rva.toString(16));
}

const sj = JSON.parse(fs.readFileSync(sjPath, 'utf8'));
const byAddr = new Map();
for (const m of sj.ScriptMethod) byAddr.set(parseInt(m.Address, 10) >>> 0, m.Name);

const start = rvaToOff(rvaStart), end = rvaToOff(rvaEnd);
for (let i = start; i < end - 5; i++) {
  const op = buf[i];
  if (op === 0xe8 || op === 0xe9) {
    const rel = buf.readInt32LE(i + 1);
    const srcRva = rvaStart + (i - start);
    const tgt = (srcRva + 5 + rel) >>> 0;
    const name = byAddr.get(tgt);
    if (name) console.log((op === 0xe8 ? 'call ' : 'jmp  ') + '0x' + srcRva.toString(16) + ' -> 0x' + tgt.toString(16) + '  ' + name);
  }
}
