using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using VContainer;

namespace EasyFramework
{
    /// <summary>业务层快速访问门面。框架内部禁止使用,模块间一律构造注入。</summary>
    public static class G
    {
        public static IEventBus Events { get; private set; }
        public static ITimerService Timer { get; private set; }
        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            IsInitialized = true;
        }

        internal static void Reset()
        {
            Events = null;
            Timer = null;
            IsInitialized = false;
        }
    }
}
