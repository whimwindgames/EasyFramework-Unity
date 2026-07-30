using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Multiplayer;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class MultiplayerServiceTests
    {
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
                readonly Action _dispose;
                public Sub(Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }
        }

        [Test]
        public void FakeMultiplayerProvider_ConnectAsync_TransitionsToConnected()
        {
            var provider = new FakeMultiplayerProvider();
            Assert.AreEqual(ConnectionState.Disconnected, provider.State);

            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ConnectionState.Connected, provider.State);
        }

        [Test]
        public void FakeMultiplayerProvider_DisconnectAsync_TransitionsToDisconnected()
        {
            var provider = new FakeMultiplayerProvider();
            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            provider.DisconnectAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ConnectionState.Disconnected, provider.State);
        }

        [Test]
        public void FakeMultiplayerProvider_SendAsync_LoopsBackToSelf()
        {
            var provider = new FakeMultiplayerProvider();
            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();
            MultiplayerMessageReceivedEvent? received = null;
            provider.MessageReceived += evt => received = evt;

            provider.SendAsync("chat", new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual("chat", received.Value.Channel);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, received.Value.Payload);
        }

        [Test]
        public void IMultiplayerProvider_ExposesMessageReceivedEvent_ViaInterfaceType()
        {
            IMultiplayerProvider provider = new FakeMultiplayerProvider();
            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();
            MultiplayerMessageReceivedEvent? received = null;
            provider.MessageReceived += evt => received = evt;

            provider.SendAsync("chat", new byte[] { 4, 5, 6 }).GetAwaiter().GetResult();

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual("chat", received.Value.Channel);
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, received.Value.Payload);
        }

        [Test]
        public void MultiplayerService_ConnectAsync_PublishesConnectionStateChangedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var events = new List<ConnectionStateChangedEvent>();
            bus.Subscribe<ConnectionStateChangedEvent>(e => events.Add(e));
            var svc = new MultiplayerService(provider, bus);

            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(ConnectionState.Connected, events[0].State);
            Assert.AreEqual(ConnectionState.Connected, svc.State);
        }

        [Test]
        public void MultiplayerService_DisconnectAsync_PublishesConnectionStateChangedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var events = new List<ConnectionStateChangedEvent>();
            var svc = new MultiplayerService(provider, bus);
            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();
            bus.Subscribe<ConnectionStateChangedEvent>(e => events.Add(e));

            svc.DisconnectAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(ConnectionState.Disconnected, events[0].State);
            Assert.AreEqual(ConnectionState.Disconnected, svc.State);
        }

        [Test]
        public void MultiplayerService_SendAsync_ProviderLoopback_PublishesMessageReceivedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var messages = new List<MultiplayerMessageReceivedEvent>();
            bus.Subscribe<MultiplayerMessageReceivedEvent>(e => messages.Add(e));
            var svc = new MultiplayerService(provider, bus);
            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            svc.SendAsync("chat", new byte[] { 9, 8, 7 }).GetAwaiter().GetResult();

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual("chat", messages[0].Channel);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, messages[0].Payload);
        }

        [Test]
        public void MultiplayerService_Dispose_UnsubscribesProviderMessages()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var messages = new List<MultiplayerMessageReceivedEvent>();
            bus.Subscribe<MultiplayerMessageReceivedEvent>(e => messages.Add(e));
            var service = new MultiplayerService(provider, bus);
            service.Dispose();

            provider.SendAsync("chat", new byte[] { 1 }).GetAwaiter().GetResult();

            CollectionAssert.IsEmpty(messages);
        }

        static EasyFramework.FrameworkOptions MakeFrameworkOptionsForBoundaryTest()
            => new EasyFramework.FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_multiplayer_boundary_" + System.Guid.NewGuid().ToString("N")),
            };

        [Test]
        public void FrameworkInstaller_DoesNotRegisterMultiplayerTypes()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptionsForBoundaryTest());
            using var container = builder.Build();

            // FrameworkInstaller 确实没有注册这两个类型,Resolve 必须抛 VContainerException——
            // 这条测试就是要证明"没注册"这件事本身,而不是绕开它。
            Assert.Throws<VContainer.VContainerException>(() => container.Resolve<IMultiplayerService>(),
                "FrameworkInstaller must not register IMultiplayerService — multiplayer is an opt-in module wired by individual games.");
            Assert.Throws<VContainer.VContainerException>(() => container.Resolve<IMultiplayerProvider>(),
                "FrameworkInstaller must not register IMultiplayerProvider — multiplayer is an opt-in module wired by individual games.");
        }

        [Test]
        public void G_DoesNotExposeMultiplayer_NoSuchMemberExistsByDesign()
        {
            // 契约式回归标记:G 不应新增 Multiplayer 属性(设计原因见 spec §3.3——
            // G 的契约是 BootCompletedEvent 后一定可用,可选模块不能挂一个可能为 null 的入口)。
            // 用反射断言,而不是直接引用 G.Multiplayer——后者一旦被误加,本测试也无法通过编译来提醒,
            // 反射断言能在"有人加了这个属性"时给出明确失败信息而不是静默编译通过。
            var member = typeof(EasyFramework.G).GetProperty("Multiplayer");
            Assert.IsNull(member,
                "G must not expose a Multiplayer property — multiplayer is opt-in and wired per-game via constructor injection, not through G.");
        }
    }
}
