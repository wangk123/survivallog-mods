// 柜子扩容 CabinetExpand 封面素材：ToolTable 工作台页，官方 cabinet-tab 页签 + 官方格子背包 API
// vanilla：超大型置物架 15x12；mod：23x18（宽高 x1.5，bag 110008 实证 15x12 -> 23x18）；格子填真实材料约一半
// 柜子页签官方语义：applyLeftGrid 对 isCabinet 隐藏负重行（照抄官方行为）
const mod = window.__COV_VARIANT === 'mod';
const R = '/Res/';
const MAT = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png', 'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', 'Material/UI_Item_Icon_Mat_M_tengman.png',
  'Material/UI_Item_Icon_Mat_M_1500110101.png', 'Material/UI_Item_Icon_Mat_M_1500200101.png',
  'Material/UI_Item_Icon_Mat_M_1500200301.png', 'Material/UI_Item_Icon_Mat_M_1500210101.png'];
const FOOD = ['Food/UI_Item_Icon_Food_M_feiyuguantou.png', 'Food/UI_Item_Icon_Food_M_huangtaoguantou.png'];

// 0) 离线没有首批消息，直接揭示（官方 revealFirstData 的兜底语义）
const ui = document.querySelector('.ui-container');
if (ui) ui.classList.remove('await-first-data');
// 制造笔记栏：配方数据到位时官方移除 no-notes（离线没消息，手动走同一路径）
const combined = document.querySelector('.combined-panel');
if (combined) combined.classList.remove('no-notes');

// 1) 官方柜子页签：left-tab cabinet-tab（页面自身 JS 同款类名），背包页签让位
const bagTab = document.querySelector('#tab-bag');
if (bagTab) bagTab.classList.remove('active');
const scroll = document.querySelector('#leftTabsScroll');
let cabTab = false;
if (scroll) {
  const cab = document.createElement('button');
  cab.className = 'left-tab cabinet-tab active';
  cab.textContent = '储物柜（超大型置物架）';
  scroll.appendChild(cab);
  cabTab = true;
}

// 2) 柜子视图：官方对 isCabinet 的处理 —— 负重行隐藏
const wsec = document.querySelector('.weight-section');
if (wsec) wsec.style.visibility = 'hidden';

// 3) 官方格子背包 API：backpack.resize 画格子、backpack.refresh 走 mapItem 官方渲染链
const cols = mod ? 23 : 15, rows = mod ? 18 : 12;
const items = [];
let id = 5000;
for (let y = 0; y < rows; y++) {
  for (let x = 0; x < cols; x++) {
    const i = y * cols + x;
    if ((i * 3 + 1) % 8 < 4) continue; // 约一半
    const pool = (i % 8 === 3) ? FOOD : MAT;
    items.push({
      itemId: id++, x: x, y: y, w: 1, h: 1,
      icon: R + pool[i % pool.length],
      count: (i % 7 === 2) ? 3 + (i % 6) : 1,
      quality: (i % 9 === 5) ? 2 : 3
    });
  }
}
let official = false;
try {
  if (typeof backpack !== 'undefined' && backpack && backpack.resize) {
    backpack.resize(cols, rows);
    backpack.refresh(items);
    official = true;
  }
} catch (e) { official = false; }
let nLeft = official ? items.length : 0;
if (!official) {
  // 兜底：手动官方类名（同 ce_cover 管线）
  const grid = document.querySelector('#backpack-grid');
  if (grid) {
    grid.innerHTML = '';
    grid.style.gridTemplateColumns = 'repeat(' + cols + ', 56px)';
    grid.style.gridTemplateRows = 'repeat(' + rows + ', 56px)';
    for (let i = 0; i < cols * rows; i++) { const s = document.createElement('div'); s.className = 'slot'; grid.appendChild(s); }
    items.forEach(it => {
      const el = document.createElement('div'); el.className = 'item';
      el.style.cssText = 'position:absolute;left:' + (it.x * 56) + 'px;top:' + (it.y * 56) + 'px;width:56px;height:56px;';
      const img = document.createElement('img'); img.src = it.icon; el.appendChild(img);
      if (it.count > 1) { const c = document.createElement('span'); c.className = 'item-count'; c.textContent = 'x' + it.count; el.appendChild(c); }
      grid.appendChild(el);
    });
    nLeft = items.length;
  }
}

// 4) 工作台侧（官方结构轻填充，同 ce 封面口径）：6 格 + 配方行
try {
  if (typeof workbench !== 'undefined' && workbench && workbench.resize) {
    workbench.resize(6, 1);
    workbench.refresh([
      { itemId: 9001, x: 0, y: 0, w: 1, h: 1, icon: R + MAT[0], count: 1, quality: 3 },
      { itemId: 9002, x: 1, y: 0, w: 1, h: 1, icon: R + MAT[1], count: 1, quality: 3 },
      { itemId: 9003, x: 2, y: 0, w: 1, h: 1, icon: R + FOOD[0], count: 1, quality: 3 }
    ]);
  }
} catch (e) { }
const recipes = ['Material/UI_Item_Icon_Mat_M_muban.png|木板', 'Material/UI_Item_Icon_Mat_M_mutiao.png|木条',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png|胶合板', 'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png|电气元件',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png|探险围板', 'Material/UI_Item_Icon_Mat_M_tengman.png|藤蔓'];
const list = document.querySelector('#recipe-list');
let nRecipes = 0;
if (list) {
  recipes.forEach((rp, i) => {
    const parts = rp.split('|');
    const el = document.createElement('div');
    el.className = 'recipe-item' + (i < 4 ? ' can-craft' : '');
    const iconBox = document.createElement('span'); iconBox.className = 'recipe-icon';
    const img = document.createElement('img'); img.src = R + parts[0]; iconBox.appendChild(img);
    const nameSpan = document.createElement('span'); nameSpan.className = 'recipe-item-name';
    nameSpan.textContent = parts[1];
    el.append(iconBox, nameSpan);
    list.appendChild(el); nRecipes++;
  });
}

// 5) 官方视口适配：body zoom 让 23x18 也整页入镜
if (typeof applyViewportFit === 'function') applyViewportFit();
await new Promise(r => setTimeout(r, 500));
if (typeof applyViewportFit === 'function') applyViewportFit();

// 6) 特写：页签行 + 格子左上角
const tabs = document.querySelector('.left-tabs');
const gridEl = document.querySelector('#backpack-grid');
let zoomClip = null;
if (tabs && gridEl) {
  const t = tabs.getBoundingClientRect(), g = gridEl.getBoundingClientRect();
  const w = Math.min(g.width + 24, 780);
  zoomClip = [Math.round(Math.max(8, t.left - 12)), Math.round(Math.max(8, t.top - 10)),
              Math.round(w), Math.round(t.height + g.height * 0.22 + 20)];
}

return {
  variant: window.__COV_VARIANT, cols: cols, rows: rows, slots: cols * rows,
  nItems: nLeft, officialApi: official, cabTab: cabTab,
  weightHidden: wsec ? getComputedStyle(wsec).visibility === 'hidden' : null,
  nRecipes: nRecipes, zoomClip: zoomClip
};
