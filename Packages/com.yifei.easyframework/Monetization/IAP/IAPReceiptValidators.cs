using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>仅供编辑器、开发包和自动化测试使用。</summary>
    public sealed class DevelopmentIAPReceiptValidator : IIAPReceiptValidator
    {
        public UniTask<ReceiptValidationResult> ValidateAsync(
            PurchaseTransaction transaction, CancellationToken ct)
            => UniTask.FromResult(ReceiptValidationResult.Valid());
    }

    /// <summary>
    /// 无游戏服务端时的客户端确认模式。只接受本次 Unity IAP 商店回调中包含完整标识和收据的交易,
    /// 不执行密码学验签。交易日志、TransactionId 幂等发奖和商店 Pending/Confirm 顺序仍然生效。
    /// </summary>
    public sealed class ClientOnlyIAPReceiptValidator : IIAPReceiptValidator
    {
        public UniTask<ReceiptValidationResult> ValidateAsync(
            PurchaseTransaction transaction, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (transaction == null ||
                string.IsNullOrWhiteSpace(transaction.ProductId) ||
                string.IsNullOrWhiteSpace(transaction.TransactionId))
                return UniTask.FromResult(
                    ReceiptValidationResult.Invalid("invalid_transaction"));
            if (transaction.IsLegacy)
                return UniTask.FromResult(
                    ReceiptValidationResult.Invalid("legacy_receipt_unavailable"));
            if (string.IsNullOrWhiteSpace(transaction.Receipt))
                return UniTask.FromResult(
                    ReceiptValidationResult.Invalid("receipt_missing"));
            return UniTask.FromResult(ReceiptValidationResult.Valid());
        }
    }

    /// <summary>正式环境未配置服务端验签时安全关闭,交易保持 Pending 且绝不发奖。</summary>
    public sealed class UnavailableIAPReceiptValidator : IIAPReceiptValidator
    {
        public UniTask<ReceiptValidationResult> ValidateAsync(
            PurchaseTransaction transaction, CancellationToken ct)
            => UniTask.FromResult(ReceiptValidationResult.Invalid(
                "receipt_validator_not_configured"));
    }
}
