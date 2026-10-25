// 无尽守卫 EndlessGuard 封面素材：ExploreMap 探索地图页（官方 Map2.webp 底图 + 官方点标记类）
// vanilla：残破超市/废弃学校 已沦陷（红标划名）+ 废弃医院「3 天后沦陷」倒计时，选中沦陷点 → 右栏「已沦陷」钢印+断供提示
// mod：同样的点全部正常态，选中残破超市可进入（供给永续）
// 驱动：官方 registerHandlers 消息（window.postMessage）→ 官方渲染路径；点名为游戏实证中文名
//   （1100 城东幸存者营地/1101 废墟回收站=FallRestorer.cs；1002 废弃学校/1004 残破超市/1005 废弃医院=EndlessGuard DESIGN）
const mod = window.__COV_VARIANT === 'mod';
const send = (type, data) => window.postMessage({ type, data, sourcePageId: 'ExploreMap' }, '*');

// 本地化词条（页面自带沦陷系中文兜底，这里只补左侧抬头/统计行/按钮的官方键；中文为等义译文）
send('WebUI_ExploreMap_LocalizationMsg', {
  areaLabel: '野外探索',
  timeDuringLabel: '探索时长',
  requireLabel: '进入条件',
  goText: '出发'
});

// 地图点列表（官方 mapList 行结构：configId/name/lockState[0开1锁2即将3沦陷]/isActive/fallInDays/damagedDays…）
// 位置由页面 markerPositionsByConfigId 官方坐标表决定
const base = [
  { configId: 1001, name: '加油站' },
  { configId: 1002, name: '废弃学校' },
  { configId: 1004, name: '残破超市' },
  { configId: 1005, name: '废弃医院' },
  { configId: 1100, name: '城东幸存者营地' },
  { configId: 1101, name: '废墟回收站' }
];
const mapList = base.map((p, i) => ({
  configId: p.configId, name: p.name,
  lockState: 0, isActive: false, fallInDays: 0, damagedDays: 0,
  pulse: 0, isNew: false, hasNew: false, campBond: 0, aidUrgency: 0,
  defendTag: '', relieved: false, fallEvac: false
}));
if (!mod) {
  // vanilla：两探险图已沦陷 + 废弃医院缓刑 3 天；选中已沦陷的残破超市
  mapList[2].lockState = 3;                 // 残破超市 沦陷
  mapList[2].isActive = true;
  mapList[1].lockState = 3;                 // 废弃学校 沦陷
  mapList[3].fallInDays = 3;                // 废弃医院 3 天后沦陷
} else {
  // mod：永不沦陷、倒计时清场；选中残破超市（正常可进入，手电光斑挂在点旁）
  mapList[2].isActive = true;
}
send('WebUI_ExploreMap_MapListMsg', { mapList });

// 右栏选中点信息（官方 MapInfoMsg；mapIcon = Config_MapPoint.WebIcon 原值）
send('WebUI_ExploreMap_MapInfoMsg', mod ? {
  mapName: '残破超市',
  mapIcon: '../../Res/MapPoint/SuperMarketAfter.png',
  timeDuring: '约 60 分钟',
  openRequire: '无',
  description: '货架被搜刮过好几轮，仍能翻出些罐头和日用品。',
  lockState: 0, isTradeMode: false, fallInDays: 0, fallDay: 0
} : {
  mapName: '残破超市',
  mapIcon: '../../Res/MapPoint/SuperMarketAfter.png',
  timeDuring: '约 60 分钟',
  openRequire: '无',
  description: '',
  lockState: 3, isTradeMode: false, fallInDays: 0, fallDay: 12
});

// 特写：mod 侧地图中部（残破超市+废弃医院一带的正常点+名称+手电光斑）；vanilla 同区域为沦陷红标
return new Promise(resolve => setTimeout(() => {
  let zoomClip = null;
  const mi = document.querySelector('.map-inner');
  if (mi) {
    const r = mi.getBoundingClientRect();
    // 地图 42%~86% 高、26%~78% 宽一带（覆盖 1004/1005/1100/1102 位）
    zoomClip = [Math.round(r.left + r.width * 0.24), Math.round(r.top + r.height * 0.38),
                Math.round(r.width * 0.56), Math.round(r.height * 0.52)];
  }
  resolve({
    variant: window.__COV_VARIANT,
    points: mapList.length,
    fallen: !mod ? 2 : 0,
    countdown: !mod ? '3天' : '无',
    zoomClip
  });
}, 160));
