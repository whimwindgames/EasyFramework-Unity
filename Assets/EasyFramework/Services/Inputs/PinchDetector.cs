using System;
using UnityEngine;

namespace EasyFramework.Services.Inputs
{
    /// <summary>双指捏合检测,纯逻辑。DeltaScale = 当前帧双指距离 / 上一帧距离。</summary>
    public sealed class PinchDetector
    {
        readonly Action<PinchEvent> _onPinch;
        float _lastDistance;
        bool _hasBaseline;

        public PinchDetector(Action<PinchEvent> onPinch) => _onPinch = onPinch;

        public void Update(Vector2 finger0, Vector2 finger1)
        {
            var dist = Vector2.Distance(finger0, finger1);
            if (!_hasBaseline || _lastDistance <= Mathf.Epsilon)
            {
                _lastDistance = dist;
                _hasBaseline = true;
                return;
            }
            var scale = dist / _lastDistance;
            _lastDistance = dist;
            _onPinch(new PinchEvent(scale));
        }

        /// <summary>松指或单指,清除基线;下次 Update 重新建立。</summary>
        public void Reset()
        {
            _hasBaseline = false;
            _lastDistance = 0f;
        }
    }
}
