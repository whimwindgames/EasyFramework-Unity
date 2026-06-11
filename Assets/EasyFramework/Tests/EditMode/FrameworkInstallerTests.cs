using System.Collections.Generic;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        [TearDown]
        public void TearDown() => EasyFramework.G.Reset();

        static FrameworkOptions MakeOptions()
            => new FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_installer_" + System.Guid.NewGuid().ToString("N")),
                ConfigTables = new List<ConfigTable>(),
                SaveProfile = null, // 用默认 DefaultSaveData profile
            };

        IObjectResolver Build()
        {
            var builder = new ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeOptions());
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
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Events);
            Assert.IsNull(EasyFramework.G.Asset);
            Assert.IsNull(EasyFramework.G.Save);
        }
    }
}
