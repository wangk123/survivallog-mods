using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;

namespace SurvivalLog.CabinetTagFilter;

/// <summary>
/// 筛选通道：读容器标签（ContainerScan）+ 标签库（Config_FurnitureTag），经
/// WebUILayer.canvasWebViewPrefab.WebView（Vuplex IWebView）ExecuteJavaScript 注入引导脚本并喂标签数据。
/// 引导脚本每周期幂等重注（WebView 崩溃恢复重建后自动重挂），数据每周期直推（体量极小，
/// 免维护"注入先后"时序）。JS 侧只读 tab 数据、只隐藏 DOM，不改下标不发事件。
/// </summary>
internal static class TagFilter
{
    private static readonly HashSet<string> Warned = new();
    private static bool _probeDone;

    internal static void Tick()
    {
        var web = GetWebView();
        if (web == null) return;
        try
        {
            web.ExecuteJavaScript(TagFilterJs.Bootstrap);
            var json = BuildDataJson();
            if (json != null)
                web.ExecuteJavaScript("window.__SLTF__&&window.__SLTF__.setData(" + json + ")");
            if (!_probeDone)
            {
                _probeDone = true;
                Probe(web);
            }

        }
        catch (Exception e)
        {
            WarnOnce("tick", "标签筛选注入失败: " + e.Message);
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

    private static void Probe(Vuplex.WebView.IWebView web)
    {
        var js = "(function(){try{var f=document.querySelector('#iframe-container iframe');"
               + "if(!f)return 'NOFRAME';"
               + "return f.contentDocument?('OK '+f.contentDocument.location.pathname):'NULLDOC'}"
               + "catch(e){return 'XORIGIN:'+e.name}})()";
        try
        {
            var cb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(
                new Action<string>(r =>
                {
                    var ok = r != null && r.StartsWith("OK", StringComparison.Ordinal);
                    CabinetTagFilterPlugin.Log.LogInfo(
                        "[CabinetTagFilter] 同源探针: " + r +
                        (ok ? "" : "（非 OK：Root↔iframe 无 DOM 通道，筛选不生效，需换方案）"));
                }));
            web.ExecuteJavaScript(js, cb);
        }
        catch (Exception e)
        {
            CabinetTagFilterPlugin.Log.LogInfo(
                "[CabinetTagFilter] 探针回调委托不可用（不影响功能，仅少了日志回读）: " + e.Message);
        }
    }

    /// <summary>
    /// 标签数据 JSON：owners = ownerId→[tagId...]（家中全部容器，含空标签；材料架补"材料"伪标签），
    /// lib = 标签库（含 dim：0=物资，1/2/3=食物细分行，4=全部食物——JS 侧把 1-4 汇总为一个"食物"选项）。
    /// </summary>
    private static string BuildDataJson()
    {
        var cm = SurvivalLog.ModShared.BagTable.Cm();
        if (cm == null) return null;

        int materialTagId = FindMaterialTagId(cm);

        var sb = new StringBuilder(4096);
        sb.Append("{\"owners\":{");
        var first = true;
        foreach (var c in ContainerScan.Home)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendOwner(sb, c, materialTagId);
        }
        sb.Append("},\"lib\":[");

        first = true;
        try
        {
            var dict = cm._Config_FurnitureTag_Dict;
            if (dict != null)
                foreach (var pair in dict)
                {
                    var t = pair.Value;
                    if (t == null) continue;
                    int id = 0, order = 0, dim = 0;
                    string name = null;
                    try
                    {
                        id = t.ID;
                        order = t.Order;
                        dim = t.Dim;
                        name = string.IsNullOrEmpty(t.TagName_Local) ? t.TagName : t.TagName_Local;
                    }
                    catch { continue; }
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(id).Append(",\"order\":").Append(order)
                      .Append(",\"dim\":").Append(dim)
                      .Append(",\"name\":\"").Append(EscapeJs(name)).Append("\"}");
                }
        }
        catch (Exception e)
        {
            WarnOnce("lib", "标签库读取失败: " + e.Message);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendOwner(StringBuilder sb, ContainerScan.ContainerInst c, int materialTagId)
    {
        sb.Append('"').Append(c.OwnerId).Append("\":[");
        var ids = c.TagIds;
        int n = ids?.Length ?? 0;
        // 材料架伪标签：不能打标签但只能放材料，筛"材料"时照常显示
        bool addMaterial = c.IsMaterialRack && materialTagId != 0 && (ids == null || Array.IndexOf(ids, materialTagId) < 0);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ids[i]);
        }
        if (addMaterial)
        {
            if (n > 0) sb.Append(',');
            sb.Append(materialTagId);
        }
        sb.Append(']');
    }

    /// <summary>"材料"标签的 ID：标签库 IconKey=="material" 的那条（JS 侧 TAG_NAME_FALLBACK 同款键）。</summary>
    private static int FindMaterialTagId(GameCore.HotUpdate.ConfigManager cm)
    {
        try
        {
            var dict = cm._Config_FurnitureTag_Dict;
            if (dict != null)
                foreach (var pair in dict)
                {
                    var t = pair.Value;
                    if (t == null) continue;
                    try { if (t.IconKey == "material") return t.ID; }
                    catch { }
                }
        }
        catch { }
        return 0;
    }

    private static string EscapeJs(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ') sb.Append(' ');
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void WarnOnce(string key, string msg)
    {
        if (Warned.Add(key))
            CabinetTagFilterPlugin.Log.LogWarning("[CabinetTagFilter] " + msg);
    }
}
