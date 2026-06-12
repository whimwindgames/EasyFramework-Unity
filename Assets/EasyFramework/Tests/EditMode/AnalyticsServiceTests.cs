using System;
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Scenes;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class AnalyticsServiceTests
    {
        sealed class RecordingBackend : IAnalyticsBackend
        {
            public readonly List<(string name, IReadOnlyDictionary<string, object> p)> Events = new();
            public readonly List<(string key, string value)> Props = new();
            public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
                => Events.Add((eventName, parameters));
            public void SetUserProperty(string key, string value) => Props.Add((key, value));
        }

        sealed class ThrowingBackend : IAnalyticsBackend
        {
            public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
                => throw new Exception("backend boom");
            public void SetUserProperty(string key, string value)
                => throw new Exception("prop boom");
        }

        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<Type, List<Delegate>> _handlers = new();
            public void Publish<T>(T evt)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((Action<T>)d)(evt);
            }
            public IDisposable Subscribe<T>(Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }
            sealed class Sub : IDisposable
            {
                readonly Action _d; public Sub(Action d) => _d = d; public void Dispose() => _d();
            }
        }

        [Test]
        public void Track_BroadcastsToAllBackends()
        {
            var a = new RecordingBackend();
            var b = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a, b });
            svc.Track("level_start", ("level", 3));
            Assert.AreEqual(1, a.Events.Count);
            Assert.AreEqual(1, b.Events.Count);
            Assert.AreEqual("level_start", a.Events[0].name);
            Assert.AreEqual(3, a.Events[0].p["level"]);
        }

        [Test]
        public void Track_BackendThrows_IsolatedFromOthersAndCaller()
        {
            var good = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { new ThrowingBackend(), good });
            Assert.DoesNotThrow(() => svc.Track("e"), "异常不传染到调用方");
            Assert.AreEqual(1, good.Events.Count, "抛异常的 backend 不影响其它 backend");
        }

        [Test]
        public void Track_NoParams_PassesEmptyDictionary()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            svc.Track("ping");
            Assert.AreEqual(0, a.Events[0].p.Count);
        }

        [Test]
        public void Track_TupleParams_ConvertedToDictionary()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            svc.Track("buy", ("item", "sword"), ("price", 9.99f));
            Assert.AreEqual("sword", a.Events[0].p["item"]);
            Assert.AreEqual(9.99f, a.Events[0].p["price"]);
        }

        [Test]
        public void SetUserProperty_BroadcastsAndIsolatesExceptions()
        {
            var good = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { new ThrowingBackend(), good });
            Assert.DoesNotThrow(() => svc.SetUserProperty("tier", "gold"));
            Assert.AreEqual(("tier", "gold"), good.Props[0]);
        }

        [Test]
        public void AutoTracker_BootCompleted_TracksEvent()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            var bus = new FakeBus();
            var tracker = new AnalyticsAutoTracker(svc, bus);
            tracker.Start();

            bus.Publish(new BootCompletedEvent());
            Assert.AreEqual("boot_completed", a.Events[0].name);

            tracker.Dispose();
            bus.Publish(new BootCompletedEvent());
            Assert.AreEqual(1, a.Events.Count, "Dispose 后退订,不再收事件");
        }

        [Test]
        public void AutoTracker_SceneLoaded_TracksWithSceneParam()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            var bus = new FakeBus();
            var tracker = new AnalyticsAutoTracker(svc, bus);
            tracker.Start();

            bus.Publish(new SceneLoadedEvent("Level1"));
            Assert.AreEqual("scene_loaded", a.Events[0].name);
            Assert.AreEqual("Level1", a.Events[0].p["scene"]);
            tracker.Dispose();
        }
    }
}
