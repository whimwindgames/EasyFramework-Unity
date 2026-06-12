using UnityEngine;

namespace EasyFramework.Services.Juice
{
    /// <summary>顿帧重入逻辑(纯,无 Unity 时间依赖,时间由调用方传入)。
    /// 多次 Request 只延长恢复 deadline,不叠加恢复动作。</summary>
    public sealed class HitStopState
    {
        float _restoreAt;

        public bool IsActive { get; private set; }

        /// <summary>请求顿帧;返回 true 表示「本次是进入」(调用方应置 timeScale=0)。</summary>
        public bool Request(float duration, float now)
        {
            var deadline = now + Mathf.Max(0f, duration);
            if (IsActive)
            {
                if (deadline > _restoreAt) _restoreAt = deadline; // 只延长,不缩短
                return false;
            }
            IsActive = true;
            _restoreAt = deadline;
            return true;
        }

        public bool ShouldRestore(float now) => IsActive && now >= _restoreAt;

        public void Restore() => IsActive = false;
    }
}
