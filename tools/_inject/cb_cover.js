// 柜子负重解放 CabinetBurden 封面素材：BackpackUI 双栏（背包 | 储物柜），官方消息驱动
// vanilla：储物柜 8x10、负重 22.8 / 24.0 Kg 红色将满，格子没满（放不下的痛点）；背包也压得满
// mod：负重行整行消失（官方 v-if maxWeight>0 语义 —— maxWeight:0 即不渲染），格子基本空着 = 还能放
// 数值依据：柜子3（储物柜家具 80042 系用包）原生 burden 24000 = 24.0 Kg、尺寸 8x10（Config_Bag dump /1.5 反推）
const mod = window.__COV_VARIANT === 'mod';
const R = '/Res/';
const MAT = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_jiaoheban.png', 'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png',
  'Material/UI_Item_Icon_Mat_M_tanxianweiban.png', 'Material/UI_Item_Icon_Mat_M_tengman.png',
  'Material/UI_Item_Icon_Mat_M_1500110101.png', 'Material/UI_Item_Icon_Mat_M_1500200101.png'];
const FOOD = ['Food/UI_Item_Icon_Food_M_feiyuguantou.png', 'Food/UI_Item_Icon_Food_M_huangtaoguantou.png',
  'Food/UI_Item_Icon_Food_M_wucanrouguantou2.png', 'Food/UI_Item_Icon_Food_M_lengdongqingdou.png'];

function buildItems(cols, rows, skipRule, foodEvery) {
  const out = [];
  let id = arguments[4] || 100;
  for (let y = 0; y < rows; y++) {
    for (let x = 0; x < cols; x++) {
      const i = y * cols + x;
      if (skipRule(i)) continue;
      const pool = (i % foodEvery === 1) ? FOOD : MAT;
      out.push({
        id: id++, x: x, y: y, w: 1, h: 1,
        icon: R + pool[i % pool.length],
        count: (i % 6 === 2) ? 2 + (i % 7) : 1,
        rarity: (i % 13 === 6) ? 2 : 0,
        quality: (i % 8 === 5) ? 1 : 0
      });
    }
  }
  return out;
}

// A：背包 8x7（普通背包原生），约七成满 —— 东西压在包里放不进柜子
const bagItems = buildItems(8, 7, i => (i * 7 + 3) % 10 < 3, 5, 100);
// B：储物柜 8x10；vanilla 约四成（格子没满但负重将满）；mod 只剩 3 件（负重行已消失，随便放）
let cabItems;
if (mod) {
  cabItems = [
    { id: 300, x: 0, y: 0, w: 1, h: 1, icon: R + MAT[0], count: 4, rarity: 0, quality: 0 },
    { id: 301, x: 1, y: 0, w: 1, h: 1, icon: R + MAT[4], count: 1, rarity: 0, quality: 0 },
    { id: 302, x: 3, y: 1, w: 1, h: 1, icon: R + FOOD[0], count: 2, rarity: 0, quality: 0 }
  ];
} else {
  cabItems = buildItems(8, 10, i => (i * 9 + 2) % 10 < 6, 4, 300); // 60% 跳过 → 四成
}

const send = (type, data) => window.postMessage({ type: type, data: data }, '*');
send('WebUI_Backpack_LocalizationMsg', {
  weightLabel: '负重', sortBackpackText: '一键整理',
  toSelfText: '全部取出', toSelfKeyLabel: 'E', toOtherText: '全部放入'
});
send('WebUI_Backpack_InfoAMsg', {
  ownerId: 1, backpackName: '背包', gridCols: 8, gridRows: 7,
  currentWeight: 6.4, maxWeight: 9.6, overweightLine: 8.64,
  isDualBag: true, isPrologue: false
});
send('WebUI_Backpack_ItemsAMsg', { itemsJson: JSON.stringify(bagItems) });
send('WebUI_Backpack_InfoBMsg', {
  ownerId: 2, backpackName: '储物柜', gridCols: 8, gridRows: 10,
  currentWeight: mod ? 0 : 22.8, maxWeight: mod ? 0 : 24.0,
  overweightLine: mod ? 0 : 21.6
});
send('WebUI_Backpack_ItemsBMsg', { itemsJson: JSON.stringify(cabItems) });

await new Promise(r => setTimeout(r, 700));

// 特写（mod 侧）：储物柜标题 + 无负重行 + 格子上部 —— 与 vanilla 同区域对比靠左右两窗完成
const panels = document.querySelectorAll('.bag-panel');
const bPanel = document.querySelector('.bag-panel.is-storage') || panels[panels.length - 1];
const bTitle = bPanel ? bPanel.querySelector('.panel-title') : null;
const bGrid = bPanel ? bPanel.querySelector('.grid-container') : null;
// B 侧（vanilla）负重行状态取证
const wrows = document.querySelectorAll('.weight-section');
let bWeightText = null, bOverload = false;
wrows.forEach(w => {
  const r = w.getBoundingClientRect();
  if (bGrid && r.top > bGrid.getBoundingClientRect().top - 260 && r.left > (bGrid.getBoundingClientRect().left - 40)) {
    const t = w.querySelector('.weight-label-right');
    if (t && t.textContent.indexOf('24.0') >= 0) {
      bWeightText = t.textContent;
      bOverload = !!w.querySelector('.weight-label-right.overload-text') ||
                  !!w.querySelector('.weight-bar-fill.overload');
    }
  }
});
let zoomClip = null;
if (bTitle && bGrid) {
  const t = bTitle.getBoundingClientRect(), g = bGrid.getBoundingClientRect();
  zoomClip = [Math.round(g.left - 10), Math.round(t.top - 8),
              Math.round(g.width + 20), Math.round(t.height + 26 + g.height * 0.3 + 16)];
}

return {
  variant: window.__COV_VARIANT,
  bagItems: bagItems.length, cabItems: cabItems.length,
  weightRows: wrows.length, bWeightText: bWeightText, bOverload: bOverload,
  bTitle: bTitle ? bTitle.textContent : null, zoomClip: zoomClip
};
