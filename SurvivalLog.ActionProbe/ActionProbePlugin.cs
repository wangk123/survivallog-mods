using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ActionProbe;

/// <summary>
/// 一次性诊断插件 v8（不发布）：
/// 1) EFFECT 段 dump（判定 9075 上厕所等动作的效果结算类型）。
/// 2) ActionWatch：每 0.25s 轮询 BattleLogicWorld._ActionManager.AgentActionSourceDict，
///    记录每个角色的当前动作切换（时间|agent|actionId|名|Type|During|ECID|MatchedFuncId|队列）。
///    用于实测：浇绿植/上厕所/吹风/看书 的真实动作 ID 与实际播放时长。
/// 输出：BepInEx\EffectDump.txt（一次性）、BepInEx\ActionWatch.txt（持续追加）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ActionProbePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.actionprobe";
    public const string Name = "ActionProbe";
    public const string Version = "0.8.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ProbeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
        var go = new GameObject("ActionProbe.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeTicker>();
        Log.LogInfo("[ActionProbe] v8 loaded（EFFECT dump + ActionWatch）");
    }
}

internal sealed class ProbeTicker : MonoBehaviour
{
    public ProbeTicker(IntPtr ptr) : base(ptr) { }
    public ProbeTicker() { }

    private float _wait;
    private float _poll;
    private bool _dumpDone;

    // agentId -> 上次见到的当前动作 ID（0=无动作）
    private readonly Dictionary<long, int> _last = new();
    // agentId -> 上次动作开始时间（真实秒，Time.realtimeSinceStartup）
    private readonly Dictionary<long, float> _lastAt = new();
    // actionId -> (name, type, during, ecid) 启动时缓存
    private readonly Dictionary<int, string> _meta = new();

    private string _watchPath;

    public void Update()
    {
        // ---- 一次性 EFFECT dump ----
        if (!_dumpDone)
        {
            _wait += Time.deltaTime;
            if (_wait >= 4f)
            {
                GameCore.HotUpdate.ConfigManager cm = null;
                try
                {
                    if (GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                        cm = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
                }
                catch { }
                if (cm != null)
                {
                    try
                    {
                        if (cm._Config_Action_Dict != null && cm._Config_Action_Dict.Count > 0 &&
                            cm._Config_Effect_Dict != null && cm._Config_Effect_Dict.Count > 0)
                        {
                            _dumpDone = true;
                            var path = System.IO.Path.Combine(Paths.BepInExRootPath, "EffectDump.txt");
                            using (var w = new System.IO.StreamWriter(path, false, new UTF8Encoding(false)))
                            {
                                DumpEffects(cm, w);
                                CacheActions(cm);
                                DumpActionMeta(cm, w);
                            }
                            ActionProbePlugin.Log.LogInfo($"[ActionProbe] EFFECT DUMP OK -> {path}");
                        }
                    }
                    catch (Exception e) { ActionProbePlugin.Log.LogError("[ActionProbe] dump 失败: " + e); _dumpDone = true; }
                }
            }
        }

        // ---- 动作监视 ----
        _poll += Time.deltaTime;
        if (_poll < 0.25f) return;
        _poll = 0f;

        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated)
                return;
            var world = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.Instance;
            if (world == null) return;
            var am = world._ActionManager;
            if (am == null) return;
            var dict = am.AgentActionSourceDict;
            if (dict == null || dict.Count == 0) return;

            _watchPath ??= System.IO.Path.Combine(Paths.BepInExRootPath, "ActionWatch.txt");

            var sb = new StringBuilder();
            foreach (var kv in dict)
            {
                var src = kv.Value;
                if (src == null) continue;
                int cur = 0, funcId = 0, queue = 0;
                try { var ca = src.CurrentAction; if (ca != null) { cur = ca.ActionId; funcId = ca.MatchedFuncId; } }
                catch { }
                try { var q = src.ActionIdList; if (q != null) queue = q.Count; }
                catch { }

                bool first = !_last.ContainsKey(kv.Key);
                int prev = first ? -1 : _last[kv.Key];
                if (cur == prev) continue;

                float now = Time.realtimeSinceStartup;
                float dur = 0f;
                if (_lastAt.TryGetValue(kv.Key, out var t0)) dur = now - t0;

                if (_meta.Count == 0) TryCacheFromWorld();
                _meta.TryGetValue(cur, out var meta);
                meta ??= cur == 0 ? "无动作" : "?";

                if (!first && prev != -1)
                    sb.Append($"[{Time.realtimeSinceStartup:0.00}] agent {kv.Key}: {prev} -> {cur}（{meta}）| 上一动作历时 {dur:0.0}s | FuncId={funcId} | 队列{queue}\n");
                else
                    sb.Append($"[{Time.realtimeSinceStartup:0.00}] agent {kv.Key}: 初始 {cur}（{meta}）| FuncId={funcId} | 队列{queue}\n");

                _last[kv.Key] = cur;
                _lastAt[kv.Key] = now;
            }
            if (sb.Length > 0)
            {
                try { System.IO.File.AppendAllText(_watchPath, sb.ToString(), new UTF8Encoding(false)); }
                catch { }
            }
        }
        catch (Exception e)
        {
            ActionProbePlugin.Log.LogWarning("[ActionProbe] watch 异常: " + e.Message);
        }
    }

    private void TryCacheFromWorld()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated) return;
            CacheActions(GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance);
        }
        catch { }
    }

    private void CacheActions(GameCore.HotUpdate.ConfigManager cm)
    {
        try
        {
            foreach (var kv in cm._Config_Action_Dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                try
                {
                    var n = a.Name_Local ?? a.Name ?? "?";
                    _meta[kv.Key] = $"{n}|T{a.ActionType}|D{a.During:0.#}|E{a.EffectConfigID}";
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DumpActionMeta(GameCore.HotUpdate.ConfigManager cm, System.IO.StreamWriter w)
    {
        w.WriteLine("== ACTION ID|Name|Type|During|ECID ==");
        try
        {
            foreach (var kv in cm._Config_Action_Dict)
            {
                var a = kv.Value;
                if (a == null) continue;
                string name = "?";
                float during = 0f; int type = 0, ecid = 0;
                try { name = a.Name_Local ?? a.Name ?? "?"; type = a.ActionType; during = a.During; ecid = a.EffectConfigID; }
                catch { }
                w.WriteLine($"{kv.Key}|{name}|{type}|{during:0.###}|{ecid}");
            }
        }
        catch (Exception e) { w.WriteLine("ERR " + e.Message); }
        w.WriteLine();
    }

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
}
