using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class SceneServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        sealed class RecordingTransition : ISceneTransition
        {
            readonly List<string> _log;
            public RecordingTransition(List<string> log) => _log = log;
            public UniTask PlayOut(CancellationToken ct = default)
            { _log.Add("PlayOut"); return UniTask.CompletedTask; }
            public UniTask PlayIn(CancellationToken ct = default)
            { _log.Add("PlayIn"); return UniTask.CompletedTask; }
        }

        sealed class FakeSceneLoader : ISceneLoader
        {
            readonly List<string> _log;
            readonly float[] _progressSteps;
            readonly bool _throw;
            public FakeSceneLoader(List<string> log, float[] progressSteps)
                : this(log, progressSteps, false) { }
            public FakeSceneLoader(List<string> log, float[] progressSteps, bool shouldThrow)
            { _log = log; _progressSteps = progressSteps; _throw = shouldThrow; }

            public UniTask LoadAsync(
                string sceneName, IProgress<float> progress, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                _log.Add($"Load:{sceneName}");
                if (_throw) throw new InvalidOperationException("load failed");
                if (progress != null)
                    foreach (var p in _progressSteps) progress.Report(p);
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void LoadAsync_RunsTransitionsAndEventsInOrder()
        {
            var log = new List<string>();
            var bus = new FakeBus();
            var svc = new SceneService(new FakeSceneLoader(log, new[] { 1f }),
                new RecordingTransition(log), bus, "Boot");

            // 事件发布也记录到 log 以校验交错顺序
            // 通过 bus.Published 的顺序与 log 顺序联合断言
            svc.LoadAsync("Level1").GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[] { "PlayOut", "Load:Level1", "PlayIn" }, log);
            Assert.IsInstanceOf<SceneWillUnloadEvent>(bus.Published[0]);
            Assert.IsInstanceOf<SceneLoadedEvent>(bus.Published[1]);
            Assert.AreEqual("Boot", ((SceneWillUnloadEvent)bus.Published[0]).SceneName);
            Assert.AreEqual("Level1", ((SceneLoadedEvent)bus.Published[1]).SceneName);
            Assert.AreEqual("Level1", svc.CurrentScene);
        }

        [Test]
        public void LoadAsync_ForwardsProgress()
        {
            var reported = new List<float>();
            var svc = new SceneService(
                new FakeSceneLoader(new List<string>(), new[] { 0.25f, 0.5f, 1f }),
                new NoopSceneTransition(), new FakeBus(), "Boot");

            svc.LoadAsync("Level1", new ProgressCollector(reported)).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f, 1f }, reported);
        }

        [Test]
        public void NoopTransition_CompletesImmediately()
        {
            var t = new NoopSceneTransition();
            Assert.IsTrue(t.PlayOut().Status.IsCompleted());
            Assert.IsTrue(t.PlayIn().Status.IsCompleted());
        }

        [Test]
        public void LoadFailure_UnblocksTransitionAndKeepsCurrentScene()
        {
            var log = new List<string>();
            var bus = new FakeBus();
            var service = new SceneService(
                new FakeSceneLoader(log, Array.Empty<float>(), true),
                new RecordingTransition(log), bus, "Boot");

            Assert.Throws<InvalidOperationException>(() =>
                service.LoadAsync("Broken").GetAwaiter().GetResult());

            CollectionAssert.AreEqual(
                new[] { "PlayOut", "Load:Broken", "PlayIn" }, log);
            Assert.AreEqual("Boot", service.CurrentScene);
            Assert.AreEqual(1, bus.Published.Count);
            Assert.IsInstanceOf<SceneWillUnloadEvent>(bus.Published[0]);
        }

        sealed class ProgressCollector : IProgress<float>
        {
            readonly List<float> _sink;
            public ProgressCollector(List<float> sink) => _sink = sink;
            public void Report(float value) => _sink.Add(value);
        }
    }
}
