using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>业务层入口。把 Provider 的状态变化/收到消息统一转成 IEventBus 事件供订阅。</summary>
    public interface IMultiplayerService
    {
        ConnectionState State { get; }
        UniTask ConnectAsync(string sessionId, CancellationToken ct = default);
        UniTask DisconnectAsync();
        UniTask SendAsync(string channel, byte[] payload);
    }
}
