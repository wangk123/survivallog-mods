using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ViewportProbe;

/// <summary>
/// 一次性诊断插件 v2（不发布）：柜子界面视口自适应取证。
/// 现象：烹饪/酿酒桶界面在 23 列置物架页签下溢出屏幕，但按 4K@PixelDensity2
/// （CSS 视口 1920x1080）静态计算面板约 1774px 本不该溢出——需要运行时实测。
///
/// v1 教训（本轮实证）：当前游戏版本转发进 Player.log 的是【Root 帧】console
/// （[3D WebView] [Web Console] 行，本会话 [Vue] Root Orchestrator 实锤），
/// iframe 页面 console 不转发——v1 把日志放页面上下文全瞎。
/// v2 做法：全部测量在 Root 上下文直读各 iframe 的同源 DOM（CabinetTagFilter
/// 生产验证过的通道），快照从 Root console.log 输出；内容有变化才打（去重）。
/// C# 侧静默出口全部打点（v1.3.2 红线）：WebView 拿到/为 null 各记一次状态迁移。
///
/// 每帧快照字段：页面名 | iframe 视口 | body 内联/计算 zoom | #app zoom |
/// 文档滚动宽高 | body 直接子元素(前4) 尺寸 | 首格子像素 | 格子总数。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ViewportProbePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.viewportprobe";
    public const string Name = "ViewportProbe";
    public const string Version = "0.2.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ProbeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
        var go = new GameObject("ViewportProbe.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeTicker>();
        Log.LogInfo("[ViewportProbe] v0.2.0 loaded（Root 上下文测量+Root console 输出）");
    }
}

internal sealed class ProbeTicker : MonoBehaviour
{
    public ProbeTicker(IntPtr ptr) : base(ptr) { }
    public ProbeTicker() { }

    private float _check;
    private bool _hadWeb;
    private int _injects;
    private bool _warned;

    private void Update()
    {
        _check += Time.deltaTime;
        if (_check < 5f) return;
        _check = 0f;
        var web = GetWebView();
        if (web == null)
        {
            if (_hadWeb)
            {
                _hadWeb = false;
                ViewportProbePlugin.Log.LogInfo("[ViewportProbe] 状态：WebView 失联（已注入 " + _injects + " 次）");
            }
            return;
        }
        if (!_hadWeb)
        {
            _hadWeb = true;
            ViewportProbePlugin.Log.LogInfo("[ViewportProbe] 状态：WebView 已就绪，开始注入");
        }
        try
        {
            web.ExecuteJavaScript(Bootstrap);
            _injects++;
        }
        catch (Exception e)
        {
            if (_warned) return;
            _warned = true;
            ViewportProbePlugin.Log.LogWarning("[ViewportProbe] 注入异常: " + e.Message);
        }
    }

    private static Vuplex.WebView.IWebView GetWebView()
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

    /// <summary>
    /// Root 上下文脚本 v3：v2 只量了各 iframe 内部（都装下了），但用户实拍显示菜谱栏被
    /// 屏幕切掉——补量 Root 层：Root 视口/滚动、#iframe-container 与 page-frame 的
    /// zoom/transform、iframe 元素在 Root 的视觉矩形 vs 其内部视口、cooking-system 与
    /// right-panel 的 getBoundingClientRect（视觉坐标，含 body zoom 缩放后）右/下边界。
    /// 只用单引号、无反斜杠。
    /// </summary>
    internal const string Bootstrap = @"
(function () {
  try {
    var parts = [];
    parts.push('ROOT vp' + innerWidth + 'x' + innerHeight
      + ' de' + document.documentElement.scrollWidth + 'x' + document.documentElement.scrollHeight
      + ' bz' + (document.body.style.zoom || '1')
      + ' cz' + getComputedStyle(document.body).zoom);
    var host = document.getElementById('iframe-container');
    if (host) {
      var hcs = getComputedStyle(host);
      parts.push('HOST cz' + hcs.zoom + ' tf' + (hcs.transform !== 'none' ? hcs.transform : '-'));
    }
    var fr = document.querySelectorAll('#iframe-container iframe');
    for (var i = 0; i < fr.length; i++) {
      var f = fr[i], w = null, doc = null;
      try { w = f.contentWindow; doc = f.contentDocument; } catch (e) { continue; }
      if (!w || !doc) continue;
      var r = f.getBoundingClientRect();
      var fcs = getComputedStyle(f);
      var m = (doc.location.pathname || '').match(/WebUI\/UI\/([A-Za-z0-9_]+)\//);
      var page = m ? m[1] : (doc.location.pathname || '?').slice(-24);
      var s = page + '[el' + Math.round(r.left) + ',' + Math.round(r.top)
        + ' ' + Math.round(r.width) + 'x' + Math.round(r.height)
        + '|vp' + w.innerWidth + 'x' + w.innerHeight
        + '|fz' + fcs.zoom + '|tf' + (fcs.transform !== 'none' ? 'Y' : '-');
      var sys = doc.querySelector('.cooking-system');
      if (sys) {
        var sr = sys.getBoundingClientRect();
        s += '|sys' + Math.round(sr.left) + ',' + Math.round(sr.top)
          + ' ' + Math.round(sr.width) + 'x' + Math.round(sr.height)
          + 'R' + Math.round(sr.right) + 'B' + Math.round(sr.bottom);
      }
      var rp = doc.querySelector('.right-panel');
      if (rp) {
        var rr = rp.getBoundingClientRect();
        s += '|rpR' + Math.round(rr.right) + 'w' + Math.round(rr.width);
      }
      parts.push(s + ']');
    }
    var k = parts.join(' ;; ');
    if (window.__VPPK === k) return;
    window.__VPPK = k;
    console.log('[VPP3] ' + k);
  } catch (e) { try { console.log('[VPP3] ERR ' + e.name); } catch (e2) {} }
})();
";
}
