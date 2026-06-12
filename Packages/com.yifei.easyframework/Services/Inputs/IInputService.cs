namespace EasyFramework.Services.Inputs
{
    public enum SwipeDirection { Up, Down, Left, Right }

    public enum DragPhase { Start, Move, End }

    public readonly struct TapEvent
    {
        public readonly UnityEngine.Vector2 ScreenPosition;
        public TapEvent(UnityEngine.Vector2 pos) => ScreenPosition = pos;
    }

    public readonly struct LongPressEvent
    {
        public readonly UnityEngine.Vector2 ScreenPosition;
        public LongPressEvent(UnityEngine.Vector2 pos) => ScreenPosition = pos;
    }

    public readonly struct SwipeEvent
    {
        public readonly UnityEngine.Vector2 Start;
        public readonly UnityEngine.Vector2 End;
        public readonly SwipeDirection Direction;
        public SwipeEvent(UnityEngine.Vector2 start, UnityEngine.Vector2 end, SwipeDirection dir)
        {
            Start = start; End = end; Direction = dir;
        }
    }

    public readonly struct DragEvent
    {
        public readonly DragPhase Phase;
        public readonly UnityEngine.Vector2 Position;
        public readonly UnityEngine.Vector2 Delta;
        public DragEvent(DragPhase phase, UnityEngine.Vector2 position, UnityEngine.Vector2 delta)
        {
            Phase = phase; Position = position; Delta = delta;
        }
    }

    public readonly struct PinchEvent
    {
        public readonly float DeltaScale;
        public PinchEvent(float deltaScale) => DeltaScale = deltaScale;
    }

    public interface IInputService
    {
        UnityEngine.Vector2 MoveAxis { get; }
        bool IsPointerOverUI { get; }
    }
}
