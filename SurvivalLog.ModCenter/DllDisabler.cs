using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace SurvivalLog.ModCenter;

/// <summary>
/// 第三方 dll 重启级禁用（任务 16，指南 §五启停语义协议的实现方）：
///
/// - **marker**：ModCenter 自身 cfg `[DisabledTp]` 节，key = plugins 目录下 dll 相对名
///   （如 <c>BetterTrade.dll</c>——dll 被禁用后插件不在链上，GUID 无从解析，故按文件名记账；
///   T6/T14 时代按 GUID 的 `[Disabled]` 名单随之退役，残留孤儿行见任务 16 报告清理记录）。
///   value=true 表示停用意图；懒 Bind 幂等，BepInEx Save 只写现役 Bind 项。
/// - **退出执行**：运行中 dll 被锁（OnApplicationQuit 时本进程仍持有），改名交**分离进程**
///   `powershell -NoProfile -EncodedCommand ...`（脚本 ASCII-only，经 base64(UTF-16LE) 彻底
///   避开引号/反斜杠转义坑）循环 Rename-Item 重试直到成功（等本进程退出释放锁，超时 10 分钟）。
///   **只改名（&lt;名&gt;.dll → &lt;名&gt;.dll.disabled），绝不删除**；BepInEx 只加载 .dll 后缀，
///   即下次启动不加载。启用 = 反向改名（dll 未加载不锁，直接 File.Move；失败才分离进程兜底）。
/// - **强杀残留自愈**：正常退出钩子没跑（taskkill/断电）→ dll 未改名但 marker=true。
///   Load 时解析自身 cfg 的 [DisabledTp] true 项与 plugins 目录对照检出残留 → 再拉一次分离进程
///   （本会话结束后生效）+ 面板下次打开 toast 提示一轮 + Tier 2 者软停回调重放（重申停用语义）。
/// - **快照缺席行**：plugins 里存在 <c>*.dll.disabled</c>（非第一方）→ 面板补一行 enabled=false
///   的"仅启停"行（guid=dll 相对名），供玩家重新启用（反向改名）。
/// 线程纪律：全部入口都在 Ticker 主线程（Load/Update/Quit/Pump）。
/// </summary>
internal static class DllDisabler
{
    private const string Section = "DisabledTp";
    private const string Suffix = ".disabled";
    private const int DisableRetryMs = 600000;   // 禁用分离进程总预算 10 分钟（进程退出后首个重试即成功）
    private const int ReverseRetryMs = 120000;   // 启用兜底分离进程 2 分钟

    // 会话内已 Bind 且当前值为 true 的 dll 名（退出执行与残留补拉的枚举源——cfg 无未 Bind 键枚举 API）
    private static readonly HashSet<string> MarkedTrue = new();
    // 残留补拉的分离进程（dll 名 → Process）：玩家当场反悔启用时撤销（改名意图已失效）
    private static readonly Dictionary<string, Process> Armed = new();
    // Load 检出的残留（dll 名）——软停重放 + toast 文案源
    private static readonly List<string> Residue = new();
    private static string _pendingToast; // 面板下次打开消费一轮（MC_OPEN → MC_TOAST）

    private static void Log(string msg) => ModCenterPlugin.Log(msg);

    // ============ 对外契约（ConfigBridge / ModCenterPlugin 消费） ============

    /// <summary>插件 dll 相对名（plugins 目录相对；Location 不在 plugins 下时退化为文件名）。</summary>
    internal static string DllRelOf(BepInEx.PluginInfo info)
    {
        try
        {
            var loc = info?.Location;
            if (string.IsNullOrEmpty(loc)) return null;
            var full = Path.GetFullPath(loc);
            var root = Paths.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length + 1);
            return Path.GetFileName(full);
        }
        catch { return null; }
    }

    /// <summary>marker 当前值（懒 Bind，读取已停用意图；Bind 失败按未停用兜底）。</summary>
    internal static bool MarkerOf(string dllRel)
    {
        try
        {
            var mk = Marker(dllRel);
            if (mk == null || !mk.Value) return false;
            MarkedTrue.Add(dllRel); // 会话内枚举源登记（quit 批/残留语义）
            return true;
        }
        catch { return false; }
    }

    /// <summary>停用：写 marker=true（DisabledTp）。退出钩子据此拉分离进程改名。</summary>
    internal static void Disable(string dllRel)
    {
        var mk = Marker(dllRel);
        if (mk == null) { Log("toggle 拒绝：marker entry 创建失败 " + dllRel); return; }
        MarkDirtySelf();
        mk.Value = true;
        MarkedTrue.Add(dllRel);
    }

    /// <summary>
    /// 启用：撤销残留补拉进程 → 若 dll 已被改名则反向改名（未加载不锁，直接 Move；
    /// 失败分离进程兜底）→ marker=false。
    /// </summary>
    internal static void Enable(string dllRel)
    {
        if (Armed.TryGetValue(dllRel, out var p))
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
            Armed.Remove(dllRel);
            Log($"enable: 已撤销残留禁用进程（{dllRel} 改名意图失效）");
        }
        var disabledPath = Path.Combine(Paths.PluginPath, dllRel + Suffix);
        try
        {
            if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, Path.Combine(Paths.PluginPath, dllRel));
                Log($"enable: dll 已恢复 {dllRel}（未加载不锁，直接反向改名）");
            }
        }
        catch (Exception e)
        {
            Log($"warn enable 直接改名失败，分离进程兜底（2 分钟内重试）: {e.Message}");
            SpawnReverse(dllRel);
        }
        try
        {
            var mk = Marker(dllRel);
            if (mk != null && mk.Value) { MarkDirtySelf(); mk.Value = false; }
        }
        catch (Exception e) { Log("warn enable marker 清除异常: " + e.Message); }
        MarkedTrue.Remove(dllRel);
    }

    /// <summary>Load 时残留检测（ModCenterPlugin.Load 末尾调）：cfg [DisabledTp] true 项 × plugins 目录对照。</summary>
    internal static void ScanOnLoad()
    {
        try
        {
            var trues = ParseMarkerTrues();
            foreach (var dllRel in trues)
            {
                var dll = Path.Combine(Paths.PluginPath, dllRel);
                var disabled = dll + Suffix;
                if (File.Exists(disabled))
                {
                    Marker(dllRel);
                    MarkedTrue.Add(dllRel); // 预 Bind：缺席行/再启用路由就绪
                    Log($"dll禁用 scan: {dllRel} 已停用（.disabled 在场）——面板将显示停用行");
                }
                else if (File.Exists(dll))
                {
                    Marker(dllRel);
                    MarkedTrue.Add(dllRel);
                    Residue.Add(dllRel);
                    SpawnDisableBatch(new List<string> { dllRel }, trackArmed: true);
                    Log($"dll禁用 scan: 残留检出 {dllRel}（marker=停用但 dll 未改名——上次进程被强杀？）"
                        + " → 已补拉分离进程（本会话仍运行，退出后改名生效）");
                }
                else
                {
                    Log($"dll禁用 scan: marker {dllRel} 对应 dll/.disabled 均不在场（惰性忽略）");
                }
            }
            if (Residue.Count > 0)
                _pendingToast = "上次停用未完成（游戏被强制结束）：" + string.Join("\u3001", Residue)
                    + " ——已补拉禁用，本次会话仍运行，退出游戏后生效";
        }
        catch (Exception e) { Log("warn dll禁用 scan 异常: " + e.Message); }
    }

    /// <summary>残留软停重放（Ticker 首帧调——此时 Chainloader 全部插件已就绪）：Tier 2 者重申 OnEnabledChanged(false)。</summary>
    internal static void ReapplySoft()
    {
        foreach (var dllRel in Residue.ToList())
        {
            try
            {
                var info = FindLoadedByDllRel(dllRel);
                if (info == null) continue;
                var ok = ThirdParty.FireEnabledChanged(info, false);
                Log($"dll禁用 residue 软停重放 {dllRel}: "
                    + (ok ? "OnEnabledChanged(false) 已处理（本会话行为已停止，退出后 dll 禁用）"
                          : "无 Tier 2 契约/已降级（重启级语义，本会话对方仍在运行）"));
            }
            catch (Exception e) { Log("warn residue 软停重放异常: " + e.Message); }
        }
    }

    /// <summary>退出执行（OnApplicationQuit 调；marker 落盘由调用方 FlushDirty 先行）：全部 true 项一批拉分离进程。</summary>
    internal static void OnQuit()
    {
        try
        {
            var pending = new List<string>();
            foreach (var dllRel in MarkedTrue)
            {
                try
                {
                    var mk = Marker(dllRel);
                    if (mk != null && mk.Value) pending.Add(dllRel);
                }
                catch { }
            }
            if (pending.Count == 0) { Log("dll禁用 quit: 无停用标记，跳过"); return; }
            SpawnDisableBatch(pending, trackArmed: false);
            Log($"dll禁用 quit: 分离进程已拉起（{pending.Count} 个：{string.Join(", ", pending)}），待本进程退出释放锁后改名 .disabled");
        }
        catch (Exception e) { Log("warn dll禁用 quit 异常: " + e.Message); }
    }

    /// <summary>plugins 目录 .disabled 扫描（快照缺席行源；第一方 SurvivalLog.* 不进第三方组）。
    /// **递归**（评审 P2：BepInEx 递归加载 plugins 子目录，禁用后的 <c>Pack\Mod.dll.disabled</c>
    /// 在子目录——非递归扫不到→缺席行永远不出现）；返回 plugins 相对名（与 marker/启用路径拼接同源）。</summary>
    internal static List<string> AbsentDisabled()
    {
        var list = new List<string>();
        try
        {
            var root = Paths.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (!name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var dllRel = name.Substring(0, name.Length - Suffix.Length);
                if (!dllRel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || dllRel.StartsWith("SurvivalLog.", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = f.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? f.Substring(root.Length + 1) : name; // 相对名（子目录含路径，与 DllRelOf 同源）
                if (!rel.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                rel = rel.Substring(0, rel.Length - Suffix.Length); // 剥 .disabled——marker/再启用路由用 dll 原名
                if (IsSafeRelName(rel)) list.Add(rel);
            }
        }
        catch (Exception e) { Log("warn .disabled 扫描异常: " + e.Message); }
        return list;
    }

    /// <summary>缺席行启停路由（快照行 guid=dll 相对名）：只有启用方向（该行本就停用）。</summary>
    internal static bool TrySetAbsent(string id, bool enabled)
    {
        try
        {
            if (!File.Exists(Path.Combine(Paths.PluginPath, id + Suffix))) return false; // 非缺席行
            if (!enabled) { Log("toggle 拒绝：该 mod 已是停用状态 " + id); return true; }
            Enable(id);
            Log($"toggle(tp-absent) {id} → 启用（dll 反向改名恢复，下次启动加载）");
            return true;
        }
        catch (Exception e) { Log("warn tp-absent 启停异常: " + e.Message); return true; }
    }

    /// <summary>残留提示（MC_OPEN 时消费一轮；无残留返回 null）。</summary>
    internal static string ConsumeToast()
    {
        if (_pendingToast == null) return null;
        var t = _pendingToast;
        _pendingToast = null;
        return t;
    }

    // ============ 内部实现 ============

    private static ConfigEntry<bool> Marker(string dllRel)
    {
        var self = ModCenterPlugin.Instance;
        if (self == null) return null;
        return self.Config.Bind(Section, dllRel, false,
            "第三方 mod 停用标记（true=已停用：退出时 dll 改名 .disabled，下次启动不加载）。key = plugins 目录下 dll 相对名。");
    }

    private static void MarkDirtySelf()
    {
        var self = ModCenterPlugin.Instance;
        if (self != null) ConfigBridge.MarkDirty(self.Config);
    }

    /// <summary>解析自身 cfg [DisabledTp] true 项（Load 时残留检测的枚举源——未 Bind 键无 API 枚举）。</summary>
    private static List<string> ParseMarkerTrues()
    {
        var trues = new List<string>();
        var path = ModCenterPlugin.Instance?.Config?.ConfigFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return trues;
        var cur = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
            {
                cur = line.Substring(1, line.Length - 2).Trim();
                continue;
            }
            if (!string.Equals(cur, Section, StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line.Substring(0, eq).Trim();
            var v = line.Substring(eq + 1).Trim();
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase) && k.Length > 0) trues.Add(k);
        }
        return trues;
    }

    private static BepInEx.PluginInfo FindLoadedByDllRel(string dllRel)
    {
        try
        {
            var plugins = BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance?.Plugins;
            if (plugins == null) return null;
            foreach (var info in plugins.Values)
                if (string.Equals(DllRelOf(info), dllRel, StringComparison.OrdinalIgnoreCase)) return info;
        }
        catch { }
        return null;
    }

    // ---- 分离进程（ASCII-only 命令行；-EncodedCommand = base64(UTF-16LE)，规避一切引号转义） ----

    /// <summary>禁用批：循环 Rename-Item 直到成功/目标消失（等本进程退出释放锁）。**只改名，绝不删除。**
    /// 子目录 dll（BepInEx 递归加载 plugins，如 <c>Pack\Mod.dll</c>）：<c>-NewName</c> 只吃文件名
    /// （吃带路径名必败→禁用静默失败，评审 P2），故喂 <c>Split-Path -Leaf</c>。名字过滤已放开
    /// （评审 P3-1）——EncodedCommand 无命令行注入面，PS 单引号串里 <c>'</c> 翻倍转义即一切合法文件名可用。</summary>
    private static void SpawnDisableBatch(List<string> names, bool trackArmed)
    {
        var usable = names.Where(IsSafeRelName).ToList(); // 仅剔绝对路径/盘符/../越界（边界守卫非字符白名单，评审 P3-1）
        if (usable.Count == 0) { Log("warn 禁用分离进程跳过：无合法 dll 名（" + string.Join(",", names) + "）"); return; }
        var b = Paths.PluginPath;
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='SilentlyContinue'\n");
        sb.Append("$b='").Append(b.Replace("'", "''")).Append("'\n");
        sb.Append("$names=@(").Append(string.Join(",", usable.Select(n => "'" + n.Replace("'", "''") + "'"))).Append(")\n");
        sb.Append("$end=(Get-Date).AddSeconds(").Append((DisableRetryMs / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(")\n");
        sb.Append("while((Get-Date) -lt $end){\n");
        sb.Append("  $left=0\n");
        sb.Append("  foreach($n in $names){\n");
        sb.Append("    $s=Join-Path $b $n\n");
        sb.Append("    $d=$s+'.disabled'\n");
        sb.Append("    if(Test-Path -LiteralPath $d){continue}\n");
        sb.Append("    if(-not(Test-Path -LiteralPath $s)){continue}\n");
        sb.Append("    $left=1\n");
        sb.Append("    try{Rename-Item -LiteralPath $s -NewName ((Split-Path -Leaf $n)+'.disabled') -ErrorAction Stop}catch{}\n"); // P2：子目录相对名须降为纯文件名（-NewName 不吃路径）
        sb.Append("  }\n");
        sb.Append("  if($left -eq 0){break}\n");
        sb.Append("  Start-Sleep -Milliseconds 1500\n");
        sb.Append("}\n");
        var p = StartDetached(sb.ToString());
        if (p != null && trackArmed && usable.Count == 1) Armed[usable[0]] = p; // 残留补拉单靶才需可撤销
    }

    /// <summary>启用兜底：反向改名重试（dll 未加载本不该锁；AV/索引器短暂占用时兜底）。
    /// 子目录相对名同样须 <c>Split-Path -Leaf</c>（P2）。</summary>
    private static void SpawnReverse(string dllRel)
    {
        if (!IsSafeRelName(dllRel)) { Log("warn 启用兜底跳过：非法相对名 " + dllRel); return; }
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='SilentlyContinue'\n");
        sb.Append("$s='").Append(Path.Combine(Paths.PluginPath, dllRel + Suffix).Replace("'", "''")).Append("'\n");
        sb.Append("$n='").Append(dllRel.Replace("'", "''")).Append("'\n");
        sb.Append("$end=(Get-Date).AddSeconds(").Append((ReverseRetryMs / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(")\n");
        sb.Append("while((Get-Date) -lt $end){\n");
        sb.Append("  if(-not(Test-Path -LiteralPath $s)){break}\n");
        sb.Append("  try{Rename-Item -LiteralPath $s -NewName (Split-Path -Leaf $n) -ErrorAction Stop;break}catch{}\n");
        sb.Append("  Start-Sleep -Milliseconds 1000\n");
        sb.Append("}\n");
        StartDetached(sb.ToString());
    }

    /// <summary>相对名边界守卫（评审 P3-1：非字符白名单——EncodedCommand 无注入面，' 翻倍转义放行；
    /// 只剔能越出 plugins 目录的形态：空/绝对路径（盘符或反斜杠开头）/ 含 .. 段）。</summary>
    private static bool IsSafeRelName(string n)
    {
        if (string.IsNullOrEmpty(n) || n.IndexOf(':') >= 0
            || n[0] == '\\' || n[0] == '/') return false;
        foreach (var seg in n.Split('\\', '/'))
            if (seg == "..") return false;
        return true;
    }

    private static Process StartDetached(string psScript)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -WindowStyle Hidden -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript)),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return Process.Start(psi);
        }
        catch (Exception e)
        {
            Log("warn 分离进程启动失败: " + e.Message);
            return null;
        }
    }
}
