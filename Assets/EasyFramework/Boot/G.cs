using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
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
        public static IPoolService Pool { get; private set; }
        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            Asset = resolver.Resolve<IAssetService>();
            Scene = resolver.Resolve<ISceneService>();
            Save = resolver.Resolve<ISaveService>();
            Config = resolver.Resolve<IConfigService>();
            Pool = resolver.Resolve<IPoolService>();
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
            Pool = null;
            IsInitialized = false;
        }
    }
}
