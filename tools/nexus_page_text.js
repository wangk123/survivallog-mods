// N网详情页文案生成器：各 mod README.md（发布口径单源）→ 双语 BBCode
// 用法：node tools/nexus_page_text.js          （全部 mod）
//       node tools/nexus_page_text.js PlanPick （单个 mod）
// 输出：发版物料/N网/页面文案/<中文名>-页面文案.txt + 发版物料/N网/物料索引.md
// 依据 docs/08 §5.2：中文在前完整五节 + [hr] 英文完整对照；安全标签集
// center/size/b/color/url/img/list/spoiler/hr（无 table/code，配置表转 list）。
// 注：docs/08 原计划 nexus_page_text.ps1；PS5.1 有 ASCII 坑（硬规则 7），改 node 实现，语义不变。
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..');

// mod 清单（发版白名单口径：套件 9 + PlanPick/PreDisasterSpeed/EndlessGuard；
// StackLimit999 禁发（硬规则 6），探针禁发（硬规则 5），CabinetSize WIP 未列）
const MODS = [
  { dir: 'SurvivalLog.BackpackExpand',   cn: '背包扩容',     en: 'Backpack Expand',        file: 'BackpackExpand', cover: 'bp-cover.jpg' },
  { dir: 'SurvivalLog.CabinetSize',     cn: '柜子容量',     en: 'Cabinet Size',           file: 'CabinetSize', cover: 'cs-cover.jpg' },
  { dir: 'SurvivalLog.FridgeChill',      cn: '冰箱保鲜',     en: 'Fridge Chill',           file: 'FridgeChill', cover: 'fc-cover.jpg' },
  { dir: 'SurvivalLog.QuickAction',      cn: '动作提速',     en: 'Quick Action',           file: 'QuickAction', cover: 'qa-cover.jpg' },
  { dir: 'SurvivalLog.CabinetEverywhere',cn: '储藏柜直达',   en: 'Cabinet Everywhere',     file: 'CabinetEverywhere', cover: 'ce-cover.jpg' },
  { dir: 'SurvivalLog.CabinetBurden',    cn: '柜子负重解放', en: 'Cabinet Burden Unbound', file: 'CabinetBurden', cover: 'cb-cover.jpg' },
  { dir: 'SurvivalLog.RoleUnlock',       cn: '角色专属解锁', en: 'Role Unlock',            file: 'RoleUnlock', cover: 'ru-cover.jpg' },
  { dir: 'SurvivalLog.RobotSpeed',       cn: '机器人提速',   en: 'Robot Speed',            file: 'RobotSpeed', cover: 'rs-cover.jpg' },
  { dir: 'SurvivalLog.ModCenter',        cn: 'MOD 管理中心', en: 'Mod Center',             file: 'ModCenter', cover: 'mc-cover.jpg' },
  { dir: 'SurvivalLog.PlanPick',         cn: '自选规划',     en: 'Plan Pick',              file: 'PlanPick', cover: 'pp-cover.jpg' },
  { dir: 'SurvivalLog.PreDisasterSpeed', cn: '灾前加速',     en: 'Pre-Disaster Speed',     file: 'PreDisasterSpeed', cover: 'pds-cover.jpg' },
  { dir: 'SurvivalLog.EndlessGuard',     cn: '无尽守卫',     en: 'Endless Guard',          file: 'EndlessGuard', cover: 'eg-cover.jpg' },
];

function readSections(md) {
  const marks = ['## 一、这是干什么的', '## 二、功能清单', '## 三、安全与还原',
    '## 四、安装与卸载', '## 五、配置说明', '## 六、English'];
  const sec = {};
  const pos = marks.map(m => md.indexOf(m));
  marks.forEach((m, i) => {
    if (pos[i] < 0) { sec[m] = ''; return; }
    const start = pos[i] + m.length;
    const end = (i + 1 < marks.length && pos[i + 1] > pos[i]) ? pos[i + 1] : md.length;
    sec[m] = md.slice(start, end).trim();
  });
  return sec;
}

function bullets(text) {
  return text.split(/\r?\n/).filter(l => /^[-*]\s+/.test(l.trim())).map(l => l.trim().replace(/^[-*]\s+/, ''));
}

function paras(text) {
  return text.split(/\r?\n\s*\r?\n/).map(p => p.trim()).filter(Boolean);
}

// markdown 配置表行 → BBCode list 项（表头/分隔行跳过）
function cfgList(md) {
  const out = [];
  md.split(/\r?\n/).forEach(line => {
    const t = line.trim();
    if (!t.startsWith('|') || /^\|[\s:|-]+\|?$/.test(t)) return;
    const cells = t.split('|').slice(1, -1).map(c => c.trim());
    if (cells.length < 3) return;
    if (cells[0] === '节.键' || /^-+$/.test(cells[0])) return;
    out.push(`[*][b]${cells[0]}[/b]（默认 ${cells[1]}）：${cells[2]}`);
  });
  return out;
}

// 英文节的 markdown → BBCode（**粗体** → [b]，- 条目 → list）
function enToBb(text) {
  const lines = text.split(/\r?\n/);
  const out = [];
  let inList = false;
  const closeList = () => { if (inList) { out.push('[/list]'); inList = false; } };
  for (const raw of lines) {
    const t = raw.trim();
    if (!t) { continue; }
    if (/^[-*]\s+/.test(t)) {
      if (!inList) { out.push('[list]'); inList = true; }
      out.push('[*]' + inline(t.replace(/^[-*]\s+/, '')));
      continue;
    }
    closeList();
    const bold = t.replace(/^\*\*(.+?)\*\*:?\s*$/, (m, g) => '[b]' + g + '[/b]');
    out.push(inline(bold.startsWith('[b]') ? bold : t));
  }
  closeList();
  return out.join('\n');
}

function inline(s) {
  return s
    .replace(/\*\*(.+?)\*\*/g, '[b]$1[/b]')
    .replace(/`([^`]+)`/g, '$1');
}

function buildPage(mod, ver) {
  const mdPath = path.join(ROOT, mod.dir, 'README.md');
  if (!fs.existsSync(mdPath)) return { mod, error: 'README.md 缺失' };
  const md = fs.readFileSync(mdPath, 'utf8');
  const sec = readSections(md);

  const feat = bullets(sec['## 二、功能清单']);
  const safe = bullets(sec['## 三、安全与还原']);
  const cfgFileLine = (sec['## 五、配置说明'].match(/`BepInEx\\config\\[^`]+`/) || ['(见 mod 文件夹 README)'])[0];
  const cfgItems = cfgList(sec['## 五、配置说明']);

  const L = [];
  L.push('[center][img]PASTE_BANNER_URL_HERE[/img][/center]');
  L.push(`[size=5][b]${mod.cn} ${mod.en}（中文说明 · English below）[/b][/size]`);
  L.push('');
  L.push(sec['## 一、这是干什么的'] ? inline(paras(sec['## 一、这是干什么的']).join(' ')) : '');
  L.push('');
  L.push('[size=4][b]📋 功能介绍 Features[/b][/size]');
  L.push('[list]');
  feat.forEach(f => L.push('[*]' + inline(f)));
  if (safe.length) safe.forEach(s => L.push('[*]' + inline(s)));
  else L.push('[*]安全承诺：不改存档 / 不改游戏文件 / 删 dll 即还原');
  L.push('[/list]');
  L.push('[size=4][b]📦 前置依赖 Requirements[/b][/size]');
  L.push('[list][*]Steam 正版《生存日志》 Survival Log（AppID 4164790）');
  L.push('[*]BepInEx 6（IL2CPP）：完整离线包已内置注入链；纯插件包需自装 BepInEx 6 IL2CPP[/list]');
  L.push('[size=4][b]🔧 安装说明 Installation[/b][/size]');
  paras(sec['## 四、安装与卸载']).forEach(p => {
    const lines = p.split(/\r?\n/).map(l => l.trim()).filter(Boolean);
    let buf = [], list = [];
    const flushBuf = () => { if (buf.length) { L.push(inline(buf.join(' '))); buf = []; } };
    const flushList = () => { if (list.length) { L.push('[list]'); list.forEach(x => L.push('[*]' + inline(x))); L.push('[/list]'); list = []; } };
    lines.forEach(l => {
      if (/^[-*]\s+/.test(l)) { flushBuf(); list.push(l.replace(/^[-*]\s+/, '')); }
      else { flushList(); buf.push(l); }
    });
    flushBuf(); flushList();
  });
  L.push('N网包与百度云 / GitHub 发布包同源同号。' +
    (/游戏更新后失效/.test(sec['## 四、安装与卸载']) ? '' : '游戏更新后失效属正常：等套件适配新版后更新即可。'));
  L.push('[size=4][b]⚙️ 配置说明 Configuration[/b][/size]');
  L.push(`配置文件：${cfgFileLine.replace(/`/g, '')}（记事本可改，多数即时生效）。常用配置：`);
  L.push('[list]');
  cfgItems.forEach(c => L.push(c));
  L.push('[/list]');
  L.push('[size=4][b]❓ 常见问题 FAQ[/b][/size]');
  L.push('[list][*]装了没反应？检查游戏根目录三件套：winhttp.dll + doorstop_config.ini、dotnet\\ 目录、BepInEx\\core 有 dll；缺了请用本页 Full 包往游戏根目录覆盖解压一次。');
  L.push('[*]游戏更新后失效？等套件适配新版即可，本 mod 不改任何游戏文件。[/list]');
  L.push('[size=4][b]✍️ 署名 Credits[/b][/size]');
  L.push('作者 Night（抖音 166660371）。免费分享，仅供学习交流；游戏本体请支持正版。');
  L.push('[color=#FFC107][b]未经作者允许，禁止转载 / 二次上传到任何平台[/b][/color]（N网规则同样禁止再上传他人 mod）。');
  L.push('[hr]');
  L.push(enToBb(sec['## 六、English']));
  L.push('[hr]');
  L.push(`[size=2]Survival Log mods by Night · suite v${ver} · cover & page in one pack[/size]`);

  return { mod, body: L.filter(x => x !== undefined).join('\n') };
}

const only = process.argv[2];
const version = JSON.parse(fs.readFileSync(path.join(ROOT, 'version.json'), 'utf8').replace(/^\uFEFF/, '')).suite;
const DL = path.join(ROOT, '发版物料', 'N网');
const OUTDIR = path.join(DL, '页面文案');
fs.mkdirSync(OUTDIR, { recursive: true });
const cname = m => m.cn.replace(/\s+/g, '');

const index = ['# N网物料索引（生成物，勿手改；单源=各 mod README.md）',
  '',
  `套件版本：v${version}（version.json 单源）。生成命令：\`node tools/nexus_page_text.js\``,
  '',
  '**上传口径**：N网按【单个 mod】建页上传，双轨包 = Full（自带 BepInEx 基座，解压即用）+ PluginOnly（纯 dll）。套件一键安装器不上 N网（百度云渠道专属）；页面安装说明即单 mod 双轨口径。',
  '',
  '| mod | 封面（封面\\） | 横幅（横幅\\） | 页面文案（页面文案\\） | 上传包名 Full | 上传包名 PluginOnly |',
  '|---|---|---|---|---|---|'];

let fail = 0;
for (const m of MODS) {
  if (only && !m.dir.includes(only)) continue;
  const r = buildPage(m, version);
  if (r.error) { console.log('SKIP ' + m.dir + ': ' + r.error); fail++; continue; }
  const n = cname(m);
  const out = path.join(OUTDIR, n + '-页面文案.txt');
  fs.writeFileSync(out, r.body.replace(/\n{3,}/g, '\n\n'), 'utf8');
  index.push(`| ${m.cn} ${m.en} | ${n}-封面.jpg | ${n}-横幅.jpg | ${n}-页面文案.txt | SurvivalLog-${m.file}-v${version}-Full.zip | SurvivalLog-${m.file}-v${version}-PluginOnly.zip |`);
  console.log('OK ' + n + '-页面文案.txt  (' + r.body.length + ' chars)');
}
if (!only) fs.writeFileSync(path.join(DL, '物料索引.md'), index.join('\n'), 'utf8');
process.exit(fail && !only ? 1 : 0);
