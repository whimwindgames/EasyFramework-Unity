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
using EasyFramework.Services.Network;
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
        static IObjectResolver _owner;
        public static IEventBus Events { get; private set; }
        public static ITimerService Timer { get; private set; }
        public static IAssetService Asset { get; private set; }
        public static ISceneService Scene { get; private set; }
        public static ISaveService Save { get; private set; }
        public static IConfigService Config { get; private set; }
        public static IHttpService Http { get; private set; }
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
            _owner = resolver;
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            Asset = resolver.ResolveOrDefault<IAssetService>();
            Scene = resolver.ResolveOrDefault<ISceneService>();
            Save = resolver.ResolveOrDefault<ISaveService>();
            Config = resolver.ResolveOrDefault<IConfigService>();
            Http = resolver.ResolveOrDefault<IHttpService>();
            ContentUpdate = resolver.ResolveOrDefault<IContentUpdateService>();
            Pool = resolver.ResolveOrDefault<IPoolService>();

            UI = resolver.ResolveOrDefault<IUIService>();

            Audio = resolver.ResolveOrDefault<IAudioService>();
            Input = resolver.ResolveOrDefault<IInputService>();
            Camera = resolver.ResolveOrDefault<ICameraService>();
            Juice = resolver.ResolveOrDefault<IJuiceService>();
            Loc = resolver.ResolveOrDefault<ILocalizationService>();
            Haptics = resolver.ResolveOrDefault<IHapticsService>();

            // Phase 4 商业化层
            Ads = resolver.ResolveOrDefault<IAdsService>();
            IAP = resolver.ResolveOrDefault<IIAPService>();
            Analytics = resolver.ResolveOrDefault<IAnalyticsService>();

            // 填充 Services 层本地化运行时访问点,供 LocalizedText 读取(避免 Services → Boot 循环依赖)。
            LocalizationRuntime.Initialize(Loc, Events);

            IsInitialized = true;
        }

        internal static void Reset() => Reset(null);

        internal static void Reset(IObjectResolver owner)
        {
            // 旧 Root 在场景切换中晚于新 Root 销毁时,不能清掉新 Root 已绑定的门面。
            if (owner != null && !ReferenceEquals(owner, _owner))
                return;

            _owner = null;
            Events = null;
            Timer = null;
            Asset = null;
            Scene = null;
            Save = null;
            Config = null;
            Http = null;
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
