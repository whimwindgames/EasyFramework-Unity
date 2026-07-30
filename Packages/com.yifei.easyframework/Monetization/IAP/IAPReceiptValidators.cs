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

    /// <summary>正式环境未配置服务端验签时安全关闭,交易保持 Pending 且绝不发奖。</summary>
    public sealed class UnavailableIAPReceiptValidator : IIAPReceiptValidator
    {
        public UniTask<ReceiptValidationResult> ValidateAsync(
            PurchaseTransaction transaction, CancellationToken ct)
            => UniTask.FromResult(ReceiptValidationResult.Invalid(
                "receipt_validator_not_configured"));
    }
}
