// 机器人提速 RobotSpeed 封面素材：RobotPanel 机器人详情面板（HK-1 整理机，冷青荧光屏）
// vanilla：移速 2.4 / 充电 1.0 趟时 / 剩余 4/6 趟 / 货舱 4×2；mod：全 ×2（4.8 / 2.0 / 9/12 趟 / 8×2）
// 数值口径 = SurvivalLog.RobotSpeed/DESIGN.md §4 默认配置（×2）+ §6 实证值（MoveSpeed 2.4→4.8）
// 驱动方式：直接写页面自曝的 window.i18nText（官方 i18n reactive 表）与 window.app.s（官方 state）→ 官方渲染路径
const mod = window.__COV_VARIANT === 'mod';

// ── 官方 i18n 词条（离线无 Unity Localization 下发；格式串与页面模板一致，中文为按官方英文样例的等义译文）──
Object.assign(window.i18nText, {
  pageTitle: 'HK-1',
  batteryLabel: '电量',
  tripsOf: '剩余 {0}/{1} 趟',
  tripsLeft: '还能跑 {0} 趟',
  chargeRemainHM: '充满还需 {0}时{1}分',
  chargeRemainM: '充满还需 {0} 分',
  specCharge: '充电 {0} 趟/时',
  specSpeed: '移速 {0} m/s',
  masterSwitch: '总开关',
  groundSwitch: '整理地面',
  mealSwitch: '收餐',
  cargoTitle: '货舱',
  cargoReadonly: '只读',
  charging: '充电中',
  noPower: '充电座停电',
  historyTitle: '整理记录',
  historyEmpty: '暂无记录',
  dayLabel: '第 {0} 天',
  modTitle: '模块'
});

const s = window.app && window.app.s;
let okState = false;
if (s) {
  okState = true;
  s.robotId = 1;
  s.robotName = 'HK-1';
  s.robotKind = 1;              // KIND_SORT 整理机
  s.masterOn = true;
  s.groundOn = true;
  s.mealCollectOn = true;
  s.mealCollectAvailable = true;
  s.batteryPercent = mod ? 92 : 64;
  s.tripsLeft = mod ? 9 : 4;
  s.tripsMax = mod ? 12 : 6;    // 续航倍率 ×2
  s.chargeRate = mod ? 2.0 : 1.0;   // 充电速率倍率 ×2
  s.moveSpeed = mod ? 4.8 : 2.4;    // 移速倍率 ×2（DESIGN §6 实证 2.4→4.8）
  s.isCharging = mod;               // mod 侧在充电 → 顺带展示「充满还需」读数
  s.chargeRemainHours = mod ? 0.5 : -1;
  s.dockPowerOff = false;
  // 货舱：4×2 → 8×2（货舱格数倍率 ×2）
  s.cargoCols = mod ? 8 : 4;
  s.cargoRows = 2;
  s.cargoTotal = mod ? 16 : 8;
  s.cargoUsed = mod ? 5 : 3;
  const R = '/Res/Material/';
  s.cargo = (mod ? [
    [R + 'UI_Item_Icon_Mat_M_muban.png', 12], [R + 'UI_Item_Icon_Mat_M_mutiao.png', 8],
    [R + 'UI_Item_Icon_Mat_M_mupian.png', 5], [R + 'UI_Item_Icon_Mat_M_bangdai.png', 4],
    [R + 'UI_Item_Icon_Mat_M_jiaoheban.png', 6]
  ] : [
    [R + 'UI_Item_Icon_Mat_M_muban.png', 12], [R + 'UI_Item_Icon_Mat_M_mutiao.png', 8],
    [R + 'UI_Item_Icon_Mat_M_mupian.png', 5]
  ]).map((it, i) => ({ x: i % (mod ? 8 : 4), y: Math.floor(i / (mod ? 8 : 4)), w: 1, h: 1, icon: it[0], count: it[1] }));
  // 整理记录（官方 hist-entry 结构）
  s.history = [
    { day: 22, text: '整理地面散落材料 ×18', highlight: false },
    { day: 22, text: '把工具间的木料归位 ×9', highlight: false }
  ];
  // 模块槽位条：原始槽 2 格（1 装电气元件 Lv1 + 1 空），官方 slot 行结构
  s.moduleSlots = [
    { slot: 0, state: 1, icon: '/Res/Material/UI_Item_Icon_Mat_M_dianqiyuanjian.png', level: 1, rarity: 1, group: 0 },
    { slot: 1, state: 0, group: 0 }
  ];
}

// 特写：屏幕读数区（状态行 + 电量 + 趟数 + 规格行 + 货舱）；等 Vue 重渲染一拍再量（Promise 链兼容 node --check）
return new Promise(resolve => setTimeout(() => {
  let zoomClip = null;
  const scr = document.querySelector('.scr');
  const cargo = document.querySelector('.cargo');
  if (scr && cargo) {
    const a = scr.getBoundingClientRect(), b = cargo.getBoundingClientRect();
    zoomClip = [Math.round(a.left - 12), Math.round(a.top - 10),
                Math.round(a.width + 24), Math.round(b.bottom - a.top + 44)];
  }
  resolve({
    variant: window.__COV_VARIANT, okState,
    speed: mod ? '4.8' : '2.4',
    charge: mod ? '2.0' : '1.0',
    trips: mod ? '9/12' : '4/6',
    cargo: mod ? '8x2' : '4x2',
    zoomClip
  });
}, 160));
