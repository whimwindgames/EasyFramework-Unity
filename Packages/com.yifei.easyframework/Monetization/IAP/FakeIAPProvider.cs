using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>编辑器 / 测试用 IAP provider。可配购买结果、恢复交易和确认记录。</summary>
    public sealed class FakeIAPProvider : IIAPProvider
    {
        public enum Outcome { Success, Cancelled, Failed }

        public event Action<PurchaseTransaction> PendingPurchaseReceived;

        public bool Initialized = true;
        public Outcome NextOutcome = Outcome.Success;
        public readonly List<IAPProductDefinition> InitializedProducts = new();
        public readonly List<PurchaseTransaction> RestoreTransactions = new();
        public readonly List<string> ConfirmedTransactionIds = new();

        public bool IsInitialized => Initialized;

        public UniTask InitializeAsync(
            IReadOnlyList<IAPProductDefinition> products, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            InitializedProducts.Clear();
            if (products != null)
                InitializedProducts.AddRange(products);
            Initialized = true;
            Debug.Log($"[EasyFramework] FakeIAPProvider initialized with {products?.Count ?? 0} products.");
            return UniTask.CompletedTask;
        }

        public UniTask<PurchaseResult> PurchaseAsync(
            string productId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var transaction = new PurchaseTransaction
            {
                ProductId = productId,
                TransactionId = $"fake-{Guid.NewGuid():N}",
                Receipt = "{\"fake\":true}",
            };
            var result = NextOutcome switch
            {
                Outcome.Success => new PurchaseResult
                {
                    Success = false,
                    StoreApproved = true,
                    ProductId = productId,
                    Transaction = transaction,
                },
                Outcome.Cancelled => new PurchaseResult
                {
                    Success = false,
                    StoreApproved = false,
                    ProductId = productId,
                    FailureReason = "cancelled",
                },
                _ => new PurchaseResult
                {
                    Success = false,
                    StoreApproved = false,
                    ProductId = productId,
                    FailureReason = "failed",
                },
            };
            Debug.Log($"[EasyFramework] Fake purchase '{productId}' -> {NextOutcome}");
            return UniTask.FromResult(result);
        }

        public UniTask RestoreAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var transaction in RestoreTransactions)
                PendingPurchaseReceived?.Invoke(transaction);
            return UniTask.CompletedTask;
        }

        public void Confirm(PurchaseTransaction transaction)
        {
            if (transaction != null && !ConfirmedTransactionIds.Contains(transaction.TransactionId))
                ConfirmedTransactionIds.Add(transaction.TransactionId);
        }
    }
}
