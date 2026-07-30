using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Monetization.IAP
{
    public sealed class IAPBootTask : IBootTask
    {
        readonly IIAPProvider _provider;
        readonly ProductCatalog _catalog;
        readonly IAPService _service;

        public IAPBootTask(IIAPProvider provider, ProductCatalog catalog, IAPService service)
        {
            _provider = provider;
            _catalog = catalog;
            _service = service;
        }

        public int Priority => 31;
        public bool IsCritical => false;   // IAP 初始化失败不阻塞启动。

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            var products = _catalog != null
                ? _catalog.ProductDefinitions()
                : new IAPProductDefinition[0];
            await _provider.InitializeAsync(products, ct);
            await _service.ReplayPendingAsync(ct);
        }
    }
}
