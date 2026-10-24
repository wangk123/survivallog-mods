using System;
using System.Reflection;

namespace SurvivalLog.PlanPick;

/// <summary>
/// Root 巡检注入器（每 2s 由 PlanTicker 调用，幂等）。ExecuteJavaScript 跑在 Root 页
/// 上下文；RogueSelect 是 Root.html 内的同源 iframe（DOM id = 'RogueSelect'）。
///
/// 每轮两段脚本（均条件式，避免每 tick 重复 eval/boot）：
/// 1) 安装：iframe 可见且 w.__PlanPickInstalled 未置（page.js 尾部自置该标记）才 eval
///    page.js —— iframe 被游戏重建/导航后标记随窗口消失，自动重装。
/// 2) 推送：payload 签名（长度+哈希）变化才调 w.__PlanPickBoot —— 防止弹层打开期间每 2s
///    重渲染打断搜索输入/滚动。签名标记存页面侧（iframe 重建即失效→自愈）；且跳过条件
///    额外要求入口按钮（#ppEntry）已在 DOM——page.js 的安装只在 boot 里重试，页面加载期
///    （body/.footer-right 未就绪）的 boot 不能作为"已送达"证据。
/// </summary>
internal static class Injector
{
    private const string ResourceName = "SurvivalLog.PlanPick.page.js";

    private static string _pageJsJson;   // page.js 的 JSON 字符串字面量（含首尾引号，装载时转义一次后复用）
    private static bool _pageJsWarned;
    private static bool _announced;      // 首轮就绪打点（目录+payload 统计）；WebView 失联时复位
    private static bool _warnedInstall;  // 注入/推送异常各只打一次（v1.3.2 红线：静默出口打点）
    private static bool _warnedPush;

    internal static void Tick()
    {
        var web = GetWebView();
        if (web == null) { _announced = false; return; }
        if (_pageJsJson == null && !LoadPageJs()) return;

        // 1) 条件安装 page.js（page.js 顶部 __PlanPickInstalled 守卫 + 尾部自置标记）
        var install =
            "(function(){var f=document.getElementById('RogueSelect');" +
            "if(!f||f.style.display==='none'||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(w.__PlanPickInstalled)return 1;" +
            "w.eval(" + _pageJsJson + ");return 2;})()";
        try { web.ExecuteJavaScript(install, null); }
        catch (Exception e) { WarnOnce(ref _warnedInstall, "注入异常", e); }

        // 2) 条件推送 payload（C# 已算好 state/reasons，JS 侧不推导）
        if (!PlanState.TryBuildPayload(out var json, out var logs)) return;
        var sig = json.Length + ":" + json.GetHashCode();
        var push =
            "(function(){var f=document.getElementById('RogueSelect');" +
            "if(!f||f.style.display==='none'||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(!w.__PlanPickBoot)return 0;" +
            "var s='" + sig + "';" +
            "if(w.__PlanPickSig===s&&w.document.getElementById('ppEntry'))return 2;" +
            "w.__PlanPickSig=s;w.__PlanPickBoot(" + json + ");return 1;})()";
        try { web.ExecuteJavaScript(push, null); }
        catch (Exception e) { WarnOnce(ref _warnedPush, "推送异常", e); }

        if (PlanPickPlugin.Verbose.Value) PlanPickPlugin.Log.LogInfo("[PlanPick] 巡检推送 sig=" + sig);

        if (!_announced)
        {
            _announced = true;
            if (!PlanPickPlugin.ListOnReady.Value) return;   // cfg 关闭则首轮不打统计
            foreach (var l in PlanCatalog.BuildLog) PlanPickPlugin.Log.LogInfo("[PlanPick] 目录: " + l);
            foreach (var l in logs) PlanPickPlugin.Log.LogInfo("[PlanPick] " + l);
        }
    }

    private static bool LoadPageJs()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                if (!_pageJsWarned)
                {
                    _pageJsWarned = true;
                    PlanPickPlugin.Log.LogWarning("[PlanPick] 内嵌资源 " + ResourceName + " 缺失；实际资源: " +
                        string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames()));
                }
                return false;
            }
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            _pageJsJson = "\"" + PlanState.JsEscapePublic(reader.ReadToEnd()) + "\"";
            return true;
        }
        catch (Exception e)
        {
            if (!_pageJsWarned) { _pageJsWarned = true; PlanPickPlugin.Log.LogWarning("[PlanPick] 读取 page.js 失败: " + e.Message); }
            return false;
        }
    }

    private static void WarnOnce(ref bool flag, string what, Exception e)
    {
        if (flag) return;
        flag = true;
        PlanPickPlugin.Log.LogWarning("[PlanPick] " + what + ": " + e.Message);
    }

    // ==== 键盘焦点交接桥（2026-10-24 装机三轮定案）====
    // 该游戏键盘不进 WebView（鼠标经 Vuplex 转发、键盘留在 Unity——实测搜索框打字无反应、
    // H 等游戏热键照触发）。IWebView.SetFocused(bool)（旧 Focus()/Blur() 已废弃）可把键盘
    // 交给网页。本桥以 0.3s 高频轮询 RogueSelect iframe 的 activeElement：是我们弹层里的
    // 输入框则 SetFocused(true)，失焦则 SetFocused(false) 交还——官方页行为零改动，
    // 键盘只在"点进自选搜索框"期间进网页。
    private static bool _kbHanded;          // 当前是否已把键盘交给网页（SetKeyboardEnabled true）
    private static DateTime _pendingSince;  // 本轮探针发出时刻（回调超时解卡用）
    private static bool _logHanded, _logReturned;
    private static string _pendingFocusCheck;   // 上轮结果（ExecuteJavaScript 回调异步写）

    /// <summary>焦点桥快通道（PlanTicker 每 0.3s 调；payload 巡检仍走 2s 的 Tick）。
    /// 键盘通道照抄官方：WebUILayer.SetKeyboardEnabled(bool)（工作台配方搜索同款，
    /// 内部写 CanvasWebViewPrefab 偏移 0x60 的键盘转发开关）——输入框聚焦开、失焦关，
    /// 仅动自己开的开关。</summary>
    internal static void FocusTick()
    {
        var layer = GetLayer();
        var web = layer?.canvasWebViewPrefab?.WebView;
        if (web == null) { if (_kbHanded) TryReturnKeyboard(layer); return; }
        if (_pendingFocusCheck != null)
        {
            // 回调超时解卡（>2s 视为该轮探针丢失，丢弃重发；持续丢只打一次）
            if ((DateTime.UtcNow - _pendingSince).TotalSeconds > 2)
            {
                _pendingFocusCheck = null;
                WarnOnce(ref _warnedInstall, "焦点探针回调超时", new Exception("callback not returned within 2s"));
            }
            else return;
        }

        _pendingFocusCheck = "w";                 // 占位：回调前非 null
        _pendingSince = DateTime.UtcNow;
        try
        {
            if (_focusCb == null)
                _focusCb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(new System.Action<string>(OnFocusProbeResult));
            // 两位探针：bit0=输入框聚焦；bit1=聚焦后已收到真键盘事件（键盘通道生效自证）。
            // 真键盘不可达时 C# 侧启用 Input.inputString 兜底转发（PlanTicker 每帧收集）。
            web.ExecuteJavaScript(
                "(function(){var f=document.getElementById('RogueSelect');if(!f||f.style.display==='none')return 0;" +
                "var d=f.contentDocument;if(!d)return 0;var i=d.activeElement;if(!i)return 0;" +
                "var inp=(i.tagName==='INPUT'||i.tagName==='TEXTAREA')?1:0;" +
                "return inp+(w9(d)?2:0);function w9(d){var w=d.defaultView;return w&&w.__ppRealKeys?1:0;}})()",
                _focusCb);
        }
        catch (Exception e)
        {
            _pendingFocusCheck = null;
            WarnOnce(ref _warnedInstall, "焦点桥轮询异常", e);
        }

        // 回调通常已同步/极快返回；处理结果（下一轮兜底再处理一次）
        ApplyFocusResult(layer, web);
    }

    /// <summary>ExecuteJavaScript 回调（il2cpp 委托，DelegateSupport 转换后缓存复用）：落焦点探针结果（"0".."3"）。
    /// 回调值可能是 JSON 序列化形态（数字带引号）——剥引号后解析；解析失败打点防静默。</summary>
    private static Il2CppSystem.Action<string> _focusCb;
    private static bool _logFirstCb;
    private static void OnFocusProbeResult(string r)
    {
        try
        {
            if (!_logFirstCb) { _logFirstCb = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 焦点探针回调首值: <" + (r ?? "null") + ">"); }
            _pendingFocusCheck = (r ?? "").Trim().Trim('"');
            _pendingSince = DateTime.UtcNow;
        }
        catch { _pendingFocusCheck = null; }
    }

    private static void ApplyFocusResult(GameCore.HotUpdate.ReduxUI.WebUILayer layer, Vuplex.WebView.IWebView web)
    {
        var r = _pendingFocusCheck;
        if (r == null) return;
        _pendingFocusCheck = null;
        int bits;
        if (!int.TryParse(r, out bits)) return;
        var focused = (bits & 1) != 0;
        var realKeys = (bits & 2) != 0;

        if (focused && !_kbHanded)
        {
            _kbHanded = true;
            _handCycles = 0; _forwardMode = false;
            try { layer.SetKeyboardEnabled(true); }
            catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick] SetKeyboardEnabled(true) 异常: " + e.Message); return; }
            if (!_logHanded) { _logHanded = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 键盘已交给网页输入（SetKeyboardEnabled true，官方通道）"); }
        }
        else if (focused && _kbHanded)
        {
            _handCycles++;
            // 交接 2 个周期（≈0.6s）后仍无真按键到达 → 官方键盘通道在此场景不送达 → 启用兜底转发
            if (!_forwardMode && _handCycles >= 2 && !realKeys)
            {
                _forwardMode = true;
                _fwdBuf = string.Empty;
                PlanPickPlugin.Log.LogWarning("[PlanPick] 官方键盘通道未送达按键，启用 Input.inputString 兜底转发");
            }
        }
        else if (!focused && _kbHanded)
        {
            _kbHanded = false; _forwardMode = false; _handCycles = 0;
            try { layer.SetKeyboardEnabled(false); } catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick] SetKeyboardEnabled(false) 异常: " + e.Message); }
            if (!_logReturned) { _logReturned = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 键盘已交还游戏（SetKeyboardEnabled false）"); }
        }
    }

    /// <summary>兜底转发缓冲（PlanTicker 每帧收集 Input.inputString，FocusTick 一次投递）。</summary>
    private static bool _forwardMode;
    private static int _handCycles;
    private static string _fwdBuf = string.Empty;

    internal static void CollectKey(char c)
    {
        if (!_forwardMode) return;
        if (c == '\r' || c == '\n' || c == '\0') return;   // 回车/空字符不入搜索框
        lock (_fwdLock) _fwdBuf += c;
    }
    private static readonly object _fwdLock = new();

    internal static void FlushKeys(Vuplex.WebView.IWebView web)
    {
        if (!_forwardMode) return;
        string s;
        lock (_fwdLock) { s = _fwdBuf; _fwdBuf = string.Empty; }
        if (string.IsNullOrEmpty(s)) return;
        var js =
            "(function(){var f=document.getElementById('RogueSelect');if(!f||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(!w.__ppType)return 0;" +
            "var d=f.contentDocument;if(!d||!d.activeElement||d.activeElement.id!=='ppSearch')return 0;" +
            "var cur=d.activeElement.value;" +
            "w.__ppType(cur+\"" + PlanState.JsEscapePublic(s) + "\");return 1;})()";
        try { web.ExecuteJavaScript(js, null); } catch { }
    }

    private static void TryReturnKeyboard(GameCore.HotUpdate.ReduxUI.WebUILayer layer)
    {
        _kbHanded = false;
        _pendingFocusCheck = null;
        try { layer?.SetKeyboardEnabled(false); } catch { }
    }

    /// <summary>ViewportProbe 同款取 Root WebView 路径（ReduxUISystem → WebUILayer → canvasWebViewPrefab）。</summary>
    internal static Vuplex.WebView.IWebView PeekWebView() => GetWebView();
    private static GameCore.HotUpdate.ReduxUI.WebUILayer GetLayer()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance?.GetWebUILayer();
        }
        catch { return null; }
    }
    private static Vuplex.WebView.IWebView GetWebView()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated) return null;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            return rus?.GetWebUILayer()?.canvasWebViewPrefab?.WebView;
        }
        catch { return null; }
    }
}
