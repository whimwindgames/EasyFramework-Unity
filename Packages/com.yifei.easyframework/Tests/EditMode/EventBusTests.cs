using EasyFramework.Core.Events;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace EasyFramework.Tests
{
    public class EventBusTests
    {
        readonly struct ScoreEvent
        {
            public readonly int Amount;
            public ScoreEvent(int amount) => Amount = amount;
        }

        IObjectResolver BuildContainer()
        {
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            return builder.Build();
        }

        [Test]
        public void PublishedEvent_ReachesSubscriber()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var total = 0;
            bus.Subscribe<ScoreEvent>(e => total += e.Amount);
            bus.Publish(new ScoreEvent(10));
            bus.Publish(new ScoreEvent(5));
            Assert.AreEqual(15, total);
        }

        [Test]
        public void DisposedSubscription_StopsReceiving()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var total = 0;
            var sub = bus.Subscribe<ScoreEvent>(e => total += e.Amount);
            bus.Publish(new ScoreEvent(10));
            sub.Dispose();
            bus.Publish(new ScoreEvent(99));
            Assert.AreEqual(10, total);
        }

        [Test]
        public void MultipleSubscribers_AllReceive()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            int a = 0, b = 0;
            bus.Subscribe<ScoreEvent>(_ => a++);
            bus.Subscribe<ScoreEvent>(_ => b++);
            bus.Publish(new ScoreEvent(1));
            Assert.AreEqual(1, a);
            Assert.AreEqual(1, b);
        }

        [Test]
        public void Reentrant_SubscribeInsideHandler_DoesNotThrow_NewSubscriberReceivesCurrentPublish()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var outerCalls = 0;
            var innerCalls = 0;
            System.IDisposable innerSub = null;

            bus.Subscribe<ScoreEvent>(e =>
            {
                outerCalls++;
                if (innerSub == null)
                    innerSub = bus.Subscribe<ScoreEvent>(_ => innerCalls++);
            });

            // 实测记录(非预设):MessagePipe 的内存 Subscriber 分发不对订阅列表做快照隔离,
            // 在处理当前事件的过程中新增的订阅会被立即追加进本次分发仍在遍历的集合,
            // 因此新订阅者会在"本次" Publish 内就收到事件,而不是要等到下一次 Publish。
            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(1)));
            Assert.AreEqual(1, outerCalls);
            Assert.AreEqual(1, innerCalls);

            bus.Publish(new ScoreEvent(2));
            Assert.AreEqual(2, outerCalls);
            Assert.AreEqual(2, innerCalls); // 已订阅,后续每次发布都会收到。
        }

        [Test]
        public void Reentrant_DisposeSelfInsideHandler_DoesNotThrow_StopsReceivingAfterward()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var calls = 0;
            System.IDisposable sub = null;
            sub = bus.Subscribe<ScoreEvent>(_ =>
            {
                calls++;
                sub.Dispose();
            });

            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(1)));
            Assert.AreEqual(1, calls);

            bus.Publish(new ScoreEvent(2));
            Assert.AreEqual(1, calls); // 已在第一次回调里自我取消订阅,第二次不应再收到。
        }

        [Test]
        public void Publish_ExceedingMaxDepth_LogsErrorAndStopsRecursion()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var depthReached = 0;

            bus.Subscribe<ScoreEvent>(e =>
            {
                depthReached++;
                if (depthReached <= MessagePipeEventBus.MaxPublishDepth + 5)
                    bus.Publish(new ScoreEvent(e.Amount + 1)); // 故意自触发形成深递归
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "exceeded max reentrancy depth"));
            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(0)));

            // 深度保护应在到达 MaxPublishDepth 后拦截,不应无限递归下去(不会栈溢出,调用次数有限)。
            Assert.LessOrEqual(depthReached, MessagePipeEventBus.MaxPublishDepth + 1);
        }
    }
}
