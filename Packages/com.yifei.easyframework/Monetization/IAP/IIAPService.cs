using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.IAP
{
    public enum ProductType { Consumable, NonConsumable }

    public sealed class PurchaseResult
    {
        public bool Success;
        public string ProductId;
        public string FailureReason;   // 取消="cancelled", 未初始化="not_initialized" 等
    }

    /// <summary>SDK 适配点(Unity IAP / Fake)。</summary>
    public interface IIAPProvider
    {
        UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct);
        bool IsInitialized { get; }
        UniTask<PurchaseResult> PurchaseAsync(string productId);
        UniTask RestoreAsync();
    }

    /// <summary>业务层入口(G.IAP)。已购持久化 / 掉单补发 / 打点在此层。</summary>
    public interface IIAPService
    {
        UniTask<PurchaseResult> PurchaseAsync(string productId);
        UniTask RestoreAsync();
        bool IsOwned(string productId);
    }
}
