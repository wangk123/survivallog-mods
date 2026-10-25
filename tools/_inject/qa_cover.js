// 动作提速 QuickAction 封面素材：CoreUI1 战内 HUD（底部左侧 .action-panel 动作队列）
// vanilla：当前动作「搜查」进度环 8% + 后排 4 个排队动作；mod：同动作进度环 96% + 排队只剩 1 个
// 全部通过官方 registerHandlers 消息（window.postMessage）驱动 → 官方渲染路径，不手造 DOM
const mod = window.__COV_VARIANT === 'mod';
const send = (type, data) => window.postMessage({ type, data, sourcePageId: 'CoreUI1' }, '*');

// 1) 天数 / 速度档（官方 TimeMsg）
send('WebUI_CoreUI1_TimeMsg', { dayNumber: 'DAY 23', speedLevel: 1 });

// 2) 挂钟 14:30（官方 ClockSyncMsg：游戏内一天 24 格挂钟）
send('WebUI_CoreUI1_ClockSyncMsg', { clockTotalSeconds: 14.5 * 3600, clockMaxSeconds: 86400 });

// 3) 按钮区激活态（官方 BtnActiveMsg）：开速度控件行、关尸潮条
send('WebUI_CoreUI1_BtnActiveMsg', { speedUpActive: true, zombieSiegeActive: false });

// 4) 属性五行（官方 AttrMsg 行结构：attrName/attrIcon/barValue/barColor/value/buffsJson）
send('WebUI_CoreUI1_AttrMsg', { attrList: [
  { attrName: '饱食', attrIcon: '🍚', value: 0.72, barColor: 'green',  barValue: '72%', tipName: '饱食', tipValue: '72%', tipDesc: '', buffsJson: '[]' },
  { attrName: '士气', attrIcon: '😊', value: 0.65, barColor: 'yellow', barValue: '65%', tipName: '士气', tipValue: '65%', tipDesc: '', buffsJson: '[]' },
  { attrName: '体力', attrIcon: '💪', value: 0.58, barColor: 'yellow', barValue: '58%', tipName: '体力', tipValue: '58%', tipDesc: '', buffsJson: '[]' },
  { attrName: '健康', attrIcon: '❤',  value: 0.80, barColor: 'green',  barValue: '80%', tipName: '健康', tipValue: '80%', tipDesc: '', buffsJson: '[]' },
  { attrName: '活力', attrIcon: '✨', value: 0.44, barColor: 'orange', barValue: '44%', tipName: '活力', tipValue: '44%', tipDesc: '', buffsJson: '[]' }
] });

// 5) 推荐动作 chips（官方 RecommendMsg chipsJson；IconPath 留空 = 纯文字 chip）
send('WebUI_CoreUI1_RecommendMsg', { chipsJson: JSON.stringify([
  { ActionName: '吃饭', FurnitureName: '', IconPath: '', RecommendType: 1, ItemInstanceId: 0, FurnitureFuncId: 0, FurnitureLogicId: 0, IsHighlight: false },
  { ActionName: '看书', FurnitureName: '', IconPath: '', RecommendType: 1, ItemInstanceId: 1, FurnitureFuncId: 0, FurnitureLogicId: 0, IsHighlight: false }
]) });

// 6) 当前动作 + 队列（官方 ActionMsg）：进度环 strokeDashoffset = 206*(1-progressPercent)
//    vanilla 8%（搜个柜才刚开始）vs mod 96%（同样动作快完事）；动作名取 Config_Action 真实名
const pend = mod ? ['翻找书堆'] : ['翻箱倒柜', '翻找书堆', '整理杂志', '上厕所'];
send('WebUI_CoreUI1_ActionMsg', {
  isActive: true, icon: '', name: '搜查', showIcon: false,
  isCancellable: true, isAiDriven: false, showProgress: true,
  parentId: 4101, countText: '',
  progressPercent: mod ? 0.96 : 0.08,
  queueJson: JSON.stringify(pend.map((nm, i) => ({
    icon: '', name: nm, showIcon: false, isCancellable: true, isAiDriven: false, parentId: 4200 + i
  })))
});

// 离线渲染没有 3D 场景：给透明 body 铺一层夜色衬底（HUD 元素本身全是官方结构）
document.body.style.background = 'radial-gradient(1200px 700px at 46% 30%, #23262c 0%, #16181d 55%, #0b0d10 100%)';

// 特写：底部左侧动作面板（当前动作进度环 + 队列 + 速度行），带上衬底余量避免 3 倍放大发虚
let zoomClip = null;
const panel = document.querySelector('.action-panel');
if (panel) {
  const r = panel.getBoundingClientRect();
  const x = Math.max(6, Math.round(r.left - 24));
  const y = Math.max(6, Math.round(r.top - 56));
  zoomClip = [x, y, Math.round(Math.min(r.width + 24 + 300, 1536 - x - 8)), Math.round(Math.min(864 - y - 6, r.height + 62))];
}

return {
  variant: window.__COV_VARIANT,
  ring: mod ? '96%' : '8%',
  nQueue: pend.length,
  nChips: 2, nAttrs: 5,
  speedRow: true,
  zoomClip
};
