using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Timing;
using PrimeTween;
using UnityEngine;

namespace EasyFramework.Services.Juice
{
    public sealed class JuiceService : IJuiceService, IDisposable
    {
        readonly ITimerService _timer;
        readonly HitStopState _hitStop = new();
        TimerHandle _restoreTimer;
        UniTaskCompletionSource _hitStopCompletion;
        float _previousTimeScale = 1f;
        bool _hasRestoreTimer;
        bool _disposed;

        public JuiceService(ITimerService timer)
            => _timer = timer ?? throw new ArgumentNullException(nameof(timer));

        public void PunchScale(Transform target, float strength = 0.2f, float duration = 0.25f)
        {
            ThrowIfDisposed();
            ValidateDuration(duration);
            if (target != null)
                Tween.PunchScale(target, Vector3.one * strength, duration);
        }

        public void Flash(SpriteRenderer renderer, Color color, float duration = 0.1f)
        {
            ThrowIfDisposed();
            ValidateDuration(duration);
            if (renderer == null) return;
            var original = renderer.color;
            Tween.Custom(color, original, duration, c =>
            {
                if (renderer != null) renderer.color = c;
            });
        }

        public UniTask HitStopAsync(
            float duration = 0.05f, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ValidateDuration(duration);
            if (duration <= 0f)
                return UniTask.CompletedTask;

            var now = Time.unscaledTime;
            var entering = _hitStop.Request(duration, now);
            if (entering)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                _hitStopCompletion = new UniTaskCompletionSource();
            }
            if (_hasRestoreTimer)
                _timer.Cancel(_restoreTimer);
            ScheduleRestore(_hitStop.Remaining(now));
            return _hitStopCompletion.Task.AttachExternalCancellation(ct);
        }

        void ScheduleRestore(float delay)
        {
            _restoreTimer = _timer.Schedule(delay, RestoreOrReschedule,
                repeat: false, useUnscaledTime: true);
            _hasRestoreTimer = true;
        }

        void RestoreOrReschedule()
        {
            _hasRestoreTimer = false;
            if (_disposed || !_hitStop.IsActive) return;
            var now = Time.unscaledTime;
            if (!_hitStop.ShouldRestore(now))
            {
                ScheduleRestore(_hitStop.Remaining(now));
                return;
            }
            RestoreTime();
            _hitStopCompletion?.TrySetResult();
        }

        void RestoreTime()
        {
            if (!_hitStop.IsActive) return;
            _hitStop.Restore();
            Time.timeScale = _previousTimeScale;
        }

        static void ValidateDuration(float duration)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JuiceService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hasRestoreTimer)
                _timer.Cancel(_restoreTimer);
            _hasRestoreTimer = false;
            RestoreTime();
            _hitStopCompletion?.TrySetCanceled();
        }
    }
}
