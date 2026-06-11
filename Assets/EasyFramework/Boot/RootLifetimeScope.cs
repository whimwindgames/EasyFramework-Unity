using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架组合根。挂在 Boot 场景的常驻 GameObject 上。</summary>
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            FrameworkInstaller.Install(builder);

            builder.RegisterEntryPoint<GameBootstrap>();
            // TimerService 已在 Installer 注册为单例,这里把它桥接到 VContainer Tick 调度
            builder.UseEntryPoints(ep => ep.Add<TimerTicker>());
            builder.RegisterBuildCallback(r => G.Initialize(r));
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
