using System;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using VContainer.Unity;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>框架自动标准事件:启动完成 / 场景加载。订阅在 Start,退订在 Dispose。</summary>
    public sealed class AnalyticsAutoTracker : IStartable, IDisposable
    {
        readonly IAnalyticsService _analytics;
        readonly IEventBus _events;
        IDisposable _bootSub;
        IDisposable _sceneSub;

        public AnalyticsAutoTracker(IAnalyticsService analytics, IEventBus events)
        {
            _analytics = analytics;
            _events = events;
        }

        public void Start()
        {
            _bootSub = _events.Subscribe<BootCompletedEvent>(_ => _analytics.Track("boot_completed"));
            _sceneSub = _events.Subscribe<SceneLoadedEvent>(e =>
                _analytics.Track("scene_loaded", ("scene", e.SceneName)));
        }

        public void Dispose()
        {
            _bootSub?.Dispose();
            _sceneSub?.Dispose();
        }
    }
}
