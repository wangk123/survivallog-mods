// N网封面统一构建器（v2 版式 · 2026-10-25 作者定调：去署名行 + 加可配置信息行 + 质感精修）
// 用法：node tools/build_covers.js [前缀...]   不带参数=全部 12 张
// 产物：assets/banners/nexus-ce/v2/<xx>-cover.html + .jpg（1920x1080，<400KB 校验）
// 版式依据 docs/08 §5.1 R4 定稿；v2 变更（作者 2026-10-25）：
//   删 by 署名行（FREE·Night·抖音）→ 换「⚙ 可配置」信息行；标题金白渐变；标签胶囊化+毛玻璃；
//   窗框细化；暗角+细噪点；夜字印 B-2 保留（如需移除改 KEEP_STAMP=false）。
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const ROOT = path.join(__dirname, '..');
const V2 = path.join(ROOT, 'assets', 'banners', 'nexus-ce', 'v2');
const SHOTS = '../../../../gamekb/webui_shots/';
const KEEP_STAMP = true;

const MODS = [
  { p: 'bp', cn: '背包扩容',     cnpx: 92, en: 'BACKPACK EXPAND · 8×7 → 11×10 · 负重五倍',  gray: '8×7 · 负重紧巴巴',    gold: '11×10 · 负重五倍',   cfg: '宽 · 高 · 负重 三键任意调' },
  { p: 'cx', cn: '柜子扩容',     cnpx: 92, en: 'CABINET EXPAND · 置物架 15×12 → 23×18',    gray: '超大置物架 15×12',    gold: '23×18 · 全家×1.5',   cfg: '柜子 / 冰箱 / 载具 倍数独立调' },
  { p: 'cb', cn: '柜子负重解放', cnpx: 88, en: 'CABINET BURDEN UNBOUND · 负重上限 → 无限制', gray: '格子没满 · 放不下',  gold: '负重清零 · 格满为限', cfg: '开关一键还原 · 名单可增删' },
  { p: 'fc', cn: '冰箱保鲜',     cnpx: 92, en: 'FRIDGE CHILL · 保鲜 ×5 → ×10 天赋解封',    gray: '保鲜×5 · 食物快坏',    gold: '保鲜×10 · 封顶百倍',  cfg: '保鲜倍数 · 总封顶 可调' },
  { p: 'ce', cn: '储藏柜直达',   cnpx: 92, en: 'CABINET EVERYWHERE · 工作台直连家里的柜子', gray: '材料压背包 · 来回倒', gold: '柜子直达 · 自动取料', cfg: '十界面视图 逐个配置' },
  { p: 'ru', cn: '角色专属解锁', cnpx: 88, en: 'ROLE UNLOCK · 角色专属 → 全角色互通',      gray: '专属内容 · 锁角色',    gold: '专卡全角色 · 想买就买', cfg: '藤蔓 / 规划 / 花瓶 独立开关' },
  { p: 'mc', cn: 'MOD 管理中心', cnpx: 80, en: 'MOD CENTER · 逐个手改 cfg → 面板一键管理',  gray: '手改 cfg · 太硬核',   gold: '全套件 · 面板直管',  cfg: '面板滑条直改 · 即时生效' },
  { p: 'pds', cn: '灾前加速',    cnpx: 92, en: 'PRE-DISASTER SPEED · 灾前干等 → 五键变速 真×3', gray: '灾前 1 倍 · 干等', gold: '五键变速 · 真×3',    cfg: '真三倍 · 快捷键 可开关' },
  { p: 'qa', cn: '动作提速',     cnpx: 92, en: 'QUICK ACTION · 吃饭15秒 → 全动作0.5秒',    gray: '搜个柜 15 秒',        gold: '全动作 0.5 秒',      cfg: '五分类毫秒 逐类可调' },
  { p: 'rs', cn: '机器人提速',   cnpx: 92, en: 'ROBOT SPEED · 慢半拍机器人 → 全面×2',      gray: '机器人 · 慢半拍',      gold: '全属性×2 · 对齐手速', cfg: '七项倍率 独立可调' },
  { p: 'pp', cn: '自选规划',     cnpx: 92, en: 'PLAN PICK · 随机三选 → 全目录自选',        gray: '随机派 3 张 · 看运气', gold: '465 项全目录 · 搜索自选', cfg: '自选加价 100%~300% 可调',
    imgs: ['rogue_vanilla_cards.png', 'pp_real_panel.png', 'pp_confirm_only.png'] },
  { p: 'eg', cn: '无尽守卫',     cnpx: 92, en: 'ENDLESS GUARD · 沦陷断供 → 供给永续',       gray: '交易点沦陷 · 断供',    gold: '永不沦陷 · 旧档复活', cfg: '四大功能 各自开关' },
];

const GRAIN = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='2' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter><rect width='180' height='180' filter='url(%23n)'/></svg>";

function tpl(m) {
  const im = m.imgs || [m.p + '_vanilla_full.png', m.p + '_mod_full.png', m.p + '_mod_zoom.png'];
  const stamp = KEEP_STAMP ? `
  <!-- 作者徽记 B-2 · 夜字印（右下钤） -->
  <div class="stamp">
    <svg width="118" height="118" viewBox="0 0 264 264">
      <rect x="8" y="8" width="248" height="248" rx="30" fill="#1b1610"/>
      <rect x="24" y="24" width="216" height="216" rx="20" fill="none" stroke="#FFC107" stroke-width="5"/>
      <text x="132" y="176" text-anchor="middle" font-family="KaiTi,STKaiti,serif" font-weight="900"
            font-size="150" fill="#FFC107">夜</text>
      <text x="132" y="222" text-anchor="middle" font-family="Georgia,serif" font-size="21"
            letter-spacing="6" fill="#FFC107" opacity=".85">NIGHT</text>
    </svg>
  </div>` : '';
  return `<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>${m.cn} · N网封面 v2 1920x1080</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { background:#000; font-family:"Microsoft YaHei","SimHei",sans-serif; }
  .board { position:relative; overflow:hidden; width:1920px; height:1080px; background:#07070a; }

  .vanilla { position:absolute; inset:0;
    background: linear-gradient(200deg,#2c3238 0%,#20252b 60%,#181c21 100%);
    clip-path: polygon(0 0, 47% 0, 33% 100%, 0 100%); }
  .modon { position:absolute; inset:0;
    background: radial-gradient(1200px 780px at 66% 40%, #2e2417 0%, #1d160e 55%, #140f09 100%);
    clip-path: polygon(47% 0, 100% 0, 100% 100%, 33% 100%); }
  .vgrid { position:absolute; inset:0; opacity:.4;
    background-image: linear-gradient(rgba(255,255,255,.03) 1px, transparent 1px),
                      linear-gradient(90deg, rgba(255,255,255,.03) 1px, transparent 1px);
    background-size:56px 56px; clip-path: polygon(0 0, 47% 0, 33% 100%, 0 100%); }
  .vt { position:absolute; left:84px; top:84px; color:#8b97a1; font-size:22px; letter-spacing:12px; font-weight:800; opacity:.9; }
  .mt { position:absolute; right:84px; top:84px; color:#FFC107; font-size:22px; letter-spacing:12px; font-weight:800; }

  .vwin { position:absolute; left:96px; top:250px; width:600px; border-radius:14px; overflow:hidden;
    border:1px solid rgba(255,255,255,.14); background:#15181c; transform:rotate(-2deg);
    box-shadow:0 30px 70px rgba(0,0,0,.62), 0 0 0 1px rgba(0,0,0,.4); }
  .vwin .bar { height:30px; background:rgba(21,24,28,.96); display:flex; align-items:center; gap:7px; padding:0 13px;
    border-bottom:1px solid rgba(255,255,255,.06); }
  .vwin .bar i { width:8px; height:8px; border-radius:50%; background:#39414a; }
  .vwin img { display:block; width:100%; filter:grayscale(.85) brightness(.82); }

  .mwin { position:absolute; left:700px; top:170px; width:1120px; border-radius:16px; overflow:hidden;
    border:1.5px solid rgba(255,193,7,.55); background:#101012; transform:rotate(.8deg); z-index:5;
    box-shadow: 0 40px 100px rgba(0,0,0,.78), 0 0 70px rgba(255,193,7,.12); }
  .mwin .bar { height:32px; background:rgba(22,22,26,.96); display:flex; align-items:center; gap:8px; padding:0 14px;
    border-bottom:1px solid rgba(255,255,255,.07); }
  .mwin .bar i { width:8px; height:8px; border-radius:50%; background:#41414a; }
  .mwin .bar i:first-child { background:rgba(255,193,7,.8); }
  .mwin img { display:block; width:100%; }

  .zoom { position:absolute; left:940px; top:640px; width:520px; z-index:9; border-radius:14px; overflow:hidden;
    border:1.5px solid rgba(255,193,7,.9); background:#151310; transform:rotate(-1.5deg);
    box-shadow: 0 26px 60px rgba(0,0,0,.82), 0 0 44px rgba(255,193,7,.18); }
  .zoom img { display:block; width:100%; }

  .lab { position:absolute; z-index:7; font-size:20px; font-weight:800; letter-spacing:2.5px; padding:9px 18px;
    border-radius:999px; color:#FFC107; border:1px solid rgba(255,193,7,.45);
    background:rgba(12,10,7,.68); backdrop-filter:blur(6px);
    box-shadow:0 10px 26px rgba(0,0,0,.5); }
  .lab.gray { border-color:rgba(168,182,196,.4); color:#b3c0cc; }

  .seam { position:absolute; inset:0; z-index:6; }

  .stamp { position:absolute; right:110px; bottom:168px; z-index:11; transform:rotate(-7deg);
    filter: drop-shadow(0 10px 22px rgba(0,0,0,.75)) drop-shadow(0 0 16px rgba(255,193,7,.35)); }

  .title { position:absolute; left:150px; bottom:84px; z-index:8; width:940px; }
  .tback { position:absolute; left:-26px; top:-30px; right:-12px; bottom:-24px; z-index:-1; border-radius:22px;
    background: linear-gradient(160deg, rgba(8,7,9,.92), rgba(8,7,9,.45) 78%, transparent); filter: blur(3px); }
  .kicker { color:#b0a078; font-size:20px; letter-spacing:14px; font-weight:600; }
  .cn { margin-top:12px; font-size:${m.cnpx}px; font-weight:900; letter-spacing:9px; line-height:1;
    background:linear-gradient(180deg,#fdf6e3 10%,#f2ddb0 55%,#dcc084 100%);
    -webkit-background-clip:text; background-clip:text; color:transparent;
    text-shadow: 0 3px 16px rgba(0,0,0,.55); }
  .en { margin-top:13px; font-family:Georgia,serif; font-size:23px; color:#e8c766; letter-spacing:5px; }
  .cfg { margin-top:14px; display:flex; align-items:center; gap:9px; color:#cdb87e; font-size:19px;
    letter-spacing:2px; }
  .cfg .g { font-size:20px; line-height:1; transform:translateY(-1px); }
  .cfg .sep { width:26px; height:1px; background:linear-gradient(90deg,transparent,#8f7c4e); }

  .vig { position:absolute; inset:0; z-index:12; pointer-events:none;
    background: radial-gradient(125% 95% at 50% 38%, transparent 55%, rgba(0,0,0,.4) 100%); }
  .grain { position:absolute; inset:0; z-index:13; pointer-events:none; opacity:.05;
    background-image:url("${GRAIN}"); background-size:180px 180px; }
</style>
</head>
<body>
<section class="board">
  <div class="vanilla"></div><div class="vgrid"></div>
  <div class="modon"></div>
  <div class="vt">VANILLA</div><div class="mt">MOD ON</div>

  <div class="vwin">
    <div class="bar"><i></i><i></i><i></i></div>
    <img src="${SHOTS}${im[0]}" alt="">
  </div>
  <div class="lab gray" style="left:150px; top:700px;">${m.gray}</div>

  <div class="mwin">
    <div class="bar"><i></i><i></i><i></i></div>
    <img src="${SHOTS}${im[1]}" alt="">
  </div>
  <div class="lab" style="right:150px; top:726px;">${m.gold}</div>

  <div class="zoom">
    <img src="${SHOTS}${im[2]}" alt="">
  </div>

  <svg class="seam" width="1920" height="1080">
    <line x1="902.4" y1="-20" x2="633.6" y2="1100" stroke="#FFC107" stroke-width="3" opacity=".8"/>
  </svg>
${stamp}
  <div class="title">
    <div class="tback"></div>
    <div class="kicker">SURVIVAL LOG · MOD</div>
    <div class="cn">${m.cn}</div>
    <div class="en">${m.en}</div>
    <div class="cfg"><span class="g">⚙</span>${m.cfg}<span class="sep"></span>cfg 即改即生效</div>
  </div>

  <div class="vig"></div>
  <div class="grain"></div>
</section>
</body>
</html>
`;
}

const only = process.argv.slice(2);
let built = 0, failed = 0;
for (const m of MODS) {
  if (only.length && !only.includes(m.p)) continue;
  const html = path.join(V2, m.p + '-cover.html');
  const jpg = path.join(V2, m.p + '-cover.jpg');
  fs.writeFileSync(html, tpl(m), 'utf8');
  const relHtml = path.relative(ROOT, html).replace(/\\/g, '/');
  const relJpg = path.relative(ROOT, jpg).replace(/\\/g, '/');
  execSync(`node "${path.join(__dirname, '_render_cover2.js')}" "${relHtml}" "${relJpg}" 1920 1080`, { stdio: 'pipe' });
  const kb = fs.statSync(jpg).size / 1024;
  const pass = kb < 400;
  console.log((pass ? 'PASS' : 'FAIL') + ' ' + m.p + '-cover.jpg ' + kb.toFixed(0) + 'KB');
  pass ? built++ : failed++;
}
console.log(`done: ${built} built, ${failed} failed`);
process.exit(failed ? 1 : 0);
