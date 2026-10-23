using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.ConfigDumpProbe;

/// <summary>
/// gamekb 表数据探针 v1（一次性取证，绝不入发布套件；设计见 docs/07 §6）。
/// 主菜单阶段轮询 ConfigManager：125 个 _Config_*_Dict 全部就绪且两轮指纹相同后，
/// 全表序列化为 JSON 落 BepInEx\gamekb_tables\&lt;RuntimeAssets版本&gt;\，以 _DONE.txt 自证。
/// 红线：不写 cfg、不挂 Harmony、不用 il2cpp 泛型方法（自写泛型经 MakeGenericMethod 调用）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class ConfigDumpProbePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.configdumpprobe";
    public const string Name = "ConfigDumpProbe";
    public const string Version = "1.0.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ProbeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
        var go = new GameObject("ConfigDumpProbe.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ProbeTicker>();
        Log.LogInfo("[ConfigDumpProbe] v1 loaded（全量 Config 表 dump -> gamekb_tables/<资源版本>）");
    }
}

internal sealed class ProbeTicker : MonoBehaviour
{
    public ProbeTicker(IntPtr ptr) : base(ptr) { }
    public ProbeTicker() { }

    private float _poll;
    private string _lastFp = "";
    private int _stableRounds;
    private bool _done;

    // (属性名去掉 _Config_ 前缀和 _Dict 后缀, 属性反射, 行类型)
    private readonly List<(string name, System.Reflection.PropertyInfo pi, Type rowType)> _dicts = new();
    // 降级字段（列名）汇总，随 _index.json 落盘
    private readonly SortedSet<string> _degraded = new();

    private static GameCore.HotUpdate.ConfigManager GetCm()
    {
        try
        {
            if (!GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.IsInstanceCreated)
                return null;
            return GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.ConfigManager>.Instance;
        }
        catch { return null; }
    }

    private static string RuntimeAssetsVersion()
    {
        try
        {
            var p = System.IO.Path.Combine(Application.dataPath,
                "StreamingAssets", "PackageManifest", "MainPackage", "RuntimeAssets_MainPackage.version");
            return System.IO.File.ReadAllText(p).Trim();
        }
        catch { return "unknown"; }
    }

    public void Update()
    {
        if (_done) return;
        _poll += Time.deltaTime;
        if (_poll < 2f) return;
        _poll = 0f;

        var cm = GetCm();
        if (cm == null) return;

        if (_dicts.Count < 100)
        {
            // 发现/再发现：表可能分批注册，凑满 100 个前每轮清空重枚举（interop 程序集普通反射）
            _dicts.Clear();
            foreach (var pi in typeof(GameCore.HotUpdate.ConfigManager).GetProperties())
            {
                var m = System.Text.RegularExpressions.Regex.Match(pi.Name, "^_Config_(.+)_Dict$");
                if (!m.Success) continue;
                var args = pi.PropertyType.GetGenericArguments();
                if (args.Length != 2) continue;
                _dicts.Add((m.Groups[1].Value, pi, args[1]));
            }
            if (_dicts.Count < 100) return; // 表未全量注册，下轮再看
            ActionProbeLog($"发现 {_dicts.Count} 个 _Config_*_Dict 属性");
        }

        // 指纹：非空表数:总行数。两轮相同（且非空表>=100）才算加载完毕
        int tables = 0, rows = 0;
        foreach (var (_, pi, _) in _dicts)
        {
            try
            {
                var d = pi.GetValue(cm, null);
                if (d == null) continue;
                var c = (int)d.GetType().GetProperty("Count").GetValue(d, null);
                if (c > 0) { tables++; rows += c; }
            }
            catch { }
        }
        var fp = $"{tables}:{rows}";
        if (fp == _lastFp && tables >= 100) _stableRounds++;
        else _stableRounds = 0;
        _lastFp = fp;
        if (_stableRounds < 2) return;

        _done = true;
        try { DumpAll(cm); }
        catch (Exception e) { ActionProbeLog("dump 失败: " + e); }
    }

    private static void ActionProbeLog(string s) => ConfigDumpProbePlugin.Log.LogInfo("[ConfigDumpProbe] " + s);

    // ---- dump ----
    private static readonly System.Reflection.BindingFlags BI =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

    private void DumpAll(GameCore.HotUpdate.ConfigManager cm)
    {
        var ver = RuntimeAssetsVersion();
        var dir = System.IO.Path.Combine(Paths.BepInExRootPath, "gamekb_tables", ver);
        System.IO.Directory.CreateDirectory(dir);
        ActionProbeLog($"开始 dump：ver={ver} 目标={dir}");

        var index = new List<object>();
        long totalRows = 0;
        foreach (var (name, pi, rowType) in _dicts)
        {
            object dictObj = null;
            try { dictObj = pi.GetValue(cm, null); } catch { }
            int count = 0;
            if (dictObj != null)
            {
                try { count = (int)dictObj.GetType().GetProperty("Count").GetValue(dictObj, null); } catch { }
            }
            var rowsJson = new List<Dictionary<string, object>>();
            if (dictObj != null && count > 0)
            {
                // 自写泛型方法 + MakeGenericMethod（普通 CLR 反射，绕开 interop 泛型限制）
                // 单表失败（key 非整型/泛型约束不符等）只降级该表，不中断全量
                try
                {
                    var mi = typeof(ProbeTicker).GetMethod(nameof(EnumerateRows),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .MakeGenericMethod(typeof(int), rowType);
                    mi.Invoke(null, new object[] { dictObj, rowsJson, _degraded, name });
                }
                catch (Exception e)
                {
                    _degraded.Add($"{name}:<enumerate-failed:{(e.InnerException ?? e).GetType().Name}>");
                }
            }
            totalRows += count;
            var columns = new List<string>();
            if (rowsJson.Count > 0) foreach (var k in rowsJson[0].Keys) columns.Add(k);
            var file = System.IO.Path.Combine(dir, $"Config_{name}.json");
            System.IO.File.WriteAllText(file, JsonSerializer.Serialize(new
            { table = $"Config_{name}", rows = count, columns, data = rowsJson }),
                new UTF8Encoding(false));
            index.Add(new { name = $"Config_{name}", rows = count, columns,
                sha256 = Sha256File(file), degraded = new string[0] });
            ActionProbeLog($"  Config_{name}: {count} 行");
        }

        var idxPath = System.IO.Path.Combine(dir, "_index.json");
        System.IO.File.WriteAllText(idxPath, JsonSerializer.Serialize(new
        { runtime_assets_version = ver, generated_at = DateTime.Now.ToString("o"),
          total_rows = totalRows, degraded_columns = _degraded, tables = index }),
            new UTF8Encoding(false));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "_DONE.txt"),
            $"tables={_dicts.Count} rows={totalRows} deg={_degraded.Count} ver={ver} at={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n",
            new UTF8Encoding(false));
        ActionProbeLog($"DONE：tables={_dicts.Count} rows={totalRows} deg={_degraded.Count}");
    }

    private static string Sha256File(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var f = System.IO.File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
    }

    // 闭式泛型：TRow=interop 行类型；调用方经 MakeGenericMethod 进入
    private static void EnumerateRows<TKey, TRow>(object dictObj, List<Dictionary<string, object>> outRows,
        SortedSet<string> degraded, string tableName) where TRow : Il2CppObjectBase
    {
        var dict = dictObj as Il2CppSystem.Collections.Generic.Dictionary<TKey, TRow>;
        if (dict == null) { degraded.Add($"{tableName}:<dict-cast-failed>"); return; }
        foreach (var kv in dict)
        {
            var row = kv.Value;
            if (row == null) continue;
            var obj = new Dictionary<string, object>();
            foreach (var p in typeof(TRow).GetProperties(BI))
            {
                object v;
                try { v = p.GetValue(row, null); } catch { continue; }
                if (v == null) { obj[p.Name] = null; continue; }
                obj[p.Name] = ValueToJson(v, degraded, $"{tableName}.{p.Name}", 0);
            }
            outRows.Add(obj);
        }
    }

    private static object ValueToJson(object v, SortedSet<string> degraded, string colId, int depth)
    {
        var t = v.GetType();
        // 实测修正（2026-10-23）：Type.IsPrimitive 对 IntPtr/UIntPtr 也为 true，但 System.Text.Json
        // 拒绝序列化 IntPtr（首表即抛 NotSupportedException），必须放行到下方 $deg 降级路径。
        if (v is string || t.IsEnum || (t.IsPrimitive && t != typeof(IntPtr) && t != typeof(UIntPtr))) return v;
        // List 代理：Il2CppSystem.Collections.Generic.List`1，用 Count+Item 反射枚举
        if (depth < 3 && t.Namespace == "Il2CppSystem.Collections.Generic" && t.Name.StartsWith("List`1"))
        {
            try
            {
                var cnt = (int)t.GetProperty("Count").GetValue(v, null);
                var item = t.GetProperty("Item");
                var arr = new List<object>();
                for (int i = 0; i < cnt; i++)
                    arr.Add(ValueToJson(item.GetValue(v, new object[] { i }), degraded, colId, depth + 1));
                return arr;
            }
            catch (Exception e) { degraded.Add($"{colId}:{e.GetType().Name}"); }
        }
        // 数组代理（Il2CppArrayBase 派生）：尽力按 IEnumerable
        if (depth < 3 && v is System.Collections.IEnumerable en)
        {
            try
            {
                var arr = new List<object>();
                foreach (var x in en) arr.Add(ValueToJson(x, degraded, colId, depth + 1));
                return arr;
            }
            catch (Exception e) { degraded.Add($"{colId}:{e.GetType().Name}"); }
        }
        // 其余（嵌套配置对象/字典等）：降级 ToString 并标注（设计 §6 允许）
        if (v is Il2CppObjectBase) degraded.Add(colId);
        return "$deg:" + t.Name + ":" + Convert.ToString(v, CultureInfo.InvariantCulture);
    }
}
