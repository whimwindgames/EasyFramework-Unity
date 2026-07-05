using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.ContentUpdate;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using EasyFramework.Services.UI;
using VContainer;

namespace EasyFramework
{
    /// <summary>业务层快速访问门面。框架内部禁止使用,模块间一律构造注入。</summary>
    public static class G
    {
        public static IEventBus Events { get; private set; }
        public static ITimerService Timer { get; private set; }
        public static IAssetService Asset { get; private set; }
        public static ISceneService Scene { get; private set; }
        public static ISaveService Save { get; private set; }
        public static IConfigService Config { get; private set; }
        public static IContentUpdateService ContentUpdate { get; private set; }
        public static IPoolService Pool { get; private set; }

        // Phase 3a UI
        public static IUIService UI { get; private set; }

        // Phase 3b 表现层
        public static IAudioService Audio { get; private set; }
        public static IInputService Input { get; private set; }
        public static ICameraService Camera { get; private set; }
        public static IJuiceService Juice { get; private set; }
        public static ILocalizationService Loc { get; private set; }
        public static IHapticsService Haptics { get; private set; }

        // Phase 4 商业化层
        public static IAdsService Ads { get; private set; }
        public static IIAPService IAP { get; private set; }
        public static IAnalyticsService Analytics { get; private set; }

        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            Asset = resolver.Resolve<IAssetService>();
            Scene = resolver.Resolve<ISceneService>();
            Save = resolver.Resolve<ISaveService>();
            Config = resolver.Resolve<IConfigService>();
            ContentUpdate = resolver.Resolve<IContentUpdateService>();
            Pool = resolver.Resolve<IPoolService>();

            UI = resolver.Resolve<IUIService>();

            Audio = resolver.Resolve<IAudioService>();
            Input = resolver.Resolve<IInputService>();
            Camera = resolver.Resolve<ICameraService>();
            Juice = resolver.Resolve<IJuiceService>();
            Loc = resolver.Resolve<ILocalizationService>();
            Haptics = resolver.Resolve<IHapticsService>();

            // Phase 4 商业化层
            Ads = resolver.Resolve<IAdsService>();
            IAP = resolver.Resolve<IIAPService>();
            Analytics = resolver.Resolve<IAnalyticsService>();

            // 填充 Services 层本地化运行时访问点,供 LocalizedText 读取(避免 Services → Boot 循环依赖)。
            LocalizationRuntime.Initialize(Loc, Events);

            IsInitialized = true;
        }

        internal static void Reset()
        {
            Events = null;
            Timer = null;
            Asset = null;
            Scene = null;
            Save = null;
            Config = null;
            ContentUpdate = null;
            Pool = null;

            UI = null;

            Audio = null;
            Input = null;
            Camera = null;
            Juice = null;
            Loc = null;
            Haptics = null;

            // Phase 4 商业化层
            Ads = null;
            IAP = null;
            Analytics = null;

            LocalizationRuntime.Reset();

            IsInitialized = false;
        }
    }
}
