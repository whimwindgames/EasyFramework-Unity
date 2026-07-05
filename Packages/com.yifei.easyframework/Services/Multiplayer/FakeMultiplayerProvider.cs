using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>本地回环 Fake:SendAsync 直接原地触发一个 MultiplayerMessageReceivedEvent,
    /// 模拟"自己发的消息自己收到"。供接入联机的游戏在编辑器/单测下开发调试用,不需要连真实后端。</summary>
    public sealed class FakeMultiplayerProvider : IMultiplayerProvider
    {
        /// <summary>测试/调试可订阅:每次 SendAsync 回环时触发。</summary>
        public event Action<MultiplayerMessageReceivedEvent> MessageReceived;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public UniTask ConnectAsync(string sessionId, CancellationToken ct)
        {
            State = ConnectionState.Connected;
            return UniTask.CompletedTask;
        }

        public UniTask DisconnectAsync()
        {
            State = ConnectionState.Disconnected;
            return UniTask.CompletedTask;
        }

        public UniTask SendAsync(string channel, byte[] payload)
        {
            MessageReceived?.Invoke(new MultiplayerMessageReceivedEvent(channel, payload));
            return UniTask.CompletedTask;
        }
    }
}
