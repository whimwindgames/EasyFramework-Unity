using System.Collections.Generic;
using EasyFramework.Core.Boot;
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
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架启动选项:把 Unity 静态依赖从组合根传入,使 Install 可脱离 MonoBehaviour 单测。</summary>
    public sealed class FrameworkOptions
    {
        /// <summary>存档目录;生产传 Application.persistentDataPath,测试传临时目录。</summary>
        public string SaveDirectory;
        /// <summary>本地配置表;可空。</summary>
        public IReadOnlyList<ConfigTable> ConfigTables;
        /// <summary>可空:游戏自定义存档 profile;为 null 时用框架默认 DefaultSaveData profile。</summary>
        public SaveProfile SaveProfile;
        /// <summary>初始场景名(SceneService.CurrentScene 初值);默认 "Boot"。</summary>
        public string InitialScene = "Boot";
        /// <summary>本地化表;可空。(Phase 3b)</summary>
        public IReadOnlyList<LocalizationTable> LocalizationTables;
        /// <summary>默认 locale;无 PlayerPrefs 时的初值。(Phase 3b)默认 "zh-CN"。</summary>
        public string DefaultLocale = "zh-CN";
        /// <summary>内购商品目录;可空(为 null 时框架用空 catalog,无商品)。</summary>
        public ProductCatalog ProductCatalog;
    }

    /// <summary>框架服务注册(纯逻辑,便于脱离 MonoBehaviour 测试)。入口点注册在 RootLifetimeScope。</summary>
    public static class FrameworkInstaller
    {
        public static void Install(IContainerBuilder builder, FrameworkOptions options)
        {
            // ---- Core(Phase 1)----
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            builder.Register<TimerService>(Lifetime.Singleton).As<ITimerService>().AsSelf();

            // ---- Asset(Phase 2)----
            builder.Register<AddressablesAssetService>(Lifetime.Singleton)
                .As<IAssetService>().AsSelf();

            // ---- Scene(Phase 2; Phase 3a: swap to FadeSceneTransition)----
            // Phase 3a:转场从 NoopSceneTransition 换成 FadeSceneTransition(淡入淡出黑幕)
            // 注:用工厂 lambda 以走 FadeSceneTransition 的默认时长构造(VContainer 不识别 C# 可选参数默认值,
            // 直接 Register<,> 会尝试解析未注册的 System.Single 而失败)。
            builder.Register<ISceneTransition>(_ => new FadeSceneTransition(), Lifetime.Singleton);
            builder.RegisterInstance<ISceneLoader>(new UnitySceneLoader());
            builder.Register<ISceneService>(c => new SceneService(
                c.Resolve<ISceneLoader>(),
                c.Resolve<ISceneTransition>(),
                c.Resolve<IEventBus>(),
                options.InitialScene), Lifetime.Singleton);

            // ---- Save(Phase 2)----
            var profile = options.SaveProfile ?? CreateDefaultSaveProfile();
            builder.RegisterInstance(profile);
            builder.Register<ISaveService>(c => new JsonSaveService(profile, options.SaveDirectory),
                Lifetime.Singleton);
            builder.Register<SaveBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Config(Phase 2)----
            builder.Register<IRemoteConfigProvider, NoopRemoteConfigProvider>(Lifetime.Singleton);
            var tables = options.ConfigTables ?? new List<ConfigTable>();
            builder.Register<ConfigService>(c => new ConfigService(tables, c.Resolve<IRemoteConfigProvider>()),
                Lifetime.Singleton).As<IConfigService>().AsSelf();
            builder.Register<ConfigBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Http(网络基础层)----
            // 通用 HTTP 基础设施,所有游戏都可能用到。UnityWebRequestTransport 是唯一发起真实网络请求的实现;
            // EditMode 测试通过注入 FakeHttpTransport 验证 HttpService 的重试/超时/反序列化逻辑。
            builder.Register<IHttpTransport, UnityWebRequestTransport>(Lifetime.Singleton);
            builder.Register<HttpService>(Lifetime.Singleton).As<IHttpService>().AsSelf();

            // ---- Pool(Phase 2; idle auto-shrink added later)----
            // 工厂 lambda 显式走 3 参生产构造:PoolService 另有一个 internal(IAssetService,IEventBus,
            // ITimerService,Func<float>)测试构造,VContainer 自动选最长构造会去解析未注册的 Func<float> 而失败
            // (与上方 ISceneTransition/SceneService、AdsService 同因)。
            builder.Register<PoolService>(c => new PoolService(
                c.Resolve<IAssetService>(),
                c.Resolve<IEventBus>(),
                c.Resolve<ITimerService>()), Lifetime.Singleton).As<IPoolService>().AsSelf();

            // ---- UI(Phase 3a)----
            // UIService 同时实现 IUIService(门面)与 ITickable(返回键检测)。
            // 构造只存 IAssetService、不碰 GameObject/Screen,UIRoot 懒加载,纯容器 Build 安全。
            builder.Register<UIService>(Lifetime.Singleton)
                .As<IUIService>()
                .As<ITickable>()
                .AsSelf();

            // ---- Audio(Phase 3b)----
            // AudioService : ITickable,以 AsSelf 注册便于 RootLifetimeScope 取出挂 Tick。
            // 工厂 lambda 显式走 1 参生产构造:AudioService 另有一个 internal(IAssetService,Func<float>)
            // 测试构造,VContainer 自动选最长构造会去解析未注册的 Func<float> 而失败(与上方 PoolService 同因)。
            builder.Register<AudioService>(c => new AudioService(c.Resolve<IAssetService>()),
                Lifetime.Singleton).As<IAudioService>().AsSelf();

            // ---- Input(Phase 3b)----
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService>().AsSelf();

            // ---- Camera(Phase 3b)----
            builder.Register<CinemachineCameraService>(Lifetime.Singleton).As<ICameraService>();

            // ---- Juice(Phase 3b)----
            builder.Register<JuiceService>(Lifetime.Singleton).As<IJuiceService>();

            // ---- Localization(Phase 3b)----
            var locTables = options.LocalizationTables ?? new List<LocalizationTable>();
            var defaultLocale = string.IsNullOrEmpty(options.DefaultLocale) ? "zh-CN" : options.DefaultLocale;
            builder.Register<ILocalizationService>(c => new TableLocalizationService(
                locTables, c.Resolve<IEventBus>(), defaultLocale), Lifetime.Singleton);

            // ---- Haptics(Phase 3b)----
            builder.Register<HapticsService>(Lifetime.Singleton).As<IHapticsService>();

            // ---- Analytics(Phase 4)----
            // 后端多注册:DebugAnalyticsBackend(编辑器/调试)。真机接 Firebase 时,
            // 在 GameLifetimeScope 追加注册 IAnalyticsBackend -> FirebaseAnalyticsBackend(#if EF_FIREBASE)。
            builder.Register<IAnalyticsBackend, DebugAnalyticsBackend>(Lifetime.Singleton);
            builder.Register<IAnalyticsService>(c =>
                new AnalyticsService(new List<IAnalyticsBackend>(c.Resolve<IReadOnlyList<IAnalyticsBackend>>())),
                Lifetime.Singleton);
            // 自动标准事件订阅(BootCompleted / SceneLoaded)。
            builder.RegisterEntryPoint<AnalyticsAutoTracker>();

            // ---- Ads(Phase 4)----
            // Fake provider 在编辑器与真机都注册(保证真机也能跑通);接 AdMob/LevelPlay 时,
            // 在 GameLifetimeScope 覆盖注册 IAdsProvider -> AdMobAdsProvider(#if EF_ADMOB)。
            builder.Register<IAdsProvider, FakeAdsProvider>(Lifetime.Singleton);
            // 工厂 lambda 显式走 3 参生产构造:AdsService 另有一个 internal(IAdsProvider,IConfigService,
            // IAnalyticsService,Func<float>)测试构造,VContainer 自动选最长构造会去解析未注册的 Func<float> 而失败
            // (与上方 ISceneTransition/SceneService 同因)。这里固定调公开构造,Func<float> 默认走 Time.realtimeSinceStartup。
            builder.Register<AdsService>(c => new AdsService(
                c.Resolve<IAdsProvider>(),
                c.Resolve<IConfigService>(),
                c.Resolve<IAnalyticsService>()), Lifetime.Singleton).As<IAdsService>().AsSelf();
            builder.Register<AdsBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- IAP(Phase 4)----
            var catalog = options.ProductCatalog;
            if (catalog == null)
                catalog = ScriptableObject.CreateInstance<ProductCatalog>(); // 空目录兜底,可空契约。
            builder.RegisterInstance(catalog);
#if UNITY_EDITOR
            // 编辑器走 Fake,EditMode 测试与编辑器联调均不触真实 SDK。
            builder.Register<IIAPProvider, FakeIAPProvider>(Lifetime.Singleton);
#else
            // 真机:Unity IAP 真实现(官方包)。这是接入槽,换其它商店 SDK 适配器也在此替换。
            builder.Register<IIAPProvider, UnityIAPProvider>(Lifetime.Singleton);
#endif
            builder.Register<IAPService>(Lifetime.Singleton).As<IIAPService>().AsSelf();
            builder.Register<IAPBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- ContentUpdate(资源热更新)----
            // 真实实现直接转发 Addressables 静态 API;单测全部用 FakeAddressablesCatalogGateway 替身。
            builder.Register<IAddressablesCatalogGateway, AddressablesCatalogGateway>(Lifetime.Singleton);
            builder.Register<ContentUpdateService>(Lifetime.Singleton)
                .As<IContentUpdateService>().AsSelf();
            builder.Register<ContentUpdateBootTask>(Lifetime.Singleton).As<IBootTask>();
        }

        static SaveProfile CreateDefaultSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(DefaultSaveData),
                CurrentVersion = 1,
                CreateNew = () => new DefaultSaveData { Version = 1 },
                Migrations = new List<ISaveMigration>(),
                FileName = "save.json",
                HmacSalt = "easyframework",
            };
    }
}
