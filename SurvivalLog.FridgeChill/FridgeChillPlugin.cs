using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SurvivalLog.ModShared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivalLog.FridgeChill;

/// <summary>
/// 冰箱冷藏增强（独立 mod 4/4）：保鲜时长按倍数延长。
///
/// 实证（反汇编 ComputeItemTimeScale / FoodPreservationRules）：
/// - 家具保鲜时长倍率 = 1 / Config_Furniture.ColdRate（ColdRate=0.2 → 保质 ×5）
/// - 天赋（Buff/AE_FridgeColdEnhance）按比例压低 ColdRate，下限 0.1，与本 mod 改表独立叠加
/// - 界面上的"保鲜率"显示（Round(1/ColdRate)）读同一字段，会自动跟随
/// 本 mod 对冰箱包名单内的家具把 ColdRate ÷ KeepMult（默认 2 = 保质期翻倍）。
/// 注意：只影响单家具冷藏；冷链房间（FridgeAreaMap/areaScale）与堆肥箱不在范围内。
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public sealed class FridgeChillPlugin : BasePlugin
{
    public const string Guid = "com.local.survivallog.fridgechill";
    public const string Name = "FridgeChill";
    public const string Version = "1.3.6";

    internal static ManualLogSource Log;

    internal static ConfigEntry<float> KeepMult;
    internal static ConfigEntry<float> MaxTotalMult;
    internal static ConfigEntry<string> BagIds;
    internal static ConfigEntry<bool> Verbose;

    internal static readonly int[] DefaultBagIds = { 1004, 115000, 115001, 115005, 3002 };
    internal static volatile bool Rerun = true;

    public override void Load()
    {
        Log = base.Log;
        KeepMult = Config.Bind("Chill", "KeepMult", 2f,
            "保鲜倍数：2 = 食物在冰箱里的保质期翻倍（ColdRate÷2）。1 = 不改。");
        MaxTotalMult = Config.Bind("Chill", "MaxTotalMult", 100f,
            "总保鲜倍数上限：表值改写与天赋/规划等加成合并后的封顶。默认 100；10 = 原生上限 ×10（即不抬高）。保鲜关闭（KeepMult≤1）时本项不生效。");
        BagIds = Config.Bind("Chill", "BagIds", "",
            "冰箱 BagId 名单（逗号分隔；留空用默认 1004,115000,115001,115005,3002，可追加）。");
        Verbose = Config.Bind("Debug", "Verbose", false, "输出每条明细。");

        System.EventHandler reload = (object s, System.EventArgs e) => { Rerun = true; };
        KeepMult.SettingChanged += (object s2, System.EventArgs e2) => { Rerun = true; TalentCapBytePatch.UpdateFloor(); };
        BagIds.SettingChanged += reload;
        MaxTotalMult.SettingChanged += (object s3, System.EventArgs e3) =>
        {
            TalentCapBytePatch.UpdateFloor();
            Log.LogInfo($"[FridgeChill] 总保鲜封顶调整为 ×{Math.Max(10f, MaxTotalMult.Value):0.##}（即时生效）");
        };

        // 抬高保鲜总上限（TalentCapBytePatch）：原生 MinColdRate=0.1 会把本 mod 表值之上的
        // 天赋加成钳为零（2026-10-18 反汇编实证，详见该类注释与 DESIGN.md §2）。
        // 零开销字节补丁，不用 Harmony——挂钩在物品密集界面（OnSyncItem/ExecuteSwap/交易 UI）
        // 每次调用都有原生↔托管跳转开销，实测卡顿。
        TalentCapBytePatch.Install(MaxTotalMult, KeepMult, Log);

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<ChillTicker>())
            ClassInjector.RegisterTypeInIl2Cpp<ChillTicker>();
        var go = new GameObject("FridgeChill.Ticker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ChillTicker>();
        Log.LogInfo($"[FridgeChill] loaded：保鲜 ×{KeepMult.Value}，总封顶 ×{Math.Max(10f, MaxTotalMult.Value):0.##}");
        SurvivalLog.ModShared.WelcomeTip.Install(Log);
    }

    internal static HashSet<int> BuildTargets()
    {
        var set = new HashSet<int>(DefaultBagIds);
        foreach (var id in BagTable.ParseIds(BagIds.Value)) set.Add(id);
        return set;
    }
}

internal sealed class ChillTicker : MonoBehaviour
{
    public ChillTicker(IntPtr ptr) : base(ptr) { }
    public ChillTicker() { }

    private float _check;
    private bool _announced;

    public void Update()
    {
        // 只改系数（ColdRate），绝不直接改物品字段（v1.1.0 教训：动 Data_Item.TimeScale
        // 会污染游戏进出容器换算，菠菜 13 天一进一出变 6.5 天）。
        // 已在冰箱里的旧物品：下次进出容器时游戏按新系数重算，速率自然翻倍。
        if (FridgeChillPlugin.KeepMult.Value <= 1.01f) return;

        _check += Time.deltaTime;
        var due = FridgeChillPlugin.Rerun || _check >= 5f;
        if (!due) return;
        _check = 0f;

        var targets = FridgeChillPlugin.BuildTargets();
        var (scanned, changed, lines) = BagTable.RewriteFridgeChill(
            targets, FridgeChillPlugin.KeepMult.Value, FridgeChillPlugin.Verbose.Value);
        // v1.1.0 存档修复器已于 2026-10-18 彻底移除（实测误伤合法天赋档 0.5→1.0、
        // 显示保质翻倍；"绝不写 Data_Item"红线自此零例外）。

        if (scanned < 0) { FridgeChillPlugin.Rerun = true; return; }
        FridgeChillPlugin.Rerun = false;

        if (changed > 0 || FridgeChillPlugin.Verbose.Value)
        {
            foreach (var l in lines) FridgeChillPlugin.Log.LogInfo("[FridgeChill] " + l);
        }

        if (!_announced && scanned >= 0)
        {
            _announced = true;
            FridgeChillPlugin.Log.LogInfo(
                $"[FridgeChill] 生效：{scanned} 个冰箱家具受管（原生保鲜 ×5 → 现 ×{5 * FridgeChillPlugin.KeepMult.Value:0.#}，仅改系数；总封顶 ×{Math.Max(10f, FridgeChillPlugin.MaxTotalMult.Value):0.##}，天赋/规划可继续叠加）。");
        }
    }
}

/// <summary>
/// 保鲜总上限抬高（2026-10-18 实证后落地；同日二次改为零开销字节补丁）。
///
/// 原生公式（FoodPreservationRules.ApplyFridgeColdEnhance，@0x292E200）：
///   coldRate >= 1 或 talentRatio <= 0 → 原样返回 coldRate（不经过下限）；
///   否则 → max(coldRate × (1 − talentRatio), 0.1)。   // 0.1=MinColdRate，乘完天赋后才封底
/// 后果：本 mod 表值 0.1 已坐在下限上，任何天赋比率都被钳回 0.1（天赋零收益、显示恒 ×10）。
///
/// 实现演变：第一版用 Harmony Postfix 挂钩，实测在物品密集界面（调用方地图含
/// ItemManager.OnSyncItem / ExecuteSwap / ComputeItemTimeScale / ReduxUI 三个显示函数等 12 处）
/// 每次调用都付原生↔托管跳转成本，无人机交易界面明显卡顿——改为本字节补丁：
/// 只改写函数内读取 0.1 常量的那条 movss 的 rel32，指向自备 scratch float（= 1/MaxTotalMult），
/// 原生代码照常执行，每次调用零额外开销。0.1 槽位被全代码段 178 处共享，故必须重定向
/// 而非直改槽值。ApplyColdChainEnhance 与本函数同一段代码（编译器合并），一处补丁覆盖
/// 冰箱/冷链房间两条通道（冷链封顶同步抬高，属一致行为）；仓储/背包通道不经过此函数，零影响。
///
/// 安全闸（任何一步不符即放弃、功能关闭、落日志，绝不盲写）：
///   ① PE 段表解析 il2cpp 段 → ② 20 字节签名扫描（rel32 通配）→ ③ 旧槽位读值 ≈0.1 双重校验
///   → ④ 补丁后回读指令字节核对。scratch 写在函数间 int3 对齐间隙（不可执行路径）。
/// 游戏更新后布局漂移由签名扫描吸收；扫描失败=功能自动关闭（回落原生 ×10 封顶，无其他影响）。
/// </summary>
internal static class TalentCapBytePatch
{
    // subss xmm3,xmm1 / maxss xmm2,xmm3 / mulss xmm2,xmm0 / movss xmm0,[rel32] / maxss xmm0,xmm2 / ret
    private static readonly byte[] Sig =
    {
        0xF3,0x0F,0x5C,0xD9,
        0xF3,0x0F,0x5F,0xD3,
        0xF3,0x0F,0x59,0xD0,
        0xF3,0x0F,0x10,0x05, 0,0,0,0,   // rel32 通配
        0xF3,0x0F,0x5F,0xC2,
        0xC3
    };
    private const int Rel32Off = 16;

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint newProt, out uint oldProt);

    internal static ManualLogSource Log;
    private static IntPtr _scratch = IntPtr.Zero;   // scratch float 地址（int3 间隙内）
    private static float _floor = 0.1f;

    internal static void Install(ConfigEntry<float> maxTotal, ConfigEntry<float> keepMult, ManualLogSource log)
    {
        Log = log;
        try
        {
            var mod = Process.GetCurrentProcess().Modules;
            ProcessModule gameAsm = null;
            for (int i = 0; i < mod.Count; i++)
                if (string.Equals(mod[i].ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    { gameAsm = mod[i]; break; }
            if (gameAsm == null) { log.LogError("[FridgeChill] 天赋上限补丁：未找到 GameAssembly 模块，功能关闭"); return; }
            var baseAddr = gameAsm.BaseAddress;

            // ① 解析 PE 段表，取 il2cpp 段范围
            uint peOff = ReadU32(baseAddr, 0x3c);
            int nSec = ReadU16(baseAddr, (int)(peOff + 6));
            int optSize = ReadU16(baseAddr, (int)(peOff + 20));
            int secOff = (int)(peOff + 24 + optSize);
            IntPtr secStart = IntPtr.Zero; uint secLen = 0;
            for (int i = 0; i < nSec; i++)
            {
                var name = Marshal.PtrToStringAnsi((IntPtr)((long)baseAddr + secOff + i * 40), 8);
                if (name != null && name.StartsWith("il2cpp"))
                {
                    secStart = (IntPtr)((long)baseAddr + ReadU32(baseAddr, secOff + i * 40 + 12));
                    secLen = ReadU32(baseAddr, secOff + i * 40 + 8);
                    break;
                }
            }
            if (secStart == IntPtr.Zero) { log.LogError("[FridgeChill] 天赋上限补丁：未找到 il2cpp 段，功能关闭"); return; }

            // ② 签名扫描（rel32 通配）
            var buf = new byte[secLen];
            Marshal.Copy(secStart, buf, 0, buf.Length);
            int hit = -1, hits = 0;
            for (int i = 0; i <= buf.Length - Sig.Length; i++)
            {
                int j = 0;
                for (; j < Sig.Length; j++)
                {
                    if (j >= Rel32Off && j < Rel32Off + 4) continue; // 通配
                    if (buf[i + j] != Sig[j]) break;
                }
                if (j == Sig.Length)
                {
                    hits++;
                    if (hit < 0) hit = i;
                }
            }
            if (hit < 0) { log.LogWarning("[FridgeChill] 天赋上限补丁：签名未命中（游戏更新可能改了实现），功能关闭，回落原生 ×10 封顶"); return; }

            // ③ 双重校验：旧 rel32 目标读值应 ≈0.1（MinColdRate）
            var insn = (IntPtr)((long)secStart + hit);
            int rel32 = BitConverter.ToInt32(buf, hit + Rel32Off);
            var oldSlot = (IntPtr)((long)insn + Rel32Off + 4 + rel32);
            float oldVal = ReadF32(oldSlot);
            if (Math.Abs(oldVal - 0.1f) > 1e-4f)
            { log.LogWarning($"[FridgeChill] 天赋上限补丁：旧常量槽读值 {oldVal:0.####}≠0.1（游戏实现已变），功能关闭"); return; }

            // ④ scratch：il2cpp 段内找 ≥8 字节 int3 间隙（函数对齐填充，永不执行）
            int gap = -1, run = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] == 0xCC) { run++; if (run >= 8 && gap < 0) gap = i - run + 1; }
                else run = 0;
            }
            if (gap < 0) { log.LogWarning("[FridgeChill] 天赋上限补丁：无 int3 间隙可用，功能关闭"); return; }
            _scratch = (IntPtr)((long)secStart + gap);
            VirtualProtect(_scratch, (UIntPtr)8, 0x04, out _); // PAGE_READWRITE

            // ⑤ 改写目标指令 rel32 → scratch
            VirtualProtect(insn, (UIntPtr)8, 0x40, out var oldProt); // PAGE_EXECUTE_READWRITE
            int newRel = (int)((long)_scratch - ((long)insn + Rel32Off + 4));
            Marshal.WriteInt32(insn, Rel32Off, newRel);
            VirtualProtect(insn, (UIntPtr)8, oldProt, out _);

            // ⑥ 回读自证
            int rb = Marshal.ReadInt32(insn, Rel32Off);
            _floor = ComputeFloor(maxTotal, keepMult);
            WriteF32(_scratch, _floor);
            float rbF = ReadF32(_scratch);
            if (rb != newRel)
            { log.LogError($"[FridgeChill] 天赋上限补丁：回读不一致（rel32={rb:X8}），已还原，功能关闭"); Marshal.WriteInt32(insn, Rel32Off, rel32); _scratch = IntPtr.Zero; return; }

            log.LogInfo($"[FridgeChill] 天赋上限字节补丁生效：指令 @0x{(long)insn - (long)baseAddr:X}，" +
                        $"scratch @0x{(long)_scratch - (long)baseAddr:X}={rbF:0.####}（总封顶 ×{1f / rbF:0.##}，原生 ×10；" +
                        $"签名命中 {hits} 处取 1）——表值改写与天赋/规划加成纯乘法叠加，零逐次调用开销");
        }
        catch (Exception e)
        {
            _scratch = IntPtr.Zero;
            log.LogError($"[FridgeChill] 天赋上限补丁异常，功能关闭（回落原生 ×10 封顶）：{e.Message}");
        }
    }

    /// <summary>cfg 变更即时生效：只重写 scratch float，不动指令。</summary>
    internal static void UpdateFloor()
    {
        if (_scratch == IntPtr.Zero) return;
        try
        {
            _floor = ComputeFloor(FridgeChillPlugin.MaxTotalMult, FridgeChillPlugin.KeepMult);
            WriteF32(_scratch, _floor);
            Log?.LogInfo($"[FridgeChill] 保鲜下限更新为 {_floor:0.####}（总封顶 ×{1f / _floor:0.##}）");
        }
        catch (Exception e) { Log?.LogWarning("[FridgeChill] 保鲜下限更新失败: " + e.Message); }
    }

    private static float ComputeFloor(ConfigEntry<float> maxTotal, ConfigEntry<float> keepMult)
    {
        // 保鲜关闭 → 原生语义（0.1）；上限不允许低于原生 ×10
        if (keepMult != null && keepMult.Value <= 1.01f) return 0.1f;
        float cap = maxTotal?.Value ?? 100f;
        if (cap < 10f) cap = 10f;
        return 1f / cap;
    }

    private static uint ReadU32(IntPtr baseAddr, int off) => (uint)Marshal.ReadInt32(baseAddr, off);
    private static int ReadU16(IntPtr baseAddr, int off) => Marshal.ReadInt16(baseAddr, off);
    private static float ReadF32(IntPtr p) => BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(p)), 0);
    private static void WriteF32(IntPtr p, float v) => Marshal.WriteInt32(p, BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
}
