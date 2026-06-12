using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.Services.Inputs
{
    /// <summary>uGUI 虚拟摇杆。输出归一化方向(死区外 magnitude 0..1)。静态注册表供 InputService 合流。</summary>
    public sealed class VirtualJoystick : MonoBehaviour,
        IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        static readonly List<VirtualJoystick> _registry = new();

        /// <summary>当前所有活跃(被按下)的摇杆中第一个的输出;无则 Vector2.zero。</summary>
        public static Vector2 CombinedAxis
        {
            get
            {
                foreach (var j in _registry)
                    if (j != null && j._pressed)
                        return j.Value;
                return Vector2.zero;
            }
        }

        [SerializeField] RectTransform _handle;
        [SerializeField] RectTransform _background;
        [SerializeField] float _radius = 80f;

        Vector2 _value;
        bool _pressed;

        public Vector2 Value => _value;

        void OnEnable() { if (!_registry.Contains(this)) _registry.Add(this); }
        void OnDisable() { _registry.Remove(this); ResetStick(); }

        public void OnPointerDown(PointerEventData e)
        {
            _pressed = true;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            var origin = _background != null
                ? (Vector2)_background.position
                : (Vector2)transform.position;
            var offset = e.position - origin;
            var clamped = Vector2.ClampMagnitude(offset, _radius);
            _value = _radius > 0f ? clamped / _radius : Vector2.zero;
            if (_handle != null) _handle.position = origin + clamped;
        }

        public void OnPointerUp(PointerEventData e) => ResetStick();

        void ResetStick()
        {
            _pressed = false;
            _value = Vector2.zero;
            if (_handle != null && _background != null)
                _handle.position = _background.position;
        }
    }
}
