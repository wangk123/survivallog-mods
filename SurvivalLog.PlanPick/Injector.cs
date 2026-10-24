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

    // ==== 键盘门（SetKeyboardEnabled——装机实证英文通道的唯一开关）+ 中文 IME 转运 ====
    // 英文/裸键：规划页激活期间 WebUILayer.SetKeyboardEnabled(true)（S1 实证：开=英文通且 H 不触发；
    // 删=全灭——S3 实证）。中文：官方机制=Unity inputString 转运（S3 实证：官方工作台打中文时
    // inputString 出组合字）——OfferImeChars 收非 ASCII+退格，FlushIme 无条件冲刷（页面侧守卫）。
    private static bool _kbHanded;
    private static bool _logHanded, _logReturned, _warnedNoKey;

    /// <summary>0.3s 快通道：键盘门 + IME 转运冲刷。</summary>
    internal static void FocusTick()
    {
        var layer = GetLayer();
        if (layer == null) { _kbHanded = false; return; }
        bool active = SafePageActive(layer, "RogueSelect") || SafePageActive(layer, "UI_RogueSelect") || SafePageActive(layer, "WebUI_RogueSelect");
        if (active && !_kbHanded)
        {
            _kbHanded = true;
            try { layer.SetKeyboardEnabled(true); }
            catch (Exception e) { _kbHanded = false; PlanPickPlugin.Log.LogWarning("[PlanPick] SetKeyboardEnabled(true) 异常: " + e.Message); return; }
            if (!_logHanded) { _logHanded = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 规划页激活，键盘交给网页（SetKeyboardEnabled true）"); }
        }
        else if (!active && _kbHanded)
        {
            _kbHanded = false;
            try { layer.SetKeyboardEnabled(false); } catch (Exception e) { PlanPickPlugin.Log.LogWarning("[PlanPick] SetKeyboardEnabled(false) 异常: " + e.Message); }
            if (!_logReturned) { _logReturned = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 规划页关闭，键盘交还游戏"); }
        }
        FlushIme(layer);
    }

    private static bool SafePageActive(GameCore.HotUpdate.ReduxUI.WebUILayer layer, string key)
    {
        try { return layer.IsPageActive(key); } catch { return false; }
    }

    // ==== 中文 IME 转运兜底（与原生 IME 互斥：原生生效时 inputString 无组合字，转运自动闲置）====
    private static readonly object _imeLock = new();
    private static string _imeBuf = string.Empty;
    private static bool _logImeFirst;

    internal static void OfferImeChars(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        lock (_imeLock)
        {
            foreach (var c in s)
                if (c >= 128 && c != '\u3000') _imeBuf += c;   // 汉字/全角符号；全角空格过滤
        }
    }

    private static void FlushIme(GameCore.HotUpdate.ReduxUI.WebUILayer layer)
    {
        string s;
        lock (_imeLock) { s = _imeBuf; _imeBuf = string.Empty; }
        if (string.IsNullOrEmpty(s)) return;
        var web = layer?.canvasWebViewPrefab?.WebView;
        if (web == null) return;
        var js =
            "(function(){var f=document.getElementById('RogueSelect');if(!f||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(!w.__ppIme)return 0;" +
            "w.__ppIme(\"" + PlanState.JsEscapePublic(s) + "\");return 1;})()";
        try
        {
            web.ExecuteJavaScript(js, null);
            if (!_logImeFirst) { _logImeFirst = true; PlanPickPlugin.Log.LogInfo("[PlanPick] 中文 IME 转运首投: " + s); }
        }
        catch (Exception e) { WarnOnce(ref _warnedInstall, "IME 转运异常", e); }
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
