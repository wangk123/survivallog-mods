// 抖音图文配图构建器 · v1.7.0 大版本（2026-10-25）
// 用法：node tools/build_douyin_v170.js [序号...]   不带参数=全部 10 张
// 产物：assets/douyin/v170/<nn>-<slug>.html + .jpg（1080x1440，抖音图文 3:4）
// 风格：沿用 N网封面 v2 定稿——暗底 + 金白渐变标题 + 夜字印 B-2；实机截图走 gamekb/webui_shots
// 版式：内容页统一「装前痛点(上/冷) → 装上解决(下/暖)」，主要体现新版解决的问题
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const OUT = path.join(__dirname, '..', 'assets', 'douyin', 'v170');
const SHOTS = '../../../gamekb/webui_shots/';

const GRAIN = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='2' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter><rect width='180' height='180' filter='url(%23n)'/></svg>";

function stampSvg(size) {
  return `<div class="stamp" style="width:${size}px;height:${size}px">
    <svg width="${size}" height="${size}" viewBox="0 0 264 264">
      <rect x="8" y="8" width="248" height="248" rx="30" fill="#1b1610"/>
      <rect x="24" y="24" width="216" height="216" rx="20" fill="none" stroke="#FFC107" stroke-width="5"/>
      <text x="132" y="176" text-anchor="middle" font-family="KaiTi,STKaiti,serif" font-weight="900"
            font-size="150" fill="#FFC107">夜</text>
      <text x="132" y="222" text-anchor="middle" font-family="Georgia,serif" font-size="21"
            letter-spacing="6" fill="#FFC107" opacity=".85">NIGHT</text>
    </svg></div>`;
}

const BASE_CSS = `
  * { margin:0; padding:0; box-sizing:border-box; }
  body { background:#000; font-family:"Microsoft YaHei","SimHei",sans-serif; }
  .board { position:relative; overflow:hidden; width:1080px; height:1440px; background:#08080b; }
  .bg { position:absolute; inset:0; }
  .grain { position:absolute; inset:0; opacity:.05; background-image:url("${GRAIN}"); background-size:180px; }
  .stamp { position:absolute; transform:rotate(-7deg); z-index:30;
    filter: drop-shadow(0 8px 18px rgba(0,0,0,.7)) drop-shadow(0 0 12px rgba(255,193,7,.3)); }
  .gold-t { background:linear-gradient(180deg,#fdf6e3 8%,#f2ddb0 55%,#dcc084 100%);
    -webkit-background-clip:text; background-clip:text; color:transparent; }
`;

// ---------- 01 封面 ----------
function tplCover() {
  const chip = (name, micro) => `<div class="chip"><div class="cn">${name}</div><div class="mc">${micro}</div></div>`;
  const chip2 = (a, b) => `<div class="chip2"><span class="a">${a}</span><span class="ar">→</span><span class="b">${b}</span></div>`;
  const win = (img, rot, w) => `<div class="mw" style="transform:rotate(${rot}deg);width:${w}px"><div class="bar"><i></i><i></i><i></i></div><img src="${SHOTS}${img}"></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音图文 01 封面 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:
    radial-gradient(900px 640px at 50% 24%, #2b2113 0%, #17120b 52%, #0a0a0d 100%); }
  .kick { position:absolute; top:88px; width:100%; text-align:center; color:#9aa0a8; font-size:27px; letter-spacing:16px; font-weight:700; }
  .ver { position:absolute; top:158px; width:100%; text-align:center; font-family:Impact,"Arial Black",sans-serif;
    font-size:224px; letter-spacing:6px; line-height:1; text-shadow:0 6px 30px rgba(0,0,0,.5); }
  .sub { position:absolute; top:452px; width:100%; text-align:center; color:#f4f1e8; font-size:40px; font-weight:900; letter-spacing:4px; }
  .sub b { color:#FFD75E; }
  .tagline { position:absolute; top:528px; width:100%; text-align:center; color:#cdb87e; font-size:27px; letter-spacing:10px; }
  .hair { position:absolute; top:606px; left:190px; right:190px; height:1px;
    background:linear-gradient(90deg,transparent,#8f7c4e,transparent); }
  .g1lab { position:absolute; top:638px; width:100%; text-align:center; color:#FFD75E; font-size:24px; letter-spacing:8px; font-weight:800; }
  .chips { position:absolute; top:682px; left:52px; right:52px; display:flex; gap:14px; }
  .chip { flex:1; border-radius:16px; padding:17px 6px 15px; text-align:center;
    background:rgba(16,14,10,.82); border:1px solid rgba(255,193,7,.38); box-shadow:0 12px 30px rgba(0,0,0,.5); }
  .chip .cn { color:#FFD75E; font-size:26px; font-weight:900; letter-spacing:1px; white-space:nowrap; }
  .chip .mc { margin-top:9px; color:#a89f8b; font-size:18px; letter-spacing:1px; white-space:nowrap; }
  .g2lab { position:absolute; top:836px; width:100%; text-align:center; color:#b8ab84; font-size:24px; letter-spacing:8px; font-weight:800; }
  .chips2 { position:absolute; top:880px; left:52px; right:52px; display:flex; gap:14px; }
  .chip2 { flex:1; border-radius:14px; padding:16px 6px; text-align:center;
    background:rgba(255,255,255,.035); border:1px solid rgba(255,255,255,.13); }
  .chip2 .a { color:#98a2ad; font-size:21px; font-weight:700; }
  .chip2 .ar { margin:0 9px; color:#8f7c4e; font-size:20px; }
  .chip2 .b { color:#FFD75E; font-size:22px; font-weight:900; }
  .wins { position:absolute; top:962px; left:44px; right:44px; display:flex; justify-content:space-between; }
  .mw { border-radius:12px; overflow:hidden; background:#101012; border:1.5px solid rgba(255,193,7,.5);
    box-shadow:0 22px 50px rgba(0,0,0,.75), 0 0 36px rgba(255,193,7,.10); }
  .mw .bar { height:22px; background:rgba(22,22,26,.96); display:flex; align-items:center; gap:6px; padding:0 10px;
    border-bottom:1px solid rgba(255,255,255,.07); }
  .mw .bar i { width:6px; height:6px; border-radius:50%; background:#41414a; }
  .mw .bar i:first-child { background:rgba(255,193,7,.8); }
  .mw img { display:block; width:100%; }
  .hook { position:absolute; top:1236px; left:70px; right:70px; text-align:center; color:#e8dfc8;
    font-size:25px; letter-spacing:2px; line-height:1.6; }
  .hook .bad { color:#8f99a4; text-decoration:line-through; text-decoration-color:rgba(179,52,42,.75); }
  .hook .ok { color:#FFD75E; font-weight:900; }
  .foot { position:absolute; bottom:34px; width:100%; text-align:center; color:#7d8792; font-size:19px; letter-spacing:3px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="kick">生存日志 MOD 合集 · 粉丝专享</div>
  <div class="ver gold-t">v1.7.0</div>
  <div class="sub"><b>7 个</b> MOD → <b>12 个</b> · 大版本更新</div>
  <div class="tagline">这 一 版 · 专 治 老 大 难</div>
  <div class="hair"></div>
  <div class="g1lab">🆕 五 个 新 成 员</div>
  <div class="chips">
    ${chip('无尽守卫', '沦陷全拦·旧档复活')}
    ${chip('自选规划', '465 项自选')}
    ${chip('灾前加速', '五键倍速')}
    ${chip('MOD 管理中心', '面板一键管理')}
    ${chip('角色专属解锁', '专属全角色互通')}
  </div>
  <div class="g2lab">🔧 三 项 大 优 化</div>
  <div class="chips2">
    ${chip2('负重 ×1000', '无限制')}
    ${chip2('柜子容量倍率', '数值直调')}
    ${chip2('背包倍率', '数值直调')}
  </div>
  <div class="wins">
    ${win('eg_mod_full.png', -1.6, 316)}
    ${win('pp_real_panel.png', 0, 328)}
    ${win('pds_mod_full.png', 1.6, 316)}
  </div>
  <div class="hook"><span class="bad">断供 · 看脸 · 干等 · 手改cfg · 锁角色</span><br>这些老大难，<span class="ok">这版一次全解决</span></div>
  <div class="foot">粉丝群置顶已更新 · 生存日志MOD安装器_v1.7.0.exe · by Night</div>
  ${stampSvg(112).replace('class="stamp"', 'class="stamp" style="right:58px;top:64px;width:112px;height:112px"')}
  </div></body></html>`;
}

// ---------- 02~08 内容页（问题 → 解决） ----------
const CONTENT = [
  {
    n: '02', slug: 'endless', tag: '🆕 新 MOD', tagCls: 'gold',
    name: '无尽守卫', en: 'ENDLESS GUARD · 沦陷断供 → 供给永续',
    bt: '无尽后期<br>交易点逐个沦陷', bd: '想买的直接断供，探险图一个个废掉，野外资源越采越空',
    st: '沦陷全拦<br>旧点还能救回来', sd: '读档即可复活已沦陷的点；再加 11 条后期拾遗规划，进图全柜保底补新货',
    vimg: 'eg_vanilla_full.png', mimg: 'eg_mod_full.png',
    cfg: '⚙ 四大功能 各自开关',
  },
  {
    n: '03', slug: 'planpick', tag: '🆕 新 MOD', tagCls: 'gold',
    name: '自选规划', en: 'PLAN PICK · 随机三选 → 全目录自选',
    bt: '每日规划三选一<br>想要啥全看脸', bd: '惦记的规划老不出现在候选里，错过一次又等一年',
    st: '465+ 项<br>全目录自选购买', sd: '含从不进抽取池的隐藏项；分类/搜索/排序，买完立即生效',
    vimg: 'rogue_vanilla_cards.png', mimg: 'pp_real_panel.png',
    cfg: '⚙ 非今日加价 100%~300% 可调',
  },
  {
    n: '04', slug: 'pds', tag: '🆕 新 MOD', tagCls: 'gold',
    name: '灾前加速', en: 'PRE-DISASTER SPEED · 灾前干等 → 五键倍速',
    bt: '灾前只有 1 倍速<br>只能干等', bd: '官方只在灾后给倍速条，灾前只能眼睁睁看着时间走',
    st: '官方五键倍速条<br>搬到灾前', sd: '暂停/慢速/原速/二倍/三倍，快捷键 ~ 1 2 3 4；跳夜不受影响',
    vimg: 'pds_vanilla_full.png', mimg: 'pds_mod_full.png',
    cfg: '⚙ 倍速档位 · 快捷键 可开关',
  },
  {
    n: '05', slug: 'modcenter', tag: '🆕 新 MOD', tagCls: 'gold',
    name: 'MOD 管理中心', en: 'MOD CENTER · 手改 cfg → 面板直管',
    bt: '改个参数<br>= 翻文件手改 cfg', bd: '记键名、改配置、重启游戏一套下来，新手直接劝退',
    st: '游戏里面板<br>一键开关 + 滑条', sd: '主菜单 / ESC 呼出，全套件集中管理，改完几秒生效不用重启',
    vimg: 'mc_vanilla_full.png', mimg: 'mc_mod_full.png',
    cfg: '⚙ 全套件集中管理 · 即时生效',
  },
  {
    n: '06', slug: 'roleunlock', tag: '🆕 新 MOD', tagCls: 'gold',
    name: '角色专属解锁', en: 'ROLE UNLOCK · 锁角色 → 全角色互通',
    bt: '藤蔓、燃烧瓶<br>锁死单角色', bd: '专属种子和配方换角色就永远拿不到，二周目只能干瞪眼',
    st: '专属内容<br>全角色互通', sd: '藤蔓任何角色都可能拾到；防御配方并集互通（各自门槛照旧，不是白送）',
    vimg: 'ru_vanilla_full.png', mimg: 'ru_mod_full.png',
    cfg: '⚙ 藤蔓 / 配方互通 独立开关',
  },
  {
    n: '07', slug: 'burden', tag: '🔧 大优化', tagCls: 'amber',
    name: '柜子负重解放', en: 'CABINET BURDEN · ×1000 → 无限制',
    bt: '负重 ×1000<br>还是不够塞', bd: '篮子、货车车厢、无人机货舱照样报满，等于改了个寂寞',
    st: '负重清零<br>＝官方无限制', sd: '0 就是游戏自己的"无上限"：格子没满随便放，负重条自动隐藏',
    vimg: 'cb_vanilla_full.png', mimg: 'cb_mod_full.png',
    cfg: '⚙ 十种容器 · 名单可增删',
  },
  {
    n: '08', slug: 'cabinetsize', tag: '🔧 大优化 · 换代', tagCls: 'amber',
    name: '柜子容量', en: 'CABINET SIZE · 倍率看不懂 → 数值直调',
    bt: '百分比倍率<br>看不懂 · 大柜格子少', bd: '×1.5 到底多大要拿计算器；超大柜反而没几个格子',
    st: '小/中/大/特大<br>四档直接填宽×高', sd: '13 个常用柜独立微调，任意柜子可单独定值；配置热生效',
    vimg: 'cx_vanilla_full.png', mimg: 'cs_mod_full.png',
    cfg: '⚙ 旧「柜子扩容」退役，安装器自动清理；背包扩容同步改数值制',
  },
];

function tplContent(s, idx, total) {
  const namePx = s.name.length >= 6 ? 54 : 64;
  const tagStyle = s.tagCls === 'gold'
    ? 'color:#FFD75E;border-color:rgba(255,193,7,.5);'
    : 'color:#e0b25c;border-color:rgba(224,178,92,.42);';
  const win = (img, gold, rot, vanilla) => `<div class="win${gold ? ' gold' : ''}" style="transform:rotate(${rot}deg)">
    <div class="bar"><i></i><i></i><i></i></div>
    <img class="${vanilla ? 'van' : ''}" src="${SHOTS}${img}"></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音图文 ${s.n} ${s.name} 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:linear-gradient(178deg,#171b20 0%,#0e1013 46%,#100c07 54%,#1a1309 100%); }
  header { position:absolute; top:44px; left:48px; right:48px; }
  .hrow { display:flex; justify-content:space-between; align-items:center; }
  .tag { font-size:22px; font-weight:800; letter-spacing:3px; padding:9px 20px; border-radius:999px;
    border:1px solid; background:rgba(12,10,7,.6); ${tagStyle} }
  .pageno { color:#6d757f; font-size:20px; letter-spacing:4px; font-family:Georgia,serif; }
  .name { margin-top:20px; font-size:${namePx}px; font-weight:900; letter-spacing:7px; line-height:1.1; }
  .en { margin-top:12px; font-family:Georgia,serif; font-size:20px; color:#c9a754; letter-spacing:4px; }
  .card { position:absolute; left:48px; right:48px; height:480px; border-radius:22px;
    display:flex; align-items:center; gap:18px; padding:20px; }
  .before { top:228px; background:linear-gradient(135deg,rgba(30,35,42,.95),rgba(20,23,28,.95));
    border:1px solid rgba(255,255,255,.10); box-shadow:0 24px 60px rgba(0,0,0,.55); }
  .after { top:788px; background:linear-gradient(135deg,rgba(41,30,15,.96),rgba(26,19,10,.96));
    border:1px solid rgba(255,193,7,.45); box-shadow:0 26px 66px rgba(0,0,0,.6), 0 0 46px rgba(255,193,7,.09); }
  .txt { width:288px; flex:none; }
  .pill { display:inline-block; font-size:19px; font-weight:800; letter-spacing:3px; padding:8px 17px;
    border-radius:999px; }
  .pill.g { color:#8f99a4; border:1px solid rgba(143,153,164,.45); background:rgba(255,255,255,.04); }
  .pill.y { color:#FFD75E; border:1px solid rgba(255,193,7,.55); background:rgba(255,193,7,.07); }
  .bt { margin-top:18px; color:#d5dde4; font-size:31px; font-weight:900; line-height:1.42; letter-spacing:1px; }
  .st { margin-top:18px; font-size:31px; font-weight:900; line-height:1.42; letter-spacing:1px;
    color:#FFD75E; text-shadow:0 2px 14px rgba(255,193,7,.22); }
  .bd { margin-top:15px; color:#939ea9; font-size:20px; line-height:1.68; }
  .sd { margin-top:15px; color:#c8b68e; font-size:20px; line-height:1.68; }
  .win { width:640px; flex:none; border-radius:13px; overflow:hidden; background:#101013;
    border:1px solid rgba(255,255,255,.14); box-shadow:0 24px 56px rgba(0,0,0,.6); }
  .win.gold { border:1.5px solid rgba(255,193,7,.75); box-shadow:0 24px 60px rgba(0,0,0,.65), 0 0 36px rgba(255,193,7,.16); }
  .win .bar { height:28px; background:rgba(22,22,26,.96); display:flex; align-items:center; gap:7px; padding:0 12px;
    border-bottom:1px solid rgba(255,255,255,.07); }
  .win .bar i { width:7px; height:7px; border-radius:50%; background:#41414a; }
  .win.gold .bar i:first-child { background:rgba(255,193,7,.85); }
  .win img { display:block; width:100%; }
  .win img.van { filter:grayscale(.82) brightness(.78); }
  .divider { position:absolute; top:712px; left:0; width:100%; text-align:center; }
  .divider span { display:inline-block; color:#FFC107; font-size:22px; font-weight:800; letter-spacing:7px;
    padding:11px 30px; border-radius:999px; border:1px solid rgba(255,193,7,.4);
    background:rgba(10,8,5,.72); box-shadow:0 10px 26px rgba(0,0,0,.5); }
  .cfgline { position:absolute; bottom:64px; left:48px; right:48px; text-align:center;
    color:#cdb87e; font-size:20px; letter-spacing:2px; }
  .brand { position:absolute; bottom:30px; width:100%; text-align:center; color:#6d757f;
    font-size:17px; letter-spacing:3px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <header>
    <div class="hrow"><span class="tag">${s.tag}</span><span class="pageno">${s.n} / 0${total}</span></div>
    <div class="name gold-t">${s.name}</div>
    <div class="en">${s.en}</div>
  </header>
  <section class="card before">
    <div class="txt"><div class="pill g">装前 · 痛点</div><div class="bt">${s.bt}</div><div class="bd">${s.bd}</div></div>
    ${win(s.vimg, false, -0.9, true)}
  </section>
  <div class="divider"><span>▼ 装上 MOD</span></div>
  <section class="card after">
    ${win(s.mimg, true, 0.9, false)}
    <div class="txt"><div class="pill y">装上后 · 解决</div><div class="st">${s.st}</div><div class="sd">${s.sd}</div></div>
  </section>
  <div class="cfgline">${s.cfg}</div>
  <div class="brand">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(58).replace('class="stamp"', 'class="stamp" style="right:26px;bottom:22px;width:58px;height:58px"')}
  </div></body></html>`;
}

// ---------- 09 全家福 ----------
const FAMILY = [
  ['无尽守卫', '沦陷全拦 · 旧档复活', 'new'],
  ['自选规划', '465 项自选购买', 'new'],
  ['灾前加速', '灾前也有五键倍速', 'new'],
  ['MOD 管理中心', '面板一键管理', 'new'],
  ['角色专属解锁', '专属全角色互通', 'new'],
  ['柜子容量', '四档容量 定宽定高', 'mod'],
  ['柜子负重解放', '负重清零 无限制', 'mod'],
  ['背包扩容', '宽·高·负重 直接填', 'mod'],
  ['冰箱保鲜', '保鲜×10 封顶可调', ''],
  ['储藏柜直达', '工作台直连家里柜', ''],
  ['动作提速', '全动作 0.5 秒', ''],
  ['机器人提速', '机器人全面 ×2', ''],
];

function tplFamily() {
  const card = ([name, desc, badge]) => `<div class="fc">
    ${badge === 'new' ? '<span class="bdg n">🆕</span>' : badge === 'mod' ? '<span class="bdg m">🔧</span>' : ''}
    <div class="fn ${badge ? 'hot' : ''}">${name}</div><div class="fd">${desc}</div></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音图文 09 全家福 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:radial-gradient(1000px 760px at 50% 18%, #2b2113 0%, #17120b 55%, #0a0a0d 100%); }
  header { position:absolute; top:56px; width:100%; text-align:center; }
  .t1 { font-size:60px; font-weight:900; letter-spacing:8px; }
  .t2 { margin-top:14px; color:#b0a078; font-size:23px; letter-spacing:6px; }
  .grid { position:absolute; top:216px; left:48px; right:48px; display:flex; flex-wrap:wrap; gap:14px; }
  .fc { position:relative; width:318px; height:158px; border-radius:16px; padding:26px 20px 0;
    background:rgba(255,255,255,.035); border:1px solid rgba(255,255,255,.12); }
  .fc:nth-child(-n+5) { background:rgba(255,193,7,.06); border-color:rgba(255,193,7,.4); }
  .fn { color:#e9e4d8; font-size:25px; font-weight:900; letter-spacing:1px; }
  .fn.hot { color:#FFD75E; }
  .fd { margin-top:11px; color:#8f99a4; font-size:19px; letter-spacing:1px; }
  .bdg { position:absolute; top:14px; right:14px; font-size:20px; }
  .upg { position:absolute; top:934px; left:48px; right:48px; border-radius:20px; padding:26px 34px;
    background:rgba(24,18,9,.9); border:1px solid rgba(255,193,7,.42);
    box-shadow:0 22px 56px rgba(0,0,0,.55); }
  .upg .ut { color:#FFD75E; font-size:27px; font-weight:900; letter-spacing:4px; }
  .upg li { margin-top:15px; list-style:none; color:#d8cdb2; font-size:21px; line-height:1.55; letter-spacing:1px; }
  .upg li b { color:#fff; }
  .upg .no { display:inline-block; width:30px; height:30px; line-height:30px; text-align:center; margin-right:12px;
    border-radius:50%; background:rgba(255,193,7,.16); border:1px solid rgba(255,193,7,.5);
    color:#FFD75E; font-size:17px; font-weight:900; }
  .foot { position:absolute; bottom:34px; width:100%; text-align:center; color:#7d8792; font-size:19px; letter-spacing:3px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <header><div class="t1 gold-t">12 个 MOD · 全家福</div><div class="t2">5 新 · 3 大优化 · 全部可自选安装</div></header>
  <div class="grid">${FAMILY.map(card).join('\n')}</div>
  <div class="upg">
    <div class="ut">⚠️ 老用户升级必读</div>
    <li><span class="no">1</span>重跑「生存日志MOD安装器_v1.7.0.exe」即<b>覆盖升级</b>，旧档照常用</li>
    <li><span class="no">2</span>旧「柜子扩容」已退役，安装器<b>自动清理</b>，不用手动删</li>
    <li><span class="no">3</span>每个 MOD 都有<b>总开关</b>，MOD 管理中心里一键启停</li>
  </div>
  <div class="foot">粉丝群置顶自取 · 安装前先看《【必读】安装说明.txt》 · by Night</div>
  ${stampSvg(96).replace('class="stamp"', 'class="stamp" style="right:40px;bottom:26px;width:96px;height:96px"')}
  </div></body></html>`;
}

// ---------- 10 尾图 CTA（点赞关注 · 进群 · 评论区征想法） ----------
function tplCta() {
  const btn = (emoji, label) => `<div class="cta-btn"><div class="e">${emoji}</div><div class="l">${label}</div></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音图文 10 互动引导 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:radial-gradient(1000px 760px at 50% 20%, #2e2313 0%, #19130b 55%, #0a0a0d 100%); }
  .kick { position:absolute; top:104px; width:100%; text-align:center; color:#9aa0a8; font-size:26px; letter-spacing:12px; }
  .h1 { position:absolute; top:172px; width:100%; text-align:center; font-size:150px; font-weight:900;
    letter-spacing:10px; line-height:1.1; text-shadow:0 6px 30px rgba(0,0,0,.5); }
  .sub { position:absolute; top:424px; width:100%; text-align:center; color:#f4f1e8; font-size:42px;
    font-weight:900; letter-spacing:5px; }
  .sub b { color:#FFD75E; }
  .desc { position:absolute; top:496px; width:100%; text-align:center; color:#8f99a4; font-size:22px;
    letter-spacing:2px; }
  .idea { position:absolute; top:590px; left:64px; right:64px; border-radius:24px; padding:40px 44px 36px;
    background:rgba(255,193,7,.06); border:1px solid rgba(255,193,7,.42);
    box-shadow:0 24px 60px rgba(0,0,0,.5); text-align:center; }
  .idea .it { color:#FFD75E; font-size:34px; font-weight:900; letter-spacing:3px; }
  .idea .id1 { margin-top:18px; color:#c8bfa8; font-size:22px; letter-spacing:1px; }
  .idea .id2 { margin-top:20px; color:#fdf6e3; font-size:29px; font-weight:900; letter-spacing:2px; }
  .idea .id2 b { color:#FFD75E; font-size:31px; }
  .btns { position:absolute; top:972px; left:70px; right:70px; display:flex; gap:22px; }
  .cta-btn { flex:1; border-radius:20px; padding:30px 0 26px; text-align:center;
    background:rgba(16,14,10,.85); border:1px solid rgba(255,193,7,.4);
    box-shadow:0 16px 40px rgba(0,0,0,.5); }
  .cta-btn .e { font-size:52px; line-height:1; }
  .cta-btn .l { margin-top:14px; color:#FFD75E; font-size:26px; font-weight:900; letter-spacing:4px; }
  .foot { position:absolute; bottom:56px; width:100%; text-align:center; color:#cdb87e;
    font-size:24px; letter-spacing:4px; }
  .foot2 { position:absolute; bottom:24px; width:100%; text-align:center; color:#6d757f;
    font-size:17px; letter-spacing:3px; }
  .pageno { position:absolute; top:44px; right:48px; color:#6d757f; font-size:20px;
    letter-spacing:4px; font-family:Georgia,serif; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="pageno">10 / 10</div>
  <div class="kick">—— 看 到 这 里 的 都 是 真 爱 ——</div>
  <div class="h1 gold-t">点赞 · 关注</div>
  <div class="sub">进粉丝群 · <b>获取最新版 MOD</b></div>
  <div class="desc">进群看置顶取「生存日志MOD安装器_v1.7.0.exe」· 一键装齐 12 个 MOD</div>
  <div class="idea">
    <div class="it">💬 有想法？评论区说出来</div>
    <div class="id1">想要的功能、想改的数值、离谱的脑洞，都行</div>
    <div class="id2">有特色的想法 —— <b>直接安排进下个版本</b></div>
  </div>
  <div class="btns">
    ${btn('👍', '点 赞')}
    ${btn('⭐', '关 注')}
    ${btn('💬', '评 论')}
  </div>
  <div class="foot">下个版本，说不定就有你的点子</div>
  <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(96).replace('class="stamp"', 'class="stamp" style="right:40px;bottom:110px;width:96px;height:96px"')}
  </div></body></html>`;
}

// ---------- 视频封面 1080x1920（抖音信息流安全区：底部 340px / 右侧 150px 不放关键信息） ----------
function tplVideoCover() {
  const win = (img, rot, left, top, w, z) => `<div class="mw" style="transform:rotate(${rot}deg);left:${left}px;top:${top}px;width:${w}px;z-index:${z}">
    <div class="bar"><i></i><i></i><i></i></div><img src="${SHOTS}${img}"></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 1080x1920</title><style>
  ${BASE_CSS}
  .board { width:1080px; height:1920px; }
  .bg { background:
    radial-gradient(1000px 800px at 50% 20%, #2e2313 0%, #19130b 52%, #0a0a0d 100%); }
  .kick { position:absolute; top:170px; width:100%; text-align:center; color:#b0a078;
    font-size:34px; letter-spacing:18px; font-weight:700; }
  .q { position:absolute; top:262px; width:100%; text-align:center; color:#e8e4da;
    font-size:78px; font-weight:900; letter-spacing:4px; }
  .a { position:absolute; top:400px; width:100%; text-align:center; font-size:170px;
    font-weight:900; letter-spacing:8px; line-height:1.1; text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:680px; width:100%; display:flex; justify-content:center; gap:26px; }
  .pl { color:#FFD75E; font-size:37px; font-weight:900; letter-spacing:5px; padding:20px 38px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.7);
    box-shadow:0 14px 36px rgba(0,0,0,.55); }
  .ver { position:absolute; top:846px; width:100%; text-align:center; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:56px;
    color:#0a0a0d; letter-spacing:3px; padding:12px 34px; border-radius:14px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 14px 34px rgba(0,0,0,.5); }
  .mw { position:absolute; border-radius:16px; overflow:hidden; background:#101012;
    border:2px solid rgba(255,193,7,.5); box-shadow:0 34px 80px rgba(0,0,0,.8), 0 0 46px rgba(255,193,7,.12); }
  .mw .bar { height:30px; background:rgba(22,22,26,.96); display:flex; align-items:center; gap:8px; padding:0 14px;
    border-bottom:1px solid rgba(255,255,255,.07); }
  .mw .bar i { width:8px; height:8px; border-radius:50%; background:#41414a; }
  .mw .bar i:first-child { background:rgba(255,193,7,.85); }
  .mw img { display:block; width:100%; }
  .foot { position:absolute; top:1700px; width:100%; text-align:center; color:#cdb87e;
    font-size:32px; letter-spacing:6px; }
  .foot2 { position:absolute; top:1762px; width:100%; text-align:center; color:#6d757f;
    font-size:24px; letter-spacing:4px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="kick">生存日志 · MOD 合集</div>
  <div class="q">还在找生存日志mod？</div>
  <div class="a gold-t">这边全都有</div>
  <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
  <div class="ver"><span>v1.7.0 大版本</span></div>
  ${win('pp_real_panel.png', -3.2, 96, 1010, 470, 3)}
  ${win('eg_mod_full.png', 0, 300, 990, 500, 4)}
  ${win('pds_mod_full.png', 3.2, 530, 1010, 470, 5)}
  <div class="foot">粉丝群置顶免费取</div>
  <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(128).replace('class="stamp"', 'class="stamp" style="right:56px;top:120px;width:128px;height:128px"')}
  </div></body></html>`;
}

// ---------- 视频封面 3:4 版 1080x1440（主页九宫格/封面裁切比例适配） ----------
function tplVideoCover34() {
  const win = (img, rot, left, top, w, z) => `<div class="mw" style="transform:rotate(${rot}deg);left:${left}px;top:${top}px;width:${w}px;z-index:${z}">
    <div class="bar"><i></i><i></i><i></i></div><img src="${SHOTS}${img}"></div>`;
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 3:4 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:
    radial-gradient(900px 700px at 50% 18%, #2e2313 0%, #19130b 52%, #0a0a0d 100%); }
  .kick { position:absolute; top:78px; width:100%; text-align:center; color:#b0a078;
    font-size:29px; letter-spacing:14px; font-weight:700; }
  .q { position:absolute; top:150px; width:100%; text-align:center; color:#e8e4da;
    font-size:60px; font-weight:900; letter-spacing:3px; }
  .a { position:absolute; top:248px; width:100%; text-align:center; font-size:136px;
    font-weight:900; letter-spacing:7px; line-height:1.05; text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:478px; width:100%; display:flex; justify-content:center; gap:20px; }
  .pl { color:#FFD75E; font-size:30px; font-weight:900; letter-spacing:4px; padding:15px 30px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.7);
    box-shadow:0 12px 30px rgba(0,0,0,.55); }
  .ver { position:absolute; top:614px; width:100%; text-align:center; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:44px;
    color:#0a0a0d; letter-spacing:3px; padding:10px 28px; border-radius:12px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 12px 28px rgba(0,0,0,.5); }
  .mw { position:absolute; border-radius:14px; overflow:hidden; background:#101012;
    border:2px solid rgba(255,193,7,.5); box-shadow:0 28px 64px rgba(0,0,0,.8), 0 0 40px rgba(255,193,7,.12); }
  .mw .bar { height:26px; background:rgba(22,22,26,.96); display:flex; align-items:center; gap:7px; padding:0 12px;
    border-bottom:1px solid rgba(255,255,255,.07); }
  .mw .bar i { width:7px; height:7px; border-radius:50%; background:#41414a; }
  .mw .bar i:first-child { background:rgba(255,193,7,.85); }
  .mw img { display:block; width:100%; }
  .foot { position:absolute; top:1150px; width:100%; text-align:center; color:#cdb87e;
    font-size:27px; letter-spacing:5px; }
  .foot2 { position:absolute; top:1208px; width:100%; text-align:center; color:#6d757f;
    font-size:20px; letter-spacing:3px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="kick">生存日志 · MOD 合集</div>
  <div class="q">还在找生存日志mod？</div>
  <div class="a gold-t">这边全都有</div>
  <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
  <div class="ver"><span>v1.7.0 大版本</span></div>
  ${win('pp_real_panel.png', -3.2, 52, 782, 300, 3)}
  ${win('eg_mod_full.png', 0, 322, 742, 436, 4)}
  ${win('pds_mod_full.png', 3.2, 724, 782, 300, 5)}
  <div class="foot">粉丝群置顶免费取</div>
  <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(100).replace('class="stamp"', 'class="stamp" style="right:44px;top:56px;width:100px;height:100px"')}
  </div></body></html>`;
}

// ---------- 视频封面 v2 纯设计版（防"与视频内容重合"限流：零实机截图，纯排版+名片墙） ----------
// 竖版 1080x1920 + 横版 1920x1080 各一张
function chipWall(cols) {
  // cols: 每行卡片数；返回 .wall 内的卡片序列（CSS grid 自动排布）
  return FAMILY.map(([name, desc, badge]) => `<div class="wc${badge === 'new' ? ' hot' : badge === 'mod' ? ' warm' : ''}">
    ${badge === 'new' ? '<span class="wbn">新</span>' : badge === 'mod' ? '<span class="wbm">优化</span>' : ''}
    <div class="wn">${name}</div><div class="wd">${desc}</div></div>`).join('\n');
}

function tplVideoCoverVert2() {
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 v2 竖版 1080x1920</title><style>
  ${BASE_CSS}
  .board { width:1080px; height:1920px; }
  .bg { background:
    radial-gradient(1000px 800px at 50% 16%, #2e2313 0%, #19130b 52%, #0a0a0d 100%); }
  .bg::after { content:''; position:absolute; inset:0; opacity:.35;
    background-image: linear-gradient(rgba(255,255,255,.03) 1px, transparent 1px),
                      linear-gradient(90deg, rgba(255,255,255,.03) 1px, transparent 1px);
    background-size:72px 72px; }
  .kick { position:absolute; top:168px; width:100%; text-align:center; color:#b0a078;
    font-size:34px; letter-spacing:18px; font-weight:700; z-index:2; }
  .q { position:absolute; top:260px; width:100%; text-align:center; color:#e8e4da;
    font-size:76px; font-weight:900; letter-spacing:4px; z-index:2; }
  .a { position:absolute; top:396px; width:100%; text-align:center; font-size:172px;
    font-weight:900; letter-spacing:8px; line-height:1.1; z-index:2;
    text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:678px; width:100%; display:flex; justify-content:center; gap:26px; z-index:2; }
  .pl { color:#FFD75E; font-size:36px; font-weight:900; letter-spacing:5px; padding:19px 36px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.72);
    box-shadow:0 14px 36px rgba(0,0,0,.55); }
  .ver { position:absolute; top:842px; width:100%; text-align:center; z-index:2; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:54px;
    color:#0a0a0d; letter-spacing:3px; padding:12px 34px; border-radius:14px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 14px 34px rgba(0,0,0,.5); }
  .wall { position:absolute; top:986px; left:70px; right:70px; z-index:2;
    display:grid; grid-template-columns:repeat(3,1fr); gap:16px; }
  .wc { position:relative; border-radius:16px; padding:24px 18px 20px; text-align:center;
    background:rgba(255,255,255,.035); border:1px solid rgba(255,255,255,.13); }
  .wc.hot { background:rgba(255,193,7,.07); border-color:rgba(255,193,7,.45); }
  .wc.warm { background:rgba(224,178,92,.05); border-color:rgba(224,178,92,.35); }
  .wn { color:#e9e4d8; font-size:26px; font-weight:900; letter-spacing:1px; white-space:nowrap; }
  .wc.hot .wn { color:#FFD75E; }
  .wd { margin-top:8px; color:#8f99a4; font-size:17px; letter-spacing:1px; white-space:nowrap; }
  .wbn,.wbm { position:absolute; top:10px; right:10px; font-size:15px; font-weight:900;
    padding:3px 8px; border-radius:7px; }
  .wbn { color:#0a0a0d; background:#FFC107; }
  .wbm { color:#e0b25c; border:1px solid rgba(224,178,92,.55); }
  .foot { position:absolute; top:1712px; width:100%; text-align:center; color:#cdb87e;
    font-size:31px; letter-spacing:6px; z-index:2; }
  .foot2 { position:absolute; top:1774px; width:100%; text-align:center; color:#6d757f;
    font-size:23px; letter-spacing:4px; z-index:2; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="kick">生存日志 · MOD 合集</div>
  <div class="q">还在找生存日志mod？</div>
  <div class="a gold-t">这边全都有</div>
  <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
  <div class="ver"><span>v1.7.0 大版本</span></div>
  <div class="wall">${chipWall(3)}</div>
  <div class="foot">粉丝群置顶免费取</div>
  <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(128).replace('class="stamp"', 'class="stamp" style="right:56px;top:110px;width:128px;height:128px"')}
  </div></body></html>`;
}

function tplVideoCoverHoriz() {
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 v2 横版 1920x1080</title><style>
  ${BASE_CSS}
  .board { width:1920px; height:1080px; }
  .bg { background:
    radial-gradient(1300px 900px at 30% 40%, #2e2313 0%, #19130b 52%, #0a0a0d 100%); }
  .bg::after { content:''; position:absolute; inset:0; opacity:.35;
    background-image: linear-gradient(rgba(255,255,255,.03) 1px, transparent 1px),
                      linear-gradient(90deg, rgba(255,255,255,.03) 1px, transparent 1px);
    background-size:76px 76px; }
  .left { position:absolute; left:110px; top:0; bottom:0; width:880px; z-index:2; }
  .kick { position:absolute; top:150px; color:#b0a078; font-size:30px; letter-spacing:16px; font-weight:700; }
  .q { position:absolute; top:236px; color:#e8e4da; font-size:66px; font-weight:900; letter-spacing:3px; }
  .a { position:absolute; top:352px; font-size:150px; font-weight:900; letter-spacing:7px; line-height:1.05;
    text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:592px; display:flex; gap:20px; }
  .pl { color:#FFD75E; font-size:31px; font-weight:900; letter-spacing:4px; padding:16px 30px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.72);
    box-shadow:0 12px 32px rgba(0,0,0,.55); }
  .ver { position:absolute; top:740px; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:46px;
    color:#0a0a0d; letter-spacing:3px; padding:10px 30px; border-radius:12px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 12px 30px rgba(0,0,0,.5); }
  .foot { position:absolute; top:880px; color:#cdb87e; font-size:27px; letter-spacing:5px; }
  .foot2 { position:absolute; top:938px; color:#6d757f; font-size:20px; letter-spacing:3px; }
  .wall { position:absolute; right:110px; top:120px; width:660px; z-index:2;
    display:grid; grid-template-columns:repeat(2,1fr); gap:14px; }
  .wc { position:relative; border-radius:14px; padding:20px 16px 17px; text-align:center;
    background:rgba(255,255,255,.035); border:1px solid rgba(255,255,255,.13); }
  .wc.hot { background:rgba(255,193,7,.07); border-color:rgba(255,193,7,.45); }
  .wc.warm { background:rgba(224,178,92,.05); border-color:rgba(224,178,92,.35); }
  .wn { color:#e9e4d8; font-size:24px; font-weight:900; letter-spacing:1px; white-space:nowrap; }
  .wc.hot .wn { color:#FFD75E; }
  .wd { margin-top:6px; color:#8f99a4; font-size:16px; letter-spacing:1px; white-space:nowrap; }
  .wbn,.wbm { position:absolute; top:8px; right:8px; font-size:14px; font-weight:900;
    padding:2px 7px; border-radius:6px; }
  .wbn { color:#0a0a0d; background:#FFC107; }
  .wbm { color:#e0b25c; border:1px solid rgba(224,178,92,.55); }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="left">
    <div class="kick">生存日志 · MOD 合集</div>
    <div class="q">还在找生存日志mod？</div>
    <div class="a gold-t">这边全都有</div>
    <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
    <div class="ver"><span>v1.7.0 大版本</span></div>
    <div class="foot">粉丝群置顶免费取</div>
    <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  </div>
  <div class="wall">${chipWall(2)}</div>
  ${stampSvg(120).replace('class="stamp"', 'class="stamp" style="right:70px;bottom:56px;width:120px;height:120px"')}
  </div></body></html>`;
}

// ---------- 视频封面 v3（抖音封面展示区=3:4：竖版直接 3:4；横版 16:9 但关键信息全部收进中央 3:4 安全区） ----------
// 视觉主体=痛点划除块（纯排版；零截图防"与视频内容重合"限流；无水印无卡片墙）
function tplVideoCoverV3Vert() {
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 v3 竖版 3:4 1080x1440</title><style>
  ${BASE_CSS}
  .bg { background:radial-gradient(900px 700px at 50% 30%, #2e2313 0%, #19130b 55%, #0a0a0d 100%); }
  .bg::after { content:''; position:absolute; inset:0; opacity:.32;
    background-image: linear-gradient(rgba(255,255,255,.03) 1px, transparent 1px),
                      linear-gradient(90deg, rgba(255,255,255,.03) 1px, transparent 1px);
    background-size:64px 64px; }
  .frame { position:absolute; inset:34px; border:1px solid rgba(255,193,7,.16); border-radius:26px; z-index:1; }
  .glow { position:absolute; left:50%; top:400px; transform:translate(-50%,-50%); width:940px; height:940px;
    border-radius:50%; z-index:0;
    background:radial-gradient(circle, rgba(255,193,7,.12) 0%, transparent 62%); }
  .kick { position:absolute; top:92px; width:100%; text-align:center; color:#b0a078;
    font-size:29px; letter-spacing:15px; font-weight:700; z-index:2; }
  .q { position:absolute; top:168px; width:100%; text-align:center; color:#e8e4da;
    font-size:57px; font-weight:900; letter-spacing:3px; z-index:2; }
  .a { position:absolute; top:262px; width:100%; text-align:center; font-size:146px;
    font-weight:900; letter-spacing:7px; line-height:1.05; z-index:2;
    text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:482px; width:100%; display:flex; justify-content:center; gap:20px; z-index:2; }
  .pl { color:#FFD75E; font-size:29px; font-weight:900; letter-spacing:4px; padding:15px 30px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.72);
    box-shadow:0 12px 32px rgba(0,0,0,.55); }
  .ver { position:absolute; top:624px; width:100%; text-align:center; z-index:2; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:46px;
    color:#0a0a0d; letter-spacing:3px; padding:10px 28px; border-radius:12px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 12px 30px rgba(0,0,0,.5); }
  .hair { position:absolute; top:766px; left:230px; right:230px; height:1px; z-index:2;
    background:linear-gradient(90deg,transparent,#8f7c4e,transparent); }
  .chips { position:absolute; top:818px; width:100%; display:flex; justify-content:center; gap:16px; z-index:2; }
  .pc { color:#8f99a4; font-size:24px; letter-spacing:2px; padding:12px 24px; border-radius:999px;
    border:1px solid rgba(143,153,164,.38); background:rgba(255,255,255,.03);
    text-decoration:line-through; text-decoration-color:rgba(179,52,42,.85); text-decoration-thickness:2.5px; }
  .sub { position:absolute; top:928px; width:100%; text-align:center; color:#fdf6e3;
    font-size:30px; font-weight:900; letter-spacing:4px; z-index:2; }
  .sub b { color:#FFD75E; font-size:32px; }
  .foot { position:absolute; top:1176px; width:100%; text-align:center; color:#cdb87e;
    font-size:28px; letter-spacing:6px; z-index:2; }
  .foot2 { position:absolute; top:1234px; width:100%; text-align:center; color:#6d757f;
    font-size:20px; letter-spacing:3px; z-index:2; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="glow"></div><div class="frame"></div>
  <div class="kick">生存日志 · MOD 合集</div>
  <div class="q">还在找生存日志mod？</div>
  <div class="a gold-t">这边全都有</div>
  <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
  <div class="ver"><span>v1.7.0 大版本</span></div>
  <div class="hair"></div>
  <div class="chips"><span class="pc">满世界找</span><span class="pc">装一堆冲突</span><span class="pc">全英文配置</span></div>
  <div class="sub">一个安装器 · <b>装齐整个合集</b></div>
  <div class="foot">粉丝群置顶免费取</div>
  <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  ${stampSvg(84).replace('class="stamp"', 'class="stamp" style="right:58px;bottom:110px;width:84px;height:84px"')}
  </div></body></html>`;
}

function tplVideoCoverV3Horiz() {
  // 安全区：中央 810px 宽（1080×3/4），x∈[555,1365]；区外只放牺牲性装饰（竖排大字）
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>抖音视频封面 v3 横版 1920x1080（中央3:4安全区）</title><style>
  ${BASE_CSS}
  .board { width:1920px; height:1080px; }
  .bg { background:radial-gradient(1300px 900px at 50% 44%, #2e2313 0%, #19130b 55%, #0a0a0d 100%); }
  .bg::after { content:''; position:absolute; inset:0; opacity:.32;
    background-image: linear-gradient(rgba(255,255,255,.03) 1px, transparent 1px),
                      linear-gradient(90deg, rgba(255,255,255,.03) 1px, transparent 1px);
    background-size:64px 64px; }
  .side { position:absolute; top:50%; transform:translateY(-50%); opacity:.09; z-index:1;
    writing-mode:vertical-rl; font-size:128px; font-weight:900; letter-spacing:26px;
    color:#FFC107; white-space:nowrap; }
  .side.l { left:66px; } .side.r { right:66px; }
  .glow { position:absolute; left:50%; top:470px; transform:translate(-50%,-50%); width:860px; height:860px;
    border-radius:50%; z-index:0;
    background:radial-gradient(circle, rgba(255,193,7,.12) 0%, transparent 62%); }
  .safe { position:absolute; left:555px; top:0; width:810px; height:1080px; z-index:2; }
  .kick { position:absolute; top:74px; width:100%; text-align:center; color:#b0a078;
    font-size:25px; letter-spacing:12px; font-weight:700; }
  .q { position:absolute; top:140px; width:100%; text-align:center; color:#e8e4da;
    font-size:50px; font-weight:900; letter-spacing:3px; }
  .a { position:absolute; top:224px; width:100%; text-align:center; font-size:128px;
    font-weight:900; letter-spacing:6px; line-height:1.05;
    text-shadow:0 8px 40px rgba(0,0,0,.55); }
  .pills { position:absolute; top:412px; width:100%; display:flex; justify-content:center; gap:15px; }
  .pl { color:#FFD75E; font-size:25px; font-weight:900; letter-spacing:4px; padding:13px 25px;
    border-radius:999px; border:2px solid rgba(255,193,7,.55); background:rgba(12,10,7,.72);
    box-shadow:0 12px 32px rgba(0,0,0,.55); }
  .ver { position:absolute; top:540px; width:100%; text-align:center; }
  .ver span { display:inline-block; font-family:Impact,"Arial Black",sans-serif; font-size:40px;
    color:#0a0a0d; letter-spacing:3px; padding:9px 25px; border-radius:12px;
    background:linear-gradient(180deg,#f2ddb0,#dcc084); box-shadow:0 12px 30px rgba(0,0,0,.5); }
  .hair { position:absolute; top:668px; left:110px; right:110px; height:1px;
    background:linear-gradient(90deg,transparent,#8f7c4e,transparent); }
  .chips { position:absolute; top:706px; width:100%; display:flex; justify-content:center; gap:13px; }
  .pc { color:#8f99a4; font-size:21px; letter-spacing:2px; padding:10px 20px; border-radius:999px;
    border:1px solid rgba(143,153,164,.38); background:rgba(255,255,255,.03);
    text-decoration:line-through; text-decoration-color:rgba(179,52,42,.85); text-decoration-thickness:2.5px; }
  .sub { position:absolute; top:806px; width:100%; text-align:center; color:#fdf6e3;
    font-size:27px; font-weight:900; letter-spacing:4px; }
  .sub b { color:#FFD75E; font-size:29px; }
  .foot { position:absolute; top:906px; width:100%; text-align:center; color:#cdb87e;
    font-size:24px; letter-spacing:6px; }
  .foot2 { position:absolute; top:956px; width:100%; text-align:center; color:#6d757f;
    font-size:18px; letter-spacing:3px; }
  </style></head><body><div class="board">
  <div class="bg"></div><div class="grain"></div>
  <div class="glow"></div>
  <div class="side l">生存日志</div><div class="side r">MOD 合集</div>
  <div class="safe">
    <div class="kick">生存日志 · MOD 合集</div>
    <div class="q">还在找生存日志mod？</div>
    <div class="a gold-t">这边全都有</div>
    <div class="pills"><span class="pl">免冲突</span><span class="pl">全中文</span><span class="pl">一键装齐</span></div>
    <div class="ver"><span>v1.7.0 大版本</span></div>
    <div class="hair"></div>
    <div class="chips"><span class="pc">满世界找</span><span class="pc">装一堆冲突</span><span class="pc">全英文配置</span></div>
    <div class="sub">一个安装器 · <b>装齐整个合集</b></div>
    <div class="foot">粉丝群置顶免费取</div>
    <div class="foot2">生存日志 MOD 合集 v1.7.0 · by Night</div>
  </div>
  ${stampSvg(76).replace('class="stamp"', 'class="stamp" style="right:600px;bottom:56px;width:76px;height:76px"')}
  </div></body></html>`;
}

// ---------- 构建 ----------
const builders = [
  { file: '01-cover', tpl: tplCover },
  ...CONTENT.map((s, i) => ({ file: `${s.n}-${s.slug}`, tpl: () => tplContent(s, i, CONTENT.length + 3) })),
  { file: '09-family', tpl: tplFamily },
  { file: '10-cta', tpl: tplCta },
  { file: 'video-cover', tpl: tplVideoCover, w: 1080, h: 1920 },
  { file: 'video-cover-34', tpl: tplVideoCover34 },
  { file: 'video-cover-vert2', tpl: tplVideoCoverVert2, w: 1080, h: 1920 },
  { file: 'video-cover-horiz', tpl: tplVideoCoverHoriz, w: 1920, h: 1080 },
  { file: 'video-cover-v3-vert', tpl: tplVideoCoverV3Vert },
  { file: 'video-cover-v3-horiz', tpl: tplVideoCoverV3Horiz, w: 1920, h: 1080 },
];

const only = process.argv.slice(2);
fs.mkdirSync(OUT, { recursive: true });
for (const b of builders) {
  if (only.length && !only.some(o => b.file.startsWith(o) || o === b.file)) continue;
  const html = path.join(OUT, b.file + '.html');
  const jpg = path.join(OUT, b.file + '.jpg');
  const W = b.w || 1080, H = b.h || 1440;
  fs.writeFileSync(html, b.tpl(), 'utf8');
  execSync(`node "${path.join(__dirname, '_render_cover2.js')}" "${path.relative(path.join(__dirname, '..'), html).replace(/\\/g, '/')}" "${path.relative(path.join(__dirname, '..'), jpg).replace(/\\/g, '/')}" ${W} ${H}`, { stdio: 'pipe' });
  const kb = fs.statSync(jpg).size / 1024;
  if (kb > 600) console.log(`WARN ${b.file}.jpg ${kb.toFixed(0)}KB > 600KB`);
}
console.log('DONE assets/douyin/v170/');
