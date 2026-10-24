// PreDisasterSpeed page.js —— 注入 CoreUI0（灾前 HUD）的官方五键倍速条（样式照抄 CoreUI1 speed-controls）。
// 约定（DESIGN §4.1/§4.3）：
//  - 安装守卫 window.__PdsInstalled；iframe 被游戏重建后标记消失，C# 巡检自动重装。
//  - C# 经 window.__PdsBoot({s1,s2}) 推灾前章节倍率（JS 不硬编码）。
//  - 点击 → window.parent.postMessage({type:'PDS_SPEED',data:{speed},sourcePageId:'CoreUI0'})
//    Root 对任意 type 无条件中继为 vuplex 协议(3)，C# 桥订阅 MessageEmitted 落地（DESIGN §3.5）。
//  - 高亮：官方 WebUI_CoreUI0_TimeScaleMsg 的 timeScale 映射档位；慢速档(4)不改 scale，
//    用本地点击态，收到任一官方 scale 同步即重算。临时时标（睡眠/空闲 0.3）全灭。
//  - 按钮为 <button>，自动进 webui-core collectRects 交互矩形上报（选择器含 button），点击可达。
(function () {
    if (window.__PdsInstalled) return;
    window.__PdsInstalled = true;

    var payload = null;              // {s1, s2}

    var GEARS = [
        { speed: 4, label: '慢速', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M9 4v11l8-5.5z"/><rect x="5" y="17.5" width="14" height="2" rx="1"/></svg>' },
        { speed: 1, label: '原速', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>' },
        { speed: 2, label: '二倍', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z"/></svg>' },
        { speed: 3, label: '三倍', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M2 18l6-6-6-6v12zm7-12v12l6-6-6-6zm7 0v12l6-6-6-6z"/></svg>' },
        { speed: 0, label: '暂停', svg: '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M6 5h4v14H6zm8 0h4v14h-4z"/></svg>' }
    ];

    function send(speed) {
        window.parent.postMessage({ type: 'PDS_SPEED', data: { speed: speed }, sourcePageId: 'CoreUI0' }, '*');
    }

    function setActive(speed) {
        var btns = document.querySelectorAll('#pdsSpeed .speed-btn');
        for (var i = 0; i < btns.length; i++) btns[i].classList.remove('active');
        if (speed < 0) return;
        var b = document.querySelector('#pdsSpeed .pds-gear-' + speed);
        if (b) b.classList.add('active');
    }

    function scaleToSpeed(v) {
        var s1 = payload ? payload.s1 : 2, s2 = payload ? payload.s2 : 32;
        if (v < 0.01) return 0;
        if (Math.abs(v - 1) < 0.01) return 1;
        if (Math.abs(v - s1) < 0.01) return 2;
        if (Math.abs(v - s2) < 0.01) return 3;
        return -1;
    }

    window.addEventListener('message', function (ev) {
        var m = ev.data;
        if (!m || typeof m.type !== 'string') return;
        if (m.type === 'WebUI_CoreUI0_TimeScaleMsg' && m.data && typeof m.data.timeScale === 'number') {
            setActive(scaleToSpeed(m.data.timeScale));
        }
    });

    window.__PdsBoot = function (p) {
        if (p && typeof p.s1 === 'number' && typeof p.s2 === 'number') payload = p;
    };

    function mount() {
        var host = document.querySelector('.top-center');
        if (!host) return false;
        if (document.getElementById('pdsSpeed')) return true;
        var css = document.createElement('style');
        css.textContent = ''
            + '#pdsSpeed{position:relative;display:flex;gap:4px;background-color:rgba(30,30,30,0.85);padding:2px;border-radius:6px;border:1px solid rgba(255,255,255,0.12);width:fit-content;}'
            + '#pdsSpeed .speed-btn{position:relative;width:30px;height:30px;background:transparent;border:1px solid transparent;border-radius:4px;color:rgba(255,255,255,0.6);cursor:pointer;transition:all .2s;padding:0;display:flex;align-items:center;justify-content:center;line-height:1;}'
            + '#pdsSpeed .speed-btn:hover{background-color:rgba(255,255,255,0.1);color:#fff;}'
            + '#pdsSpeed .speed-btn.active{background-color:rgba(255,193,7,0.15);color:#FFC107;border-color:rgba(255,193,7,0.3);font-weight:bold;box-shadow:inset 0 0 4px rgba(255,193,7,0.2);}'
            + '#pdsSpeed .speed-tooltip{position:absolute;bottom:130%;left:50%;transform:translateX(-50%);background-color:rgba(20,20,20,0.95);border:1px solid rgba(255,255,255,0.2);border-radius:4px;padding:6px 10px;color:#eee;font-size:12px;white-space:nowrap;pointer-events:none;opacity:0;transition:opacity .2s;}'
            + '#pdsSpeed .speed-btn:hover .speed-tooltip{opacity:1;}';
        (document.head || document.documentElement).appendChild(css);
        var strip = document.createElement('div');
        strip.id = 'pdsSpeed';
        GEARS.forEach(function (g) {
            var b = document.createElement('button');
            b.className = 'speed-btn pds-gear-' + g.speed;
            b.innerHTML = g.svg + '<div class="speed-tooltip">' + g.label + '</div>';
            b.addEventListener('click', function () { send(g.speed); setActive(g.speed); });
            strip.appendChild(b);
        });
        host.appendChild(strip);
        return true;
    }

    // CoreUI0 body 未就绪时重试（250ms×40=10s）；挂载后交互矩形由 webui-core 的 MutationObserver 自动重报
    var tries = 0;
    var timer = setInterval(function () {
        if (mount() || ++tries >= 40) clearInterval(timer);
    }, 250);
    mount();
})();
