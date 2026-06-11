using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using MessagePipe;
using VContainer;

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

            // ---- Asset ----
            builder.Register<AddressablesAssetService>(Lifetime.Singleton)
                .As<IAssetService>().AsSelf();

            // ---- Scene ----
            builder.Register<ISceneTransition, NoopSceneTransition>(Lifetime.Singleton);
            builder.RegisterInstance<ISceneLoader>(new UnitySceneLoader());
            builder.Register<ISceneService>(c => new SceneService(
                c.Resolve<ISceneLoader>(),
                c.Resolve<ISceneTransition>(),
                c.Resolve<IEventBus>(),
                options.InitialScene), Lifetime.Singleton);

            // ---- Save ----
            var profile = options.SaveProfile ?? CreateDefaultSaveProfile();
            builder.RegisterInstance(profile);
            builder.Register<ISaveService>(c => new JsonSaveService(profile, options.SaveDirectory),
                Lifetime.Singleton);
            builder.Register<SaveBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Config ----
            builder.Register<IRemoteConfigProvider, NoopRemoteConfigProvider>(Lifetime.Singleton);
            var tables = options.ConfigTables ?? new List<ConfigTable>();
            builder.Register<ConfigService>(c => new ConfigService(tables, c.Resolve<IRemoteConfigProvider>()),
                Lifetime.Singleton).As<IConfigService>().AsSelf();
            builder.Register<ConfigBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Pool ----
            builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();
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
