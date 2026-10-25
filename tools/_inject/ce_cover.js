// 储藏柜直达 CabinetEverywhere 封面素材：ToolTable 工作台页
// vanilla：只有 背包 页签、材料少、配方大多做不了（缺料）；mod：原生 cabinet-tab「储物柜」+ 柜中材料直达、配方 can-craft 高亮
const mod = window.__COV_VARIANT === 'mod';
const R = '/Res/';
const mats = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png', 'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', 'Material/UI_Item_Icon_Mat_M_tengman.png',
  'Material/UI_Item_Icon_Mat_M_1500110101.png', 'Material/UI_Item_Icon_Mat_M_1500200101.png'];
const food = 'Food/UI_Item_Icon_Food_M_feiyuguantou.png';

// 1) mod：注入「储物柜」页签（官方 cabinet-tab 类）并设为当前
let cabTab = false;
if (mod) {
  const bagTab = document.querySelector('#tab-bag');
  if (bagTab) bagTab.classList.remove('active');
  const scroll = document.querySelector('#leftTabsScroll');
  const cab = document.createElement('button');
  cab.className = 'left-tab cabinet-tab active';
  cab.textContent = '储物柜';
  if (scroll) scroll.appendChild(cab); else if (bagTab) bagTab.parentNode.appendChild(cab);
  cabTab = !!cab.parentNode;
}

function fillGrid(sel, slotCount, itemPaths) {
  const grid = document.querySelector(sel);
  if (!grid) return 0;
  grid.innerHTML = '';
  const slots = [];
  for (let i = 0; i < slotCount; i++) {
    const s = document.createElement('div'); s.className = 'slot';
    s.style.width = '56px'; s.style.height = '56px';
    grid.appendChild(s); slots.push(s);
  }
  const grect = grid.getBoundingClientRect();
  itemPaths.forEach((p, i) => {
    const cell = slots[Math.min(i * 3 + (i % 2), slots.length - 1)];
    const crect = cell.getBoundingClientRect();
    const it = document.createElement('div'); it.className = 'item';
    it.style.left = (crect.left - grect.left) + 'px';
    it.style.top = (crect.top - grect.top) + 'px';
    it.style.width = '56px'; it.style.height = '56px';
    it.style.pointerEvents = 'none';
    const img = document.createElement('img'); img.src = R + p;
    img.style.width = '80%'; img.style.height = '80%';
    img.style.objectFit = 'contain'; img.style.position = 'relative'; img.style.zIndex = '2';
    it.appendChild(img); grid.appendChild(it);
  });
  return slots.length;
}

// 左栏：vanilla=背包 6 件材料；mod=柜子里 10 件材料直达
const nLeft = fillGrid('#backpack-grid', mod ? 30 : 18, mod ? mats : mats.slice(0, 6));
// 右栏：工作台格 + 产品预览
const nRight = fillGrid('#workbench-grid', 6, [mats[0], mats[1], food]);
const pp = document.querySelector('#pp-slot');
if (pp) { pp.innerHTML = ''; const im = document.createElement('img'); im.src = R + mats[0]; pp.appendChild(im); }

// 2) 配方列表（官方 recipe-item 行）：mod 柜子材料直达 → 多行 can-craft 高亮；vanilla 缺料灰行
const recipes = ['Material/UI_Item_Icon_Mat_M_muban.png|木板', 'Material/UI_Item_Icon_Mat_M_mutiao.png|木条',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png|胶合板', 'Material/UI_Item_Icon_Mat_M_bangdai.png|布带',
  'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png|电气元件', 'Material/UI_Item_Icon_Mat_M_tanxianweiban.png|探险围板'];
const list = document.querySelector('#recipe-list');
let nRecipes = 0;
if (list) {
  recipes.forEach((rp, i) => {
    const [icon, name] = rp.split('|');
    const el = document.createElement('div');
    el.className = 'recipe-item' + (mod && i < 4 ? ' can-craft' : '');
    const iconBox = document.createElement('span'); iconBox.className = 'recipe-icon';
    const img = document.createElement('img'); img.src = R + icon; iconBox.appendChild(img);
    const nameSpan = document.createElement('span'); nameSpan.className = 'recipe-item-name';
    nameSpan.textContent = name;
    el.append(iconBox, nameSpan);
    list.appendChild(el); nRecipes++;
  });
}

// 特写：页签行 + 格子上部（工作台能看到柜子页签与柜中材料）
let zoomClip = null;
const tabs = document.querySelector('#tab-bag') && document.querySelector('#tab-bag').closest('.left-tabs') || document.querySelector('#tab-bag').parentNode;
const grid = document.querySelector('#backpack-grid');
if (tabs && grid) {
  const t = tabs.getBoundingClientRect(), g = grid.getBoundingClientRect();
  zoomClip = [Math.max(8, t.left - 14), Math.max(8, t.top - 10), g.width + 28, (t.height) + g.height * 0.85 + 24];
  zoomClip = zoomClip.map(Math.round);
}

return { variant: window.__COV_VARIANT, nLeft, nRight, cabTab, nRecipes, zoomClip };
