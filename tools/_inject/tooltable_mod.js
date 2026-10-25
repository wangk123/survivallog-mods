// 工作台：注入「储物柜」原生 cabinet-tab（mod 效果）+ 柜子物品 + 右侧配方物品
const R = '/Res/';
const mats = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png', 'Material/UI_Item_Icon_Mat_buxiugangban.png',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', 'Material/UI_Item_Icon_Mat_bolidanzhu.png'];
const food = 'Food/UI_Item_Icon_Food_M_feiyuguantou.png';

// 1) 页签：储物柜（游戏原生 cabinet-tab 类），设为当前页签
const bagTab = document.querySelector('#tab-bag');
if (bagTab) bagTab.classList.remove('active');
const scroll = document.querySelector('#leftTabsScroll');
const cab = document.createElement('button');
cab.className = 'left-tab cabinet-tab active';
cab.textContent = '储物柜';
if (scroll) scroll.appendChild(cab); else if (bagTab) bagTab.parentNode.appendChild(cab);

function fillGrid(sel, slotCount, itemPaths) {
  const grid = document.querySelector(sel);
  if (!grid) return 0;
  grid.innerHTML = '';
  const slots = [];
  for (let i = 0; i < slotCount; i++) {
    const s = document.createElement('div');
    s.className = 'slot';
    s.style.width = '56px'; s.style.height = '56px';
    grid.appendChild(s); slots.push(s);
  }
  const grect = grid.getBoundingClientRect();
  itemPaths.forEach((p, i) => {
    const cell = slots[Math.min(i * 3 + (i % 2), slots.length - 1)];
    const crect = cell.getBoundingClientRect();
    const it = document.createElement('div');
    it.className = 'item';
    it.style.left = (crect.left - grect.left) + 'px';
    it.style.top = (crect.top - grect.top) + 'px';
    it.style.width = '56px'; it.style.height = '56px';
    it.style.pointerEvents = 'none';
    const img = document.createElement('img');
    img.src = R + p; img.style.width = '80%'; img.style.height = '80%';
    img.style.objectFit = 'contain'; img.style.position = 'relative'; img.style.zIndex = '2';
    it.appendChild(img);
    grid.appendChild(it);
  });
  return slots.length;
}

const nLeft = fillGrid('#backpack-grid', 24, mats.slice(0, 8));
const nRight = fillGrid('#workbench-grid', 6, [mats[0], mats[1], food]);

// 3) 产品预览 + 制造按钮
const pp = document.querySelector('#pp-slot');
if (pp) { pp.innerHTML = ''; const im = document.createElement('img');
  im.src = R + 'Material/UI_Item_Icon_Mat_M_muban.png'; pp.appendChild(im); }
let craftShown = 0;
document.querySelectorAll('button').forEach(b => {
  const t = (b.textContent || '').trim();
  if ((t.includes('制造') || t.includes('制作')) && b.offsetParent === null) { b.style.display = ''; craftShown++; }
});

return { nLeft, nRight, cabTab: !!cab.parentNode, craftShown };
