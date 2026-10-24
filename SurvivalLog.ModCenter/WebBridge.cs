using System;
using System.Collections.Generic;

namespace SurvivalLog.ModCenter;

/// <summary>
/// WebView 通道（任务 5）：ReduxUISystem → GetWebUILayer() → canvasWebViewPrefab.WebView
/// （Vuplex IWebView），ExecuteJavaScript 落在 Root 文档上下文。
/// GetWebView 原样移植自 CabinetEverywhere/TagFilter.cs:64-77（该通道线上长期实证）；
/// Eval 回调经 DelegateSupport.ConvertDelegate 包装为 Il2CppSystem.Action&lt;string&gt;
/// （手法见 CabinetEverywhere DESIGN.md 附录 C5：命名空间无 InteropTypes 后缀，
/// 包装不可用时仅少日志回读，不影响注入本身）。
/// 全部异常只 WarnOnce 不抛——WebView 未就绪/崩溃重建期间轮询永不中断（TagFilter.cs:253-257 模式）。
/// </summary>
internal static class WebBridge
{
    private static readonly HashSet<string> Warned = new();

    /// <summary>取 Root WebView；未就绪/已销毁返回 null（调用方跳过本轮即可）。</summary>
    internal static Vuplex.WebView.IWebView GetWebView()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.IsInstanceCreated)
                return null;
            var rus = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ReduxUI.ReduxUISystem>.Instance;
            var layer = rus?.GetWebUILayer();
            var prefab = layer?.canvasWebViewPrefab;
            return prefab?.WebView;
        }
        catch { return null; }
    }

    /// <summary>执行 JS（无回调；注入与 C#→JS 推送共用的主通道——Root 上下文）。异常只 Warn 不中断轮询。</summary>
    internal static void Exec(Vuplex.WebView.IWebView web, string js)
    {
        try { web.ExecuteJavaScript(js); }
        catch (Exception e) { WarnOnce("exec", "ExecuteJavaScript 失败（WebView 重建中？）: " + e.Message); }
    }

    /// <summary>
    /// 执行 JS 并回读结果字符串。任务 6 起承载 JS→C# 消息回读（outbox drain）；回调经
    /// DelegateSupport 包装成 Il2Cpp 委托交给 Vuplex；包装抛异常时只 Warn 降级（消息该轮丢失、
    /// 下轮补上，不影响注入本身——同源探针同款降级语义），返回 false 供调用方感知。
    /// </summary>
    internal static bool Eval(Vuplex.WebView.IWebView web, string js, Action<string> cb)
    {
        try
        {
            var wrapped = Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<Il2CppSystem.Action<string>>(cb);
            web.ExecuteJavaScript(js, wrapped);
            return true;
        }
        catch (Exception e)
        {
            WarnOnce("eval", "Eval 回调委托不可用（仅少日志回读，不影响注入）: " + e.Message);
            return false;
        }
    }

    private static void WarnOnce(string key, string msg)
    {
        if (Warned.Add(key))
            ModCenterPlugin.Log("warn " + msg);
    }
}
