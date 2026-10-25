// UnityFS(data.unity3d) 解包第一步：LZ4 解 blocksInfo + 全部数据块，拼接 CAB payload 落盘
// 用法：node tools/unityfs_extract.js <in.unity3d> <out.bin>
const fs = require('fs');

function lz4Decompress2(src, uncompressedSize) {
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

const inPath = process.argv[2], outPath = process.argv[3];
const buf = fs.readFileSync(inPath);
let p = 0;
function cstr() { const e = buf.indexOf(0, p); const s = buf.toString('latin1', p, e); p = e + 1; return s; }
function u32be() { const v = buf.readUInt32BE(p); p += 4; return v; }
function u16be() { const v = buf.readUInt16BE(p); p += 2; return v; }
function i32be() { const v = buf.readInt32BE(p); p += 4; return v; }
function i64be() { const v = Number(buf.readBigInt64BE(p)); p += 8; return v; }

const sig = cstr();
const version = u32be();
const unityVer = cstr();
const unityRev = cstr();
const bundleSize = i64be();
const cInfoSize = u32be();
const uInfoSize = u32be();
const flags = u32be();
console.log('sig', sig, 'ver', version, unityVer, unityRev, 'size', bundleSize, 'cInfo', cInfoSize, 'uInfo', uInfoSize, 'flags 0x' + flags.toString(16));

// flags 位与实测可能不符：优先 16 对齐前端，失败再试文件尾（手工 trace 实证 0x40 处为合法 LZ4）
const candOffs = [];
candOffs.push(Math.ceil(p / 16) * 16);
candOffs.push(buf.length - cInfoSize);
let info = null, infoOff = -1;
for (const off of candOffs) {
  if (off < 0 || off + cInfoSize > buf.length) continue;
  const raw = buf.subarray(off, off + cInfoSize);
  let r = null;
  if ((flags & 0x3F) === 0) r = { out: Buffer.from(raw), consumed: raw.length };
  else r = lz4Decompress2(raw, uInfoSize);
  if (r && r.consumed === raw.length) { info = r.out; infoOff = off; break; }
}
if (!info) { console.error('blocksInfo 两个候选位置都解不开'); process.exit(1); }
console.log('infoOff 0x' + infoOff.toString(16));
const dataP0 = infoOff + cInfoSize;
console.log('dataStart 0x' + dataP0.toString(16) + ' dataAvail ' + (buf.length - dataP0));

// blocksInfo 布局实测：先扫前 64 字节找 int32 合理 blockCount（其后紧贴块三元组）
let q = -1;
for (let cand = 0; cand <= 60; cand += 4) {
  const n = info.readInt32BE(cand);
  if (n > 0 && n < 100000 && cand + 4 + n * 10 <= info.length) {
    const u0 = info.readUInt32BE(cand + 4);
    if (u0 > 0 && u0 <= 1 << 21) { q = cand + 4; console.log('blockCount', n, '@0x' + cand.toString(16)); break; }
  }
}
if (q < 0) { console.error('找不到 blockCount'); process.exit(1); }
const blockCount = info.readInt32BE(q - 4);
const blocks = [];
for (let i = 0; i < blockCount; i++) {
  const u = info.readUInt32BE(q); q += 4;
  const c = info.readUInt32BE(q); q += 4;
  const f = info.readUInt16BE(q); q += 2;
  blocks.push({ u, c, f });
}
const nodeCount = info.readInt32BE(q); q += 4;
const nodes = [];
for (let i = 0; i < nodeCount; i++) {
  const off = Number(info.readBigInt64BE(q)); q += 8;
  const size = Number(info.readBigInt64BE(q)); q += 8;
  const nf = info.readUInt32BE(q); q += 4;
  const e = info.indexOf(0, q); const name = info.toString('latin1', q, e); q = e + 1;
  nodes.push({ off, size, nf, name });
}
console.log('blocks', blockCount, 'nodes', nodes.map(n => n.name + '@' + n.off + '+' + n.size).join(', '));

// 数据块顺序解压：LZ4 必须精确消耗 c 并产出 u，否则在 ±256 字节内重同步起点
let dataP = dataP0;
const outChunks = [];
for (let i = 0; i < blocks.length; i++) {
  const b = blocks[i];
  const bf = b.f & 0x3F;
  if (bf === 0) { outChunks.push(Buffer.from(buf.subarray(dataP, dataP + b.c))); dataP += b.c; continue; }
  if (bf !== 1 && bf !== 2 && bf !== 3) { console.error('数据块压缩类型不支持: ' + bf); process.exit(1); }
  let done = false;
  for (let shift = 0; shift <= 256 && !done; shift++) {
    for (const s2 of (shift === 0 ? [0] : [shift, -shift])) {
      const st = dataP + s2;
      if (st < 0 || st + b.c > buf.length) continue;
      const r = lz4Decompress2(buf.subarray(st, st + b.c), b.u);
      if (r && r.consumed === b.c) {
        outChunks.push(r.out); dataP = st + b.c; done = true; break;
      }
    }
  }  if (!done) { console.error('block ' + i + ' 解压失败，p=' + dataP); process.exit(1); }
}
const payload = Buffer.concat(outChunks);
fs.writeFileSync(outPath, payload);
console.log('payload OK', payload.length, '->', outPath);
