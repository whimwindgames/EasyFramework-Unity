using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using EasyFramework.Services.UI;
using MessagePipe;
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

            // ---- Pool(Phase 2)----
            builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();

            // ---- UI(Phase 3a)----
            // UIService 同时实现 IUIService(门面)与 ITickable(返回键检测)。
            // 构造只存 IAssetService、不碰 GameObject/Screen,UIRoot 懒加载,纯容器 Build 安全。
            builder.Register<UIService>(Lifetime.Singleton)
                .As<IUIService>()
                .As<ITickable>()
                .AsSelf();

            // ---- Audio(Phase 3b)----
            // AudioService : ITickable,以 AsSelf 注册便于 RootLifetimeScope 取出挂 Tick。
            builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().AsSelf();

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
