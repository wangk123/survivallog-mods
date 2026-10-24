using System;
using System.Collections.Generic;

namespace ModCenter.Integration
{
    /// <summary>ModCenter 深度对接约定类型。
    /// 在你的插件类所在程序集定义 public static class ModCenterIntegration
    /// 并实现本接口即可被 ModCenter 发现（找不到时自动降级 Tier 1/0，不报错）。</summary>
    public interface IModCenterIntegration
    {
        /// <summary>协议版本，当前返回 1。</summary>
        int McSpec { get; }

        /// <summary>启停回调：enabled=false 时你的 mod 应立即停止全部生效行为
        /// （还原表值/卸下注入/放行官方逻辑），true 时恢复。
        /// 在 BepInEx 主线程调用。返回 true 表示已处理；返回 false 表示不支持软停
        /// （ModCenter 自动降级为重启级语义展示）。</summary>
        bool OnEnabledChanged(bool enabled);

        /// <summary>可选状态回报：返回给面板展示的运行时状态。
        /// 例：字节补丁安装失败时返回 failed 项让对应配置置灰。</summary>
        IReadOnlyList<McItemState> QueryStates();
    }

    /// <summary>单项运行时状态。</summary>
    public sealed class McItemState
    {
        /// <summary>cfg 键，格式 "Section:Key"；空串代表整个 mod。</summary>
        public string Key { get; set; } = "";
        /// <summary>ok=正常；failed=置灰并展示 Reason（如补丁未安装）。</summary>
        public string State { get; set; } = "ok";
        public string Reason { get; set; } = "";
    }
}
