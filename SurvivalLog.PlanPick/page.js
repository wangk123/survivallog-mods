/* SurvivalLog.PlanPick — page.js（内嵌资源，C# 注入 RogueSelect 页）
 *
 * 唯一真源：prototype/index.html（v5 定稿）移植。差异（DESIGN.md §3）：
 *  - 无原型调试条；数据全部由 window.__PlanPickBoot(payload) 推送（C# 已算好
 *    state('bought'|'locked'|'ok')/reasons，JS 不推导解锁条件）；
 *    JS 侧仅算一项显示逻辑：state==='ok' 且 points < pay 时标"点数不足"。
 *  - 购买 = window.parent.postMessage({type:'CLICK_BUY',...}) 走官方链，
 *    本地乐观置 bought/扣点，等 C# 下一轮推送校正。
 *  - 不碰官方区 DOM（原生零接触）：只往 .footer-right 首位插入口按钮，
 *    弹层/确认/原因/toast 挂 body；官方页 CSS 变量（--card-bg 等）页内自带。
 *  - 幂等：重复注入脚本直接 return；重复 __PlanPickBoot 只更新数据。
 *    .footer-right 尚未就绪时入口按钮挂起，下次 boot 重试。
 */
(function () {
'use strict';
if (window.__PlanPickInstalled) return;   // 双注入守卫

/* ---------- 状态 ---------- */
const S = {
    plans: [], byId: {},
    points: 0, mult: 1, day: 0, role: 0, roleNames: {},
    tab: 'all', search: '', sub: null, subExpand: false, stateF: 'all', sort: 'id', rendered: 0,
    CHUNK: 120,
};
const $ = id => document.getElementById(id);
const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/* 自选价：今日候选原价，否则 原价 × 倍率 四舍五入取整（payload.mult，C# cfg）——与 C# 实扣
   (int)Math.Round(orig*mult) 对齐，小数倍率下展示/乐观扣点即实际扣费值 */
function payCost(p) { return p.today ? p.cost : Math.round(p.cost * S.mult); }

/* 展示态：payload.state 优先（bought/locked）；ok 但点数不足 → poor（唯一 JS 侧显示逻辑） */
function stateOf(p) {
    if (p.state === 'bought') return 'bought';
    if (p.state === 'locked') return 'locked';
    return S.points >= payCost(p) ? 'ok' : 'poor';
}

/* ---------- 样式（prototype v5 全部 .pp-* 规则；无调试条） ---------- */
const CSS = `
.footer-right { display: flex; align-items: center; gap: 10px; }
.pp-entry-btn {
    background: linear-gradient(180deg, rgba(64,52,30,.95), rgba(38,30,18,.95));
    border: 1px solid rgba(255, 200, 80, 0.45);
    color: var(--highlight);
    padding: 9px 22px; border-radius: 3px;
    font-size: 14px; font-family: var(--font-main);
    letter-spacing: 2px; cursor: pointer;
    transition: all .18s ease;
    box-shadow: 0 0 0 rgba(255,200,58,0);
}
.pp-entry-btn:hover {
    border-color: var(--highlight);
    box-shadow: 0 0 12px var(--highlight-glow);
    transform: translateY(-1px);
}
.pp-entry-btn .sub { font-size: 11px; color: var(--text-dim); letter-spacing: 0; margin-left: 6px; }

.pp-overlay {
    position: fixed; inset: 0; z-index: 300;
    display: none;
    background: rgba(8, 6, 4, 0.82);
    animation: ppFadeIn .2s ease-out;
}
.pp-overlay.open { display: flex; align-items: center; justify-content: center; }
@keyframes ppFadeIn { from { opacity: 0; } to { opacity: 1; } }

.pp-panel {
    width: min(1240px, 94vw); height: min(820px, 92vh);
    display: flex; flex-direction: column;
    background:
        url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 0.06 0'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>"),
        linear-gradient(180deg, rgba(34,29,22,.99), rgba(20,17,13,.99));
    background-size: 160px 160px, 100% 100%;
    border: 1px solid var(--border-light);
    border-radius: 6px;
    box-shadow: 0 24px 80px rgba(0,0,0,.75), inset 0 1px 0 rgba(255,230,170,.08);
    padding: 22px 26px 16px;
    animation: ppPanelIn .28s cubic-bezier(.2,.8,.2,1);
}
@keyframes ppPanelIn {
    from { opacity: 0; transform: scale(.97) translateY(8px); }
    to   { opacity: 1; transform: scale(1) translateY(0); }
}

.pp-head { display: flex; align-items: center; gap: 16px; margin-bottom: 6px; }
.pp-title {
    font-size: 22px; font-weight: 800; letter-spacing: 4px; color: #fff;
    text-shadow: 0 2px 10px rgba(0,0,0,.6);
}
.pp-title .sub {
    font-size: 12px; font-weight: 400; letter-spacing: 1px; color: var(--text-dim); margin-left: 10px;
}
.pp-head-right { margin-left: auto; display: flex; align-items: center; gap: 14px; }
.pp-points { font-size: 14px; color: var(--text-dim); }
.pp-points .own-num { font-size: 19px; }
.pp-points .mult-hint { font-size: 12px; color: #ffb84d; margin-left: 8px; }

.pp-toolbar { display: flex; align-items: center; gap: 8px; padding: 8px 0 10px; border-bottom: 1px solid var(--border-divider); }
.pp-tab {
    background: transparent; border: 1px solid transparent;
    color: var(--text-dim); font-size: 14px; font-family: var(--font-main);
    padding: 6px 14px; border-radius: 3px; cursor: pointer; letter-spacing: 1px;
    transition: all .15s ease;
}
.pp-tab:hover { color: var(--text-main); }
.pp-tab.active {
    color: var(--highlight);
    border-color: rgba(255,200,58,.45);
    background: rgba(255,200,58,.08);
}
.pp-tab .cnt { font-size: 11px; color: var(--text-meta); margin-left: 4px; font-family: var(--font-num); }
.pp-search {
    margin-left: auto;
    background: rgba(0,0,0,.35); border: 1px solid var(--border-card);
    color: var(--text-main); font-size: 13px; font-family: var(--font-main);
    padding: 6px 12px; border-radius: 3px; width: 200px; outline: none;
}
.pp-search:focus { border-color: rgba(255,200,58,.5); }
.pp-search::placeholder { color: var(--text-meta); }

.pp-toolbar2 {
    display: flex; align-items: flex-start; gap: 10px;
    padding: 6px 0 8px; border-bottom: 1px solid var(--border-divider);
}
.pp-subrow {
    position: relative;
    display: flex; flex-wrap: wrap; gap: 6px; flex: 1; min-width: 0;
    max-height: 28px;            /* 默认 1 行；expanded 撑开（行高 chip 22 + gap 6 = 28） */
    overflow: hidden;
    transition: max-height .25s ease;
}
.pp-subrow.expanded { max-height: 600px; overflow: visible; }
.pp-subrow .fade {
    position: absolute; right: 0; bottom: 0; height: 100%;
    width: 60px; pointer-events: none;
    background: linear-gradient(90deg, transparent, rgba(20,17,13,.95));
}
.pp-sub {
    flex-shrink: 0;
    background: rgba(0,0,0,.25); border: 1px solid var(--border-card);
    color: var(--text-dim); font-size: 12px; font-family: var(--font-main);
    padding: 3px 10px; border-radius: 12px; cursor: pointer; white-space: nowrap;
    transition: all .15s ease;
}
.pp-sub:hover { color: var(--text-main); }
.pp-sub.active {
    color: var(--highlight); border-color: rgba(255,200,58,.5); background: rgba(255,200,58,.08);
}
.pp-sub .cnt { font-size: 10px; color: var(--text-meta); margin-left: 3px; font-family: var(--font-num); }
.pp-submore {
    flex-shrink: 0; align-self: flex-start;
    background: transparent; border: 1px dashed rgba(220,180,100,.35);
    color: var(--text-dim); font-size: 12px; font-family: var(--font-main);
    padding: 3px 10px; border-radius: 12px; cursor: pointer; white-space: nowrap;
}
.pp-submore:hover { color: var(--highlight); border-color: rgba(255,200,58,.5); }
.pp-side { display: flex; align-items: center; gap: 8px; flex-shrink: 0; margin-left: auto; padding-top: 1px; }
.pp-state {
    background: transparent; border: 1px solid transparent;
    color: var(--text-meta); font-size: 12px; font-family: var(--font-main);
    padding: 3px 8px; border-radius: 10px; cursor: pointer; white-space: nowrap;
}
.pp-state.active { color: #9fe8a4; border-color: rgba(140,220,140,.45); background: rgba(140,220,140,.07); }
.pp-state.active.st-locked { color: #d8a24a; border-color: rgba(216,162,74,.5); background: rgba(216,162,74,.07); }
.pp-state.active.st-bought { color: var(--text-dim); border-color: var(--border-card); background: rgba(0,0,0,.25); }
.pp-sort {
    background: rgba(0,0,0,.35); border: 1px solid var(--border-card);
    color: var(--text-main); font-size: 12px; font-family: var(--font-main);
    padding: 3px 6px; border-radius: 10px; outline: none; cursor: pointer;
}
.pp-sort option { background: #241f18; color: #f0e8d8; }

.pp-list { flex: 1; overflow-y: auto; padding: 10px 8px 12px 2px; }
.pp-list::-webkit-scrollbar { width: 8px; }
.pp-list::-webkit-scrollbar-track { background: rgba(0,0,0,.25); border-radius: 4px; }
.pp-list::-webkit-scrollbar-thumb { background: rgba(220,180,100,.28); border-radius: 4px; }

.pp-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(348px, 1fr));
    gap: 10px; align-content: start;
}

.pp-card {
    position: relative;
    display: flex; gap: 12px; align-items: center;
    background: radial-gradient(ellipse at 50% -10%, rgba(255,210,120,.10), transparent 55%), var(--card-bg);
    border: 1px solid var(--border-card);
    border-radius: 5px; padding: 10px 12px;
    cursor: pointer; overflow: hidden;
    transition: transform .15s ease, border-color .15s ease, box-shadow .15s ease;
}
.pp-card:hover { border-color: rgba(255,193,7,.45); transform: translateY(-1px); }
.pp-card .icon-frame { width: 64px; height: 64px; }
.pp-card .card-info { flex: 1; min-width: 0; }
.pp-card .card-name { font-size: 14px; }
.pp-card .card-desc {
    font-size: 12px; line-height: 1.5; margin-top: 3px;
    display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
}
.pp-card .card-cost { font-size: 12px; margin-top: 5px; }
.pp-card .card-cost .num { font-size: 14px; }
.pp-card .cost-base { color: var(--text-meta); text-decoration: line-through; margin-right: 6px; font-family: var(--font-num); }
.pp-card .cost-x2 { color: #ffb84d; }

.pp-card.locked { background: var(--card-bg-locked); cursor: pointer; }
.pp-card.locked .icon-frame img { filter: grayscale(1) brightness(.55); }
.pp-card.locked .card-name { color: var(--text-dim); }
.pp-card.locked:hover { border-color: rgba(255,180,60,.35); }
.pp-lock-badge {
    flex-shrink: 0; font-size: 11px; letter-spacing: 2px;
    color: #d8a24a; border: 1px solid rgba(216,162,74,.5);
    padding: 3px 10px; border-radius: 3px; background: rgba(216,162,74,.08);
    align-self: center; white-space: nowrap;
}
.pp-card.bought { opacity: .55; cursor: default; }
.pp-card.bought:hover { transform: none; border-color: var(--border-card); }
.pp-card.tooexpensive .cost-x2 { color: #ff7a6b; }

.pp-badges { display: flex; gap: 6px; align-items: center; flex-shrink: 0; }
.pp-tag {
    font-size: 11px; padding: 2px 8px; border-radius: 3px; white-space: nowrap;
    border: 1px solid rgba(255,209,106,.45); color: var(--exclusive-accent);
    background: rgba(255,209,106,.08);
}
.pp-tag.today { border-color: rgba(140,220,140,.5); color: #9fe8a4; background: rgba(140,220,140,.08); }
.pp-tag.unpooled { border-color: rgba(160,160,220,.5); color: #b3b3ee; background: rgba(160,160,220,.08); }
.pp-tag.reget { border-color: rgba(160,200,230,.5); color: #a8d4ee; background: rgba(160,200,230,.08); }

.pp-more {
    display: block; margin: 14px auto 4px;
    background: rgba(0,0,0,.3); border: 1px solid var(--border-light);
    color: var(--text-dim); font-size: 13px; font-family: var(--font-main);
    padding: 7px 26px; border-radius: 3px; cursor: pointer;
}
.pp-more:hover { color: var(--highlight); border-color: var(--highlight); }

.pp-empty { text-align: center; color: var(--text-meta); padding: 60px 0; font-size: 14px; letter-spacing: 2px; }

.pp-why {
    position: fixed; z-index: 320; display: none;
    min-width: 240px; max-width: 340px;
    background: rgba(24,21,18,.98); border: 1px solid rgba(224,163,60,.45);
    border-radius: 6px; padding: 12px 14px;
    box-shadow: 0 10px 30px rgba(0,0,0,.6);
    font-size: 13px; color: #e8ddc8; line-height: 1.7;
}
.pp-why.open { display: block; }
.pp-why .why-title { color: #E0A33C; font-weight: 700; margin-bottom: 4px; letter-spacing: 1px; }
.pp-why .why-row { display: flex; gap: 6px; }
.pp-why .why-row::before { content: '·'; color: var(--text-meta); }

.pp-confirm {
    position: fixed; inset: 0; z-index: 330; display: none;
    align-items: center; justify-content: center;
    background: rgba(8,6,4,.6);
}
.pp-confirm.open { display: flex; }
.pp-confirm-box {
    width: 380px;
    background: linear-gradient(180deg, rgba(40,34,26,.99), rgba(24,20,15,.99));
    border: 1px solid rgba(255,200,80,.4); border-radius: 6px;
    padding: 22px 24px 18px;
    box-shadow: 0 18px 60px rgba(0,0,0,.7);
    animation: ppPanelIn .22s cubic-bezier(.2,.8,.2,1);
}
.pp-confirm-box h3 { font-size: 17px; letter-spacing: 2px; color: #fff; margin-bottom: 12px; }
.pp-confirm-box .row { display: flex; justify-content: space-between; font-size: 14px; color: var(--text-dim); padding: 5px 0; }
.pp-confirm-box .row .v { color: var(--text-main); font-family: var(--font-num); }
.pp-confirm-box .row .v.pay { color: #ffb84d; font-size: 17px; font-weight: 700; }
.pp-confirm-box .row.after { color: var(--text-meta); font-size: 12px; }
.pp-confirm-box .btns { display: flex; gap: 10px; justify-content: flex-end; }

#pp-toast {
    position: fixed; left: 50%; bottom: 96px; transform: translateX(-50%);
    z-index: 350; display: none;
    background: rgba(24,21,18,.97); border: 1px solid rgba(224,163,60,.5);
    color: #f0e8d8; font-size: 14px; letter-spacing: 1px;
    padding: 10px 26px; border-radius: 4px;
    box-shadow: 0 8px 24px rgba(0,0,0,.55);
    animation: ppFadeIn .15s ease-out;
}
#pp-toast.err { border-color: rgba(255,120,100,.55); color: #ffd9d2; }
`;

/* ---------- DOM 安装（幂等；.footer-right 未就绪则下次 boot 重试） ---------- */
const OVERLAY_HTML = `
<div class="pp-overlay" id="ppOverlay">
    <div class="pp-panel">
        <div class="pp-head">
            <div class="pp-title">自选生存规划<span class="sub" id="ppTitleSub"></span></div>
            <div class="pp-head-right">
                <div class="pp-points">剩余生存点：<span class="own-num" id="ppPoints">0</span><span class="mult-hint" id="ppMultHint"></span></div>
                <button class="ghost-btn" id="ppClose">关 闭</button>
            </div>
        </div>
        <div class="pp-toolbar" id="ppTabs"></div>
        <div class="pp-toolbar2" id="ppSubBar"></div>
        <div class="pp-list" id="ppList"></div>
    </div>
</div>
<div class="pp-why" id="ppWhy"></div>
<div class="pp-confirm" id="ppConfirm">
    <div class="pp-confirm-box">
        <h3 id="cfName"></h3>
        <div class="row"><span>原价</span><span class="v" id="cfBase">--</span></div>
        <div class="row"><span>自选价（×<span id="cfMult">2</span>）</span><span class="v pay" id="cfPay">--</span></div>
        <div class="row after"><span>购买后剩余</span><span class="v" id="cfAfter">--</span></div>
        <div class="btns">
            <button class="ghost-btn" id="cfCancel">取 消</button>
            <button class="primary-btn" id="cfOk">确 认 激 活</button>
        </div>
    </div>
</div>
<div id="pp-toast"></div>`;

let entryPending = true;
function install() {
    if (!document.body) return;                       // 页面未就绪：下次 boot 再试
    if (!$('pp-style')) {
        const st = document.createElement('style');
        st.id = 'pp-style'; st.textContent = CSS;
        (document.head || document.body).appendChild(st);
    }
    if (!$('pp-root')) {
        const root = document.createElement('div');
        root.id = 'pp-root'; root.innerHTML = OVERLAY_HTML;
        document.body.appendChild(root);
        wireGlobal();
    }
    if (entryPending) {
        const fr = document.querySelector('.footer-right');
        if (fr) {
            const e = document.createElement('button');
            e.className = 'pp-entry-btn'; e.id = 'ppEntry';
            e.innerHTML = '自选规划<span class="sub"></span>';
            e.onclick = () => {                                   // 每次打开都重渲染（计数/数据可能已换）
                $('ppOverlay').classList.add('open');
                renderTabs(); renderSubBar(); renderList(true);
            };
            fr.insertBefore(e, fr.firstChild);                   // 首位（"离开"之前）
            entryPending = false;
        }
    }
}

/* ---------- 渲染（与 prototype v5 逐项一致） ---------- */
const byState = p => S.stateF === 'all' || stateOf(p) === S.stateF;   // 状态筛选联动一二级计数
function renderTabs() {
    const cats = [{ key: 'all', name: '全部' }].concat(S.categories);
    $('ppTabs').innerHTML = cats.map(c => {
        const cnt = c.key === 'all'
            ? S.plans.filter(byState).length
            : S.plans.filter(p => p.cat === c.key && byState(p)).length;
        return '<button class="pp-tab' + (S.tab === c.key ? ' active' : '') + '" data-tab="' + c.key + '">' + c.name + '<span class="cnt">' + cnt + '</span></button>';
    }).join('') +
    '<input class="pp-search" id="ppSearch" placeholder="搜索名称 / 描述 / ID…" value="' + esc(S.search) + '">';
    $('ppTabs').querySelectorAll('.pp-tab').forEach(b => b.onclick = () => { S.tab = b.dataset.tab; S.sub = null; S.subExpand = false; renderTabs(); renderSubBar(); renderList(true); });
    const si = $('ppSearch');
    si.oninput = () => { S.search = si.value.trim(); renderList(true); };
}

function renderSubBar() {
    let list = S.plans;
    if (S.tab !== 'all') list = list.filter(p => p.cat === S.tab);
    if (S.stateF !== 'all') list = list.filter(byState);   // 联动：chips 计数只统计当前状态
    const cnt = new Map();
    let solo = 0, total = 0;
    list.forEach(p => { total++; if (p.sub === null) solo++; else cnt.set(p.sub, (cnt.get(p.sub) || 0) + 1); });
    // 当前选中的二级被状态筛没（计数 0 / chip 已隐藏）→ 自动回"全部"
    if (S.sub === '__solo' && solo === 0) S.sub = '__all';
    else if (S.sub && S.sub !== '__all' && S.sub !== '__solo' && !(cnt.get(S.sub) > 0)) S.sub = '__all';
    let html = '';
    if (S.tab !== 'all') {   // 全部 tab 二级无意义（系列跨类混合），只留状态+排序
        const subs = [...cnt.entries()].sort((a, b) => b[1] - a[1] || String(a[0]).localeCompare(String(b[0]), 'zh'));
        const chip = (k, label, c) => '<button class="pp-sub' + (S.sub === k ? ' active' : '') + '" data-sub="' + esc(k) + '">' + esc(label) + '<span class="cnt">' + c + '</span></button>';
        let chips = chip('__all', '全部', total);
        subs.forEach(([k, c]) => chips += chip(k, k, c));          // 计数 0 的系列已在 cnt 之外（不会出现 0 计数）
        if (solo) chips += chip('__solo', '独立规划', solo);       // 0 时整组隐藏
        // 自动换行、默认收起一行，超出才出现"更多"展开（永不出现横向滚动条）
        html = '<div class="pp-subrow' + (S.subExpand ? ' expanded' : '') + '" id="ppSubRow">' + chips + '</div>' +
               '<button class="pp-submore" id="ppSubMore" style="display:none">' + (S.subExpand ? '收起 ▴' : '更多 ▾') + '</button>';
    }
    const states = [['all', '全部'], ['ok', '可购'], ['locked', '未解锁'], ['bought', '已激活']];
    html += '<div class="pp-side">' +
        states.map(([k, n]) => '<button class="pp-state' + (k !== 'all' ? ' st-' + k : '') + (S.stateF === k ? ' active' : '') + '" data-state="' + k + '">' + n + '</button>').join('') +
        '<select class="pp-sort" id="ppSort">' +
        '<option value="id"' + (S.sort === 'id' ? ' selected' : '') + '>默认排序</option>' +
        '<option value="costAsc"' + (S.sort === 'costAsc' ? ' selected' : '') + '>价格 ↑</option>' +
        '<option value="costDesc"' + (S.sort === 'costDesc' ? ' selected' : '') + '>价格 ↓</option>' +
        '<option value="day"' + (S.sort === 'day' ? ' selected' : '') + '>解锁日</option>' +
        '</select></div>';
    $('ppSubBar').innerHTML = html;
    const row = $('ppSubRow');
    if (row) {
        const over = () => row.scrollHeight > row.clientHeight + 4;
        const more = $('ppSubMore');
        // 收起且溢出 → 显示"更多"+右缘渐隐提示；展开 → 全部可见且按钮变"收起"（保持可点）
        requestAnimationFrame(() => {
            const expanded = row.classList.contains('expanded');
            more.style.display = (expanded || over()) ? '' : 'none';
            const fade = row.querySelector('.fade');
            if (!expanded && over()) {
                if (!fade) { const f = document.createElement('div'); f.className = 'fade'; row.appendChild(f); }
            } else if (fade) fade.remove();
        });
        if (more) more.onclick = () => {
            S.subExpand = !S.subExpand;
            renderSubBar();
            if (!S.subExpand) $('ppList').scrollTop = 0;
        };
    }
    $('ppSubBar').querySelectorAll('.pp-sub').forEach(b => b.onclick = () => { S.sub = b.dataset.sub; renderSubBar(); renderList(true); });
    $('ppSubBar').querySelectorAll('.pp-state').forEach(b => b.onclick = () => { S.stateF = b.dataset.state; renderTabs(); renderSubBar(); renderList(true); });
    $('ppSort').onchange = e => { S.sort = e.target.value; renderList(true); };
}

function filtered() {
    let list = S.plans;
    if (S.tab !== 'all') list = list.filter(p => p.cat === S.tab);
    if (S.sub === '__solo') list = list.filter(p => p.sub === null);
    else if (S.sub && S.sub !== '__all') list = list.filter(p => p.sub === S.sub);
    if (S.stateF !== 'all') list = list.filter(p => stateOf(p) === S.stateF);
    if (S.search) {
        const q = S.search.toLowerCase();
        list = list.filter(p => p.name.toLowerCase().includes(q) || (p.desc || '').toLowerCase().includes(q) || String(p.id).includes(q));
    }
    const byCost = (a, b, dir) => (a.cost - b.cost) * dir || a.id - b.id;
    if (S.sort === 'costAsc') list = list.slice().sort((a, b) => byCost(a, b, 1));
    else if (S.sort === 'costDesc') list = list.slice().sort((a, b) => byCost(a, b, -1));
    else if (S.sort === 'day') list = list.slice().sort((a, b) => (a.unlockDay || 999) - (b.unlockDay || 999) || a.id - b.id);
    return list;
}

function cardHtml(p) {
    const st = stateOf(p);
    const price = payCost(p);
    const costHtml = p.today
        ? '<span class="num">' + p.cost + '</span><span style="color:#9fe8a4;font-size:11px;margin-left:6px">今日候选·原价</span>'
        : '<span class="cost-base">' + p.cost + '</span><span class="num cost-x2">×' + S.mult + ' = ' + price + '</span>';
    const badges =
        (p.today ? '<span class="pp-tag today">今日·原价</span>' : '') +
        (p.unlockDay === null ? '<span class="pp-tag unpooled">不入池</span>' : '') +
        (p.exclusive ? '<span class="pp-tag">专</span>' : '') +
        (p.isReget ? '<span class="pp-tag reget">可重购</span>' : '');
    const lockBadge = st === 'locked' ? '<div class="pp-lock-badge">未解锁</div>' : '';
    const cls = 'pp-card ' + (st === 'locked' ? 'locked' : st === 'bought' ? 'bought' : st === 'poor' ? 'tooexpensive' : '');
    return '<div class="' + cls + '" data-id="' + p.id + '">' +
        '<div class="icon-frame">' + (p.icon ? '<img loading="lazy" src="' + esc(p.icon) + '" alt="">' : '<span class="icon-placeholder">[icon]</span>') + '</div>' +
        '<div class="card-info"><div class="card-head"><div class="card-name">' + esc(p.name) + '</div><div class="pp-badges">' + badges + '</div></div>' +
        '<div class="card-desc">' + esc(p.desc) + '</div>' +
        '<div class="card-cost">需要生存点：' + costHtml + '</div></div>' +
        lockBadge +
        (st === 'bought' ? '<div class="stamp" style="right:-6px;top:8px">已激活</div>' : '') +
        '</div>';
}

function renderList(reset) {
    if (reset) S.rendered = S.CHUNK;
    const list = filtered();
    const slice = list.slice(0, S.rendered);
    $('ppList').innerHTML = slice.length
        ? '<div class="pp-grid">' + slice.map(cardHtml).join('') + '</div>' +
          (list.length > slice.length ? '<button class="pp-more" id="ppMore">加载更多（' + (list.length - slice.length) + '）</button>' : '')
        : '<div class="pp-empty">无匹配规划</div>';
    const more = $('ppMore');
    if (more) more.onclick = () => { S.rendered += S.CHUNK; renderList(false); };
    $('ppList').querySelectorAll('.pp-card').forEach(el => el.onclick = e => onCard(+el.dataset.id, el, e));
}

/* ---------- 交互 ---------- */
let confirmTarget = null;
function onCard(id, el) {
    const p = S.byId[id];
    if (!p) return;
    const st = stateOf(p);
    if (st === 'bought') { toast('已激活过该规划'); return; }
    if (st === 'locked') { showWhy(el, p.reasons || []); return; }
    if (st === 'poor') { toast('生存点不足（需 ' + payCost(p) + ' 点）', true); return; }
    // 可购 → 确认弹窗（三行：原价/自选价/购买后剩余，无提示句）
    confirmTarget = p;
    $('cfName').textContent = p.name;
    $('cfBase').textContent = p.cost + ' 点';
    $('cfMult').textContent = S.mult;
    $('cfPay').textContent = payCost(p) + ' 点';
    $('cfAfter').textContent = S.points + ' → ' + (S.points - payCost(p)) + ' 点';
    $('ppConfirm').classList.add('open');
}

function showWhy(el, reasons) {
    const w = $('ppWhy');
    w.innerHTML = '<div class="why-title">未解锁原因</div>' + reasons.map(r => '<div class="why-row">' + esc(r) + '</div>').join('');
    w.classList.add('open');
    const r = el.getBoundingClientRect();
    const ww = Math.min(340, Math.max(240, w.offsetWidth));
    let x = Math.min(Math.max(8, r.left - ww - 8), innerWidth - ww - 8);
    let y = Math.min(r.top, innerHeight - w.offsetHeight - 12);
    w.style.left = x + 'px'; w.style.top = Math.max(8, y) + 'px';
    clearTimeout(showWhy._t);
    showWhy._t = setTimeout(() => w.classList.remove('open'), 3800);
}

let toastT = 0;
function toast(msg, err) {
    const t = $('pp-toast');
    t.textContent = msg;
    t.className = err ? 'err' : '';
    t.style.display = 'block';
    clearTimeout(toastT);
    toastT = setTimeout(() => t.style.display = 'none', 2200);
}

function wireGlobal() {
    $('cfOk').onclick = () => {
        const p = confirmTarget; if (!p) return;
        const pay = payCost(p);
        S.points -= pay;                       // 本地乐观更新，等 C# 下轮推送校正
        p.state = 'bought'; p.reasons = [];
        $('ppConfirm').classList.remove('open');
        // 走官方购买链（照抄 webui-core sendMessage；OnBuySurvivalPlan 不校验候选名单）
        window.parent.postMessage({ type: 'CLICK_BUY', data: { id: p.id }, sourcePageId: 'RogueSelect' }, '*');
        toast('已激活「' + p.name + '」（-' + pay + ' 点）');
        syncAll();
    };
    $('cfCancel').onclick = () => $('ppConfirm').classList.remove('open');
    $('ppClose').onclick = () => $('ppOverlay').classList.remove('open');
    $('ppOverlay').onclick = e => { if (e.target === $('ppOverlay')) $('ppOverlay').classList.remove('open'); };
    document.addEventListener('click', e => { if (!e.target.closest('.pp-card')) $('ppWhy').classList.remove('open'); }, true);
}

/* ---------- 数据同步 ---------- */
function syncAll() {
    const o = $('ppOverlay'); if (!o) return;
    $('ppPoints').textContent = S.points;
    $('ppMultHint').textContent = '自选消耗 ×' + S.mult;
    $('ppTitleSub').textContent = '全 ' + S.plans.length + ' 项 · 未解锁项点击查看原因';
    const sub = document.querySelector('#ppEntry .sub');
    if (sub) sub.textContent = '×' + S.mult;
    if (o.classList.contains('open')) { renderTabs(); renderSubBar(); renderList(false); }
}

/* ---------- 唯一入口：C# 每轮推送调用（可反复） ---------- */
window.__PlanPickBoot = function (payload) {
    if (!payload || !payload.plans) return;
    S.plans = payload.plans;
    S.categories = payload.categories || [];
    S.byId = {};
    S.plans.forEach(p => { S.byId[p.id] = p; });
    S.points = payload.points | 0;
    S.mult = Math.max(1, +payload.mult || 1);
    S.day = payload.day | 0;
    S.role = payload.role | 0;
    S.roleNames = payload.roleNames || {};
    install();
    syncAll();
};

window.__PlanPickInstalled = true;
})();
