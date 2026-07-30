using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using MessagePipe;
using NUnit.Framework;
using UnityEngine.TestTools;
using VContainer;

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

        sealed class FastTask : IBootTask
        {
            public int Priority => 0;
            public bool IsCritical => false;
            public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        sealed class CancelledTask : IBootTask
        {
            public int Priority => 0;
            public bool IsCritical => false;
            public UniTask InitializeAsync(CancellationToken ct) => UniTask.FromCanceled(ct);
        }

        sealed class SlowTask : IBootTask
        {
            readonly int _delayMs;
            public SlowTask(int delayMs) => _delayMs = delayMs;
            public int Priority => 0;
            public bool IsCritical => false;
            public UniTask InitializeAsync(CancellationToken ct)
            {
                // 同步阻塞而非 `await UniTask.Yield()`:后者的续体挂在 Unity PlayerLoop 上,
                // 在普通 [Test](非 [UnityTest] 协程)里没有人推进 PlayerLoop,会导致
                // `.GetAwaiter().GetResult()` 抛出 "Not yet completed" 而非阻塞等待。
                // 用 Thread.Sleep 换取一个立即同步完成、但真实耗时 >= threshold 的 UniTask。
                System.Threading.Thread.Sleep(_delayMs);
                return UniTask.CompletedTask;
            }
        }

        static IEventBus BuildEventBus()
        {
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            return builder.Build().Resolve<IEventBus>();
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

        [Test]
        public void SlowBootTask_ExceedingThreshold_LogsTiming()
        {
            var tasks = new List<IBootTask> { new SlowTask((int)GameBootstrap.SlowTaskThresholdMs + 20) };
            var bootstrap = new GameBootstrap(tasks, BuildEventBus());

            LogAssert.Expect(UnityEngine.LogType.Log, new System.Text.RegularExpressions.Regex(
                $"{nameof(SlowTask)}.*ms"));
            bootstrap.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        [Test]
        public void FastBootTask_UnderThreshold_DoesNotLogTiming()
        {
            var tasks = new List<IBootTask> { new FastTask() };
            var bootstrap = new GameBootstrap(tasks, BuildEventBus());

            // 未超阈值不应打印耗时日志;仅允许 BootCompletedEvent 之外没有额外 Log。
            bootstrap.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Cancellation_IsNotSwallowedByNonCriticalTask()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var bus = new FakeBus();
            var bootstrap = new GameBootstrap(new IBootTask[] { new CancelledTask() }, bus);

            Assert.Throws<OperationCanceledException>(() =>
                bootstrap.StartAsync(cancellation.Token).GetAwaiter().GetResult());
            CollectionAssert.IsEmpty(bus.Published);
        }
    }
}
