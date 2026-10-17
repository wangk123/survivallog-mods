// ============================================================================
// tools/cabinet_views_cdp.js —— 场景视图逐界面断言（CDP 直读游戏 WebView DOM，只读不改）
// 前提：ViewportProbe [DevTools] Enabled=true（开发机 9222 端口）+ 游戏已运行 + 作者已进档
//       + 目标界面已在游戏中打开（iframe 存在才有帧可断言）。
// 依赖解析同 tools/devtools_cdp_test.js：C:/temp/baidu_up/node_modules/playwright-core。
//
// 用法（三种模式）：
//   node tools/cabinet_views_cdp.js <pageId>                计数断言：可见页签数 + 结构性事实
//   node tools/cabinet_views_cdp.js --dd <pageId>           下拉断言：存在性/挂载位置（absolute+列容器内+贴列左下）
//   node tools/cabinet_views_cdp.js --mem <pageId> <tagId>  记忆断言：localStorage sltf_sel_v3 与触发器文本
//     tagId = 标签数字 id | 'other'（其他/未打标签） | 0（全部柜子）
//     --mem 只读比对，不模拟点击：CDP evaluate 不经屏幕输入链路，直改 localStorage/调 __SLTF__
//     属于"写态"操作不可靠，故记忆断言 = 作者实际选过之后由脚本读态验证。
//     流程：作者在界面下拉里选标签 → 关闭界面 → 重开界面 → 跑 --mem（两项都过 = 跨开关持久）。
//
// pageId（=实测 iframe 元素 id；TradeUI 为别名 → Trade，T1 report L203-214 实测）：
//   食物组（食物标签柜 Dim1-4 + 全部冰箱）        Cooking / RatCage / BrewPanel / DyePanel / Compressor
//   材料组（材料+工具+未标签柜+材料架，无冰箱）    ToolTable / Shredder / GreenhouseBuild
//   下拉组（默认全部容器，按需筛选）              Trade / DroneHub
//
// 退出码：0=全部通过  1=断言失败  2=用法错误  3=环境错误（9222 连不上 / 目标界面未打开）
//
// ---- EXPECT 回填说明（作者）----
// 下方 EXPECT 常量块 = 期望「可见页签数」，-1=未回填 → 跳过该页计数断言（结构性事实仍断言：
// 固定页无 .sltf-dd-btn、browser 页有且挂列容器 absolute 贴左下）。回填流程：
//   1) 进测试存档打开目标界面 → 2) 跑本脚本 <pageId> 看 "visible=N" 与明细
//   3) 按存档柜子打标实况核对 N（语义见上方分组注释，spec §3 场景视图表）
//   4) 填入 EXPECT 后复跑应全绿。
//
// ---- K2/K3/K4 取证操作手册（作者配合节点；本脚本不代跑，结论回填 task-9-report.md）----
// K2 工作台是否有配方消耗食物域材料（决定材料组是否需要 MaterialIncludeFood）：
//    开工作台翻配方页。手段：DevTools Console（9222 的 ToolTable 帧上下文）查 recipe 数据，
//    或 ActionProbe dump 配方表；重点找食材/烹饪产物类材料。
//    结论二选一：A. 无食物配方 → 材料组维持「材料+工具+未标签」（默认，无需改码）
//                B. 有 → 需 cfg Scenario.MaterialIncludeFood=true + JS S.matFood=true
//                   （Task 6 Step 2 已留钩子，届时按 brief Step 3 构建+部署+复测再 commit）。
// K3 口粮压制机页签构成：开 Compressor 界面，记录页签清单里冰箱页签的有无/数量
//    （本脚本 <Compressor> 输出的 visible 明细即证据），核对食物视图「全部冰箱一律显示」
//    语义与实际呈现一致。
// K4 交易/调度机队无域限制：开 Trade/DroneHub 逐页签 A/D 翻动，确认全部容器可见、无域过滤
//    （维持 browser 默认的前提成立），跑本脚本 <Trade>/<DroneHub> 记 visible 数。
//
// 回填格式（追加到 .superpowers/sdd/2026-10-17-cabinet-merge-scenario-views/task-9-report.md）：
//   ## 断言实跑与取证回填（作者）
//   - EXPECT 回填值：Cooking=N, RatCage=N, ...（逐页，含冰箱/柜子构成说明）
//   - 逐页断言：node tools/cabinet_views_cdp.js <pageId> 全绿/失败明细
//   - K2 结论：A 或 B + 证据摘要（配方样本/Console 输出）
//   - K3 结论：冰箱页签 xN；呈现与「全部冰箱」语义 一致/不一致 + 现象
//   - K4 结论：Trade visible=N / DroneHub visible=N；翻页无异常
// ============================================================================

const NM_PATH = 'C:/temp/baidu_up/node_modules/playwright-core'; // 与 tools/devtools_cdp_test.js 相同
const CDP_URL = 'http://localhost:9222';

// 页面档案：group=场景视图组别（spec §3 定稿）；kind=mod tagfilter.page.js detectC 应命中的页签栏类型
const PAGES = {
  Cooking:         { group: 'food',     kind: 'bag'   }, // #bagTabsScroll .bag-tab（pin 背包在 #bagTabPinned，不在 cands）
  RatCage:         { group: 'food',     kind: 'bag'   },
  BrewPanel:       { group: 'food',     kind: 'bag'   },
  DyePanel:        { group: 'food',     kind: 'bag'   },
  Compressor:      { group: 'food',     kind: 'cart0' }, // #tabs .left-tab（无 pin 分离）
  ToolTable:       { group: 'material', kind: 'tt'    }, // #leftTabsScroll .cabinet-tab（抽屉 #tab-drawer 无此类）
  Shredder:        { group: 'material', kind: 'cart'  }, // #tabsScroll .left-tab（pin=tabs[0]）
  GreenhouseBuild: { group: 'material', kind: 'cart'  }, // 与 Shredder 同构
  Trade:           { group: 'browser',  kind: 'trade' }, // .bag-tabs-scroll .bag-tab[data-owner]
  DroneHub:        { group: 'browser',  kind: 'bag'   },
};
const ALIAS = { TradeUI: 'Trade' }; // spec 示例名 TradeUI → 实测 iframe id=Trade

// 期望可见页签数（作者回填；-1=未回填→跳过计数断言，结构事实仍断言）
const EXPECT = {
  Cooking: -1, RatCage: -1, BrewPanel: -1, DyePanel: -1, Compressor: -1,
  ToolTable: -1, Shredder: -1, GreenhouseBuild: -1,
  Trade: -1, DroneHub: -1,
};

// ---------------------------------------------------------------------------
// 帧内探针（在目标 iframe 里 evaluate）：detectC 与 mod 同源、判定顺序一致，只读
function probeFrame() {
  function detectC(doc) {
    if (doc.getElementById('leftTabsScroll')) return { kind: 'tt', list: doc.getElementById('leftTabsScroll'), sel: '.cabinet-tab' };
    if (doc.getElementById('bagTabsScroll')) return { kind: 'bag', list: doc.getElementById('bagTabsScroll'), sel: '.bag-tab' };
    if (doc.querySelector('.bag-tabs-scroll .bag-tab[data-owner]')) return { kind: 'trade', list: doc.querySelector('.bag-tabs-scroll'), sel: '.bag-tab' };
    if (doc.getElementById('tabsScroll')) return { kind: 'cart', list: doc.getElementById('tabsScroll'), sel: '.left-tab' };
    if (doc.getElementById('tabs')) return { kind: 'cart0', list: doc.getElementById('tabs'), sel: '.left-tab' };
    return null;
  }
  var c = detectC(document);
  if (!c) return { found: false };
  var cands = c.list.querySelectorAll(c.sel);
  var total = 0, visible = [];
  for (var i = 0; i < cands.length; i++) {
    var el = cands[i];
    total++;
    // 可见判定与 mod 一致：display:none 才算藏（mod 隐藏即写 display:none）
    if (el.style.display === 'none') continue;
    var label = (el.getAttribute('data-owner') != null ? '#' + el.getAttribute('data-owner') + ' ' : '')
      + (el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 20);
    visible.push(label);
  }
  var dd = document.querySelector('.sltf-dd-btn');
  var ddInfo = null;
  if (dd) {
    // 触发器 2026-10-17 第三轮起挂列容器（placeDD：高>300 祖先）、position:absolute 贴列左下
    //（测量避让：窄左底按钮让位/宽底栏上移）——断言按锚定列+几何距离取证（与实现同源的走法）
    var cs = null;
    try { var dv = dd.ownerDocument.defaultView; if (dv) cs = dv.getComputedStyle(dd); } catch (e) {}
    var dr = dd.getBoundingClientRect();
    var col = null;
    for (var p = dd.parentElement; p; p = p.parentElement) {
      if (p === document.body || p === document.documentElement) break; // 列容器=面板布局子树，不认 body/html 根
      try { if (p.getBoundingClientRect().height > 300) { col = p; break; } } catch (e) {}
    }
    var cr = col ? col.getBoundingClientRect() : null;
    ddInfo = {
      connected: dd.isConnected,
      displayNone: dd.style.display === 'none',
      pos: cs ? cs.position : '',
      inCol: !!col, // 挂在高度>300 的列容器内（在面板内，不再挂 body 挂视口角落）
      colDesc: col ? ((col.id ? '#' + col.id : '') + '.' + col.className) : null,
      leftInCol: cr ? dr.left - cr.left : NaN,    // 距列容器左缘（避让窄左按钮后仍应 <120）
      bottomInCol: cr ? cr.bottom - dr.bottom : NaN, // 距列容器底缘（避让宽底栏后仍应 <60）
      parentDesc: dd.parentElement ? ((dd.parentElement.id ? '#' + dd.parentElement.id : '') + '.' + dd.parentElement.className) : null,
      text: (dd.textContent || '').trim().slice(0, 16),
    };
  }
  return {
    found: true,
    kind: c.kind,
    total: total,
    visibleCount: visible.length,
    visible: visible,
    dd: ddInfo,
    pop: !!document.querySelector('.sltf-dd-pop'),
  };
}

// 记忆探针（在 Root 主帧 evaluate）：只读 localStorage sltf_sel_v3（{ pageId: tagId|'other'|0 }）
function probeMem(pageId) {
  var raw = null;
  try { raw = localStorage.getItem('sltf_sel_v3'); } catch (e) {}
  var all = {};
  try { all = JSON.parse(raw || '{}') || {}; } catch (e) {}
  return { raw: raw, sel: (all[pageId] === undefined ? null : all[pageId]), all: all };
}
// ---------------------------------------------------------------------------

function usage() {
  const ids = Object.keys(PAGES).join(' / ');
  return [
    '场景视图逐界面断言（CDP 只读；前提=ViewportProbe [DevTools] Enabled=true + 游戏运行 + 界面已打开）',
    '',
    '用法:',
    '  node tools/cabinet_views_cdp.js <pageId>                计数断言（可见页签数 + 结构性事实）',
    '  node tools/cabinet_views_cdp.js --dd <pageId>           下拉断言（absolute+列容器内+贴列左下）',
    '  node tools/cabinet_views_cdp.js --mem <pageId> <tagId>  记忆断言（tagId=数字|other|0；作者选过后读态比对）',
    '',
    'pageId: ' + ids,
    '        （TradeUI 为 Trade 的别名；组别/期望语义见文件头注释）',
    '',
    '退出码: 0=通过  1=断言失败  2=用法错误  3=环境错误（9222 连不上/界面未打开）',
    'EXPECT 期望数回填与 K2/K3/K4 取证手册见本文件头部注释。',
  ].join('\n');
}

// 解析 argv → { type:'count'|'dd'|'mem', pageId, tagId }；出错返回 { err }
function parseArgs(argv) {
  const a = argv;
  if (!a.length) return { err: '缺少参数' };
  if (a[0] === '-h' || a[0] === '--help') return { help: true };
  let type, rest;
  if (a[0] === '--dd') { type = 'dd'; rest = a.slice(1); }
  else if (a[0] === '--mem') { type = 'mem'; rest = a.slice(1); }
  else if (a[0].startsWith('--')) return { err: '未知选项 ' + a[0] };
  else { type = 'count'; rest = a.slice(0); }
  if (type === 'mem') {
    if (rest.length < 2) return { err: '--mem 需要 <pageId> <tagId> 两个参数' };
    if (rest.length > 2) return { err: '--mem 参数过多：' + rest.join(' ') };
  } else {
    if (rest.length < 1) return { err: (type === 'dd' ? '--dd ' : '') + '需要 <pageId>' };
    if (rest.length > 1) return { err: '参数过多：' + rest.join(' ') };
  }
  let pageId = rest[0];
  if (ALIAS[pageId]) pageId = ALIAS[pageId];
  if (!PAGES[pageId]) return { err: '未知 pageId "' + rest[0] + '"（可用: ' + Object.keys(PAGES).join('/') + '；TradeUI 为别名）' };
  const out = { type, pageId };
  if (type === 'mem') {
    const t = rest[1];
    if (t !== 'other' && t !== '0' && !/^\d+$/.test(t)) return { err: 'tagId 非法 "' + t + '"（应为数字 | other | 0）' };
    out.tagId = t === 'other' ? 'other' : parseInt(t, 10);
    if (PAGES[pageId].group !== 'browser') return { err: '--mem 仅支持下拉组页面（Trade/DroneHub），' + pageId + ' 是 ' + PAGES[pageId].group + ' 组（固定视图无筛选记忆语义）' };
  }
  return out;
}

// 连接 9222 并按 iframe 元素 id 定位目标帧；返回 { page, frame, seen:[...] } 或抛错
async function findTargetFrame(browser, pageId) {
  const seen = [];
  for (const ctx of browser.contexts()) {
    for (const page of ctx.pages()) {
      for (const f of page.frames()) {
        if (f === page.mainFrame()) continue;
        // 帧自证身份：同源 iframe 里 window.frameElement 即父文档中的 <iframe> 元素
        const fid = await f.evaluate(() => {
          try { return (window.frameElement && window.frameElement.id) || ''; } catch (e) { return ''; }
        }).catch(() => '');
        if (!fid) continue;
        if (seen.indexOf(fid) < 0) seen.push(fid);
        if (fid === pageId) return { page, frame: f };
      }
    }
  }
  const err = new Error('NOT_FOUND');
  err.seen = seen;
  throw err;
}

// 断言收集器
function mkChecks() {
  const list = [];
  return {
    ok(name, pass, detail) { list.push({ name, pass, detail: detail || '' }); },
    skip(name, detail) { list.push({ name, pass: null, detail: detail || '' }); },
    list,
    failed: () => list.filter((c) => c.pass === false).length,
    print() {
      for (const c of list) {
        const tag = c.pass === null ? 'SKIP' : c.pass ? 'PASS' : 'FAIL';
        console.log('  [' + tag + '] ' + c.name + (c.detail ? '  → ' + c.detail : ''));
      }
    },
  };
}

async function main() {
  const parsed = parseArgs(process.argv.slice(2));
  if (parsed.err !== undefined || parsed.help) {
    if (parsed.help) { console.log(usage()); return 0; }
    console.error('用法错误: ' + parsed.err + '\n\n' + usage());
    return 2;
  }
  const { type, pageId } = parsed;
  const meta = PAGES[pageId];

  let chromium;
  try {
    chromium = require(NM_PATH).chromium;
  } catch (e) {
    console.error('环境错误: 找不到 playwright-core（' + NM_PATH + '）——依赖解析同 tools/devtools_cdp_test.js，请先补齐该目录');
    return 3;
  }

  let browser;
  try {
    browser = await chromium.connectOverCDP(CDP_URL);
  } catch (e) {
    console.error('环境错误: CDP 连接失败（' + CDP_URL + '）：' + e.message.split('\n')[0]);
    console.error('请检查: 1) 游戏是否在运行且已进档  2) ViewportProbe 配置 [DevTools] Enabled=true（仅开发机生效）  3) 9222 端口是否监听');
    return 3;
  }

  try {
    let target;
    try {
      target = await findTargetFrame(browser, pageId);
    } catch (e) {
      if (e.seen) {
        console.error('环境错误: 未找到 iframe id=' + pageId + '——目标界面当前未在游戏中打开（现有 iframe: ' + (e.seen.join(', ') || '无') + '）。请先在游戏里打开该界面再跑。');
      } else {
        console.error('环境错误: 帧枚举失败: ' + e.message);
      }
      return 3;
    }

    const p = await target.frame.evaluate(probeFrame);
    if (!p.found) {
      console.error('环境错误: iframe ' + pageId + ' 已找到，但 detectC 未命中页签栏（DOM 未就绪或非目标界面）——等界面渲染完成（页签出现）后再跑');
      return 3;
    }

    const checks = mkChecks();
    const title = pageId + '（' + meta.group + '组，detectC=' + p.kind + '，页签 ' + p.visibleCount + '/' + p.total + ' 可见）';
    console.log('== ' + (type === 'count' ? '计数断言' : type === 'dd' ? '下拉断言' : '记忆断言') + ': ' + title);

    // detectC 命中类型与页面档案一致（结构性事实：选择器同源判定不错位）
    checks.ok('detectC 命中 ' + meta.kind, p.kind === meta.kind, '实际=' + p.kind);

    if (type === 'count') {
      if (EXPECT[pageId] >= 0) {
        checks.ok('可见页签数 = ' + EXPECT[pageId], p.visibleCount === EXPECT[pageId], '实际=' + p.visibleCount);
      } else {
        checks.skip('可见页签数断言', 'EXPECT.' + pageId + '=-1 未回填（作者进档核对后填入再复跑）；本次 visible=' + p.visibleCount);
      }
      console.log('  可见页签明细: ' + (p.visible.length ? p.visible.join(' | ') : '(无)'));
      // 结构性事实（无论 EXPECT 是否回填都断言）
      if (meta.group === 'browser') {
        assertDdPresent(checks, p);
      } else {
        checks.ok('固定视图页无下拉触发器 .sltf-dd-btn', p.dd === null, p.dd ? '存在（text=' + p.dd.text + '）' : '');
        checks.ok('固定视图页无下拉弹层 .sltf-dd-pop', !p.pop, p.pop ? '存在' : '');
      }
    } else if (type === 'dd') {
      if (meta.group === 'browser') {
        assertDdPresent(checks, p);
        if (p.dd) {
          checks.ok('触发器已连接文档', p.dd.connected);
          checks.ok('触发器未隐藏（display≠none）', !p.dd.displayNone);
          checks.ok('未点开时无弹层残留 .sltf-dd-pop', !p.pop);
        }
      } else {
        checks.ok('固定视图页无下拉触发器 .sltf-dd-btn', p.dd === null, p.dd ? '存在（text=' + p.dd.text + '）' : '');
        checks.ok('固定视图页无下拉弹层 .sltf-dd-pop', !p.pop, p.pop ? '存在' : '');
      }
    } else { // mem
      const mem = await target.page.mainFrame().evaluate(probeMem, pageId);
      const expect = parsed.tagId;
      console.log('  localStorage sltf_sel_v3 = ' + (mem.raw === null ? '(null，从未选择过)' : mem.raw));
      if (mem.raw === null) {
        checks.ok('localStorage 存在 sltf_sel_v3（作者需先在该界面选择过）', false, '键不存在');
      } else if (expect === 'other') {
        checks.ok('记忆值 = "other"', mem.sel === 'other', '实际=' + JSON.stringify(mem.sel));
      } else {
        // 0=全部柜子：显式存 0 或键缺省（pageSel 兜底 0）都渲染"全部柜子"
        const pass = expect === 0 ? (mem.sel === 0 || mem.sel === null) : Number(mem.sel) === expect;
        checks.ok('记忆值 = ' + (expect === 0 ? '0（全部柜子）' : expect), pass, '实际=' + JSON.stringify(mem.sel));
      }
      if (p.dd) {
        const expectText = expect === 'other' ? '其他' : expect === 0 ? '全部柜子' : null;
        if (expectText) {
          checks.ok('触发器文本 = "' + expectText + '"', p.dd.text === expectText, '实际="' + p.dd.text + '"');
        } else {
          // 标签名来自 lib（脚本侧不可知），只断言"已选中态"并打印供作者核对
          checks.ok('触发器文本为选中标签名（非"全部柜子"）', !!p.dd.text && p.dd.text !== '全部柜子', '实际="' + p.dd.text + '"（请与所选标签名核对）');
        }
      } else {
        checks.ok('触发器存在（读文本比对）', false, '该帧无 .sltf-dd-btn——mod 未注入或界面重开未完成');
      }
    }

    checks.print();
    const failed = checks.failed();
    if (failed) {
      console.log('FAILED: ' + pageId + ' 有 ' + failed + ' 项断言未通过');
      return 1;
    }
    console.log('OK: ' + pageId + ' 断言全部通过' + (checks.list.some((c) => c.pass === null) ? '（含 SKIP 项，见上）' : ''));
    return 0;
  } finally {
    try { await browser.close(); } catch (e) {} // connectOverCDP 下仅断开连接，不影响游戏
  }
}

// browser 组的下拉存在性 + 位置硬事实（计数/dd 两模式共用）：2026-10-17 第三轮起触发器=列容器内
// absolute 贴左下（placeDD 动态锚定：从页签行向上取高>300 祖先为列容器并测量避让——窄左底按钮
// 让位/宽底栏上移/外来按钮卸载自动贴边；不再挂 body+fixed 视口角落——作者实锤会跑出面板外）。
// 距离阈值：距列左<120（避让 bt-view 类按钮后≈64，贴边=6）、距列底<60（避让负重条后≈cap高+8）
function assertDdPresent(checks, p) {
  const has = !!p.dd;
  checks.ok('下拉触发器 .sltf-dd-btn 存在（browser 页应有）', has, has ? 'text="' + p.dd.text + '" parent=' + p.dd.parentDesc : '未找到——mod 未注入或 lib 为空');
  if (has) {
    checks.ok('触发器 computed position=absolute', p.dd.pos === 'absolute', '实际=' + p.dd.pos);
    checks.ok('触发器挂在高度>300 的列容器内', p.dd.inCol, p.dd.colDesc ? '锚定列=' + p.dd.colDesc + '（parent=' + p.dd.parentDesc + '）' : '祖先链无高>300 列容器（parent=' + p.dd.parentDesc + '）');
    checks.ok('触发器贴列左下（距列左<120 且距列底<60，测量避让后落点）', p.dd.leftInCol < 120 && p.dd.bottomInCol < 60, '距列左=' + p.dd.leftInCol + 'px 距列底=' + p.dd.bottomInCol + 'px');
  }
}

main().then(
  (code) => { process.exit(code); },
  (e) => { console.error('FAIL:', (e && e.message) || e); process.exit(1); }
);
