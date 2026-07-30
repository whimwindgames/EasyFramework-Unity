using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>IMultiplayerService 默认实现。把 Provider 的状态变化/收到消息统一转成 IEventBus 事件,
    /// 业务层订阅 IEventBus 即可,不用关心底层是 Photon 回调还是 HTTP 轮询在触发。
    /// 订阅 IMultiplayerProvider.MessageReceived 走接口本身(不做具体实现的向下类型转换)——
    /// 任何 IMultiplayerProvider 实现都保证暴露这个事件,不局限于 FakeMultiplayerProvider。</summary>
    public sealed class MultiplayerService : IMultiplayerService, IDisposable
    {
        readonly IMultiplayerProvider _provider;
        readonly IEventBus _events;
        bool _disposed;

        public MultiplayerService(IMultiplayerProvider provider, IEventBus events)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            _provider.MessageReceived += OnProviderMessageReceived;
        }

        public ConnectionState State => _provider.State;

        public async UniTask ConnectAsync(string sessionId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await _provider.ConnectAsync(sessionId, ct);
            _events.Publish(new ConnectionStateChangedEvent(_provider.State));
        }

        public async UniTask DisconnectAsync()
        {
            ThrowIfDisposed();
            await _provider.DisconnectAsync();
            _events.Publish(new ConnectionStateChangedEvent(_provider.State));
        }

        public UniTask SendAsync(string channel, byte[] payload)
        {
            ThrowIfDisposed();
            return _provider.SendAsync(channel, payload);
        }

        void OnProviderMessageReceived(MultiplayerMessageReceivedEvent evt)
        {
            if (!_disposed) _events.Publish(evt);
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MultiplayerService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _provider.MessageReceived -= OnProviderMessageReceived;
        }
    }
}
