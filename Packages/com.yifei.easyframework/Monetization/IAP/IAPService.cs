using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    public sealed class IAPService : IIAPService
    {
        const string OwnedKey = "ef.iap.owned";
        const string PendingKey = "ef.iap.pending";

        readonly IIAPProvider _provider;
        readonly ProductCatalog _catalog;
        readonly IAnalyticsService _analytics;

        readonly HashSet<string> _owned;
        readonly List<string> _pending;
        Action<string> _rewardHandler;

        public IAPService(IIAPProvider provider, ProductCatalog catalog, IAnalyticsService analytics)
        {
            _provider = provider;
            _catalog = catalog;
            _analytics = analytics;
            _owned = new HashSet<string>(LoadList(OwnedKey));
            _pending = new List<string>(LoadList(PendingKey));
            _rewardHandler = id => Debug.Log($"[EasyFramework] Default reward grant for '{id}' (override via SetRewardHandler).");
        }

        /// <summary>由 GameLifetimeScope 注册游戏侧发奖逻辑。</summary>
        public void SetRewardHandler(Action<string> handler)
            => _rewardHandler = handler ?? throw new ArgumentNullException(nameof(handler));

        public bool IsOwned(string productId) => _owned.Contains(productId);

        public async UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            _analytics.Track("iap_purchase_start", ("product", productId));

            if (!_provider.IsInitialized)
            {
                _analytics.Track("iap_purchase_fail", ("product", productId), ("reason", "not_initialized"));
                return new PurchaseResult { Success = false, ProductId = productId, FailureReason = "not_initialized" };
            }

            var result = await _provider.PurchaseAsync(productId);
            if (!result.Success)
            {
                _analytics.Track("iap_purchase_fail", ("product", productId),
                    ("reason", result.FailureReason ?? "unknown"));
                return result;
            }

            // 购买成功:标记非消耗型已购,发奖(发奖失败入 pending 补发)。
            if (_catalog.TryGet(productId, out var entry) && entry.Type == ProductType.NonConsumable)
                MarkOwned(productId);

            _analytics.Track("iap_purchase_success", ("product", productId));
            TryGrantReward(productId);
            return result;
        }

        public UniTask RestoreAsync() => _provider.RestoreAsync();

        /// <summary>重放 pending 掉单(IAPBootTask 启动时调)。逐个发奖,成功者移出队列。</summary>
        public void ReplayPending()
        {
            if (_pending.Count == 0) return;
            var still = new List<string>();
            foreach (var id in _pending)
            {
                try { _rewardHandler(id); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Pending reward replay failed for '{id}': {e.Message}");
                    still.Add(id);
                }
            }
            _pending.Clear();
            _pending.AddRange(still);
            SaveList(PendingKey, _pending);
        }

        void TryGrantReward(string productId)
        {
            try
            {
                _rewardHandler(productId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Reward grant threw for '{productId}'; enqueuing for replay: {e.Message}");
                if (!_pending.Contains(productId))
                {
                    _pending.Add(productId);
                    SaveList(PendingKey, _pending);
                }
            }
        }

        void MarkOwned(string productId)
        {
            if (_owned.Add(productId))
                SaveList(OwnedKey, new List<string>(_owned));
        }

        // ---- PlayerPrefs JSON id 列表持久化 ----
        [Serializable] sealed class IdList { public List<string> Ids = new(); }

        static List<string> LoadList(string key)
        {
            var json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return JsonUtility.FromJson<IdList>(json)?.Ids ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        static void SaveList(string key, List<string> ids)
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(new IdList { Ids = ids }));
            PlayerPrefs.Save();
        }

        // ---- 测试钩子 ----
        internal IReadOnlyList<string> PendingForTest() => _pending;
    }
}
