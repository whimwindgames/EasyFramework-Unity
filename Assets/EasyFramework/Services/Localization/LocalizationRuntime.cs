using EasyFramework.Core.Events;

namespace EasyFramework.Services.Localization
{
    /// <summary>
    /// Services 层本地化运行时访问点。由 Boot 层 <c>G.Initialize</c> 填充,供 Services 程序集内的
    /// 表现层组件(<see cref="LocalizedText"/>)读取,避免 Services → Boot 的反向(循环)程序集依赖。
    /// Game 层仍可走 <c>G.Loc</c> / <c>G.Events</c> 门面。
    /// </summary>
    public static class LocalizationRuntime
    {
        public static ILocalizationService Loc { get; private set; }
        public static IEventBus Events { get; private set; }
        public static bool IsInitialized { get; private set; }

        public static void Initialize(ILocalizationService loc, IEventBus events)
        {
            Loc = loc;
            Events = events;
            IsInitialized = loc != null && events != null;
        }

        public static void Reset()
        {
            Loc = null;
            Events = null;
            IsInitialized = false;
        }
    }
}
