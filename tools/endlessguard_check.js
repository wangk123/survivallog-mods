// EndlessGuard 数据单源离线校验：node endlessguard_check.js
// 校验 scavenge_plans.json 对 gamekb 快照的合法性；全绿 exit 0，任何一条红 exit 1。
const fs = require('fs');
const path = require('path');
const ROOT = path.join(__dirname, '..');
const KB = fs.readdirSync(path.join(ROOT, 'gamekb')).filter(d => /^\d{4}-\d{2}-\d{2}_/.test(d)).sort().pop();
const T = f => JSON.parse(fs.readFileSync(path.join(ROOT, 'gamekb', KB, 'tables', f), 'utf8')).data;
const data = JSON.parse(fs.readFileSync(path.join(ROOT, 'SurvivalLog.EndlessGuard', 'scavenge_plans.json'), 'utf8'));
let bad = 0;
const fail = m => { console.error('[FAIL]', m); bad++; };
const ok = m => console.log('[ok]', m);

// 1) plans：11 条、ID/条件集在 gamekb 空闲、链一致、日/价单调
const talentIds = new Set(T('Config_Talent.json').map(r => r._ID_k__BackingField));
const condIds = new Set(T('Config_ConditionSet.json').map(r => r.ID ?? r._ID_k__BackingField));
if (data.plans.length !== 11) fail('plans 应为 11 条，实际 ' + data.plans.length);
for (const p of data.plans) {
  if (talentIds.has(p.talentId)) fail(`talentId ${p.talentId} 已被占用`);
  if (condIds.has(p.condId)) fail(`condId ${p.condId} 已被占用`);
}
const byMap = {};
for (const p of data.plans) (byMap[p.mapId] = byMap[p.mapId] || []).push(p);
for (const [m, list] of Object.entries(byMap)) {
  list.sort((a, b) => a.level - b.level);
  for (let i = 1; i < list.length; i++) {
    if (list[i].day <= list[i - 1].day) fail(`图${m} ${list[i].name} 解锁日未递增`);
    if (list[i].cost <= list[i - 1].cost) fail(`图${m} ${list[i].name} 消耗未递增`);
    if (list[i].prereq !== list[i - 1].talentId) fail(`图${m} ${list[i].name} 前置链断裂`);
  }
}
const first = { 1004: 31605, 1005: 31622, 1002: 31632 };
for (const [m, f] of Object.entries(first)) {
  const l = byMap[m][0];
  if (l.prereq !== f) fail(`图${m} 首条前置应为 ${f}，实际 ${l.prereq}`);
}
if (!bad) ok('plans：11 条，ID 全空闲，链/日/价全单调');

// 2) baskets：名字在 Config_Item 唯一命中、不在对应图现有池
const items = T('Config_Item.json');
const byName = new Map();
for (const r of items) {
  const n = r._ItemName_Local_k__BackingField;
  if (n == null) continue;
  if (!byName.has(n)) byName.set(n, []); byName.get(n).push(r._ID_k__BackingField);
}
const rg = new Map(T('Config_RandomGroup.json').map(r => [r._ID_k__BackingField, r]));
const mapPools = { 1004: [8001,8002,8003,8004,8005,8006,8007,8008,8009,8010], 1005: [8024,8025,8026,8215,8216,8217,8218,8219,8220], 1002: [8012,8013,8014,8015,8016,8017,8018,8019,8020,8023,8221,8222,8223,8224,8225,8226] };
const poolItems = {};
for (const [m, ids] of Object.entries(mapPools)) {
  const s = new Set();
  for (const gid of ids) for (const i of (rg.get(gid)?._IdList_k__BackingField || [])) s.add(i);
  poolItems[m] = s;
}
const itemIds = new Set(items.map(r => r._ID_k__BackingField));
const resolvedIds = [];
for (const b of data.baskets) {
  if (!mapPools[b.mapId].includes(b.poolId)) fail(`basket 图${b.mapId} 池 ${b.poolId} 不属于该图`);
  for (const it of b.items) {
    const hits = byName.get(it.name);
    let id;
    if (it.itemId !== undefined) {
      // 显式 itemId：以 itemId 为准；名字唯一解析结果必须等于 itemId，名字多义时要求 itemId ∈ 同名候选集
      if (!itemIds.has(it.itemId)) { fail(`itemId 不存在于 Config_Item：${it.name}(${it.itemId})`); continue; }
      if (!hits || hits.length === 0) { fail(`物品名未命中：${it.name}`); continue; }
      if (hits.length > 1) {
        if (!hits.includes(it.itemId)) { fail(`itemId 不在同名候选集：${it.name} json=${it.itemId} 候选=${hits.join(',')}`); continue; }
      } else if (hits[0] !== it.itemId) { fail(`itemId 与名字不符：${it.name} json=${it.itemId} 实际=${hits[0]}`); continue; }
      id = it.itemId;
    } else {
      if (!hits || hits.length === 0) { fail(`物品名未命中：${it.name}`); continue; }
      if (hits.length > 1) { fail(`物品名多义（${hits.join(',')}）：${it.name}`); continue; }
      id = hits[0];
      it.itemId = id;
    }
    if (poolItems[b.mapId].has(id)) fail(`物品已在图${b.mapId}池中：${it.name}(${id})`);
    resolvedIds.push(`${it.name}=${id}`);
  }
}
if (!bad) ok('baskets：' + resolvedIds.length + ' 项全部唯一命中且不在现有池');

// 3) demotions：池里确实有该物品且原门槛等于 fromLevel
for (const d of data.demotions) {
  const g = rg.get(d.poolId);
  const idx = (g?._IdList_k__BackingField || []).indexOf(d.itemId);
  if (idx < 0) { fail(`降档目标不在池 ${d.poolId}：item ${d.itemId}`); continue; }
  const lv = (g._DiscoverLvList_k__BackingField || [])[idx];
  if (lv !== d.fromLevel) fail(`池 ${d.poolId} item ${d.itemId} 实际门槛 Lv${lv}，声明 fromLevel=${d.fromLevel}`);
  if (d.toLevel >= d.fromLevel) fail(`降档必须降：${d.note}`);
}
if (!bad) ok('demotions：9 项原门槛全部核对一致');

// 4) nativeUnlocks：池内物品在该等级门槛存在（防表版本漂移）
for (const n of data.nativeUnlocks) {
  const g = rg.get(n.poolId);
  const idx = (g?._IdList_k__BackingField || []).indexOf(n.itemId);
  if (idx < 0) { fail(`原生解锁项不在池 ${n.poolId}：item ${n.itemId}`); continue; }
  const lv = (g._DiscoverLvList_k__BackingField || [])[idx];
  if (lv !== n.level) fail(`原生项 ${n.poolId}:${n.itemId} 实际 Lv${lv}，声明 ${n.level}`);
}
if (!bad) ok('nativeUnlocks：11 项原生门槛核对一致');

// 5) 每级覆盖检查：11 张卡每张至少 1 项解锁物（篮子+降档+原生 并集）
const unlockAt = new Map();
const bump = (mapId, level) => unlockAt.set(mapId + ':' + level, (unlockAt.get(mapId + ':' + level) || 0) + 1);
for (const b of data.baskets) bump(b.mapId, b.level);
for (const d of data.demotions) {
  const p = data.plans.find(x => x.mapId === (rg.get(d.poolId) ? mapOfPool(d.poolId) : 0) && x.level === d.toLevel);
  if (p) bump(p.mapId, p.level);
}
for (const n of data.nativeUnlocks) bump(mapOfPool(n.poolId), n.level);
function mapOfPool(pid) {
  for (const [m, ids] of Object.entries(mapPools)) if (ids.includes(Number(pid))) return Number(m);
  return 0;
}
for (const p of data.plans) {
  const c = unlockAt.get(p.mapId + ':' + p.level) || 0;
  if (c === 0) fail(`${p.name}（Lv${p.level}）无任何解锁物（空档级）`);
}
if (!bad) ok('每张卡至少 1 项解锁物，无空档');

if (bad) { console.error(`共 ${bad} 条失败`); process.exit(1); }
console.log('ALL GREEN');
