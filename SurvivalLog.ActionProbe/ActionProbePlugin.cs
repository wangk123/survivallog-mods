using System;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ActionProbe;

/// <summary>
/// 一次性诊断插件 v6（不发布）：实证"按耗时收益"与"引用轴"。
/// dump 六段到 BepInEx\EffectDump.txt：
///   EFFECT   Config_Effect 全表（Start/Interval/End 全字段）
///   ACTION   Config_Action（ID|名|类型|During|EffectConfigID|AttributeHold）
///   FUNC     Config_FurnitureFunc.ActionIds（家具按钮引用轴）
///   ITEM     Config_Item.UseAction（物品引用轴）
///   THINK    ThinkOpenAction.ActionID + ThinkActionType 随机/自选动作（AI 引用轴）
///   DYED     _dyedUseActionToBase（染色动作映射）
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ActionProbePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.actionprobe";
    public const string Name = "ActionProbe";
    public const string Version = "0.6.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ProbeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
        var go = new GameObject("ActionProbe.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeTicker>();
        Log.LogInfo("[ActionProbe] v6 loaded");
    }
}

internal sealed class ProbeTicker : MonoBehaviour
{
    public ProbeTicker(IntPtr ptr) : base(ptr) { }
    public ProbeTicker() { }

    private float _wait;
    private bool _done;

    public void Update()
    {
        if (_done) return;
        _wait += Time.deltaTime;
        if (_wait < 4f) return;

        GameCore.HotUpdate.ConfigManager cm = null;
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return;
            cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return; }
        if (cm == null) return;
        try
        {
            if (cm._Config_Action_Dict == null || cm._Config_Action_Dict.Count == 0) return;
            if (cm._Config_Effect_Dict == null || cm._Config_Effect_Dict.Count == 0) return;
        }
        catch { return; }

        _done = true;
        var path = System.IO.Path.Combine(Paths.BepInExRootPath, "EffectDump.txt");
        try
        {
            using var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false));
            DumpEffects(cm, w);
            DumpActions(cm, w);
            DumpFuncs(cm, w);
            DumpItems(cm, w);
            DumpThink(cm, w);
            DumpDyed(cm, w);
            ActionProbePlugin.Log.LogInfo($"[ActionProbe] DUMP OK -> {path}");
        }
        catch (Exception e)
        {
            ActionProbePlugin.Log.LogError("[ActionProbe] dump 失败: " + e);
        }
    }

    private static string S(string s) => s ?? "";
    private static string F(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void DumpEffects(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== EFFECT ID|SStart|MStart|StStart|HStart|VStart|AInt|SInt|MInt|StInt|HInt|VInt|BInt|BSInt|BMInt|BStInt|BHInt|BVInt|SEnd|MEnd|StEnd|HEnd|VEnd ==");
        try
        {
            foreach (var kv in cm._Config_Effect_Dict)
            {
                var e = kv.Value;
                if (e == null) continue;
                w.WriteLine(string.Join("|", new[]
                {
                    kv.Key.ToString(),
                    F(e.ItemSatietyStart), F(e.ItemMoraleStart), F(e.ItemStaminaStart), F(e.ItemHealthStart), F(e.ItemVitalityStart),
                    F(e.ItemAttrInterval), F(e.ItemSatietyInterval), F(e.ItemMoraleInterval), F(e.ItemStaminaInterval), F(e.ItemHealthInterval), F(e.ItemVitalityInterval),
                    F(e.BuffInterval), F(e.BuffSatietyInterval), F(e.BuffMoraleInterval), F(e.BuffStaminaInterval), F(e.BuffHealthInterval), F(e.BuffVitalityInterval),
                    F(e.ItemSatietyEnd), F(e.ItemMoraleEnd), F(e.ItemStaminaEnd), F(e.ItemHealthEnd), F(e.ItemVitalityEnd)
                }));
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static string ListCsv(Il2CppSystem.Collections.Generic.List<int> list)
    {
        if (list == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(list[i]);
        }
        return sb.ToString();
    }

    private static string ListCsvStr(Il2CppSystem.Collections.Generic.List<string> list)
    {
        if (list == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(list[i]);
        }
        return sb.ToString();
    }

    private static void DumpActions(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== ACTION ID|Name|ActionType|During|EffectConfigID|AttributeHold ==");
        try
        {
            foreach (var kv in cm._Config_Action_Dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                string name = "?", hold = "";
                float during = 0f; int type = 0, ecid = 0;
                try { name = S(a.Name_Local); type = a.ActionType; during = a.During; ecid = a.EffectConfigID; hold = ListCsv(a.AttributeHold); }
                catch { }
                w.WriteLine($"{kv.Key}|{name}|{type}|{during:0.###}|{ecid}|{hold}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpFuncs(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== FUNC FuncID|ActionIds ==");
        try
        {
            foreach (var kv in cm._Config_FurnitureFunc_Dict)
            {
                var f = kv.Value;
                if (f == null) continue;
                w.WriteLine($"{kv.Key}|{ListCsv(f.ActionIds)}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpItems(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== ITEM ItemID|UseAction ==");
        try
        {
            foreach (var kv in cm._Config_Item_Dict)
            {
                var it = kv.Value;
                if (it == null) continue;
                int ua = 0;
                try { ua = it.UseAction; } catch { }
                if (ua != 0) w.WriteLine($"{kv.Key}|{ua}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpThink(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== THINKOPEN ID|ActionID ==");
        try
        {
            foreach (var kv in cm._Config_ThinkOpenAction_Dict)
            {
                var t = kv.Value;
                if (t == null) continue;
                int aid = 0;
                try { aid = t.ActionID; } catch { }
                w.WriteLine($"{kv.Key}|{aid}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine("== THINKTYPE ID|RandomThink|PlayerSelectId ==");
        try
        {
            foreach (var kv in cm._Config_ThinkActionType_Dict)
            {
                var t = kv.Value;
                if (t == null) continue;
                string r = "", p = "";
                try { r = ListCsvStr(t.RandomThink); p = ListCsv(t.PlayerSelectId); } catch { }
                w.WriteLine($"{kv.Key}|{r}|{p}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpDyed(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== DYED DyedActionId|BaseActionId ==");
        try
        {
            foreach (var kv in cm._dyedUseActionToBase)
                w.WriteLine($"{kv.Key}|{kv.Value}");
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
    }
}
