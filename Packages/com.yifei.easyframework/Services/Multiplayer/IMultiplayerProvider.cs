using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }

    public readonly struct ConnectionStateChangedEvent
    {
        public readonly ConnectionState State;
        public ConnectionStateChangedEvent(ConnectionState state) => State = state;
    }

    public readonly struct MultiplayerMessageReceivedEvent
    {
        public readonly string Channel;
        public readonly byte[] Payload;
        public MultiplayerMessageReceivedEvent(string channel, byte[] payload)
        {
            Channel = channel;
            Payload = payload;
        }
    }

    /// <summary>SDK 适配点。具体游戏在自己的 GameLifetimeScope 里注册真实实现;默认 FakeMultiplayerProvider(本地回环)。</summary>
    public interface IMultiplayerProvider
    {
        ConnectionState State { get; }
        event Action<MultiplayerMessageReceivedEvent> MessageReceived;
        UniTask ConnectAsync(string sessionId, CancellationToken ct);
        UniTask DisconnectAsync();
        UniTask SendAsync(string channel, byte[] payload);
    }
}
