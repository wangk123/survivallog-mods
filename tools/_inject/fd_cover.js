// 冰箱保鲜 FridgeChill 特写素材：FurnitureDetail 家具详情页，官方 UpdateMsg/LocalizationMsg 消息驱动
// 双门冰箱（家具 215 实证：$40 / 槽位中 / bag 1004 原生 8x7 / 耗电 2W / ColdRate 0.2→0.1）
// vanilla：保鲜率 ×5；mod：保鲜率 ×10。负重上限行 v-if weightMax>0 —— 冰箱原生 0，整行不渲染（官方语义）
const mod = window.__COV_VARIANT === 'mod';
const send = (type, data) => window.postMessage({ type: type, data: data }, '*');

send('WebUI_FurnitureDetail_LocalizationMsg', {
  slotLabel: '家具槽位', installTimeLabel: '安装时间',
  installPreLabel: '灾前', installPostLabel: '灾后',
  capacityLabel: '储物容量', weightMaxLabel: '负重上限',
  powerConsumeLabel: '耗电功率', frostCoeffLabel: '保鲜率',
  buyBtnText: '确认购买', typeLabelFrozen: '冷冻储物'
});
send('WebUI_FurnitureDetail_UpdateMsg', {
  configId: 215, furnitureType: 'frozen', name: '双门冰箱',
  icon: '/Res/Furniture/UI_Item_Icon_bingxiang.png', price: 40,
  slotName: '中', installTimePre: '1小时', installTimePost: '1小时',
  capacityW: 8, capacityH: 7, weightMax: 0, powerConsume: 2,
  frostCoeff: mod ? '×10' : '×5',
  description: '带双开门的家用冰箱，可整理食材、取用，也能做冰块。',
  canBuy: true, soldOut: false, demoTwoBlocked: false
});

await new Promise(r => setTimeout(r, 600));

// 特写：属性区（.perf-specs 官方类）整块 clip —— 含 储物容量 / 耗电功率 / 保鲜率 行
const modal = document.querySelector('.modal-box');
const specs = document.querySelector('.perf-specs');
let zoomClip = null;
if (specs) {
  const s = specs.getBoundingClientRect();
  const m = modal ? modal.getBoundingClientRect() : null;
  const x = m ? Math.max(8, Math.min(s.left, m.left) - 6) : Math.max(8, s.left - 10);
  zoomClip = [Math.round(x), Math.round(s.top - 8),
              Math.round((m ? Math.max(s.right, m.right - 6) : s.right + 10) - x),
              Math.round(s.height + 16)];
}
const rows = document.querySelectorAll('.perf-row');
let frostRow = null;
rows.forEach(r => {
  const l = r.querySelector('.perf-label');
  const v = r.querySelector('.perf-value');
  if (l && l.textContent.indexOf('保鲜率') >= 0) frostRow = (v || {}).textContent;
});

return {
  variant: window.__COV_VARIANT, ready: !!modal && modal.offsetParent !== null,
  frostCoeff: frostRow, perfRows: rows.length,
  weightRow: !!document.querySelector('.perf-row .perf-label') &&
             Array.from(document.querySelectorAll('.perf-label')).some(l => l.textContent.indexOf('负重') >= 0),
  zoomClip: zoomClip
};
