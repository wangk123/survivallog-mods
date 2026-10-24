// PlanPick 原型数据生成器：从 gamekb 表生成 plan_data.json
// 用法：node gen_data.js <gamekb版本目录>  （默认 ../../../gamekb/2026-10-23_26157732）
// 规则与 DESIGN.md §4 一致；游戏更新后重跑 + 过目 diff。
// 注意：icon 由官方 WebIcon "../../Res/..." 改写为 "/Res/..."（本地预览用；游戏内实装用原始路径）。
const fs = require('fs');
const path = require('path');

const gamekb = path.resolve(process.argv[2] || path.join(__dirname, '../../gamekb/2026-10-23_26157732'));
const tables = p => JSON.parse(fs.readFileSync(path.join(gamekb, 'tables', p), 'utf8'));

const talent = tables('Config_Talent.json').data;
const groups = tables('Config_RandomGroup.json').data;
const daily = tables('Config_DailyRandom.json').data;
const condSets = tables('Config_ConditionSet.json').data;

// ---- 池最早出现日（DailyRandom 引用为准）----
const poolFirstDay = {};
daily.forEach(r => (r.RandomPlanID || []).concat(r.SpecifiedPlanID || []).forEach(g => {
    if (g && !(g in poolFirstDay)) poolFirstDay[g] = r.Day;
}));

// ---- 分类（按序判定）----
const materialIds = new Set();
groups.filter(g => [1005, 1009].includes(g.ID)).forEach(g => (g.IdList || []).forEach(id => materialIds.add(id)));
const expandPool = new Set((groups.find(g => g.ID === 9122) || { IdList: [] }).IdList);

function category(r) {
    if (materialIds.has(r.ID)) return 'material';
    if (String(r.Name_Local || '').startsWith('扩建·') || expandPool.has(r.ID)) return 'expand';
    if (r.ID >= 30000 && r.ID < 31000) return 'attribute';
    return 'function';
}

const rows_raw = talent.filter(r => r.Type === 2 && r.Available === 1);

// ---- 每规划解锁日 = 所属各池最早日的最小值 ----
const planDay = {};
groups.forEach(g => (g.IdList || []).forEach(id => {
    const d = poolFirstDay[g.ID];
    if (d && (!(id in planDay) || d < planDay[id])) planDay[id] = d;
}));

// ---- 二级分类（作者 2026-10-24 反馈：一级不够找）----
// 规则：家具扩建 → 区域（"扩建·二楼中型位"取"二楼"，剥掉尾部 大/中/小/桌面位）；
//       其余 → 系列（PlanLvGate 前置链成员取名字公共前缀；无链/孤立系列 → 独立规划）。
const condById = {};
condSets.forEach(c => condById[c.ID] = c);
const t2allIds = new Set(talent.filter(r => r.Type === 2).map(r => r.ID));
const parent = {};   // id -> 前置 id（仅 PlanLvGate 单前置链）
rows_raw.forEach(r => {
    if (!r.ConditionSetId) return;
    const c = condById[r.ConditionSetId];
    if (!c) return;
    const tn = c.TalentNeed || [];
    if (tn.length === 1 && t2allIds.has(tn[0]) && String(c.Source || '').startsWith('PlanLvGate')) parent[r.ID] = tn[0];
});
function rootOf(id) { const seen = new Set(); while (parent[id] && !seen.has(id)) { seen.add(id); id = parent[id]; } return id; }
function lcp(names) {
    if (names.length < 2) return '';
    let s = names[0];
    for (const n of names) { let i = 0; while (i < s.length && i < n.length && s[i] === n[i]) i++; s = s.slice(0, i); }
    return s;
}
const chainMembers = {};   // rootId -> [row]
rows_raw.forEach(r => (chainMembers[rootOf(r.ID)] = chainMembers[rootOf(r.ID)] || []).push(r));
const seriesLabel = {};    // id -> 系列名（null = 独立）
Object.values(chainMembers).forEach(g => {
    if (g.length > 1) {
        let lbl = lcp(g.map(r => r.Name_Local))
            .replace(/\s*Lv[\s·]*$/, '')   // "探索老手 Lv1/2/3" LCP 尾巴
            .replace(/[\s·]+$/, '');       // "超市拾遗·一/二" 尾巴
        if (lbl.length < 2) lbl = g[0].Name_Local.replace(/ Lv\d+$/, '');
        g.forEach(r => seriesLabel[r.ID] = lbl);
    }
});
function subOf(r) {
    if (category(r) === 'expand') {
        if (String(r.Name_Local || '').startsWith('扩建·')) {
            const seg = String(r.Name_Local).split('·')[1] || '';
            const region = seg.replace(/(大型位|中型位|小型位|桌面位)$/, '') || seg;
            return region;
        }
        if (Array.isArray(r.PlayerSelectId) && r.PlayerSelectId.length) return '专属家具位'; // 9122 池内角色家具位
    }
    return seriesLabel[r.ID] || null;   // null -> 独立规划
}

// ---- 条件集：前置链 + 特殊条件种类 ----
const nameById = {};
talent.forEach(r => nameById[r.ID] = r.Name_Local);

function conditions(r) {
    if (!r.ConditionSetId) return { prereq: [], special: [] };
    const c = condById[r.ConditionSetId];
    if (!c) return { prereq: [], special: [] };
    const prereq = (c.TalentNeed || []).map(id => ({ id, name: nameById[id] || ('#' + id) }));
    const special = [];
    if (c.SurvivalToDay) special.push('生存至第' + c.SurvivalToDay + '天');
    if ((c.NeedFurnitureIds || []).length) special.push('需持有特定家具');
    if ((c.EventTag || []).length) special.push('需解锁特定地点/事件');
    if (c.DefenseLevel) special.push('需防御等级' + c.DefenseLevel);
    if ((c.NeedTrapObtainable || []).length) special.push('需可制作特定防御装置');
    if ((c.BuffNeed || []).length) special.push('需特定状态');
    if (c.HistoryCount) special.push('需历史记录');
    return { prereq, special };
}

// ---- 输出 ----
const rows = rows_raw.map(r => {
    const { prereq, special } = conditions(r);
    const icon = (r.WebIcon || '').replace(/^(\.\.\/)+/, '/');
    return {
        id: r.ID,
        name: r.Name_Local,
        desc: r.Dec_Local,
        icon: icon || null,
        cost: r.LvUpCost,
        isReget: !!r.IsReget,
        cat: category(r),
        sub: subOf(r),                                          // 二级分类；null = 独立规划
        unlockDay: (r.ID in planDay) ? planDay[r.ID] : null,   // null = 不入池
        prereq, special,
        role: Array.isArray(r.PlayerSelectId) ? r.PlayerSelectId : [],
    };
});

const stats = { total: rows.length, byCat: {}, byCatSub: {} };
rows.forEach(r => {
    stats.byCat[r.cat] = (stats.byCat[r.cat] || 0) + 1;
    const key = r.cat + '/' + (r.sub === null ? '(独立)' : r.sub);
    stats.byCatSub[key] = (stats.byCatSub[key] || 0) + 1;
});

const out = {
    generatedFrom: path.basename(gamekb),
    generatedAt: new Date().toISOString(),
    stats,
    categories: [
        { key: 'attribute', name: '属性成长' },
        { key: 'function', name: '功能强化' },
        { key: 'expand', name: '家具扩建' },
        { key: 'material', name: '物资贸易' },
    ],
    // 今日候选样例（原型演示"候选内原价"用；实装为运行期真实名单）
    todaySample: [30011, 30661, 31442],
    plans: rows,
};

const outPath = path.join(__dirname, 'plan_data.json');
fs.writeFileSync(outPath, JSON.stringify(out));
console.log('OK', outPath, JSON.stringify(stats));
