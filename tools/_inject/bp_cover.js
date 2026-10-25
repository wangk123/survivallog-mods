// 背包扩容 BackpackExpand 封面素材：BackpackUI 页，走官方 createWebUICore 注册的消息 handler
// vanilla：普通背包 8x7 + 负重 9.6 / 9.6 Kg 红色满条；mod：11x10 + 12.0 / 48.0 Kg 绿色宽裕（金色 25%）
// 原生值依据：Config_Bag id1 = 8x7 / 9600（BackpackExpand DESIGN §2）；mod 默认三键 11/10/48000
const mod = window.__COV_VARIANT === 'mod';
const R = '/Res/';
const MAT = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png', 'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', 'Material/UI_Item_Icon_Mat_M_tengman.png',
  'Material/UI_Item_Icon_Mat_M_1500110101.png', 'Material/UI_Item_Icon_Mat_M_1500200101.png'];
const FOOD = ['Food/UI_Item_Icon_Food_M_feiyuguantou.png', 'Food/UI_Item_Icon_Food_M_huangtaoguantou.png',
  'Food/UI_Item_Icon_Food_M_ludan.png', 'Food/UI_Item_Icon_Food_M_banana.png'];

// 1) 物品清单：官方 itemsJson 结构（id/icon/x/y/w/h/count/rarity/quality），约七成满
const cols = mod ? 11 : 8, rows = mod ? 10 : 7;
const items = [];
let id = 1;
for (let y = 0; y < rows; y++) {
  for (let x = 0; x < cols; x++) {
    const i = y * cols + x;
    if ((i * 7 + 3) % 10 < 3) continue; // 跳约三成 → 七成满
    const pool = (i % 5 === 0) ? FOOD : MAT;
    items.push({
      id: id++, x: x, y: y, w: 1, h: 1,
      icon: R + pool[i % pool.length],
      count: (i % 6 === 1) ? 2 + (i % 9) : 1,
      rarity: (i % 14 === 7) ? 4 : ((i % 9 === 4) ? 2 : 0),
      quality: (i % 7 === 3) ? 1 : 0
    });
  }
}

// 2) 官方消息驱动：InfoA（背包名/尺寸/负重）+ ItemsA + 本地化
const send = (type, data) => window.postMessage({ type: type, data: data }, '*');
send('WebUI_Backpack_LocalizationMsg', { weightLabel: '负重', sortBackpackText: '一键整理' });
send('WebUI_Backpack_InfoAMsg', {
  ownerId: 1, backpackName: '背包', gridCols: cols, gridRows: rows,
  currentWeight: mod ? 12.0 : 9.6, maxWeight: mod ? 48.0 : 9.6,
  overweightLine: mod ? 43.2 : 8.64,
  isDualBag: false, isPrologue: false
});
send('WebUI_Backpack_ItemsAMsg', { itemsJson: JSON.stringify(items) });

await new Promise(r => setTimeout(r, 700)); // 等 Vue 渲染 + applyViewportFit

// 3) 特写：负重行 + 格子上部（官方 .weight-section / .grid-container 元素）
const wsec = document.querySelector('.weight-section');
const grid = document.querySelector('.grid-container');
let zoomClip = null;
if (wsec && grid) {
  const w = wsec.getBoundingClientRect(), g = grid.getBoundingClientRect();
  zoomClip = [Math.round(g.left - 10), Math.round(w.top - 10),
              Math.round(g.width + 20), Math.round(w.height + g.height * 0.34 + 20)];
}
const overEl = wsec ? wsec.querySelector('.weight-label-right.overload-text') : null;
const fillEl = wsec ? wsec.querySelector('.weight-bar-fill') : null;

return {
  variant: window.__COV_VARIANT, cols: cols, rows: rows, slots: cols * rows,
  nItems: items.length, weightRow: !!wsec, overloadText: !!overEl,
  fillW: fillEl ? fillEl.style.width : null,
  weightText: wsec ? (wsec.querySelector('.weight-label-right') || {}).textContent : null,
  zoomClip: zoomClip
};
