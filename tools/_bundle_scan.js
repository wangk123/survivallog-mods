// 批量扫 MainPackage bundle：UnityFS 解压后搜配置表标记，定位 DefenseLv 等表所在 bundle
// 用法: node tools/_bundle_scan.js <MainPackage目录>
const fs = require('fs');
const path = require('path');

function lz4(src, uncompressedSize) {
  const dst = Buffer.allocUnsafe(uncompressedSize);
  let s = 0, d = 0;
  while (s < src.length && d < dst.length) {
    const token = src[s++];
    let litLen = token >> 4;
    if (litLen === 15) { let l; do { l = src[s++]; litLen += l; } while (l === 255); }
    if (s + litLen > src.length || d + litLen > dst.length) return null;
    src.copy(dst, d, s, s + litLen); s += litLen; d += litLen;
    if (s >= src.length || d >= dst.length) break;
    if (s + 2 > src.length) return null;
    const offset = src[s] | (src[s + 1] << 8); s += 2;
    if (offset === 0 || offset > d) return null;
    let matchLen = (token & 15) + 4;
    if ((token & 15) === 15) { let l; do { l = src[s++]; matchLen += l; } while (l === 255); }
    if (d + matchLen > dst.length) return null;
    let m = d - offset;
    if (offset === 1) { const bb = dst[m]; for (let i = 0; i < matchLen; i++) dst[d++] = bb; }
    else for (let i = 0; i < matchLen; i++) dst[d++] = dst[m++];
  }
  if (d !== dst.length) return null;
  return { out: dst, consumed: s };
}

function extractPayload(buf) {
  let p = 0;
  const cstr = () => { const e = buf.indexOf(0, p); const s = buf.toString('latin1', p, e); p = e + 1; return s; };
  try {
    const sig = cstr(); if (!sig.startsWith('UnityFS')) return null;
    cstr(); // version u32 skip via manual below
  } catch { return null; }
  // 重置重走（上面吃掉了 version 的读取顺序，直接手撸偏移更稳）
  p = 0;
  const sig2 = cstr();
  const version = buf.readUInt32BE(p); p += 4;
  const unityVer = cstr(); const unityRev = cstr();
  p += 8; // bundleSize i64
  const cInfoSize = buf.readUInt32BE(p); p += 4;
  const uInfoSize = buf.readUInt32BE(p); p += 4;
  const flags = buf.readUInt32BE(p); p += 4;
  const candOffs = [Math.ceil(p / 16) * 16, buf.length - cInfoSize];
  let info = null;
  for (const off of candOffs) {
    if (off < 0 || off + cInfoSize > buf.length) continue;
    const raw = buf.subarray(off, off + cInfoSize);
    let r = null;
    if ((flags & 0x3F) === 0) r = { out: Buffer.from(raw), consumed: raw.length };
    else r = lz4(raw, uInfoSize);
    if (r && r.consumed === raw.length) { info = r.out; break; }
  }
  if (!info) return null;
  const dataP0 = (candOffs[1] >= 0 && candOffs[1] + cInfoSize <= buf.length && info) ? -1 : -1; // 占位无用
  // 重算 infoOff：上面循环没有返回 off——重扫一遍
  let infoOff = -1;
  for (const off of candOffs) {
    if (off < 0 || off + cInfoSize > buf.length) continue;
    const raw = buf.subarray(off, off + cInfoSize);
    let ok;
    if ((flags & 0x3F) === 0) ok = true;
    else { const r = lz4(raw, uInfoSize); ok = r && r.consumed === raw.length; }
    if (ok) { infoOff = off; break; }
  }
  let dataStart = infoOff + cInfoSize;
  let q = -1;
  for (let cand = 0; cand <= 60; cand += 4) {
    const n = info.readInt32BE(cand);
    if (n > 0 && n < 100000 && cand + 4 + n * 10 <= info.length) {
      const u0 = info.readUInt32BE(cand + 4);
      if (u0 > 0 && u0 <= 1 << 21) { q = cand + 4; break; }
    }
  }
  if (q < 0) return null;
  const blockCount = info.readInt32BE(q - 4);
  const chunks = [];
  let dataP = dataStart;
  for (let i = 0; i < blockCount; i++) {
    const u = info.readUInt32BE(q); q += 4;
    const c = info.readUInt32BE(q); q += 4;
    const f = info.readUInt16BE(q); q += 2;
    const bf = f & 0x3F;
    if (bf === 0) { chunks.push(Buffer.from(buf.subarray(dataP, dataP + c))); dataP += c; continue; }
    if (bf !== 1 && bf !== 2 && bf !== 3) return null;
    let done = false;
    for (let shift = 0; shift <= 256 && !done; shift++) {
      for (const s2 of (shift === 0 ? [0] : [shift, -shift])) {
        const st = dataP + s2;
        if (st < 0 || st + c > buf.length) continue;
        const r = lz4(buf.subarray(st, st + c), u);
        if (r && r.consumed === c) { chunks.push(r.out); dataP = st + c; done = true; break; }
      }
    }
    if (!done) return null;
  }
  return Buffer.concat(chunks);
}

const dir = process.argv[2];
const MARKERS = ['DefenseLv_Info', 'Talent_Name_32111', 'Config_MemoryPack', 'ProductionList_ShopName_325'];
const files = fs.readdirSync(dir).filter(f => f.endsWith('.bundle'));
let hits = 0, fails = 0;
for (const f of files) {
  const buf = fs.readFileSync(path.join(dir, f));
  const payload = extractPayload(buf) || buf;
  const found = MARKERS.filter(m => payload.includes(Buffer.from(m, 'utf8')) || payload.includes(Buffer.from(m, 'latin1')));
  if (found.length) { console.log(f, '->', found.join(',')); hits++; }
}
console.error('scanned', files.length, 'hits', hits);
