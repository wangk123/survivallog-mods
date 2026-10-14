using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SurvivalLog.ModShared
{
    /// <summary>
    /// 防盗署名气泡（2026-10-14）：进入存档后在主角头顶显示 3 秒欢迎文字。
    /// 通道 AgentModel.Unit_ShowHeadTalk(text, 3)（主角模型经 WebGm.GetLeadingRoleModel 一行获取）。
    /// 实测定稿：主角头顶条为官方无框文字样式（带框气泡仅 NPC 管线；silentHeadTalk 默认
    /// false、ImmediateShowTalk/ShowTalk 直调同样无框）；During≈真实秒（传 360 会远超 3 秒）。
    /// 7 个套件 mod 的 Load() 都调用 Install()，靠 GameObject 名去重：先到者创建
    /// DontDestroyOnLoad 物体挂本 ticker，其余插件直接跳过 → 装任意一个 mod 即有提示，
    /// 装多个也只弹一条。每次进档弹一次（BattleLogicWorld 重建沿复位），无 cfg 开关
    /// （防盗署名为内置行为，同时避开 cfg 首跑固化坑）。只读游戏状态，不改任何游戏数据。
    /// </summary>
    public static class WelcomeTip
    {
        // 文案长度按原生气泡宽度实测校准（过长会被截断）
        public const string Text = "欢迎！MOD作者Night免费分享，请支持正版（抖音号 166660371）";
        public const float ShowSec = 3f;
        private const string GoName = "SurvivalLog.WelcomeTip";

        internal static ManualLogSource Log; // 先到者插件的日志源，供 ticker 记录显示结果

        public static void Install(ManualLogSource log)
        {
            try
            {
                if (GameObject.Find(GoName) != null) return; // 另一个套件插件已挂载
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp<WelcomeTicker>())
                    ClassInjector.RegisterTypeInIl2Cpp<WelcomeTicker>();
                var go = new GameObject(GoName);
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<WelcomeTicker>();
                Log = log;
                log?.LogInfo("[WelcomeTip] 署名气泡已挂载：每次进档主角头顶显示 " + ShowSec + " 秒");
            }
            catch (Exception e)
            {
                log?.LogWarning("[WelcomeTip] 挂载失败（不影响其他功能）：" + e.Message);
            }
        }
    }

    /// <summary>轮询 BattleLogicWorld 生命周期沿；沿 false→true（进档）等主角模型就绪后弹一次。</summary>
    internal sealed class WelcomeTicker : MonoBehaviour
    {
        public WelcomeTicker(IntPtr ptr) : base(ptr) { }
        public WelcomeTicker() { }

        private const float PollSec = 0.5f;
        private const float ResolveTimeoutSec = 15f; // 进档后等主角模型就绪的最长时间，超时静默放弃

        private float _check;
        private bool _worldReady;
        private bool _shown;   // 本档已弹
        private bool _giveUp;  // 本档等不到模型，放弃
        private float _waited;

        public void Update()
        {
            _check += Time.unscaledDeltaTime;
            if (_check < PollSec) return;
            _check = 0f;

            bool ready;
            try
            {
                ready = GameCore.HotUpdate.BaseSingleton<GameCore.HotUpdate.Battle.Logic.BattleLogicWorld>.IsInstanceCreated;
            }
            catch { return; }

            if (!ready)
            {
                _worldReady = false; // 回主菜单/退档：下次进档再弹
                return;
            }

            if (!_worldReady)
            {
                _worldReady = true;
                _shown = false;
                _giveUp = false;
                _waited = 0f;
            }
            if (_shown || _giveUp) return;

            _waited += PollSec;
            if (_waited > ResolveTimeoutSec) { _giveUp = true; return; }

            try
            {
                var model = GameCore.HotUpdate.WebGm.GetLeadingRoleModel(out _);
                if (model == null) return; // ShowWorld 主角模型未就绪，下轮再试

                // 定稿（5 轮实测）：主角头顶条为官方无框文字样式（silentHeadTalk 本就 false，
                // ImmediateShowTalk/ShowTalk/直调全部无框，非本 mod 缺陷）；
                // During≈真实秒——传 3 恰好约 3 秒，传 360（游戏秒换算）会远超 3 秒。
                model.Unit_ShowHeadTalk(WelcomeTip.Text, WelcomeTip.ShowSec);

                _shown = true;
                WelcomeTip.Log?.LogInfo($"[WelcomeTip] 署名气泡已显示（3 秒文字版，等待 {_waited:0.0}s）");
            }
            catch { /* 下轮重试，超时自然放弃，绝不打扰游戏 */ }
        }
    }
}
