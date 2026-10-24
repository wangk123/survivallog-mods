using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.CabinetSize;

/// <summary>
/// 柜子容量（数值型；2026-10-24 定稿取代 CabinetExpand，设计文档见本目录 DESIGN.md）。
/// 家里的柜子按 小/中/大/特大 四档调到固定宽高；12 常用包 cfg 显式条目；其余按档自动生效；
/// 全局上限默认 20x12（官方自适应设计预算内，弃用一切自研视口适配）。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class CabinetSizePlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.cabinetsize";
    public const string Name = "CabinetSize";
    public const string Version = "1.3.8"; // 跟随套件 version.json（版本号由作者定义）

    internal static ManualLogSource Log;
    internal static volatile bool Rerun = true;

    /// <summary>家门口塑料袋专用包（VehicleConfig.DoorBoxBagId 常量镜像，与 SUV 后备箱共用）。</summary>
    internal const int DoorBoxBagId = 1023;

    public override void Load()
    {
        Log = base.Log;

        // 双开防御：前任 CabinetExpand 在场则自禁（两 mod 同写 Config_Bag.Size 必互相污染）
        try
        {
            if (IL2CPPChainloader.Instance?.Plugins?.ContainsKey("com.local.survivallog.cabinetexpand") == true)
            {
                Log.LogWarning("[CabinetSize] 检测到旧 mod CabinetExpand 仍在运行：两 mod 同写 Config_Bag.Size 会互相污染，"
                               + "本 mod 已自禁。请删除 BepInEx\\plugins\\SurvivalLog.CabinetExpand.dll 与其 cfg 后重启游戏。");
                return;
            }
        }
        catch (Exception e) { Log.LogWarning("[CabinetSize] 双开检测异常（继续加载）: " + e.Message); }

        // Task 2：cfg 绑定 + BagSections 初始化
        // Task 3/4：改写引擎 + Ticker

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<SizeTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<SizeTicker>();
        var go = new GameObject("CabinetSize.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SizeTicker>();

        SurvivalLog.ModShared.WelcomeTip.Install(Log);
        Log.LogInfo("[CabinetSize] loaded（数值型四档；上限/名单生效后见 ListOnReady 日志与 trace 文件）");
    }

    internal sealed class SizeTicker : MonoBehaviour
    {
        public SizeTicker(IntPtr ptr) : base(ptr) { }
        public SizeTicker() { }
        public void Update() { /* Task 3 填充 */ }
    }
}
