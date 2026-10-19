// findreset.js — find writes of byte 0 to [reg+0x38] (deviceScanDone=false) in a method region.
// usage: node findreset.js <pe> <scriptjson> <hexStart> <hexEnd>
const fs = require('fs');
const [pePath, sjPath, sS, eS] = process.argv.slice(2);
const s = parseInt(sS, 16), e = parseInt(eS, 16);
const buf = fs.readFileSync(pePath);
const peOff = buf.readUInt32LE(0x3c);
const numSections = buf.readUInt16LE(peOff + 6);
const optSize = buf.readUInt16LE(peOff + 20);
const secOff = peOff + 24 + optSize;
const secs = [];
for (let i = 0; i < numSections; i++) {
  const o = secOff + i * 40;
  secs.push({ name: buf.toString('ascii', o, o + 8).replace(/\0+$/, ''), va: buf.readUInt32LE(o + 12), rawSize: buf.readUInt32LE(o + 16), rawPtr: buf.readUInt32LE(o + 20) });
}
function rvaToOff(rva) { for (const x of secs) if (rva >= x.va && rva < x.va + x.rawSize) return x.rawPtr + (rva - x.va); throw new Error('bad rva'); }
const sj = JSON.parse(fs.readFileSync(sjPath, 'utf8'));
const arr = sj.ScriptMethod.map(m => ({ a: parseInt(m.Address, 10), n: m.Name })).sort((x, y) => x.a - y.a);
function methodOf(rva) { let lo = 0, hi = arr.length - 1, b = -1; while (lo <= hi) { const mid = (lo + hi) >> 1; if (arr[mid].a <= rva) { b = mid; lo = mid + 1; } else hi = mid - 1; } return b >= 0 ? arr[b].n + ' @0x' + arr[b].a.toString(16) : '?'; }
const off0 = rvaToOff(s), off1 = rvaToOff(e - 1);
for (let i = off0; i < off1 - 4; i++) {
  const rva = s + (i - off0);
  // C6 /0 ib  mov byte [reg+disp8],0  →  C6 4r 38 00 (mod=01, rm!=4)
  if (buf[i] === 0xC6 && (buf[i+1] & 0xF8) === 0x40 && (buf[i+1] & 0x07) !== 0x04 && buf[i+2] === 0x38 && buf[i+3] === 0x00) {
    console.log('mov byte[reg+38h],0  @0x' + rva.toString(16) + '  in  ' + methodOf(rva));
  }
  // C6 80/81..  mov byte [reg+disp32],0 → C6 8r 38000000 00
  if (buf[i] === 0xC6 && (buf[i+1] & 0xF8) === 0x80 && buf[i+2] === 0x38 && buf[i+3] === 0x00 && buf[i+4] === 0x00 && buf[i+5] === 0x00 && buf[i+6] === 0x00) {
    console.log('mov byte[reg+38h],0(d32) @0x' + rva.toString(16) + '  in  ' + methodOf(rva));
  }
  // C6 44 24 38 00  mov byte [rsp+38h],0 (stack, skip semantics but report)
  if (buf[i] === 0xC6 && buf[i+1] === 0x44 && buf[i+2] === 0x24 && buf[i+3] === 0x38 && buf[i+4] === 0x00) {
    console.log('mov byte[rsp+38h],0  @0x' + rva.toString(16) + '  in  ' + methodOf(rva) + '  (stack!)');
  }
}
console.log('scan done');
