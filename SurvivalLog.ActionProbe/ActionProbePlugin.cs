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
/// 一次性诊断插件 v5（不发布）：dump 动作表 + 交互按钮表 + 家具表三段，
/// 用于生成每个动作的"使用场景"备注（家具 → 功能按钮 → 动作 反查）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ActionProbePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.actionprobe";
    public const string Name = "ActionProbe";
    public const string Version = "0.5.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ProbeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
        var go = new GameObject("ActionProbe.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeTicker>();
        Log.LogInfo("[ActionProbe] v5 loaded");
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
        }
        catch { return; }

        _done = true;
        var path = System.IO.Path.Combine(Paths.BepInExRootPath, "ActionDump.txt");
        try
        {
            using var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false));
            DumpActions(cm, w);
            DumpFuncs(cm, w);
            DumpFurniture(cm, w);
            ActionProbePlugin.Log.LogInfo($"[ActionProbe] DUMP OK -> {path}");
        }
        catch (Exception e)
        {
            ActionProbePlugin.Log.LogError("[ActionProbe] dump 失败: " + e);
        }
    }

    private static string S(string s) => s ?? "";

    private static void DumpActions(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== Config_Action（ID|Name_Local|NameDoing_Local|ActionType|During） ==");
        try
        {
            foreach (var kv in cm._Config_Action_Dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                w.WriteLine($"{kv.Key}|{S(a.Name_Local)}|{S(a.NameDoing_Local)}|{a.ActionType}|{a.During:0.###}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpFuncs(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== Config_FurnitureFunc（ID|BtnName_Local|ActionIds） ==");
        try
        {
            foreach (var kv in cm._Config_FurnitureFunc_Dict)
            {
                var f = kv.Value;
                if (f == null) continue;
                var sb = new StringBuilder();
                try
                {
                    var ids = f.ActionIds;
                    if (ids != null)
                        for (int i = 0; i < ids.Count; i++)
                        {
                            if (sb.Length > 0) sb.Append(',');
                            sb.Append(ids[i]);
                        }
                }
                catch { sb.Append("ERR"); }
                w.WriteLine($"{kv.Key}|{S(f.BtnName_Local)}|{sb}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

    private static void DumpFurniture(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== Config_Furniture（ID|Name_Local|FurnitureType|FuncIds） ==");
        try
        {
            foreach (var kv in cm._Config_Furniture_Dict)
            {
                var f = kv.Value;
                if (f == null) continue;
                var sb = new StringBuilder();
                try
                {
                    var ids = f.FurnitureFunc;
                    if (ids != null)
                        for (int i = 0; i < ids.Count; i++)
                        {
                            if (sb.Length > 0) sb.Append(',');
                            sb.Append(ids[i]);
                        }
                }
                catch { sb.Append("ERR"); }
                w.WriteLine($"{kv.Key}|{S(f.Name_Local)}|{f.FurnitureType}|{sb}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }
}
