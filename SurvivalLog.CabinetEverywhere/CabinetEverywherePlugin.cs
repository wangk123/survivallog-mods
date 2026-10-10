using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetEverywhere;

/// <summary>
/// 储藏柜直达（独立 mod）。
///
/// 让普通储物类家具（储物柜/金属柜/床头柜等）出现在制作类界面的容器页签里：
/// 1. 白名单注入：把柜子家具 ID 加进 ToolCabinetConfig.CraftSourceFurnitureIds（官方"制作材料源"
///    白名单，原生驱动 工作台/无人机交易/无人机站/粉碎机/温室 五个系统的页签与自动取料，
///    GetAllLinkedOwnerIds 实证遍历 CabinetTabs 参与自动补料）。
/// 2. 页签注入：烹饪/老鼠笼/酿酒桶 的容器过滤是内联的"冷链电器"判定（不走白名单），
///    改为向各自 Redux 状态的 BagTabs（ObservableList）追加柜子页签；切换/取放路径
///    ApplySwitchBag→GetBagData(ownerId) 已实证为通用取包，无类型过滤。
///
/// 实证依据见 DESIGN.md（2026-10-10 全程反汇编）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetEverywherePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabineteverywhere";
    public const string Name = "CabinetEverywhere";
    public const string Version = "1.1.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> EnableCraftSource;
    internal static ConfigEntry<bool> EnableCooking;
    internal static ConfigEntry<bool> EnableRatCage;
    internal static ConfigEntry<bool> EnableBrew;
    internal static ConfigEntry<string> IncludeFurnitureIds;
    internal static ConfigEntry<string> ExcludeFurnitureIds;
    internal static ConfigEntry<string> ExcludeBagIds;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> ListOnReady;

    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        EnableCraftSource = Config.Bind("Access", "EnableCraftSource", true,
            "白名单注入：工作台/无人机交易/无人机站/粉碎机/温室 的柜子页签与自动取料。");
        EnableCooking = Config.Bind("Access", "EnableCooking", true, "烹饪界面注入柜子页签。");
        EnableRatCage = Config.Bind("Access", "EnableRatCage", true, "老鼠笼界面注入柜子页签。");
        EnableBrew = Config.Bind("Access", "EnableBrew", true, "酿酒桶界面注入柜子页签。");
        IncludeFurnitureIds = Config.Bind("Access", "IncludeFurnitureIds", "",
            "追加的家具 ConfigId（逗号分隔，强制加入，如置物架等）。");
        ExcludeFurnitureIds = Config.Bind("Access", "ExcludeFurnitureIds", "",
            "排除的家具 ConfigId（优先级最高）。");
        ExcludeBagIds = Config.Bind("Access", "ExcludeBagIds", "110000,110001,110008,5001,5002",
            "排除的 BagId。默认：置物架系列(110000/110001/110008，工作台已原生物料架左栏，110001 与工具柜共用包且工具柜已在官方白名单)；炉具(5001 燃料炉/5002 电烤箱，属烹饪电器非储藏)。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "配置表就绪时在日志列出发现的全部柜子家具名单（首次核对用，可关）。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        IncludeFurnitureIds.SettingChanged += reload;
        ExcludeFurnitureIds.SettingChanged += reload;
        ExcludeBagIds.SettingChanged += reload;
        EnableCraftSource.SettingChanged += reload;
        EnableCooking.SettingChanged += reload;
        EnableRatCage.SettingChanged += reload;
        EnableBrew.SettingChanged += reload;

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<AccessTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<AccessTicker>();
        var go = new GameObject("CabinetEverywhere.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<AccessTicker>();
        Log.LogInfo("[CabinetEverywhere] loaded：白名单注入=" + EnableCraftSource.Value +
                    "，页签注入=烹饪/老鼠笼/酿酒(" + EnableCooking.Value + "/" + EnableRatCage.Value + "/" + EnableBrew.Value + ")");
    }
}

internal sealed class AccessTicker : MonoBehaviour
{
    public AccessTicker(IntPtr ptr) : base(ptr) { }
    public AccessTicker() { }

    private float _slowCheck;          // 白名单 + 柜子名单重扫（5s 幂等）
    private float _fastCheck;          // 页签注入轮询（0.5s，仅目标界面打开时）
    private float _instanceCacheAge;   // 家中柜子实例缓存时效
    private bool _listedOnce;

    public void Update()
    {
        try { Tick(Time.deltaTime); }
        catch { /* 单帧异常不终止轮询 */ }
    }

    private void Tick(float dt)
    {
        _slowCheck += dt;
        var slowDue = CabinetEverywherePlugin.Rerun || _slowCheck >= 5f;

        if (slowDue)
        {
            _slowCheck = 0f;
            // 1) 柜子家具名单（配置层，幂等重扫）
            var cfgIds = CabinetCatalog.RebuildConfigIds();
            if (cfgIds == null) return; // 配置表未就绪
            CabinetEverywherePlugin.Rerun = false;

            if (!_listedOnce && CabinetEverywherePlugin.ListOnReady.Value)
            {
                _listedOnce = true;
                CabinetEverywherePlugin.Log.LogInfo("[CabinetEverywhere] 柜子名单（" + cfgIds.Count + " 条）：\n  " +
                                                    string.Join("\n  ", CabinetCatalog.DescribeAll()));
            }

            // 2) 白名单注入（工作台/无人机交易/无人机站/粉碎机/温室）
            if (CabinetEverywherePlugin.EnableCraftSource.Value)
                CraftSourceInjector.Ensure(cfgIds);
        }

        // 3) 页签注入（烹饪/老鼠笼/酿酒）
        _fastCheck += dt;
        if (_fastCheck < 0.5f) return;
        _fastCheck = 0f;
        _instanceCacheAge += 0.5f;
        if (_instanceCacheAge >= 5f)
        {
            _instanceCacheAge = 0f;
            CabinetCatalog.RefreshHomeInstances();
        }
        TabInjector.PollAll();
    }
}
