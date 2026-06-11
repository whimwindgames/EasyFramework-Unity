using EasyFramework.Core.Events;
using MessagePipe;
using NUnit.Framework;
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
    }
}
