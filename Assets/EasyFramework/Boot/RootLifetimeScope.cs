using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Saves;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架组合根。挂在 Boot 场景的常驻 GameObject 上。</summary>
    public class RootLifetimeScope : LifetimeScope
    {
        [Header("本地配置表(可空)")]
        [SerializeField] List<ConfigTable> _configTables = new();

        [Header("初始场景名")]
        [SerializeField] string _initialScene = "Boot";

        protected override void Configure(IContainerBuilder builder)
        {
            var options = new FrameworkOptions
            {
                SaveDirectory = Application.persistentDataPath,
                ConfigTables = _configTables,
                SaveProfile = null,
                InitialScene = _initialScene,
            };

            FrameworkInstaller.Install(builder, options);

            builder.RegisterEntryPoint<GameBootstrap>();
            builder.UseEntryPoints(ep => ep.Add<TimerTicker>());

            // SaveOnPauseListener 挂到本组合根 GameObject,build 后绑定 ISaveService。
            var pauseListener = gameObject.AddComponent<SaveOnPauseListener>();

            builder.RegisterBuildCallback(r =>
            {
                pauseListener.Bind(r.Resolve<ISaveService>());
                G.Initialize(r);
            });
        }
    }

    /// <summary>把 TimerService.Tick() 桥接到 VContainer 的 PlayerLoop Tick 循环。</summary>
    sealed class TimerTicker : ITickable
    {
        readonly TimerService _timer;
        public TimerTicker(TimerService timer) => _timer = timer;
        public void Tick() => _timer.Tick();
    }
}
