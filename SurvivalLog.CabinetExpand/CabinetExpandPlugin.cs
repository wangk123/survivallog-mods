using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetExpand;

/// <summary>
/// 工具柜扩容（独立 mod 2/4）。
/// 默认名单：10007（工具柜 406）。游戏里同名工具柜还有 70006（BagId 110001，与置物架共用包），
/// 若实测某工具柜无效，把 110001 或对应 BagId 加入 ExtraIds（打开工具柜界面时日志会打印
/// 实际用到的 BagConfigId，据此添加）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetExpandPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetexpand";
    public const string Name = "CabinetExpand";
    public const string Version = "1.0.1";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> WidthMult;
    internal static ConfigEntry<float> HeightMult;
    internal static ConfigEntry<string> ExtraIds;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> DiagOnOpen;

    internal static readonly int[] DefaultIds = { 10007, 110001, 1002, 1003, 10002, 10005, 10006, 10008 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        WidthMult = Config.Bind("Expand", "WidthMult", 1.5f, "宽度倍数（四舍五入取整）。");
        HeightMult = Config.Bind("Expand", "HeightMult", 1.5f, "高度倍数（四舍五入取整）。");
        ExtraIds = Config.Bind("Expand", "ExtraIds", "",
            "追加的 BagId（逗号分隔）。默认已含：110001(工具柜/中型置物架共用包),10007(工具柜),1002,1003(柜子),10002,10005,10006,10008(金属柜系列)。置物架(110000/110001/110008)、床头柜(110003)、纸箱(1010/2003)等未含，需要可自行添加。");
        ExcludeIds = Config.Bind("Expand", "ExcludeIds", "", "排除的 BagId（优先级最高）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");
        DiagOnOpen = Config.Bind("Debug", "DiagOnOpen", false,
            "读档时在日志列出全部容器的 BagConfigId（确认名单用，平时关闭防刷屏）。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        WidthMult.SettingChanged += reload;
        HeightMult.SettingChanged += reload;
        ExtraIds.SettingChanged += reload;
        ExcludeIds.SettingChanged += reload;

        try
        {
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(BagInitDiag));
            Log.LogInfo("[CabinetExpand] 诊断补丁已挂（读档时列出全部容器的 BagConfigId）");
        }
        catch (Exception e)
        {
            Log.LogWarning("[CabinetExpand] 诊断补丁挂载失败（不影响改写功能）: " + e.Message);
        }

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CabinetTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<CabinetTicker>();
        var go = new GameObject("CabinetExpand.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CabinetTicker>();
        Log.LogInfo($"[CabinetExpand] loaded：宽×{WidthMult.Value} 高×{HeightMult.Value}");
    }

    internal static HashSet<int> BuildTargets()
    {
        var set = new HashSet<int>(DefaultIds);
        foreach (var id in BagTable.ParseIds(ExtraIds.Value)) set.Add(id);
        foreach (var id in BagTable.ParseIds(ExcludeIds.Value)) set.Remove(id);
        return set;
    }
}

/// <summary>
/// 诊断 v3：patch BagComponent.OnInit(int)（实例方法，此通道旧 mod 已验证可靠；
/// v1 的 SendAction（泛型参数）与 v2 的 Trigger_RA_Open 均未触发——工具柜界面
/// 大概率不走 ToolTable，而是走背包双栏视图）。
/// 读档时每个容器实体初始化都会经过这里，一次性列出全部容器及其 BagConfigId。
/// </summary>
internal static class BagInitDiag
{
    private static readonly System.Collections.Generic.HashSet<int> Seen = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameCore.HotUpdate.Battle.Logic.BagComponent), "OnInit")]
    private static void Postfix(GameCore.HotUpdate.Battle.Logic.BagComponent __instance, int bagConfigId)
    {
        if (!CabinetExpandPlugin.DiagOnOpen.Value) return;
        try
        {
            if (!Seen.Add(bagConfigId)) return;
            string name = "?"; int w = 0, h = 0;
            var cm = BagTable.Cm();
            var dict = cm == null ? null : BagTable.BagDict(cm);
            if (dict != null)
            {
                GameCore.HotUpdate.Config_Bag bag = null;
                try { dict.TryGetValue(bagConfigId, out bag); } catch { }
                if (bag != null)
                {
                    name = BagTable.Name(bag);
                    var sz = bag.Size;
                    if (sz != null && sz.Count >= 2) { w = sz[0]; h = sz[1]; }
                }
            }
            CabinetExpandPlugin.Log.LogInfo(
                $"[CabinetExpand][诊断] 容器初始化 bagConfigId={bagConfigId}（{name}，当前 {w}x{h}，初始包={__instance.GetInitialBagConfigId()}）");
        }
        catch (Exception e)
        {
            CabinetExpandPlugin.Log.LogWarning("[CabinetExpand][诊断] OnInit 读取失败: " + e.Message);
        }
    }
}

internal sealed class CabinetTicker : MonoBehaviour
{
    public CabinetTicker(IntPtr ptr) : base(ptr) { }
    public CabinetTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 持续轮询幂等改写（同 BackpackExpand：防读档/切场景配置表重载覆盖）
        _check += Time.deltaTime;
        var due = CabinetExpandPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = CabinetExpandPlugin.BuildTargets();
        var (scanned, changed, lines) = BagTable.RewriteSize(
            targets, CabinetExpandPlugin.WidthMult.Value, CabinetExpandPlugin.HeightMult.Value,
            CabinetExpandPlugin.Verbose.Value);

        if (scanned < 0) { CabinetExpandPlugin.Rerun = true; return; }
        CabinetExpandPlugin.Rerun = false;

        if (changed > 0 || CabinetExpandPlugin.Verbose.Value)
            foreach (var l in lines) CabinetExpandPlugin.Log.LogInfo("[CabinetExpand] " + l);

        if (!_announced && scanned > 0)
        {
            _announced = true;
            CabinetExpandPlugin.Log.LogInfo(
                $"[CabinetExpand] 生效：{scanned} 条配置受管（宽×{CabinetExpandPlugin.WidthMult.Value} 高×{CabinetExpandPlugin.HeightMult.Value}）。");
        }
    }
}
