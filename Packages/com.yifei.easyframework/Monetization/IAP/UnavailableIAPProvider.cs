using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>
    /// 未安装商店扩展包时的安全关闭实现。保持核心框架可独立运行,且不会生成或确认任何交易。
    /// </summary>
    public sealed class UnavailableIAPProvider : IIAPProvider
    {
        public event Action<PurchaseTransaction> PendingPurchaseReceived
        {
            add { }
            remove { }
        }

        public bool IsInitialized => false;

        public UniTask InitializeAsync(
            IReadOnlyList<IAPProductDefinition> products, CancellationToken ct)
            => UniTask.CompletedTask;

        public UniTask<PurchaseResult> PurchaseAsync(
            string productId, CancellationToken ct = default)
            => UniTask.FromResult(new PurchaseResult
            {
                Success = false,
                StoreApproved = false,
                ProductId = productId,
                FailureReason = "iap_provider_not_installed",
            });

        public UniTask RestoreAsync(CancellationToken ct = default)
            => UniTask.CompletedTask;

        public void Confirm(PurchaseTransaction transaction) { }
    }
}
