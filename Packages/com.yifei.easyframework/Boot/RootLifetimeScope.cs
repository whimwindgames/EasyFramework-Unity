using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Localization;
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

        [Header("本地化表(可空)")]
        [SerializeField] List<LocalizationTable> _localizationTables = new();

        [Header("初始场景名")]
        [SerializeField] string _initialScene = "Boot";

        [Header("默认语言")]
        [SerializeField] string _defaultLocale = "zh-CN";

        [Header("内购商品目录(可空)")]
        [SerializeField] ProductCatalog _productCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            var options = new FrameworkOptions
            {
                SaveDirectory = Application.persistentDataPath,
                ConfigTables = _configTables,
                SaveProfile = GetSaveProfile(),
                InitialScene = _initialScene,
                LocalizationTables = _localizationTables,
                DefaultLocale = _defaultLocale,
                ProductCatalog = _productCatalog,
            };
            ConfigureFrameworkOptions(options);

            FrameworkInstaller.Install(builder, options);

            builder.RegisterEntryPoint<GameBootstrap>();
            builder.UseEntryPoints(ep =>
            {
                ep.Add<TimerTicker>();
                ep.Add<AudioTicker>();
                ep.Add<InputTicker>();
            });

            // SaveOnPauseListener 挂到本组合根 GameObject,build 后绑定 ISaveService。
            var pauseListener = gameObject.AddComponent<SaveOnPauseListener>();

            builder.RegisterBuildCallback(r =>
            {
                pauseListener.Bind(r.Resolve<ISaveService>());
                G.Initialize(r);
            });
        }

        /// <summary>
        /// 子类可覆盖以提供游戏自定义存档 profile(决定 G.Save.Data&lt;T&gt;() 的载荷类型)。
        /// 默认返回 null,框架用 DefaultSaveData。SaveProfile 是普通 C# 类(非 ScriptableObject),
        /// 故经此代码 seam 注入,而非 Inspector 字段。
        /// </summary>
        protected virtual SaveProfile GetSaveProfile() => null;

        /// <summary>
        /// 在根容器构建前配置广告、内购、统计和远程配置等适配器。
        /// 这些根服务不能在 GameLifetimeScope 子作用域中覆盖。
        /// </summary>
        protected virtual void ConfigureFrameworkOptions(FrameworkOptions options) { }

        protected override void OnDestroy()
        {
            G.Reset(Container);
            base.OnDestroy();
        }
    }

    /// <summary>把 TimerService.Tick() 桥接到 VContainer 的 PlayerLoop Tick 循环。</summary>
    sealed class TimerTicker : ITickable
    {
        readonly TimerService _timer;
        public TimerTicker(TimerService timer) => _timer = timer;
        public void Tick() => _timer.Tick();
    }

    /// <summary>把 AudioService.Tick(BGM 淡变推进)桥接到 Tick 循环。</summary>
    sealed class AudioTicker : ITickable
    {
        readonly AudioService _audio;
        public AudioTicker(AudioService audio) => _audio = audio;
        public void Tick() => _audio.Tick();
    }

    /// <summary>把 InputService.Tick(读输入、喂手势)桥接到 Tick 循环。</summary>
    sealed class InputTicker : ITickable
    {
        readonly InputService _input;
        public InputTicker(InputService input) => _input = input;
        public void Tick() => _input.Tick();
    }
}
