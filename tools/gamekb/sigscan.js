// sigscan.js — gamekb 工具：在 GameAssembly.dll 文件内静态扫描字节签名（PE 段表自解析）
// CLI: node sigscan.js <GameAssembly.dll>   （内置 FridgeChill TalentCapBytePatch 签名，输出 RESULT 行）
// 模块导出（modcompat_check.js 复用）:
//   parsePE(buf) -> { sections:[{name,virtSize,virtAddr,rawSize,rawPtr}], il2cpp }
//   rva2Off(sections, rva) -> 文件偏移（-1=不在任何段）
//   hexToSig("F30F5CD9????????C3") -> number[]（?? -> -1 通配）
//   scanPattern(secBuf, sig) -> number[]（段缓冲内全部命中偏移，升序）
//   readRel32Target(peBuf, sections, il2cpp, secBuf, hitOff, rel32ByteOff) -> { insnRva, slotRva }
'use strict';
const fs = require('fs');

function parsePE(pe) {
  const u16 = o => pe.readUInt16LE(o), u32 = o => pe.readUInt32LE(o);
  const peOff = u32(0x3c), nSec = u16(peOff + 6), optSize = u16(peOff + 20);
  const sections = [];
  for (let i = 0; i < nSec; i++) {
    const b = peOff + 24 + optSize + i * 40;
    sections.push({
      name: pe.toString('latin1', b, b + 8).replace(/\0+$/, ''),
      virtSize: u32(b + 8), virtAddr: u32(b + 12), rawSize: u32(b + 16), rawPtr: u32(b + 20),
    });
  }
  return { sections, il2cpp: sections.find(s => s.name === 'il2cpp') };
}
function rva2Off(sections, rva) {
  for (const s of sections)
    if (rva >= s.virtAddr && rva < s.virtAddr + Math.max(s.virtSize, s.rawSize)) return s.rawPtr + (rva - s.virtAddr);
  return -1;
}
function hexToSig(sigHex) {
  const out = [];
  for (let i = 0; i < sigHex.length; i += 2) {
    const h = sigHex.substr(i, 2);
    out.push(h === '??' ? -1 : parseInt(h, 16));
  }
  return out;
}
function scanPattern(buf, sig) {
  const hits = [];
  for (let i = 0; i <= buf.length - sig.length; i++) {
    let ok = true;
    for (let j = 0; j < sig.length; j++) { if (sig[j] < 0) continue; if (buf[i + j] !== sig[j]) { ok = false; break; } }
    if (ok) hits.push(i);
  }
  return hits;
}
// hitOff：命中在 il2cpp 段缓冲内的偏移；rel32ByteOff：rel32 在签名内的字节下标
function readRel32Target(peBuf, sections, il2cpp, secBuf, hitOff, rel32ByteOff) {
  const insnRva = il2cpp.virtAddr + hitOff;
  const rel32 = secBuf.readInt32LE(hitOff + rel32ByteOff);
  const slotRva = insnRva + rel32ByteOff + 4 + rel32;
  return { insnRva, slotRva };
}

module.exports = { parsePE, rva2Off, hexToSig, scanPattern, readRel32Target };

// ---- CLI ----
if (require.main === module) {
  const pe = fs.readFileSync(process.argv[2]);
  const { sections, il2cpp } = parsePE(pe);
  if (!il2cpp) { console.log('NO il2cpp section'); process.exit(1); }
  console.log(`il2cpp section: VA=0x${il2cpp.virtAddr.toString(16)} size=0x${il2cpp.virtSize.toString(16)} raw=0x${il2cpp.rawPtr.toString(16)}`);
  // F30F5CD9 F30F5FD3 F30F59D0 F30F1005 <rel32 通配> F30F5FC2 C3 —— rel32 在第 16 字节
  const sig = hexToSig('F30F5CD9F30F5FD3F30F59D0F30F1005????????F30F5FC2C3');
  const buf = pe.subarray(il2cpp.rawPtr, il2cpp.rawPtr + il2cpp.rawSize);
  const hits = scanPattern(buf, sig);
  hits.forEach((h, n) => {
    const { insnRva, slotRva } = readRel32Target(pe, sections, il2cpp, buf, h, 16);
    const off = rva2Off(sections, slotRva);
    const val = off >= 0 ? pe.readFloatLE(off) : NaN;
    console.log(`HIT#${n + 1} insnRVA=0x${insnRva.toString(16)} -> slotRVA=0x${slotRva.toString(16)} value=${val}`);
  });
  console.log(hits.length === 0 ? 'RESULT: SIGNATURE NOT FOUND' : `RESULT: ${hits.length} hit(s)`);
}
