using UnityEngine;

namespace EasyFramework.Services.Audio
{
    /// <summary>BGM 交叉淡变的纯逻辑(无 Unity 对象依赖),供 ITickable 手动驱动与 EditMode 单测。</summary>
    public sealed class CrossfadeState
    {
        readonly float _duration;
        readonly float _target;
        float _elapsed;

        public CrossfadeState(float fadeSeconds, float targetVolume)
        {
            _duration = Mathf.Max(0f, fadeSeconds);
            _target = Mathf.Clamp01(targetVolume);
            _elapsed = _duration <= 0f ? 1f : 0f; // 0 时长直接完成
        }

        /// <summary>淡入声道当前音量(0..target)。</summary>
        public float InVolume
        {
            get
            {
                var t = Progress;
                return _target * t;
            }
        }

        /// <summary>淡出声道当前音量(target..0)。</summary>
        public float OutVolume
        {
            get
            {
                var t = Progress;
                return _target * (1f - t);
            }
        }

        public bool IsDone => Progress >= 1f;

        float Progress => _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);

        public void Advance(float deltaTime)
        {
            if (_duration <= 0f) { _elapsed = 1f; return; }
            _elapsed += deltaTime;
        }
    }
}
