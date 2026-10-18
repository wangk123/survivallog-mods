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

/// <summary>场景视图：Food=食物标签柜+全部冰箱；Material=材料/工具/未标签柜（不含冰箱）；Browser=下拉自由筛选。</summary>
public enum ScenarioView { Food, Material, Browser }

/// <summary>
/// 储藏柜直达 + 标签筛选/场景视图（原独立 mod CabinetTagFilter 已于 2026-10-17 并回）。
///
/// 1. 直达：让普通储物类家具（储物柜/金属柜/床头柜等）出现在制作类界面的容器页签：
///    a. 白名单注入：把柜子家具 ID 加进 ToolCabinetConfig.CraftSourceFurnitureIds（官方"制作材料源"
///       白名单，原生驱动 工作台/无人机交易/无人机站/粉碎机/温室 五个系统的页签与自动取料，
///       GetAllLinkedOwnerIds 实证遍历 CabinetTabs 参与自动补料）。
///    b. 页签注入：烹饪/老鼠笼/酿酒桶/颜料桶 的容器过滤是内联的"冷链电器"判定（不走白名单），
///       改为向各自 Redux 状态的 BagTabs（ObservableList）追加柜子页签；切换/取放路径
///       ApplySwitchBag→GetBagData(ownerId) 已实证为通用取包，无类型过滤。
/// 2. 筛选/场景视图：按界面 cfg（[Scenario] View_*）固定收窄页签视野（食物/材料），或 Browser
///    模式自绘下拉按官方标签筛选；A/D 步进自动跳过被藏柜子（JS 侧扣发+伪造，DESIGN.md 附录）。
///
/// 实证依据见 DESIGN.md（2026-10-10 全程反汇编 + 附录 A/C 取证史）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetEverywherePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabineteverywhere";
    public const string Name = "CabinetEverywhere";
    public const string Version = "1.3.7";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> EnableCraftSource;
    internal static ConfigEntry<bool> EnableCooking;
    internal static ConfigEntry<bool> EnableRatCage;
    internal static ConfigEntry<bool> EnableBrew;
    internal static ConfigEntry<bool> EnableDye;
    internal static ConfigEntry<string> IncludeFurnitureIds;
    internal static ConfigEntry<string> ExcludeFurnitureIds;
    internal static ConfigEntry<string> ExcludeBagIds;
    internal static ConfigEntry<bool> Verbose;
    internal static ConfigEntry<bool> ListOnReady;
    internal static ConfigEntry<bool> DumpFurniture;
    internal static ConfigEntry<bool> FilterEnable;

    // 场景视图（键=实测 iframe id，T1 report L203-214：Trade 不是 TradeUI、Brew 页 id=BrewPanel）
    internal static ConfigEntry<ScenarioView> ViewCooking, ViewRatCage, ViewBrewPanel, ViewDyePanel, ViewCompressor,
                                              ViewToolTable, ViewShredder, ViewGreenhouseBuild, ViewTrade, ViewDroneHub;

    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        EnableCraftSource = Config.Bind("Access", "EnableCraftSource", true,
            "白名单注入：工作台/无人机交易/无人机站/粉碎机/温室 的柜子页签与自动取料。");
        EnableCooking = Config.Bind("Access", "EnableCooking", true, "烹饪界面注入柜子页签。");
        EnableRatCage = Config.Bind("Access", "EnableRatCage", true, "老鼠笼界面注入柜子页签。");
        EnableBrew = Config.Bind("Access", "EnableBrew", true, "酿酒桶界面注入柜子页签。");
        EnableDye = Config.Bind("Access", "EnableDye", true, "颜料桶界面注入柜子页签。");
        IncludeFurnitureIds = Config.Bind("Access", "IncludeFurnitureIds", "",
            "追加的家具 ConfigId（逗号分隔，强制加入）。");
        ExcludeFurnitureIds = Config.Bind("Access", "ExcludeFurnitureIds", "313,80033,80164,1024,1025,1026,1027,1028,42033,66196,330,384,389,9126",
            "排除的家具 ConfigId（优先级最高）。默认排除：313/80033/80164自制工具台（非储物，同名三个变体）；载具类 1024购物塑料袋/1025购物车/1026自行车/1027小汽车/1028SUV/42033旧SUV；66196染料桶、330/384/389/9126材料堆（四个变体）——功能容器或角色专属，与纸箱/自制工具台共用包只能按家具排除（2026-10-17 作者实测日志实锤）。");
        ExcludeBagIds = Config.Bind("Access", "ExcludeBagIds",
            "1008,1009,1014,1050,1060,10001,10004,3001,115004,5001,5002,5003,5004,5005,5006,5007,5008,5009,5011,5014,5015,5017,5020",
            "排除的 BagId（整包系排除）。默认排除非储物容器：厨房电器(5001燃料炉/5002电烤箱/5003微波炉/5004燃气灶/5005榨汁机/5006咖啡机/5011电火锅)、机器功能槽(5007-5009/5014老鼠笼/5015粉碎机/5017口粮压制机/5020暖房工程台/115004酿酒桶)、围栏(3001)、吊篮(1050)、无人机货舱(1008)/无人机中控台(1009，2026-10-17 作者实测补)、门(1060)、窗(1014)、床(10001)、垃圾桶(10004)。其余带储物包家具默认全部生效。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");
        ListOnReady = Config.Bind("Debug", "ListOnReady", true,
            "配置表就绪时在日志列出发现的全部柜子家具名单（首次核对用，可关）。");
        DumpFurniture = Config.Bind("Debug", "DumpFurniture", false,
            "置 true 后（游戏运行中改 cfg 约 5 秒内生效）在日志列出家中全部带包家具明细：owner/配置/名称/锁定态/是否入选。排查'新柜子不生效'用，平时关。");
        FilterEnable = Config.Bind("Filter", "Enable", true,
            "标签筛选总开关：关闭后不注入筛选（场景视图也一并失效，容器页签全部显示）。");
        FilterEnable.SettingChanged += (object s, System.EventArgs e) => { Rerun = true; };

        // 场景视图默认值按 spec 场景表：烹饪/老鼠笼/酿酒/颜料桶/口粮压制机=Food，
        // 工作台/粉碎机/温室=Material，无人机交易/调度机队=Browser（键名=实测页面 id）
        const string viewDesc = "界面视图：Food=食物标签柜+全部冰箱；Material=材料/工具/未标签柜（不含冰箱）；Browser=下拉自由筛选。";
        ViewCooking         = Config.Bind("Scenario", "View_Cooking",         ScenarioView.Food,    "烹饪" + viewDesc);
        ViewRatCage         = Config.Bind("Scenario", "View_RatCage",         ScenarioView.Food,    "老鼠笼" + viewDesc);
        ViewBrewPanel       = Config.Bind("Scenario", "View_BrewPanel",       ScenarioView.Food,    "酿酒桶" + viewDesc);
        ViewDyePanel        = Config.Bind("Scenario", "View_DyePanel",        ScenarioView.Food,    "颜料桶" + viewDesc);
        ViewCompressor      = Config.Bind("Scenario", "View_Compressor",      ScenarioView.Food,    "口粮压制机" + viewDesc);
        ViewToolTable       = Config.Bind("Scenario", "View_ToolTable",       ScenarioView.Material, "工作台" + viewDesc);
        ViewShredder        = Config.Bind("Scenario", "View_Shredder",        ScenarioView.Material, "粉碎机" + viewDesc);
        ViewGreenhouseBuild = Config.Bind("Scenario", "View_GreenhouseBuild", ScenarioView.Material, "温室工程台" + viewDesc);
        ViewTrade           = Config.Bind("Scenario", "View_Trade",           ScenarioView.Browser, "无人机交易" + viewDesc);
        ViewDroneHub        = Config.Bind("Scenario", "View_DroneHub",        ScenarioView.Browser, "调度机队（无人机站）" + viewDesc);

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        IncludeFurnitureIds.SettingChanged += reload;
        ExcludeFurnitureIds.SettingChanged += reload;
        ExcludeBagIds.SettingChanged += reload;
        EnableCraftSource.SettingChanged += reload;
        EnableCooking.SettingChanged += reload;
        EnableRatCage.SettingChanged += reload;
        EnableBrew.SettingChanged += reload;
        EnableDye.SettingChanged += reload;
        ViewCooking.SettingChanged += reload;
        ViewRatCage.SettingChanged += reload;
        ViewBrewPanel.SettingChanged += reload;
        ViewDyePanel.SettingChanged += reload;
        ViewCompressor.SettingChanged += reload;
        ViewToolTable.SettingChanged += reload;
        ViewShredder.SettingChanged += reload;
        ViewGreenhouseBuild.SettingChanged += reload;
        ViewTrade.SettingChanged += reload;
        ViewDroneHub.SettingChanged += reload;
        DumpFurniture.SettingChanged += (object s, System.EventArgs e) => { CabinetCatalog.DumpRequested = true; };

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<AccessTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<AccessTicker>();
        if (DumpFurniture.Value) CabinetCatalog.DumpRequested = true; // cfg 启动即 true 也生效（进档后 dump 一次）
        var go = new GameObject("CabinetEverywhere.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<AccessTicker>();
        Log.LogInfo("[CabinetEverywhere] loaded：白名单注入=" + EnableCraftSource.Value +
                    "，页签注入=烹饪/老鼠笼/酿酒/颜料桶(" + EnableCooking.Value + "/" + EnableRatCage.Value + "/" + EnableBrew.Value + "/" + EnableDye.Value + ")");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }
}

internal sealed class AccessTicker : MonoBehaviour
{
    public AccessTicker(IntPtr ptr) : base(ptr) { }
    public AccessTicker() { }

    private float _slowCheck;          // 白名单 + 柜子名单重扫 + 统一容器扫描（5s 幂等）
    private float _fastCheck;          // 页签注入轮询（0.5s，仅目标界面打开时）
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

            // 3) 统一容器扫描（5s 只扫一遍，原直达/筛选两个 5s 扫描合一）：单遍产出
            //    AllContainers（全容器+标签，筛选用）+ HomeInstances（柜子子集，直达注入用）
            CabinetCatalog.RefreshHomeInstances();
            CabinetCatalog.FlushDump();

            // 4) 筛选：JS 注入/喂标签数据（容器枚举已并入上面的统一扫描）
            if (CabinetEverywherePlugin.FilterEnable.Value)
                TagFilter.Tick();
        }

        // 页签注入（烹饪/老鼠笼/酿酒/颜料桶）：读 5s 刷新的柜子子集 HomeInstances，0.5s 轮询补挂
        _fastCheck += dt;
        if (_fastCheck < 0.5f) return;
        _fastCheck = 0f;
        TabInjector.PollAll();
    }
}
