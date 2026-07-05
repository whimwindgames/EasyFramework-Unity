using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Multiplayer;
using NUnit.Framework;

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
    }
}
