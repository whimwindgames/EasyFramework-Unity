using System.Collections.Generic;
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
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        const string BgmKey = "ef.audio.bgm";
        const string SfxKey = "ef.audio.sfx";
        const string LocaleKey = "ef.locale";
        const string HapticsKey = "ef.haptics";

        [SetUp]
        public void SetUp()
        {
            // 避免 Audio/Loc/Haptics 服务构造读到污染的 PlayerPrefs
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.DeleteKey(HapticsKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            EasyFramework.G.Reset();
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.DeleteKey(HapticsKey);
            PlayerPrefs.Save();
        }

        static FrameworkOptions MakeOptions()
            => new FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_installer_" + System.Guid.NewGuid().ToString("N")),
                ConfigTables = new List<ConfigTable>(),
                SaveProfile = null, // 用默认 DefaultSaveData profile
                LocalizationTables = new List<LocalizationTable>(),
                DefaultLocale = "zh-CN",
                ProductCatalog = ScriptableObject.CreateInstance<ProductCatalog>(),
            };

        IObjectResolver Build()
        {
            return Build(MakeOptions());
        }

        static IObjectResolver Build(FrameworkOptions options)
        {
            var builder = new ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, options);
            return builder.Build();
        }

        [Test]
        public void Install_ResolvesCoreServices()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IEventBus>());
            Assert.NotNull(c.Resolve<ITimerService>());
        }

        [Test]
        public void CoreOnly_DoesNotRegisterOptionalServices_AndGRemainsUsable()
        {
            var options = MakeOptions();
            options.Features = FrameworkFeatureSets.CoreOnly;
            var c = Build(options);

            Assert.NotNull(c.Resolve<IEventBus>());
            Assert.NotNull(c.Resolve<ITimerService>());
            Assert.IsFalse(c.TryResolve<IUIService>(out _));
            Assert.IsFalse(c.TryResolve<IAssetService>(out _));
            Assert.IsFalse(c.TryResolve<IAdsService>(out _));

            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.UI);
            Assert.IsNull(EasyFramework.G.Asset);
            Assert.IsNull(EasyFramework.G.Ads);
        }

        [Test]
        public void Install_RejectsEnabledModuleWithMissingDependency()
        {
            var options = MakeOptions();
            options.Features = FrameworkFeatures.Core | FrameworkFeatures.UI;
            var builder = new ContainerBuilder();

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                EasyFramework.FrameworkInstaller.Install(builder, options));
            StringAssert.Contains("Assets", error.Message);
        }

        [Test]
        public void Install_UsesExternalInputWithoutPresentationModules()
        {
            var input = new StubInputService();
            var options = MakeOptions();
            options.Features = FrameworkFeatures.Core | FrameworkFeatures.Input;
            options.InputServiceFactory = _ => input;

            var c = Build(options);
            Assert.AreSame(input, c.Resolve<IInputService>());
            Assert.IsFalse(c.TryResolve<IAssetService>(out _));
            Assert.IsFalse(c.TryResolve<ICameraService>(out _));
        }

        [Test]
        public void Install_ResolvesPhase2Services()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IAssetService>());
            Assert.NotNull(c.Resolve<ISceneService>());
            Assert.NotNull(c.Resolve<ISaveService>());
            Assert.NotNull(c.Resolve<IConfigService>());
            Assert.NotNull(c.Resolve<IPoolService>());
        }

        [Test]
        public void Install_ResolvesUIService()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IUIService>());
        }

        [Test]
        public void Install_ResolvesPhase3bServices()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IAudioService>());
            Assert.NotNull(c.Resolve<IInputService>());
            Assert.NotNull(c.Resolve<ICameraService>());
            Assert.NotNull(c.Resolve<IJuiceService>());
            Assert.NotNull(c.Resolve<ILocalizationService>());
            Assert.NotNull(c.Resolve<IHapticsService>());
        }

        [Test]
        public void GFacade_BindsPhase2Services()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IEventBus>(), EasyFramework.G.Events);
            Assert.AreSame(c.Resolve<ITimerService>(), EasyFramework.G.Timer);
            Assert.AreSame(c.Resolve<IAssetService>(), EasyFramework.G.Asset);
            Assert.AreSame(c.Resolve<ISceneService>(), EasyFramework.G.Scene);
            Assert.AreSame(c.Resolve<ISaveService>(), EasyFramework.G.Save);
            Assert.AreSame(c.Resolve<IConfigService>(), EasyFramework.G.Config);
            Assert.AreSame(c.Resolve<IPoolService>(), EasyFramework.G.Pool);
        }

        [Test]
        public void GFacade_BindsUIService()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.AreSame(c.Resolve<IUIService>(), EasyFramework.G.UI);
        }

        [Test]
        public void GFacade_BindsPhase3bServices()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IAudioService>(), EasyFramework.G.Audio);
            Assert.AreSame(c.Resolve<IInputService>(), EasyFramework.G.Input);
            Assert.AreSame(c.Resolve<ICameraService>(), EasyFramework.G.Camera);
            Assert.AreSame(c.Resolve<IJuiceService>(), EasyFramework.G.Juice);
            Assert.AreSame(c.Resolve<ILocalizationService>(), EasyFramework.G.Loc);
            Assert.AreSame(c.Resolve<IHapticsService>(), EasyFramework.G.Haptics);
        }

        [Test]
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Events);
            Assert.IsNull(EasyFramework.G.Asset);
            Assert.IsNull(EasyFramework.G.Save);
            Assert.IsNull(EasyFramework.G.UI);
            Assert.IsNull(EasyFramework.G.Audio);
            Assert.IsNull(EasyFramework.G.Loc);
            Assert.IsNull(EasyFramework.G.Haptics);
            Assert.IsNull(EasyFramework.G.Ads);
            Assert.IsNull(EasyFramework.G.IAP);
            Assert.IsNull(EasyFramework.G.Analytics);
            Assert.IsNull(EasyFramework.G.ContentUpdate);
            Assert.IsNull(EasyFramework.G.Http);
        }

        [Test]
        public void Install_ResolvesMonetizationServices()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IAdsService>());
            Assert.NotNull(c.Resolve<IIAPService>());
            Assert.NotNull(c.Resolve<IAnalyticsService>());
        }

        [Test]
        public void Install_UsesRootOptionsAdsProviderFactory()
        {
            var provider = new FakeAdsProvider
            {
                Delay = 0f,
                IsRewardedReady = true,
                NextResult = AdResult.Skipped,
            };
            var builder = new ContainerBuilder();
            var options = MakeOptions();
            options.AdsProviderFactory = _ => provider;
            EasyFramework.FrameworkInstaller.Install(builder, options);
            var container = builder.Build();

            Assert.AreSame(provider, container.Resolve<IAdsProvider>());
            Assert.AreEqual(AdResult.Skipped,
                container.Resolve<IAdsService>().ShowRewardedAsync("test")
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void UnavailableAdsProvider_NeverCompletesReward()
        {
            var provider = new UnavailableAdsProvider();
            Assert.IsFalse(provider.IsRewardedReady);
            Assert.AreEqual(AdResult.NotReady,
                provider.ShowRewardedAsync("test").GetAwaiter().GetResult());
        }

        [Test]
        public void GFacade_BindsMonetizationServices()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.AreSame(c.Resolve<IAdsService>(), EasyFramework.G.Ads);
            Assert.AreSame(c.Resolve<IIAPService>(), EasyFramework.G.IAP);
            Assert.AreSame(c.Resolve<IAnalyticsService>(), EasyFramework.G.Analytics);
        }

        [Test]
        public void Install_ResolvesContentUpdateService()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IContentUpdateService>());
        }

        [Test]
        public void GFacade_BindsContentUpdateService()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.AreSame(c.Resolve<IContentUpdateService>(), EasyFramework.G.ContentUpdate);
        }

        sealed class StubInputService : IInputService
        {
            public Vector2 MoveAxis => Vector2.zero;
            public bool IsPointerOverUI => false;
        }
    }
}
