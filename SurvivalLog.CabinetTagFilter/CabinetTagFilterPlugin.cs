using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetTagFilter;

/// <summary>
/// 柜子标签筛选（独立 mod，2026-10-14 从 CabinetEverywhere 拆出）：
/// 在带容器页签的界面（工作台/烹饪/老鼠笼/酿酒/无人机交易/无人机站/粉碎机/压缩机）的页签栏
/// 注入"按标签筛选"下拉框——选中后实时只显示带该标签的容器页签（背包页签不受影响）。
///
/// 不依赖其他 mod：容器页签来自游戏原生（冰箱/工具柜）或储藏柜直达 mod，本 mod 只做显示层筛选；
/// 下拉选项 = 完整标签库（食物维度 1-4 汇总为一个"食物"，物资维度 0 逐个列出）；
/// 材料架不能打标签，自动补"材料"伪标签（筛材料时显示）。
/// 技术路线与实证见 DESIGN.md：Vuplex ExecuteJavaScript 注入 Root + 同源 iframe DOM 隐藏，
/// 不改任何下发的数据数组（索引制 TAB_SWITCH 界面零错位）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetTagFilterPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinettagfilter";
    public const string Name = "CabinetTagFilter";
    public const string Version = "1.3.2"; // 占位版本号：正式发版号由作者定（version.json 单源）

    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enable;

    public override void Load()
    {
        Log = base.Log;
        Enable = Config.Bind("Filter", "Enable", true,
            "总开关：关闭后不再注入下拉框（已注入的界面下次刷新后自然消失）。");
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<Ticker>())
            ClassInjector.RegisterTypeInIl2Cpp<Ticker>();
        var go = new GameObject("CabinetTagFilter.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();
        Log.LogInfo("[CabinetTagFilter] loaded：Enable=" + Enable.Value);
        WelcomeTip.Install(Log);
    }
}

/// <summary>5s 周期：容器枚举 + JS 注入/喂标签数据（全部幂等）。</summary>
internal sealed class Ticker : MonoBehaviour
{
    public Ticker(IntPtr ptr) : base(ptr) { }
    public Ticker() { }

    private float _check;

    public void Update()
    {
        try
        {
            _check += Time.deltaTime;
            if (_check < 5f) return;
            _check = 0f;
            if (!CabinetTagFilterPlugin.Enable.Value) return;
            ContainerScan.Refresh();
            TagFilter.Tick();
        }
        catch { /* 单帧异常不终止轮询 */ }
    }
}
