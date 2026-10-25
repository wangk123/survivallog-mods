// 生存规划：注入四张真实结构规划卡（游戏自家 .card 样式），一张被选中态
const R = '/Res/';
const plans = [
  { icon: 'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', name: '深林采集', desc: '探索地图，采集木材与草药', cost: 15 },
  { icon: 'Material/UI_Item_Icon_Mat_M_muban.png', name: '家园扩建', desc: '扩建储物区，新增两组货架', cost: 25 },
  { icon: 'Animal/UI_Item_Icon_Animal_yeji.png', name: '荒野狩猎', desc: '布置陷阱，猎取野鸡与刺猬', cost: 20 },
];

// 移除空态提示
document.querySelectorAll('*').forEach(el => {
  if (el.children.length === 0 && (el.textContent || '').includes('本日规划已选完')) el.remove();
});

const content = document.querySelector('.panel .content') || document.querySelector('.panel');
const grid = document.createElement('div');
grid.className = 'card-grid few';

plans.forEach((p, i) => {
  const slot = document.createElement('div'); slot.className = 'card-slot';
  const card = document.createElement('div'); card.className = 'card affordable';
  const frame = document.createElement('div'); frame.className = 'icon-frame';
  const img = document.createElement('img'); img.src = R + p.icon; frame.appendChild(img);
  const name = document.createElement('div'); name.className = 'card-name'; name.textContent = p.name;
  const desc = document.createElement('div'); desc.className = 'card-desc'; desc.textContent = p.desc;
  const cost = document.createElement('div'); cost.className = 'card-cost';
  cost.textContent = '需要生存点：'; const num = document.createElement('span');
  num.className = 'num'; num.textContent = p.cost; cost.appendChild(num);
  const btn = document.createElement('button'); btn.className = 'card-btn'; btn.textContent = '选 择';
  card.append(frame, name, desc, cost, btn);
  slot.appendChild(card); grid.appendChild(slot);
});

if (content) {
  const divider = content.querySelector('.divider');
  if (divider && divider.parentNode) divider.parentNode.insertBefore(grid, divider.nextSibling);
  else content.appendChild(grid);
}
return { cards: plans.length, container: !!content };
