using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>
    /// 交易协调器。所有商店回调都先进入持久化 journal,通过验签并成功发奖后才通知商店确认。
    /// 游戏侧发奖处理器必须使用 TransactionId 做幂等,以覆盖“发奖成功后、journal 落盘前”崩溃的窗口。
    /// </summary>
    public sealed class IAPService : IIAPService, IDisposable
    {
        const string LegacyOwnedKey = "ef.iap.owned";
        const string LegacyPendingKey = "ef.iap.pending";
        readonly IIAPProvider _provider;
        readonly ProductCatalog _catalog;
        readonly IAnalyticsService _analytics;
        readonly IIAPReceiptValidator _receiptValidator;
        readonly IIAPTransactionStore _store;
        readonly SemaphoreSlim _purchaseGate = new(1, 1);
        readonly SemaphoreSlim _transactionGate = new(1, 1);
        readonly IAPTransactionState _state;
        readonly HashSet<string> _completed;
        readonly HashSet<string> _owned;

        Func<PurchaseTransaction, CancellationToken, UniTask<bool>> _rewardHandler;
        bool _disposed;

        public IAPService(IIAPProvider provider, ProductCatalog catalog, IAnalyticsService analytics,
            IIAPReceiptValidator receiptValidator, IIAPTransactionStore store)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _receiptValidator = receiptValidator ?? throw new ArgumentNullException(nameof(receiptValidator));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _state = _store.Load() ?? new IAPTransactionState();
            _state.Pending ??= new List<PurchaseTransaction>();
            _state.CompletedTransactionIds ??= new List<string>();
            _state.OwnedProductIds ??= new List<string>();
            MigrateLegacyPlayerPrefs();
            _completed = new HashSet<string>(_state.CompletedTransactionIds);
            _owned = new HashSet<string>(_state.OwnedProductIds);
            _provider.PendingPurchaseReceived += OnPendingPurchaseReceived;
        }

        /// <summary>
        /// 设置游戏发奖逻辑。返回 true 表示奖励已持久化;实现必须按 TransactionId 幂等。
        /// 未配置处理器时交易会安全保留在 pending,不会被商店确认。
        /// </summary>
        public void SetRewardHandler(
            Func<PurchaseTransaction, CancellationToken, UniTask<bool>> handler)
        {
            _rewardHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            if (_state.Pending.Count > 0)
                ReplayAfterHandlerConfiguredAsync().Forget();
        }

        /// <summary>同步发奖兼容入口;新项目应优先使用带 TransactionId 的异步重载。</summary>
        public void SetRewardHandler(Action<string> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            SetRewardHandler((transaction, _) =>
            {
                handler(transaction.ProductId);
                return UniTask.FromResult(true);
            });
        }

        public bool IsOwned(string productId) => !string.IsNullOrEmpty(productId) && _owned.Contains(productId);

        public async UniTask<PurchaseResult> PurchaseAsync(
            string productId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(productId) || !_catalog.TryGet(productId, out var product))
                return Failed(productId, "unknown_product");
            if (product.Type == ProductType.NonConsumable && IsOwned(productId))
                return Failed(productId, "already_owned");

            await _purchaseGate.WaitAsync(ct);
            try
            {
                _analytics.Track("iap_purchase_start", ("product", productId));
                if (!_provider.IsInitialized)
                    return TrackFailure(Failed(productId, "not_initialized"));

                PurchaseResult storeResult;
                try
                {
                    storeResult = await _provider.PurchaseAsync(productId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Store purchase failed: {e.Message}");
                    return TrackFailure(Failed(productId, "provider_error"));
                }

                if (storeResult == null)
                    return TrackFailure(Failed(productId, "empty_store_result"));
                if (!storeResult.StoreApproved && !storeResult.Success)
                    return TrackFailure(storeResult);

                var transaction = storeResult.Transaction;
                if (!IsValidTransaction(transaction) || transaction.ProductId != productId)
                    return TrackFailure(Failed(productId, "transaction_mismatch", true, transaction));

                var failure = await ProcessTransactionAsync(transaction, ct);
                if (failure != null)
                    return TrackFailure(Failed(productId, failure, true, transaction));

                _analytics.Track("iap_purchase_success", ("product", productId),
                    ("transaction", transaction.TransactionId));
                return new PurchaseResult
                {
                    Success = true,
                    StoreApproved = true,
                    ProductId = productId,
                    Transaction = transaction,
                };
            }
            finally
            {
                _purchaseGate.Release();
            }
        }

        public async UniTask RestoreAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await _provider.RestoreAsync(ct);
            await ReplayPendingAsync(ct);
        }

        /// <summary>启动时重放 journal 中尚未确认的交易。</summary>
        public async UniTask ReplayPendingAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var snapshot = _state.Pending.ToArray();
            foreach (var transaction in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessTransactionAsync(transaction, ct);
            }
        }

        async UniTask<string> ProcessTransactionAsync(
            PurchaseTransaction transaction, CancellationToken ct)
        {
            if (!IsValidTransaction(transaction))
                return "invalid_transaction";

            await _transactionGate.WaitAsync(ct);
            try
            {
                if (_completed.Contains(transaction.TransactionId))
                {
                    _provider.Confirm(transaction);
                    return null;
                }

                if (FindPending(transaction.TransactionId) == null)
                {
                    _state.Pending.Add(Clone(transaction));
                    if (!TrySave())
                        return "journal_write_failed";
                }

                ReceiptValidationResult validation;
                try
                {
                    validation = await _receiptValidator.ValidateAsync(transaction, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] IAP receipt validation failed: {e.Message}");
                    return "receipt_validation_error";
                }

                if (validation == null || !validation.IsValid)
                    return validation?.FailureReason ?? "invalid_receipt";
                if (_rewardHandler == null)
                    return "reward_handler_not_configured";

                bool granted;
                try
                {
                    granted = await _rewardHandler(transaction, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[EasyFramework] Reward grant failed for transaction '{transaction.TransactionId}': {e.Message}");
                    return "reward_failed";
                }

                if (!granted)
                    return "reward_pending";

                _completed.Add(transaction.TransactionId);
                _state.CompletedTransactionIds.Add(transaction.TransactionId);
                RemovePending(transaction.TransactionId);
                if (_catalog.TryGet(transaction.ProductId, out var entry) &&
                    entry.Type != ProductType.Consumable &&
                    _owned.Add(transaction.ProductId))
                {
                    _state.OwnedProductIds.Add(transaction.ProductId);
                }

                if (!TrySave())
                {
                    // 内存状态回退,让本次会话仍可重新处理并保持商店 Pending。
                    _completed.Remove(transaction.TransactionId);
                    _state.CompletedTransactionIds.Remove(transaction.TransactionId);
                    if (entry.Type != ProductType.Consumable && _owned.Remove(transaction.ProductId))
                        _state.OwnedProductIds.Remove(transaction.ProductId);
                    if (FindPending(transaction.TransactionId) == null)
                        _state.Pending.Add(Clone(transaction));
                    return "journal_write_failed";
                }

                _provider.Confirm(transaction);
                return null;
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        void OnPendingPurchaseReceived(PurchaseTransaction transaction)
        {
            if (_disposed) return;
            ProcessBackgroundAsync(transaction).Forget();
        }

        async UniTaskVoid ProcessBackgroundAsync(PurchaseTransaction transaction)
        {
            try
            {
                var failure = await ProcessTransactionAsync(transaction, CancellationToken.None);
                if (failure != null)
                    Debug.LogWarning(
                        $"[EasyFramework] Pending IAP transaction '{transaction?.TransactionId}' remains queued: {failure}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        async UniTaskVoid ReplayAfterHandlerConfiguredAsync()
        {
            try
            {
                await ReplayPendingAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        PurchaseResult TrackFailure(PurchaseResult result)
        {
            _analytics.Track("iap_purchase_fail", ("product", result.ProductId),
                ("reason", result.FailureReason ?? "unknown"),
                ("store_approved", result.StoreApproved));
            return result;
        }

        static PurchaseResult Failed(string productId, string reason,
            bool storeApproved = false, PurchaseTransaction transaction = null)
            => new PurchaseResult
            {
                Success = false,
                StoreApproved = storeApproved,
                ProductId = productId,
                FailureReason = reason,
                Transaction = transaction,
            };

        bool TrySave()
        {
            try
            {
                _store.Save(_state);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EasyFramework] IAP transaction journal write failed: {e.Message}");
                return false;
            }
        }

        PurchaseTransaction FindPending(string transactionId)
            => _state.Pending.Find(x => x != null && x.TransactionId == transactionId);

        void RemovePending(string transactionId)
            => _state.Pending.RemoveAll(x => x != null && x.TransactionId == transactionId);

        static bool IsValidTransaction(PurchaseTransaction transaction)
            => transaction != null &&
               !string.IsNullOrWhiteSpace(transaction.ProductId) &&
               !string.IsNullOrWhiteSpace(transaction.TransactionId);

        static PurchaseTransaction Clone(PurchaseTransaction transaction)
            => new PurchaseTransaction
            {
                ProductId = transaction.ProductId,
                TransactionId = transaction.TransactionId,
                Receipt = transaction.Receipt,
                IsLegacy = transaction.IsLegacy,
            };

        [Serializable]
        sealed class LegacyIdList
        {
            public List<string> Ids = new();
        }

        void MigrateLegacyPlayerPrefs()
        {
            if (_state.LegacyPlayerPrefsMigrated) return;

            var legacyOwned = LoadLegacyIds(LegacyOwnedKey);
            var legacyPending = LoadLegacyIds(LegacyPendingKey);
            foreach (var productId in legacyOwned)
                if (!string.IsNullOrWhiteSpace(productId) &&
                    !_state.OwnedProductIds.Contains(productId))
                    _state.OwnedProductIds.Add(productId);

            foreach (var productId in legacyPending)
            {
                if (string.IsNullOrWhiteSpace(productId)) continue;
                var transactionId = LegacyTransactionId(productId);
                if (_state.Pending.Exists(x => x?.TransactionId == transactionId))
                    continue;
                _state.Pending.Add(new PurchaseTransaction
                {
                    ProductId = productId,
                    TransactionId = transactionId,
                    Receipt = null,
                    IsLegacy = true,
                });
            }

            _state.LegacyPlayerPrefsMigrated = true;
            try
            {
                _store.Save(_state);
                PlayerPrefs.DeleteKey(LegacyOwnedKey);
                PlayerPrefs.DeleteKey(LegacyPendingKey);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                _state.LegacyPlayerPrefsMigrated = false;
                Debug.LogWarning($"[EasyFramework] Legacy IAP state migration failed: {e.Message}");
            }
        }

        static List<string> LoadLegacyIds(string key)
        {
            var json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try
            {
                return JsonUtility.FromJson<LegacyIdList>(json)?.Ids ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        static string LegacyTransactionId(string productId)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(productId));
            return "legacy-" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IAPService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _provider.PendingPurchaseReceived -= OnPendingPurchaseReceived;
            _purchaseGate.Dispose();
            _transactionGate.Dispose();
        }

        internal IReadOnlyList<PurchaseTransaction> PendingForTest() => _state.Pending;
    }
}
