using EasyFramework.Core.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace EasyFramework.Services.Inputs
{
    /// <summary>事件总线发射器:把手势转发到 IEventBus。</summary>
    internal sealed class EventBusGestureEmitter : IGestureEmitter
    {
        readonly IEventBus _bus;
        public EventBusGestureEmitter(IEventBus bus) => _bus = bus;
        public void Emit(TapEvent e) => _bus.Publish(e);
        public void Emit(LongPressEvent e) => _bus.Publish(e);
        public void Emit(SwipeEvent e) => _bus.Publish(e);
        public void Emit(DragEvent e) => _bus.Publish(e);
    }

    public sealed class InputService : IInputService, ITickable
    {
        readonly IEventBus _bus;
        readonly GestureDetector _gestures;
        readonly PinchDetector _pinch;

        bool _pointerWasDown;
        bool _pointerBlockedByUi;

        public Vector2 MoveAxis { get; private set; }
        public bool IsPointerOverUI { get; private set; }

        public InputService(IEventBus bus)
        {
            _bus = bus;
            _gestures = new GestureDetector(new EventBusGestureEmitter(bus));
            _pinch = new PinchDetector(e => _bus.Publish(e));
        }

        public void Tick()
        {
            var time = Time.unscaledTime;
            _gestures.PixelScale = Screen.dpi > 0f ? Screen.dpi / 160f : 1f;
            var pinching = FeedPinch();
            if (pinching)
            {
                _gestures.Cancel();
                _pointerWasDown = false;
                _pointerBlockedByUi = false;
            }
            else
            {
                FeedPointer(time);
            }
            MoveAxis = ResolveMoveAxis();
        }

        void FeedPointer(float time)
        {
            Vector2 pos;
            bool down;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                pos = touch.primaryTouch.position.ReadValue();
                down = true;
                IsPointerOverUI = IsOverUi(touch.primaryTouch.touchId.ReadValue());
            }
            else if (touch != null && touch.primaryTouch.press.wasReleasedThisFrame)
            {
                pos = touch.primaryTouch.position.ReadValue();
                down = false;
                IsPointerOverUI = false;
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null)
                {
                    IsPointerOverUI = false;
                    HandleRelease(time);
                    return;
                }
                pos = mouse.position.ReadValue();
                down = mouse.leftButton.isPressed;
                IsPointerOverUI = IsOverUi();
            }

            if (down && !_pointerWasDown)
            {
                _pointerBlockedByUi = IsPointerOverUI;
                _pointerWasDown = true;
                if (!_pointerBlockedByUi)
                    _gestures.OnPointerDown(pos, time);
            }
            else if (down && _pointerBlockedByUi) { }
            else if (down && _pointerWasDown) { _gestures.OnPointerMove(pos, time); }
            else if (!down && _pointerWasDown)
            {
                if (!_pointerBlockedByUi)
                    _gestures.OnPointerUp(pos, time);
                _pointerWasDown = false;
                _pointerBlockedByUi = false;
            }
        }

        void HandleRelease(float time)
        {
            if (_pointerWasDown)
            {
                if (!_pointerBlockedByUi)
                    _gestures.OnPointerUp(Vector2.zero, time);
                _pointerWasDown = false;
                _pointerBlockedByUi = false;
            }
        }

        bool FeedPinch()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.touches.Count >= 2
                && touch.touches[0].press.isPressed && touch.touches[1].press.isPressed)
            {
                _pinch.Update(touch.touches[0].position.ReadValue(),
                              touch.touches[1].position.ReadValue());
                IsPointerOverUI = IsOverUi(touch.touches[0].touchId.ReadValue()) ||
                                  IsOverUi(touch.touches[1].touchId.ReadValue());
                return true;
            }
            else
            {
                _pinch.Reset();
                return false;
            }
        }

        static bool IsOverUi(int pointerId = -1)
            => EventSystem.current != null &&
               (pointerId >= 0
                   ? EventSystem.current.IsPointerOverGameObject(pointerId)
                   : EventSystem.current.IsPointerOverGameObject());

        Vector2 ResolveMoveAxis()
        {
            var joystick = VirtualJoystick.CombinedAxis;
            if (joystick.sqrMagnitude > 0.0001f) return joystick;

            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            var x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            var y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }
}
