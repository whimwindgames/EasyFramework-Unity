using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        [TearDown]
        public void TearDown() => EasyFramework.G.Reset();

        IObjectResolver Build()
        {
            var builder = new ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder);
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
        public void GFacade_BindsAfterInitialize()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IEventBus>(), EasyFramework.G.Events);
            Assert.AreSame(c.Resolve<ITimerService>(), EasyFramework.G.Timer);
        }

        [Test]
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Events);
        }
    }
}
