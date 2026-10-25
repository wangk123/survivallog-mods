// 角色专属解锁 RoleUnlock 封面素材：RogueSelect 页真卡（官方 card DOM）
// vanilla：专属卡 locked（未 解 锁）＝其他角色的痛点；mod：同卡 exclusive 可购（专 印章金光）
// 卡片数据取自真实规划表（烈焰配方=燃烧瓶强化·打工仔专属线；老主顾=仓库管理员专属）
const mod = window.__COV_VARIANT === 'mod';

const EX = [
  { icon: '/Res/Consumable/UI_Item_Icon_Cons_ranshaoping.png', name: '烈焰配方', desc: '燃烧瓶的爆燃伤害提升25%', cost: 15 },
  { icon: '/Res/icon/UI_Item_Icon_Buff_laozhugu.png', name: '老主顾', desc: '陌生人货架最多可摆出 6 种货物', cost: 80 },
];
const NM = [
  { icon: '/Res/Literature/UI_Item_Icon_Liter_M_1400295201.png', name: '大厨手感', desc: '做菜更得心应手，烹饪耗时-30%', cost: 15 },
  { icon: '/Res/Food/UI_Item_Icon_Food_M_1301100101_seeds.png', name: '种子库 Lv2', desc: '收获有20%概率额外获得1颗种子', cost: 20 },
];

// 移除空态提示（离线无数据 Vue cards 为空）
document.querySelectorAll('.empty-state').forEach(e => e.remove());

function card(p, state, exclusive) {
  const slot = document.createElement('div'); slot.className = 'card-slot';
  const c = document.createElement('div');
  c.className = 'card ' + state + (exclusive ? ' exclusive' : '');
  if (state === 'locked') {
    const lb = document.createElement('div'); lb.className = 'lock-badge'; lb.textContent = '未 解 锁'; c.appendChild(lb);
  }
  if (exclusive) {
    const es = document.createElement('div'); es.className = 'exclusive-seal'; es.textContent = '专'; c.appendChild(es);
  }
  const frame = document.createElement('div'); frame.className = 'icon-frame';
  const img = document.createElement('img'); img.src = p.icon; img.alt = p.name; frame.appendChild(img);
  if (state === 'locked') { const lo = document.createElement('div'); lo.className = 'lock-overlay'; frame.appendChild(lo); }
  const name = document.createElement('div'); name.className = 'card-name'; name.textContent = p.name;
  const desc = document.createElement('div'); desc.className = 'card-desc'; desc.textContent = p.desc;
  const cost = document.createElement('div'); cost.className = 'card-cost';
  cost.textContent = '需要生存点：'; const num = document.createElement('span'); num.className = 'num'; num.textContent = p.cost; cost.appendChild(num);
  const btn = document.createElement('button'); btn.className = 'card-btn' + (state !== 'affordable' ? ' disabled' : '');
  btn.textContent = state === 'locked' ? '未 解 锁' : '激 活';
  c.append(frame, name, desc, cost, btn);
  slot.appendChild(c);
  return slot;
}

const content = document.querySelector('.panel .content') || document.querySelector('.panel');
let grid = content && content.querySelector('.card-grid');
if (!grid) {
  grid = document.createElement('div'); grid.className = 'card-grid';
  const divider = content.querySelector('.divider');
  if (divider && divider.parentNode) divider.parentNode.insertBefore(grid, divider.nextSibling);
  else content.appendChild(grid);
}
grid.innerHTML = '';

let plans;
if (mod) plans = [card(EX[0], 'affordable', true), card(EX[1], 'affordable', true), card(NM[0], 'affordable', false), card(NM[1], 'affordable', false)];
else plans = [card(NM[0], 'affordable', false), card(EX[0], 'locked', true), card(NM[1], 'affordable', false)];
plans.forEach(p => grid.appendChild(p));

// 玩家点数展示：拿真实界面右上角样式（有就更新数字，没有就算了）
let pointsShown = null;
document.querySelectorAll('*').forEach(el => {
  if (el.children.length === 0 && /生存点/.test(el.textContent || '') && !pointsShown) pointsShown = el.textContent.trim();
});

return {
  variant: window.__COV_VARIANT,
  cards: plans.length,
  exclusive: mod ? 2 : 1,
  grid: grid.className,
  zoom: mod ? '.card.exclusive' : null,
};
