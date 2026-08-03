using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Saves;
using EasyFramework.Services.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架组合根。挂在 Boot 场景的常驻 GameObject 上。</summary>
    public class RootLifetimeScope : LifetimeScope
    {
        [Header("启用模块")]
        [SerializeField] FrameworkFeatures _features = FrameworkFeatures.All;

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

        [Header("UI Root（横竖屏/渲染模式/缩放）")]
        [SerializeField] UIRootProfile _uiRootProfile = new();

        protected override void Configure(IContainerBuilder builder)
        {
            var options = new FrameworkOptions
            {
                Features = _features,
                SaveDirectory = Application.persistentDataPath,
                ConfigTables = _configTables,
                SaveProfile = GetSaveProfile(),
                InitialScene = _initialScene,
                LocalizationTables = _localizationTables,
                DefaultLocale = _defaultLocale,
                ProductCatalog = _productCatalog,
                UIRootProfile = _uiRootProfile,
            };
            ConfigureFrameworkOptions(options);

            FrameworkInstaller.Install(builder, options);

            builder.RegisterEntryPoint<GameBootstrap>();
            builder.UseEntryPoints(ep =>
            {
                ep.Add<TimerTicker>();
                if (options.IsEnabled(FrameworkFeatures.UI) && options.UIServiceFactory == null)
                    ep.Add<UITicker>();
                if (options.IsEnabled(FrameworkFeatures.Audio))
                    ep.Add<AudioTicker>();
                if (options.IsEnabled(FrameworkFeatures.Input) && options.InputServiceFactory == null)
                    ep.Add<InputTicker>();
            });

            // SaveOnPauseListener 仅在启用存档模块时创建。
            var pauseListener = options.IsEnabled(FrameworkFeatures.Save)
                ? gameObject.AddComponent<SaveOnPauseListener>()
                : null;

            builder.RegisterBuildCallback(r =>
            {
                if (pauseListener != null)
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

    /// <summary>把 UIService.Tick(返回键检测)桥接到 PlayerLoop Tick 循环。</summary>
    sealed class UITicker : ITickable
    {
        readonly UIService _ui;
        public UITicker(UIService ui) => _ui = ui;
        public void Tick() => _ui.Tick();
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
