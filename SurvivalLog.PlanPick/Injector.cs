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
    private static bool _kbHanded;          // 当前是否已 SetFocused(true)
    private static bool _logHanded, _logReturned;
    private static string _pendingFocusCheck;   // 上轮结果（ExecuteJavaScript 回调异步写）

    /// <summary>焦点桥快通道（PlanTicker 每 0.3s 调；payload 巡检仍走 2s 的 Tick）。</summary>
    internal static void FocusTick()
    {
        var web = GetWebView();
        if (web == null) { if (_kbHanded) TryReturnKeyboard(web); return; }
        if (_pendingFocusCheck != null) return;   // 上轮回调未回，跳过本轮（防堆积）

        _pendingFocusCheck = "w";                 // 占位：回调前非 null
        try
        {
            if (_focusCb == null)
                _focusCb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(new System.Action<string>(OnFocusProbeResult));
            web.ExecuteJavaScript(
                "(function(){var f=document.getElementById('RogueSelect');if(!f||f.style.display==='none')return 0;" +
                "var d=f.contentDocument;if(!d)return 0;var i=d.activeElement;" +
                "return i&&(i.tagName==='INPUT'||i.tagName==='TEXTAREA')?1:0;})()",
                _focusCb);
        }
        catch (Exception e)
        {
            _pendingFocusCheck = null;
            WarnOnce(ref _warnedInstall, "焦点桥轮询异常", e);
        }

        // 回调通常已同步/极快返回；处理结果（下一轮兜底再处理一次）
        ApplyFocusResult(web);
    }

    /// <summary>ExecuteJavaScript 回调（il2cpp 委托，DelegateSupport 转换后缓存复用）：落焦点探针结果。</summary>
    private static Il2CppSystem.Action<string> _focusCb;
    private static void OnFocusProbeResult(string r)
    {
        try
        {
            var focusedNow = string.Equals(r, "1", StringComparison.Ordinal) || string.Equals(r, "true", StringComparison.OrdinalIgnoreCase);
            _pendingFocusCheck = focusedNow ? "1" : "0";
        }
        catch { _pendingFocusCheck = null; }
    }

    private static void ApplyFocusResult(Vuplex.WebView.IWebView web)
    {
        var r = _pendingFocusCheck;
        if (r == null) return;
        _pendingFocusCheck = null;
        if (r == "1" && !_kbHanded)
        {
            _kbHanded = true;
            try { web.SetFocused(true); } catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick] SetFocused(true) 异常: " + e.Message); return; }
            if (!_logHanded) { _logHanded = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 键盘已交给网页输入（SetFocused true）"); }
        }
        else if (r != "1" && _kbHanded)
        {
            _kbHanded = false;
            try { web.SetFocused(false); } catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick] SetFocused(false) 异常: " + e.Message); }
            if (!_logReturned) { _logReturned = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 键盘已交还游戏（SetFocused false）"); }
        }
    }

    private static void TryReturnKeyboard(Vuplex.WebView.IWebView web)
    {
        _kbHanded = false;
        _pendingFocusCheck = null;
        try { web?.SetFocused(false); } catch { }
    }

    /// <summary>ViewportProbe 同款取 Root WebView 路径（ReduxUISystem → WebUILayer → canvasWebViewPrefab）。</summary>
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
