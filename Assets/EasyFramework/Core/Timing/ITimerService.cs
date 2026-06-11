using System;

namespace EasyFramework.Core.Timing
{
    public interface ITimerService
    {
        TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false);
        void Cancel(TimerHandle handle);
    }
}
