using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Purchasing;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>
    /// Unity IAP 5 适配器。所有新购与恢复交易都保持 Pending;只有 IAPService 完成验签、
    /// 持久化和幂等发奖后才 ConfirmPurchase。
    /// </summary>
    public sealed class UnityIAPProvider : IIAPProvider, IDisposable
    {
        public int InitTimeoutMs = 15_000;
        public int PurchaseTimeoutMs = 120_000;

        public event Action<PurchaseTransaction> PendingPurchaseReceived;

        readonly Dictionary<string, PendingOrder> _pendingOrders = new();
        StoreController _controller;
        UniTaskCompletionSource<List<Product>> _productsTcs;
        UniTaskCompletionSource<Orders> _purchasesTcs;
        UniTaskCompletionSource<PurchaseResult> _purchaseTcs;
        string _activeProductId;
        bool _initialized;
        bool _disposed;

        public bool IsInitialized => _initialized && !_disposed;

        public async UniTask InitializeAsync(
            IReadOnlyList<IAPProductDefinition> products, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (IsInitialized) return;

            _controller = UnityIAPServices.StoreController();
            Subscribe();
            _controller.ProcessPendingOrdersOnPurchasesFetched(false);

            var connectTask = _controller.Connect().AsUniTask();
            if (await connectTask.AttachExternalCancellation(ct)
                    .TimeoutWithoutException(TimeSpan.FromMilliseconds(InitTimeoutMs)))
                throw new TimeoutException("Unity IAP store connection timed out.");

            _productsTcs = new UniTaskCompletionSource<List<Product>>();
            var definitions = new List<UnityEngine.Purchasing.ProductDefinition>();
            if (products != null)
            {
                foreach (var product in products)
                {
                    if (!string.IsNullOrWhiteSpace(product.Id))
                        definitions.Add(new UnityEngine.Purchasing.ProductDefinition(
                            product.Id, ToUnityProductType(product.Type)));
                }
            }

            _controller.FetchProducts(definitions);
            var (productsTimedOut, _) = await _productsTcs.Task
                .AttachExternalCancellation(ct)
                .TimeoutWithoutException(TimeSpan.FromMilliseconds(InitTimeoutMs));
            if (productsTimedOut)
                throw new TimeoutException("Unity IAP product fetch timed out.");

            _initialized = true;
            await FetchExistingPurchasesAsync(ct, InitTimeoutMs);
        }

        public async UniTask<PurchaseResult> PurchaseAsync(
            string productId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (!IsInitialized)
                return Failed(productId, "not_initialized");
            if (_purchaseTcs != null)
                return Failed(productId, "purchase_in_progress");
            if (_controller.GetProductById(productId) == null)
                return Failed(productId, "unknown_store_product");

            var completion = new UniTaskCompletionSource<PurchaseResult>();
            _purchaseTcs = completion;
            _activeProductId = productId;
            try
            {
                _controller.PurchaseProduct(productId);
                var (isTimeout, result) = await completion.Task
                    .AttachExternalCancellation(ct)
                    .TimeoutWithoutException(TimeSpan.FromMilliseconds(PurchaseTimeoutMs));
                return isTimeout ? Failed(productId, "timeout") : result;
            }
            finally
            {
                if (ReferenceEquals(_purchaseTcs, completion))
                {
                    _purchaseTcs = null;
                    _activeProductId = null;
                }
            }
        }

        public async UniTask RestoreAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (!IsInitialized)
                throw new InvalidOperationException("Unity IAP is not initialized.");

            var completion = new UniTaskCompletionSource<(bool success, string error)>();
            _controller.RestoreTransactions((success, error) =>
                completion.TrySetResult((success, error)));
            var (isTimeout, restore) = await completion.Task
                .AttachExternalCancellation(ct)
                .TimeoutWithoutException(TimeSpan.FromMilliseconds(PurchaseTimeoutMs));
            if (isTimeout)
                throw new TimeoutException("Unity IAP restore timed out.");
            if (!restore.success)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(restore.error) ? "Unity IAP restore failed." : restore.error);

            await FetchExistingPurchasesAsync(ct, PurchaseTimeoutMs);
        }

        public void Confirm(PurchaseTransaction transaction)
        {
            if (!IsInitialized || transaction == null) return;
            if (_pendingOrders.TryGetValue(transaction.TransactionId, out var order))
                _controller.ConfirmPurchase(order);
        }

        async UniTask FetchExistingPurchasesAsync(CancellationToken ct, int timeoutMs)
        {
            _purchasesTcs = new UniTaskCompletionSource<Orders>();
            _controller.FetchPurchases();
            var (isTimeout, _) = await _purchasesTcs.Task
                .AttachExternalCancellation(ct)
                .TimeoutWithoutException(TimeSpan.FromMilliseconds(timeoutMs));
            if (isTimeout)
                throw new TimeoutException("Unity IAP purchase fetch timed out.");
        }

        void OnProductsFetched(List<Product> products)
            => _productsTcs?.TrySetResult(products);

        void OnProductsFetchFailed(ProductFetchFailed failure)
            => _productsTcs?.TrySetException(new InvalidOperationException(
                $"Unity IAP product fetch failed: {failure?.FailureReason}"));

        void OnPurchasesFetched(Orders orders)
        {
            if (orders?.PendingOrders != null)
            {
                foreach (var order in orders.PendingOrders)
                    HandlePendingOrder(order);
            }

            // 非消耗型恢复后可能已是 ConfirmedOrder。交给同一幂等链路恢复 entitlement;
            // 此类订单无需再次向商店 Confirm。
            if (orders?.ConfirmedOrders != null)
            {
                foreach (var order in orders.ConfirmedOrders)
                {
                    var transaction = ToTransaction(order);
                    if (transaction != null)
                        PendingPurchaseReceived?.Invoke(transaction);
                }
            }

            _purchasesTcs?.TrySetResult(orders);
        }

        void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
            => _purchasesTcs?.TrySetException(new InvalidOperationException(
                $"Unity IAP purchase fetch failed: {failure?.Message}"));

        void OnPurchasePending(PendingOrder order) => HandlePendingOrder(order);

        void HandlePendingOrder(PendingOrder order)
        {
            var transaction = ToTransaction(order);
            if (transaction == null) return;
            _pendingOrders[transaction.TransactionId] = order;

            if (_purchaseTcs != null && transaction.ProductId == _activeProductId)
            {
                _purchaseTcs.TrySetResult(new PurchaseResult
                {
                    Success = false,
                    StoreApproved = true,
                    ProductId = transaction.ProductId,
                    Transaction = transaction,
                });
            }
            else
            {
                PendingPurchaseReceived?.Invoke(transaction);
            }
        }

        void OnPurchaseFailed(FailedOrder order)
        {
            var productId = FirstProductId(order);
            if (_purchaseTcs == null || productId != _activeProductId)
                return;
            var reason = order.FailureReason == PurchaseFailureReason.UserCancelled
                ? "cancelled"
                : order.FailureReason.ToString();
            _purchaseTcs.TrySetResult(Failed(productId, reason));
        }

        void OnPurchaseDeferred(DeferredOrder order)
        {
            var productId = FirstProductId(order);
            if (_purchaseTcs != null && productId == _activeProductId)
                _purchaseTcs.TrySetResult(Failed(productId, "deferred"));
        }

        void OnPurchaseConfirmed(Order order)
        {
            var transactionId = order?.Info?.TransactionID;
            if (!string.IsNullOrWhiteSpace(transactionId))
                _pendingOrders.Remove(transactionId);
        }

        static PurchaseTransaction ToTransaction(Order order)
        {
            var productId = FirstProductId(order);
            if (string.IsNullOrWhiteSpace(productId) || order?.Info == null)
                return null;
            var transactionId = order.Info.TransactionID;
            var receipt = order.Info.Receipt;
            if (string.IsNullOrWhiteSpace(transactionId))
                transactionId = CreateFallbackTransactionId(productId, receipt);
            return new PurchaseTransaction
            {
                ProductId = productId,
                TransactionId = transactionId,
                Receipt = receipt,
            };
        }

        static string FirstProductId(Order order)
            => order?.CartOrdered?.Items()?.FirstOrDefault()?.Product?.definition?.id;

        static PurchaseResult Failed(string productId, string reason)
            => new PurchaseResult
            {
                Success = false,
                StoreApproved = false,
                ProductId = productId,
                FailureReason = reason,
            };

        static UnityEngine.Purchasing.ProductType ToUnityProductType(ProductType type)
            => type switch
            {
                ProductType.NonConsumable => UnityEngine.Purchasing.ProductType.NonConsumable,
                ProductType.Subscription => UnityEngine.Purchasing.ProductType.Subscription,
                _ => UnityEngine.Purchasing.ProductType.Consumable,
            };

        static string CreateFallbackTransactionId(string productId, string receipt)
        {
            var source = $"{productId}\n{receipt}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source));
            return "receipt-" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        void Subscribe()
        {
            _controller.OnStoreDisconnected += OnStoreDisconnected;
            _controller.OnProductsFetched += OnProductsFetched;
            _controller.OnProductsFetchFailed += OnProductsFetchFailed;
            _controller.OnPurchasesFetched += OnPurchasesFetched;
            _controller.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
            _controller.OnPurchasePending += OnPurchasePending;
            _controller.OnPurchaseFailed += OnPurchaseFailed;
            _controller.OnPurchaseDeferred += OnPurchaseDeferred;
            _controller.OnPurchaseConfirmed += OnPurchaseConfirmed;
        }

        void Unsubscribe()
        {
            if (_controller == null) return;
            _controller.OnStoreDisconnected -= OnStoreDisconnected;
            _controller.OnProductsFetched -= OnProductsFetched;
            _controller.OnProductsFetchFailed -= OnProductsFetchFailed;
            _controller.OnPurchasesFetched -= OnPurchasesFetched;
            _controller.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;
            _controller.OnPurchasePending -= OnPurchasePending;
            _controller.OnPurchaseFailed -= OnPurchaseFailed;
            _controller.OnPurchaseDeferred -= OnPurchaseDeferred;
            _controller.OnPurchaseConfirmed -= OnPurchaseConfirmed;
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription _) => _initialized = false;

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityIAPProvider));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _initialized = false;
            Unsubscribe();
            _pendingOrders.Clear();
        }
    }
}
