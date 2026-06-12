namespace EasyFramework.Services.Inputs
{
    /// <summary>手势检测器的输出口;生产实现把事件转发到 IEventBus,测试用记录型替身。</summary>
    public interface IGestureEmitter
    {
        void Emit(TapEvent e);
        void Emit(LongPressEvent e);
        void Emit(SwipeEvent e);
        void Emit(DragEvent e);
    }
}
