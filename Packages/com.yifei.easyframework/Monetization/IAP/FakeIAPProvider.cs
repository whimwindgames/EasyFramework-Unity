using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>编辑器 / 测试用 IAP provider。可配购买结果。</summary>
    public sealed class FakeIAPProvider : IIAPProvider
    {
        public enum Outcome { Success, Cancelled, Failed }

        public bool Initialized = true;
        public Outcome NextOutcome = Outcome.Success;

        public bool IsInitialized => Initialized;

        public UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct)
        {
            Initialized = true;
            Debug.Log($"[EasyFramework] FakeIAPProvider initialized with {productIds?.Count ?? 0} products.");
            return UniTask.CompletedTask;
        }

        public UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            var result = NextOutcome switch
            {
                Outcome.Success => new PurchaseResult { Success = true, ProductId = productId },
                Outcome.Cancelled => new PurchaseResult { Success = false, ProductId = productId, FailureReason = "cancelled" },
                _ => new PurchaseResult { Success = false, ProductId = productId, FailureReason = "failed" },
            };
            Debug.Log($"[EasyFramework] Fake purchase '{productId}' -> {NextOutcome}");
            return UniTask.FromResult(result);
        }

        public UniTask RestoreAsync()
        {
            Debug.Log("[EasyFramework] Fake restore (no-op).");
            return UniTask.CompletedTask;
        }
    }
}
