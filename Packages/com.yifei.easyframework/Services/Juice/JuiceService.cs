using Cysharp.Threading.Tasks;
using EasyFramework.Core.Timing;
using PrimeTween;
using UnityEngine;

namespace EasyFramework.Services.Juice
{
    /// <summary>手感套件:punch / 闪白 / 顿帧。视觉缓动用 PrimeTween;顿帧重入逻辑见 HitStopState。
    /// PrimeTween API 名(Tween.PunchScale / Tween.Custom)以安装版本为准(验证代理 unity_reflect 核对)。</summary>
    public sealed class JuiceService : IJuiceService
    {
        readonly ITimerService _timer;
        readonly HitStopState _hitStop = new();

        public JuiceService(ITimerService timer) => _timer = timer;

        public void PunchScale(Transform target, float strength = 0.2f, float duration = 0.25f)
        {
            if (target == null) return;
            Tween.PunchScale(target, Vector3.one * strength, duration);
        }

        public void Flash(SpriteRenderer renderer, Color color, float duration = 0.1f)
        {
            if (renderer == null) return;
            var original = renderer.color;
            // 闪到 color 再回到原色;用 Custom 驱动 color(PrimeTween 也有 Tween.Color,验证代理择优)
            Tween.Custom(color, original, duration, c => renderer.color = c);
        }

        public UniTask HitStopAsync(float duration = 0.05f)
        {
            var now = Time.unscaledTime;
            var entering = _hitStop.Request(duration, now);
            if (!entering)
                return UniTask.CompletedTask; // 重入:已有恢复定时器在跑,只延长了 deadline

            Time.timeScale = 0f;
            ScheduleRestore(duration);
            return UniTask.CompletedTask;
        }

        void ScheduleRestore(float delay)
        {
            _timer.Schedule(delay, () =>
            {
                var now = Time.unscaledTime;
                if (_hitStop.ShouldRestore(now))
                {
                    _hitStop.Restore();
                    Time.timeScale = 1f;
                }
                else
                {
                    // 被延长:重排到剩余时间后再查
                    ScheduleRestore(0.01f);
                }
            }, repeat: false, useUnscaledTime: true);
        }
    }
}
