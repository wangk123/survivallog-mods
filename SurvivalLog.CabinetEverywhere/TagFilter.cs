using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 筛选通道：读容器标签（CabinetCatalog.AllContainers 统一容器扫描）+ 标签库（Config_FurnitureTag），经
/// WebUILayer.canvasWebViewPrefab.WebView（Vuplex IWebView）ExecuteJavaScript 注入引导脚本并喂标签数据。
/// 引导脚本每周期幂等重注（WebView 崩溃恢复重建后自动重挂），数据每周期直推（体量极小，
/// 免维护"注入先后"时序）。JS 侧截读消息、按视图/标签隐藏 DOM，A/D 步进到被藏柜子时以
/// 页面同款事件伪造切换（不改任何下发的 tabs 数组/下标；全貌见 DESIGN.md 附录 A/C）。
/// </summary>
internal static class TagFilter
{
    private static readonly HashSet<string> Warned = new();
    private static bool _probeDone;
    private static bool _containerLogged;

    private static string _bootstrap;
    internal static string BootstrapJs => _bootstrap ??= ReadBootstrap();
    private static string ReadBootstrap()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("SurvivalLog.CabinetEverywhere.tagfilter.page.js");
        if (s == null) { WarnOnce("res", "内嵌资源 tagfilter.page.js 缺失"); return ""; }
        using var r = new System.IO.StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    internal static void Tick()
    {
        // General.Enabled 停用门（2026-10-24）：false=不再注入筛选 JS/不再喂标签数据（=不藏 UI）；
        // 已打开界面的存量注入随界面重开由原生重建清除，无需主动刷。
        if (CabinetEverywherePlugin.Enabled != null && !CabinetEverywherePlugin.Enabled.Value)
            return;

        // 容器计数日志（原 ContainerScan.Refresh 的首报，随扫描合一迁到消费侧）
        if (!_containerLogged && CabinetCatalog.AllContainers.Count > 0)
        {
            _containerLogged = true;
            CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere.筛选] 家中容器 " +
                CabinetCatalog.AllContainers.Count + " 个进入筛选名单（含冰箱/柜子/置物架/材料架）");
        }

        var web = GetWebView();
        if (web == null) return;
        try
        {
            web.ExecuteJavaScript(BootstrapJs);
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
                    CabinetEverywherePlugin.Log.LogInfo(
                        "[CabinetEverywhere.筛选] 同源探针: " + r +
                        (ok ? "" : "（非 OK：Root↔iframe 无 DOM 通道，筛选不生效，需换方案）"));
                }));
            web.ExecuteJavaScript(js, cb);
        }
        catch (Exception e)
        {
            CabinetEverywherePlugin.Log.LogInfo(
                "[CabinetEverywhere.筛选] 探针回调委托不可用（不影响功能，仅少了日志回读）: " + e.Message);
        }
    }

    /// <summary>
    /// 标签数据 JSON v3：owners = ownerId→{t:[tagId...], kind:'c'|'f'|'r'}（家中全部容器，含空标签；
    /// 材料架补"材料"伪标签保留在 t 数组内）；lib = 标签库（含 dim：0=物资，1/2/3=食物细分行，4=全部
    /// 食物——JS 侧把 1-4 汇总为一个"食物"选项；v3 追加 icon/color 原样字符串透出，JS 不解析只透传）；
    /// scenario = 页面 id→"food"|"material"|"browser"（cfg [Scenario] View_*，页面 id 用 T1 实测清单——
    /// Trade 不是 TradeUI；T5 仅下发，JS 消费在 T6）。
    /// </summary>
    private static string BuildDataJson()
    {
        var cm = SurvivalLog.ModShared.BagTable.Cm();
        if (cm == null) return null;

        int materialTagId = FindMaterialTagId(cm);

        var sb = new StringBuilder(4096);
        sb.Append("{\"owners\":{");
        var first = true;
        foreach (var c in CabinetCatalog.AllContainers)
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
                    string name = null, icon = null, color = null;
                    try
                    {
                        id = t.ID;
                        order = t.Order;
                        dim = t.Dim;
                        name = string.IsNullOrEmpty(t.TagName_Local) ? t.TagName : t.TagName_Local;
                        icon = t.IconKey;   // T1 实测：System.String（33 键 TAG_ICONS 之一）
                        color = t.Color;    // T1 实测：System.String（色值字符串，原样透出）
                    }
                    catch { continue; }
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(id).Append(",\"order\":").Append(order)
                      .Append(",\"dim\":").Append(dim)
                      .Append(",\"name\":\"").Append(EscapeJs(name)).Append("\"")
                      .Append(",\"icon\":\"").Append(EscapeJs(icon)).Append("\"")
                      .Append(",\"color\":\"").Append(EscapeJs(color)).Append("\"}");
                }
        }
        catch (Exception e)
        {
            WarnOnce("lib", "标签库读取失败: " + e.Message);
        }

        // scenario 段（v3 新增）：页面 id→视图字符串，页面 id 与 cfg 键后缀同为 T1 实测清单
        sb.Append("],\"scenario\":{");
        sb.Append("\"Cooking\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewCooking)).Append("\",");
        sb.Append("\"RatCage\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewRatCage)).Append("\",");
        sb.Append("\"BrewPanel\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewBrewPanel)).Append("\",");
        sb.Append("\"DyePanel\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewDyePanel)).Append("\",");
        sb.Append("\"Compressor\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewCompressor)).Append("\",");
        sb.Append("\"ToolTable\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewToolTable)).Append("\",");
        sb.Append("\"Shredder\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewShredder)).Append("\",");
        sb.Append("\"GreenhouseBuild\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewGreenhouseBuild)).Append("\",");
        sb.Append("\"Trade\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewTrade)).Append("\",");
        sb.Append("\"DroneHub\":\"").Append(ViewStr(CabinetEverywherePlugin.ViewDroneHub)).Append("\"");
        sb.Append("}}");
        return sb.ToString();
    }

    /// <summary>v3：owner 值对象形式 {"t":[tagId...],"kind":"c|f|r"}（Kind 直出 char；材料架伪标签仍追加在 t 内）。</summary>
    private static void AppendOwner(StringBuilder sb, CabinetCatalog.ContainerInst c, int materialTagId)
    {
        sb.Append('"').Append(c.OwnerId).Append("\":{\"t\":[");
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
        sb.Append("],\"kind\":\"").Append(c.Kind).Append("\"}");
    }

    /// <summary>cfg 枚举 → scenario JSON 值：Food→"food" / Material→"material" / Browser→"browser"。</summary>
    private static string ViewStr(ConfigEntry<ScenarioView> e)
    {
        var v = e?.Value ?? ScenarioView.Browser;
        return v switch
        {
            ScenarioView.Food => "food",
            ScenarioView.Material => "material",
            _ => "browser",
        };
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
            CabinetEverywherePlugin.Log.LogWarning("[CabinetEverywhere.筛选] " + msg);
    }
}
