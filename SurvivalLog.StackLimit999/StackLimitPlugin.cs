using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.StackLimit999;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class StackLimitPlugin : BasePlugin
{
    public const string PluginGuid = "com.local.survivallog.stacklimit999";
    public const string PluginName = "StackLimit999";
    public const string PluginVersion = "2.9.28";

    public override void Load()
    {
        StackConfig.Init(Config);
        StackPatches.Init(Log);

        var harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(StackPatches));
        harmony.PatchAll(typeof(BagEventPatches));
        Log.LogInfo("[StackLimit999] Harmony patches applied (StackLimit + Burden + bag events).");
        try
        {
            // 消耗诊断钩子单独挂（签名若随游戏更新变化，只放弃诊断，不影响主功能）
            harmony.PatchAll(typeof(ConsumeTracePatches));
            Log.LogInfo("[StackLimit999] consume-trace patches applied.");
        }
        catch (Exception e)
        {
            Log.LogWarning("[StackLimit999] consume-trace patches failed (diagnosis only): " + e.Message);
        }

        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<StackRewriteTicker>())
                ClassInjector.RegisterTypeInIl2Cpp<StackRewriteTicker>();

            var go = new GameObject("StackLimit999.Ticker");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<StackRewriteTicker>();
            Log.LogInfo("[StackLimit999] data rewrite ticker created.");
        }
        catch (Exception e)
        {
            Log.LogError("[StackLimit999] failed to create ticker: " + e);
        }

        Log.LogInfo($"[StackLimit999] loaded. StackLimit={StackConfig.StackLimit.Value}, Mode={StackConfig.Mode.Value}, " +
                    $"BurdenMultiplier={StackConfig.BurdenMultiplier.Value}, MergeOnSort={StackConfig.MergeOnSort.Value}, " +
                    $"DragMerge={StackConfig.DragMerge.Value}, Split=Shift+左键 x{StackConfig.SplitCount.Value}");
    }
}

internal static class StackConfig
{
    public enum StackMode
    {
        All,
        OnlyStackable
    }

    internal static ConfigEntry<int> StackLimit;
    internal static ConfigEntry<StackMode> Mode;
    internal static ConfigEntry<string> ExcludedItemIds;
    internal static ConfigEntry<string> ExcludedCategories;
    internal static ConfigEntry<bool> RewriteData;
    internal static ConfigEntry<float> RewriteIntervalSeconds;
    internal static ConfigEntry<int> BurdenMultiplier;
    internal static ConfigEntry<bool> MergeOnSort;
    internal static ConfigEntry<bool> DragMerge;
    internal static ConfigEntry<bool> SplitOnKey;
    internal static ConfigEntry<int> SplitCount;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<string> RestoreLosses;

    internal static event Action SettingsChanged;

    public static void Init(ConfigFile cfg)
    {
        StackLimit = cfg.Bind("General", "StackLimit", 999,
            new ConfigDescription("物品堆叠上限（每格最大数量）。",
                new AcceptableValueRange<int>(1, 999999)));

        Mode = cfg.Bind("General", "Mode", StackMode.All,
            "All = 所有物品都可堆叠到上限（本游戏原版所有物品上限都是1，等于给全物品开启堆叠）；" +
            "OnlyStackable = 只提高原版本来就可堆叠（原上限>1）的物品，保持不可堆叠物品原样。");

        ExcludedItemIds = cfg.Bind("General", "ExcludedItemIds", "",
            "不修改的物品ID（Config_Item.ID），逗号分隔，例如: 1,9044");

        ExcludedCategories = cfg.Bind("General", "ExcludedCategories", "",
            "不修改的物品类别ID（Config_Item.Category），逗号分隔。");

        RewriteData = cfg.Bind("General", "RewriteData", true,
            "定期直接改写配置表中的堆叠上限（兜底，防止个别代码路径绕过运行时钩子）。");

        RewriteIntervalSeconds = cfg.Bind("General", "RewriteIntervalSeconds", 15f,
            new ConfigDescription("配置表改写的执行间隔（秒）。",
                new AcceptableValueRange<float>(1f, 600f)));

        BurdenMultiplier = cfg.Bind("Weight", "BurdenMultiplier", 100,
            new ConfigDescription("负重上限倍数：游戏原版各背包配置的 Burden × 该倍数。" +
                "100 倍 = 普通背包约 960Kg。1 或 0 = 不修改负重。",
                new AcceptableValueRange<int>(0, 100000)));

        MergeOnSort = cfg.Bind("General", "MergeOnSort", true,
            "点背包「一键整理」按钮时，先合并同类物品再整理。");

        DragMerge = cfg.Bind("General", "DragMerge", true,
            "把一个物品拖拽到同类物品上时自动合并（含跨容器：拖到柜子里同种物品上）。" +
            "有使用次数的物品（食物类）不参与合并与堆叠改写：官方烹饪按单份处理。");

        SplitOnKey = cfg.Bind("Split", "SplitOnKey", true,
            "拆分：按住 Shift + 左键点击物品堆，从该堆拆出指定数量（替代原版 Shift 快速转移；右键转移保留）。背包、柜子、工作台制作页均可。单件物品仍走原版转移。");

        SplitCount = cfg.Bind("Split", "SplitCount", 1,
            new ConfigDescription("每次拆分出来的数量（Shift+左键触发）。0 = 拆一半。",
                new AcceptableValueRange<int>(0, 9999)));

        DebugLogging = cfg.Bind("Debug", "DebugLogging", true,
            "输出诊断日志（全部确认正常后可改为 false）。");

        RestoreLosses = cfg.Bind("General", "RestoreLosses", "",
            "损失补偿：配置ID:目标总数，逗号分隔，例如 20001:18（补足全世界的纸片到至少18个）。" +
            "存档加载后核对一次，差额用游戏正规 AddItem 管线补齐。用完请清空。");

        StackLimit.SettingChanged += (_, _) => OnChanged();
        Mode.SettingChanged += (_, _) => OnChanged();
        ExcludedItemIds.SettingChanged += (_, _) => OnChanged();
        ExcludedCategories.SettingChanged += (_, _) => OnChanged();
        RewriteIntervalSeconds.SettingChanged += (_, _) => OnChanged();
        BurdenMultiplier.SettingChanged += (_, _) => OnChanged();
        MergeOnSort.SettingChanged += (_, _) => OnChanged();
        DragMerge.SettingChanged += (_, _) => OnChanged();
        SplitOnKey.SettingChanged += (_, _) => OnChanged();
        SplitCount.SettingChanged += (_, _) => OnChanged();
    }

    private static void OnChanged()
    {
        StackPatches.RebuildBlacklists();
        SettingsChanged?.Invoke();
    }
}
