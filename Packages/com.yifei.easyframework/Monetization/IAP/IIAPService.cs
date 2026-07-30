using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.IAP
{
    public enum ProductType { Consumable, NonConsumable, Subscription }

    public readonly struct IAPProductDefinition
    {
        public readonly string Id;
        public readonly ProductType Type;

        public IAPProductDefinition(string id, ProductType type)
        {
            Id = id;
            Type = type;
        }
    }

    [Serializable]
    public sealed class PurchaseTransaction
    {
        public string ProductId;
        public string TransactionId;
        public string Receipt;
        /// <summary>由 0.2.0 PlayerPrefs pending 队列迁入,没有可供服务端验证的旧收据。</summary>
        public bool IsLegacy;
    }

    public sealed class PurchaseResult
    {
        /// <summary>商店付款与游戏侧发奖均已完成。</summary>
        public bool Success;
        /// <summary>商店已批准付款,但验签或发奖仍可能处于 pending。</summary>
        public bool StoreApproved;
        public string ProductId;
        public PurchaseTransaction Transaction;
        public string FailureReason;   // 取消="cancelled", 未初始化="not_initialized" 等
    }

    /// <summary>SDK 适配点(Unity IAP / Fake)。</summary>
    public interface IIAPProvider
    {
        /// <summary>商店在启动/恢复时交付的未确认交易。处理成功前 provider 必须保持 Pending。</summary>
        event Action<PurchaseTransaction> PendingPurchaseReceived;
        UniTask InitializeAsync(IReadOnlyList<IAPProductDefinition> products, CancellationToken ct);
        bool IsInitialized { get; }
        UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken ct = default);
        UniTask RestoreAsync(CancellationToken ct = default);
        void Confirm(PurchaseTransaction transaction);
    }

    public sealed class ReceiptValidationResult
    {
        public bool IsValid;
        public string FailureReason;

        public static ReceiptValidationResult Valid()
            => new ReceiptValidationResult { IsValid = true };

        public static ReceiptValidationResult Invalid(string reason)
            => new ReceiptValidationResult { IsValid = false, FailureReason = reason };
    }

    /// <summary>交易收据验证扩展点;默认客户端模式可替换为服务端验签。</summary>
    public interface IIAPReceiptValidator
    {
        UniTask<ReceiptValidationResult> ValidateAsync(
            PurchaseTransaction transaction, CancellationToken ct);
    }

    /// <summary>业务层入口(G.IAP)。已购持久化 / 掉单补发 / 打点在此层。</summary>
    public interface IIAPService
    {
        UniTask<PurchaseResult> PurchaseAsync(string productId, CancellationToken ct = default);
        UniTask RestoreAsync(CancellationToken ct = default);
        bool IsOwned(string productId);
    }
}
