using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class GameBootstrapTests
    {
        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        sealed class FakeTask : IBootTask
        {
            readonly Action _onRun;
            public FakeTask(int priority, bool critical, Action onRun = null, bool fail = false)
            { Priority = priority; IsCritical = critical; _onRun = onRun; _fail = fail; }
            readonly bool _fail;
            public int Priority { get; }
            public bool IsCritical { get; }
            public UniTask InitializeAsync(CancellationToken ct)
            {
                _onRun?.Invoke();
                return _fail ? UniTask.FromException(new Exception("boom")) : UniTask.CompletedTask;
            }
        }

        [Test]
        public void Tasks_RunInPriorityOrder()
        {
            var order = new List<int>();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(20, true, () => order.Add(20)),
                new FakeTask(0, true, () => order.Add(0)),
                new FakeTask(10, true, () => order.Add(10)),
            }, new FakeBus());
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 0, 10, 20 }, order);
        }

        [Test]
        public void NonCriticalFailure_DoesNotAbortBoot()
        {
            var ranAfter = false;
            var bus = new FakeBus();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(0, critical: false, fail: true),
                new FakeTask(10, true, () => ranAfter = true),
            }, bus);
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(ranAfter);
            Assert.AreEqual(1, bus.Published.Count, "完成事件仍应发布");
        }

        [Test]
        public void CriticalFailure_AbortsBoot()
        {
            var ranAfter = false;
            var bus = new FakeBus();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(0, critical: true, fail: true),
                new FakeTask(10, true, () => ranAfter = true),
            }, bus);
            Assert.Throws<BootFailedException>(() =>
                boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsFalse(ranAfter);
            Assert.AreEqual(0, bus.Published.Count);
        }

        [Test]
        public void Completion_PublishesBootCompletedEvent()
        {
            var bus = new FakeBus();
            var boot = new GameBootstrap(Array.Empty<IBootTask>(), bus);
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsInstanceOf<BootCompletedEvent>(bus.Published[0]);
        }
    }
}
