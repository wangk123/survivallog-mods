// 冰箱保鲜 FridgeChill 封面素材（整窗图）：BackpackUI 双栏（背包 | 双门冰箱），官方消息驱动
// 两侧冰箱同为原生 8x7（FridgeChill 不改格子）；差异点：
//   vanilla：bagTips「保鲜率 ×5」+ 食物有快过期（near-expiry-mask 官方角标）/已过期（expired-mask）
//   mod：bagTips「保鲜率 ×10」+ 食物全部新鲜
// 官方机制：isFreezing+isPowered → .grid-container.is-frozen 冰蓝格子 + .item-frost 霜罩（Refrigerator_ICE.png）
const mod = window.__COV_VARIANT === 'mod';
const R = '/Res/';
const FOOD = ['Food/UI_Item_Icon_Food_M_feiyuguantou.png', 'Food/UI_Item_Icon_Food_M_huangtaoguantou.png',
  'Food/UI_Item_Icon_Food_M_wucanrouguantou2.png', 'Food/UI_Item_Icon_Food_M_lengdongqingdou.png',
  'Food/UI_Item_Icon_Food_M_lengdongxilanhua.png', 'Food/UI_Item_Icon_Food_M_ludan.png',
  'Food/UI_Item_Icon_Food_M_banana.png', 'Food/UI_Item_Icon_Food_M_90bingan.png',
  'Food/UI_Item_Icon_Food_M_sudabinggan.png', 'Food/UI_Item_Icon_Food_M_xinbaogu.png'];
const MAT = ['Material/UI_Item_Icon_Mat_M_muban.png', 'Material/UI_Item_Icon_Mat_M_mutiao.png',
  'Material/UI_Item_Icon_Mat_M_mupian.png', 'Material/UI_Item_Icon_Mat_M_bangdai.png',
  'Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png', 'Material/UI_Item_Icon_Mat_M_1500110101.png'];

// A：背包 8x7，一半食物一半材料
const bagItems = [];
let bid = 1;
for (let y = 0; y < 7; y++) {
  for (let x = 0; x < 8; x++) {
    const i = y * 8 + x;
    if ((i * 11 + 3) % 10 < 5) continue;
    const pool = (i % 2 === 0) ? FOOD : MAT;
    bagItems.push({ id: bid++, x: x, y: y, w: 1, h: 1, icon: R + pool[i % pool.length],
      count: (i % 5 === 0) ? 2 + (i % 6) : 1, rarity: 0, quality: 0 });
  }
}

// B：双门冰箱 8x7；vanilla 14 件（含 3 件快过期 + 1 件已过期）；mod 22 件全新鲜
const fridgeItems = [];
let fid = 500;
const take = mod ? 22 : 14;
for (let y = 0; y < 7 && fridgeItems.length < take; y++) {
  for (let x = 0; x < 8 && fridgeItems.length < take; x++) {
    const i = y * 8 + x;
    if ((i * 11 + 5) % 10 < 4) continue;
    const n = fridgeItems.length;
    const it = { id: fid++, x: x, y: y, w: 1, h: 1, icon: R + FOOD[i % FOOD.length],
      count: (i % 6 === 0) ? 2 + (i % 5) : 1, rarity: 0, quality: 0 };
    if (!mod) {
      if (n === 2 || n === 7 || n === 11) it.nearExpiry = true;
      if (n === 5) it.isExpired = true;
    }
    fridgeItems.push(it);
  }
}

const send = (type, data) => window.postMessage({ type: type, data: data }, '*');
send('WebUI_Backpack_LocalizationMsg', {
  weightLabel: '负重', sortBackpackText: '一键整理',
  toSelfText: '全部取出', toSelfKeyLabel: 'E', toOtherText: '全部放入'
});
send('WebUI_Backpack_InfoAMsg', {
  ownerId: 1, backpackName: '背包', gridCols: 8, gridRows: 7,
  currentWeight: 4.2, maxWeight: 9.6, overweightLine: 8.64,
  isDualBag: true, isPrologue: false
});
send('WebUI_Backpack_ItemsAMsg', { itemsJson: JSON.stringify(bagItems) });
send('WebUI_Backpack_InfoBMsg', {
  ownerId: 2, backpackName: '双门冰箱', gridCols: 8, gridRows: 7,
  currentWeight: 0, maxWeight: 0,
  isFreezing: true, isPowered: true,
  bagTips: mod ? '保鲜率 ×10' : '保鲜率 ×5', bagTipsColor: '#8fd7ff'
});
send('WebUI_Backpack_ItemsBMsg', { itemsJson: JSON.stringify(fridgeItems) });

await new Promise(r => setTimeout(r, 700));

// 取证：冰箱侧霜化 + bagTips + 角标
const panels = document.querySelectorAll('.bag-panel');
const bPanel = document.querySelector('.bag-panel.is-storage') || panels[panels.length - 1];
const frozen = bPanel ? !!bPanel.querySelector('.grid-container.is-frozen') : false;
const frostCount = bPanel ? bPanel.querySelectorAll('.item-frost').length : 0;
const tips = bPanel ? bPanel.querySelector('.bag-tips') : null;
const nearMask = bPanel ? bPanel.querySelectorAll('.near-expiry-mask').length : 0;
const expMask = bPanel ? bPanel.querySelectorAll('.expired-mask').length : 0;

return {
  variant: window.__COV_VARIANT, bagItems: bagItems.length, fridgeItems: fridgeItems.length,
  frozen: frozen, frostCount: frostCount,
  bagTips: tips ? tips.textContent : null,
  nearExpiryMasks: nearMask, expiredMasks: expMask
};
