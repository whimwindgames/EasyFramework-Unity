// ===========================================================================
// UnityIAPProvider —— Unity IAP(com.unity.purchasing)真实现,仅真机路径。
//
// 官方包,真实现非占位符。但其异步回调链路只在真机(配置好 IAP catalog 与
// 商店密钥)上可跑通,EditMode 无法稳定驱动,故不写 EditMode 单测;编辑器与
// 全部 EditMode 测试走 FakeIAPProvider(FrameworkInstaller 在 #if UNITY_EDITOR
// 下注册 Fake,真机注册本类)。Unity IAP API 名/签名以工程安装版本为准——
// 验证代理用 unity_reflect 核对后等效调整,IIAPProvider 契约不变。
//
// 超时 / 取消策略:
//   InitializeAsync — 观察传入的 CancellationToken(ct.Register→TrySetCanceled);
//                     另加 InitTimeoutMs(默认 15 s)兜底,避免商店永不回调致永久 pending。
//   PurchaseAsync   — 无商店超时时用户可能永久挂起弹窗;加 PurchaseTimeoutMs(默认 120 s)
//                     的竞态超时,超时后 TrySetCanceled + 返回 FailureReason="timeout"。
//                     IIAPProvider 契约签名不变(PurchaseAsync 无 CancellationToken 参数)。
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace EasyFramework.Monetization.IAP
{
    public sealed class UnityIAPProvider : IIAPProvider, IDetailedStoreListener
    {
        /// <summary>初始化超时(毫秒)。默认 15 000 ms;真机接入时可按需调大。</summary>
        public int InitTimeoutMs = 15_000;

        /// <summary>单笔购买超时(毫秒)。默认 120 000 ms;用于用户挂起弹窗或商店进程被杀的兜底。</summary>
        public int PurchaseTimeoutMs = 120_000;

        IStoreController _controller;
        IExtensionProvider _extensions;
        UniTaskCompletionSource _initTcs;
        UniTaskCompletionSource<PurchaseResult> _purchaseTcs;

        public bool IsInitialized => _controller != null;

        public async UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct)
        {
            if (IsInitialized) return;
            _initTcs = new UniTaskCompletionSource();

            // 观察外部取消令牌:商店永不回调时上层可取消。
            CancellationTokenRegistration ctReg = ct.Register(
                () => _initTcs.TrySetCanceled(), useSynchronizationContext: false);

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            // 类型未知时按 Consumable 注册兜底;实际类型由业务侧 catalog 决定,真机接入时映射。
            foreach (var id in productIds)
                builder.AddProduct(id, UnityEngine.Purchasing.ProductType.Consumable);

            UnityPurchasing.Initialize(this, builder);

            try
            {
                // 竞态:SDK 回调 vs CancellationToken vs 超时兜底。
                await _initTcs.Task.TimeoutWithoutException(TimeSpan.FromMilliseconds(InitTimeoutMs));
            }
            finally
            {
                ctReg.Dispose();
            }
        }

        public async UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            if (!IsInitialized)
                return new PurchaseResult
                { Success = false, ProductId = productId, FailureReason = "not_initialized" };

            _purchaseTcs = new UniTaskCompletionSource<PurchaseResult>();
            _controller.InitiatePurchase(productId);

            // 带超时的竞态等待:防止商店永不回调(用户挂起弹窗/网络卡死/商店进程被杀)。
            // TimeoutWithoutException<T> 返回 (bool IsTimeout, T Result)。
            var (isTimeout, result) = await _purchaseTcs.Task
                .TimeoutWithoutException(TimeSpan.FromMilliseconds(PurchaseTimeoutMs));

            if (isTimeout)
            {
                // 超时:清理 TCS 以免后续 SDK 回调触发残留副作用。
                _purchaseTcs.TrySetCanceled();
                _purchaseTcs = null;
                return new PurchaseResult
                { Success = false, ProductId = productId, FailureReason = "timeout" };
            }

            return result;
        }

        public UniTask RestoreAsync()
        {
            // iOS: AppleExtensions.RestoreTransactions;其它平台一般无需手动恢复。
            var apple = _extensions?.GetExtension<IAppleExtensions>();
            apple?.RestoreTransactions((_, __) => { });
            return UniTask.CompletedTask;
        }

        // ---- IDetailedStoreListener 回调(主线程)----
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;
            _initTcs?.TrySetResult();
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => _initTcs?.TrySetResult(); // 初始化失败不阻塞:IsInitialized 仍 false,后续购买返回 not_initialized。

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => _initTcs?.TrySetResult();

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = true, ProductId = args.purchasedProduct.definition.id });
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            var reason = failureDescription.reason == PurchaseFailureReason.UserCancelled
                ? "cancelled" : failureDescription.reason.ToString();
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = false, ProductId = product.definition.id, FailureReason = reason });
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            var reason = failureReason == PurchaseFailureReason.UserCancelled
                ? "cancelled" : failureReason.ToString();
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = false, ProductId = product.definition.id, FailureReason = reason });
        }
    }
}
