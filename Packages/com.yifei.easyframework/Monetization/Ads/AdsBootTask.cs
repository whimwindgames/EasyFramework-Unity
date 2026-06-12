using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdsBootTask : IBootTask
    {
        readonly IAdsProvider _provider;
        public AdsBootTask(IAdsProvider provider) => _provider = provider;

        public int Priority => 30;
        public bool IsCritical => false;   // SDK 初始化失败降级该服务,不阻塞启动。

        public async UniTask InitializeAsync(CancellationToken ct)
            => await _provider.InitializeAsync(ct);
    }
}
