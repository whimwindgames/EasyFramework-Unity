using UnityEngine;

namespace EasyFramework.Services.Inputs
{
    /// <summary>单指手势状态机,纯逻辑无 Unity 输入依赖。坐标为屏幕像素,Y 向上。</summary>
    public sealed class GestureDetector
    {
        public const float TapMaxDuration = 0.3f;
        public const float MoveThreshold = 20f;      // 超过即非 Tap / 进 Drag
        public const float LongPressDuration = 0.5f;
        public const float SwipeMinDistance = 80f;
        public const float SwipeMaxDuration = 0.5f;

        readonly IGestureEmitter _emitter;

        bool _down;
        Vector2 _startPos;
        float _startTime;
        Vector2 _lastPos;
        bool _dragging;
        bool _longPressFired;

        public GestureDetector(IGestureEmitter emitter) => _emitter = emitter;

        public void OnPointerDown(Vector2 pos, float time)
        {
            _down = true;
            _startPos = _lastPos = pos;
            _startTime = time;
            _dragging = false;
            _longPressFired = false;
        }

        public void OnPointerMove(Vector2 pos, float time)
        {
            if (!_down) return;
            var totalDist = Vector2.Distance(pos, _startPos);

            if (!_dragging && totalDist >= MoveThreshold)
            {
                _dragging = true;
                _emitter.Emit(new DragEvent(DragPhase.Start, pos, pos - _startPos));
                _lastPos = pos;
                return;
            }

            if (_dragging)
            {
                _emitter.Emit(new DragEvent(DragPhase.Move, pos, pos - _lastPos));
                _lastPos = pos;
                return;
            }

            // 仍静止:检查长按
            if (!_longPressFired && totalDist < MoveThreshold
                && time - _startTime >= LongPressDuration)
            {
                _longPressFired = true;
                _emitter.Emit(new LongPressEvent(pos));
            }
            _lastPos = pos;
        }

        public void OnPointerUp(Vector2 pos, float time)
        {
            if (!_down) return;
            _down = false;
            var duration = time - _startTime;
            var delta = pos - _startPos;
            var dist = delta.magnitude;

            if (_dragging)
            {
                _emitter.Emit(new DragEvent(DragPhase.End, pos, pos - _lastPos));
                return;
            }

            if (_longPressFired) return; // 已长按,松手不再判其它

            if (dist >= SwipeMinDistance && duration < SwipeMaxDuration)
            {
                _emitter.Emit(new SwipeEvent(_startPos, pos, MainAxis(delta)));
                return;
            }

            if (dist < MoveThreshold && duration < TapMaxDuration)
            {
                _emitter.Emit(new TapEvent(pos));
            }
            // 其余(慢速大位移但未触发 drag 等)不发任何事件
        }

        static SwipeDirection MainAxis(Vector2 d)
        {
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
                return d.x >= 0 ? SwipeDirection.Right : SwipeDirection.Left;
            return d.y >= 0 ? SwipeDirection.Up : SwipeDirection.Down;
        }
    }
}
