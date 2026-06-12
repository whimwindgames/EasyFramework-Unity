namespace EasyFramework.Core.Timing
{
    public readonly struct TimerHandle
    {
        internal readonly long Id;
        internal TimerHandle(long id) => Id = id;
        public bool IsValid => Id != 0;
    }
}
