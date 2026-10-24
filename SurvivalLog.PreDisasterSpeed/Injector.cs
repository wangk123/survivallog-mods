using System;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace SurvivalLog.PreDisasterSpeed;

/// <summary>Root 巡检注入器（每 2s，幂等，PlanPick 同款骨架）：
/// 1) page.js 装进 CoreUI0 iframe（__PdsInstalled 守卫，iframe 重建自愈重装）；
/// 2) 回执经 ExecuteJavaScript callback 读安装脚本返回值（0=不在/隐藏 >0=在）喂自愈；
/// 3) 章节倍率 payload 签名变化才推（防每 2s 重渲染）。点击回程走 Harmony prefix 桥（SpeedLink）。</summary>
internal static class Injector
{
    private const string ResourceName = "SurvivalLog.PreDisasterSpeed.page.js";

    private static string _pageJsJson;    // page.js 的 JSON 字符串字面量（装载时转义一次后复用）
    private static bool _pageJsWarned;
    private static bool _warnInstall;
    private static bool _warnPush;
    private static bool _announced;
    private static string _payloadSig = "";

    internal static void Tick()
    {
        if (!PreDisasterSpeedPlugin.Enabled.Value) return;
        var web = GetWebView();
        if (web == null) { SpeedLink.OnCoreUi0State(0); return; }   // WebView 失联视同不在（含灾后场景）
        if (_pageJsJson == null && !LoadPageJs()) return;

        // 1) 条件安装 + 状态回执（回执是异步的，晚一拍到自愈无碍——2s 巡检周期消化）
        var install =
            "(function(){var f=document.getElementById('CoreUI0');" +
            "if(!f||f.style.display==='none'||!f.contentWindow)return 0;" +
            "var w=f.contentWindow;if(w.__PdsInstalled)return 1;" +
            "w.eval(" + _pageJsJson + ");return 2;})()";
        // interop 重生成后 Il2CppSystem.Action 非 CLR 委托，lambda 直传不再编译：
        // 走 DelegateSupport.ConvertDelegate 编组（Task 4 报告 §3 预授权的降级写法，回调体不变）。
        try
        {
            // 新装页（回执 v==2）未收过 payload：重置签名门，下一 tick 对其强制重推（终审 I-1）
            web.ExecuteJavaScript(install, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(
                new Action<string>(s => { int v; if (int.TryParse(s, out v)) { SpeedLink.OnCoreUi0State(v); if (v == 2) _payloadSig = ""; } })));
        }
        catch (Exception e) { WarnOnce(ref _warnInstall, "注入异常", e); }

        // 2) payload：章节倍率（C# 读表，JS 不硬编码）；签名变化才推
        if (SpeedLink.TryGetChapterSpeeds(out var s1, out var s2))
        {
            var sig = s1 + ":" + s2;
            if (sig != _payloadSig)
            {
                _payloadSig = sig;
                var push =
                    "(function(){var f=document.getElementById('CoreUI0');" +
                    "if(!f||!f.contentWindow)return;var w=f.contentWindow;" +
                    "if(!w.__PdsBoot)return;w.__PdsBoot({\"s1\":" + s1 + ",\"s2\":" + s2 + "});})()";
                try { web.ExecuteJavaScript(push, null); }
                catch (Exception e) { WarnOnce(ref _warnPush, "推送异常", e); }
                if (PreDisasterSpeedPlugin.Verbose.Value)
                    PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] 推 payload s1=" + s1 + " s2=" + s2);
            }
        }

        // 3) 桥已改 Harmony prefix（WebUILayer.OnPageMessage 观察者，见 SpeedLink.PageMessagePatch），无需此处保活

        if (!_announced)
        {
            _announced = true;
            PreDisasterSpeedPlugin.Log.LogInfo("[PdsSpeed] 巡检就绪（CoreUI0 灾前 HUD，五键条待注入）");
        }
    }

    /// <summary>档位直推（装机五轮）：SetSpeed 成功后把当前档推给 page.js 高亮——
    /// 官方 TimeScaleMsg 在灾前手动变速时不保证推送，热键路径无本地态会露馅。CoreUI0 不在则静默空转。</summary>
    internal static void PushGear(int speed)
    {
        var web = GetWebView();
        if (web == null) return;
        var push =
            "(function(){var f=document.getElementById('CoreUI0');" +
            "if(!f||!f.contentWindow)return;var w=f.contentWindow;" +
            "if(w.__PdsGear)w.__PdsGear(" + speed + ");})()";
        try { web.ExecuteJavaScript(push, null); }
        catch (Exception e) { WarnOnce(ref _warnPush, "档位直推异常", e); }
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
                    PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 内嵌资源 " + ResourceName + " 缺失；实际资源: " +
                        string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames()));
                }
                return false;
            }
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            _pageJsJson = "\"" + JsEscape(reader.ReadToEnd()) + "\"";
            return true;
        }
        catch (Exception e)
        {
            if (!_pageJsWarned) { _pageJsWarned = true; PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] 读取 page.js 失败: " + e.Message); }
            return false;
        }
    }

    private static string JsEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static void WarnOnce(ref bool flag, string what, Exception e)
    {
        if (flag) return;
        flag = true;
        PreDisasterSpeedPlugin.Log.LogWarning("[PdsSpeed] " + what + ": " + e.Message);
    }

    /// <summary>ViewportProbe/PlanPick 同款取 Root WebView 路径（ReduxUISystem → WebUILayer → canvasWebViewPrefab）。</summary>
    internal static Vuplex.WebView.IWebView GetWebView()
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
