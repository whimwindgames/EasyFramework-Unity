using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class IAPServiceTests
    {
        const string LegacyOwnedKey = "ef.iap.owned";
        const string LegacyPendingKey = "ef.iap.pending";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(LegacyOwnedKey);
            PlayerPrefs.DeleteKey(LegacyPendingKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(LegacyOwnedKey);
            PlayerPrefs.DeleteKey(LegacyPendingKey);
        }

        sealed class FakeAnalytics : IAnalyticsService
        {
            public readonly List<string> Names = new();
            public void Track(string eventName) => Names.Add(eventName);
            public void Track(string eventName, params (string key, object value)[] parameters)
                => Names.Add(eventName);
            public void SetUserProperty(string key, string value) { }
        }

        sealed class MemoryStore : IIAPTransactionStore
        {
            public IAPTransactionState State = new();
            public int SaveCount;
            public IAPTransactionState Load() => State;
            public void Save(IAPTransactionState state)
            {
                State = state;
                SaveCount++;
            }
        }

        sealed class RejectingValidator : IIAPReceiptValidator
        {
            public UniTask<ReceiptValidationResult> ValidateAsync(
                PurchaseTransaction transaction, CancellationToken ct)
                => UniTask.FromResult(ReceiptValidationResult.Invalid("invalid_receipt"));
        }

        static ProductCatalog MakeCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<ProductCatalog>();
            catalog.AddEntryForTest("coins_100", ProductType.Consumable,
                new List<string> { "+100 coins" });
            catalog.AddEntryForTest("remove_ads", ProductType.NonConsumable,
                new List<string> { "no more ads" });
            catalog.AddEntryForTest("vip_monthly", ProductType.Subscription,
                new List<string> { "vip" });
            return catalog;
        }

        static IAPService MakeService(FakeIAPProvider provider, MemoryStore store,
            FakeAnalytics analytics = null, IIAPReceiptValidator validator = null)
            => new IAPService(provider, MakeCatalog(), analytics ?? new FakeAnalytics(),
                validator ?? new DevelopmentIAPReceiptValidator(), store);

        [Test]
        public void Purchase_Success_JournalsRewardsAndConfirms()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            var store = new MemoryStore();
            var analytics = new FakeAnalytics();
            var rewarded = new List<string>();
            using var service = MakeService(provider, store, analytics);
            service.SetRewardHandler((transaction, _) =>
            {
                rewarded.Add(transaction.TransactionId);
                return UniTask.FromResult(true);
            });

            var result = service.PurchaseAsync("coins_100").GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.StoreApproved);
            Assert.AreEqual(1, rewarded.Count);
            CollectionAssert.Contains(provider.ConfirmedTransactionIds, result.Transaction.TransactionId);
            CollectionAssert.IsEmpty(service.PendingForTest());
            CollectionAssert.AreEqual(
                new[] { "iap_purchase_start", "iap_purchase_success" }, analytics.Names);
            Assert.GreaterOrEqual(store.SaveCount, 2, "发奖前写 pending,发奖后再写 completed");
        }

        [Test]
        public void Purchase_Cancelled_DoesNotRewardOrConfirm()
        {
            var provider = new FakeIAPProvider
            {
                Initialized = true,
                NextOutcome = FakeIAPProvider.Outcome.Cancelled,
            };
            var rewarded = 0;
            using var service = MakeService(provider, new MemoryStore());
            service.SetRewardHandler((_, __) =>
            {
                rewarded++;
                return UniTask.FromResult(true);
            });

            var result = service.PurchaseAsync("coins_100").GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.StoreApproved);
            Assert.AreEqual("cancelled", result.FailureReason);
            Assert.Zero(rewarded);
            CollectionAssert.IsEmpty(provider.ConfirmedTransactionIds);
        }

        [Test]
        public void ReceiptRejected_RemainsPendingAndNeverRewards()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            var rewarded = 0;
            using var service = MakeService(
                provider, new MemoryStore(), validator: new RejectingValidator());
            service.SetRewardHandler((_, __) =>
            {
                rewarded++;
                return UniTask.FromResult(true);
            });

            var result = service.PurchaseAsync("coins_100").GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.StoreApproved);
            Assert.AreEqual("invalid_receipt", result.FailureReason);
            Assert.Zero(rewarded);
            Assert.AreEqual(1, service.PendingForTest().Count);
            CollectionAssert.IsEmpty(provider.ConfirmedTransactionIds);
        }

        [Test]
        public void ClientOnlyValidator_AcceptsCompleteStoreTransaction()
        {
            var validator = new ClientOnlyIAPReceiptValidator();
            var result = validator.ValidateAsync(new PurchaseTransaction
            {
                ProductId = "coins_100",
                TransactionId = "transaction-1",
                Receipt = "{\"Store\":\"GooglePlay\"}",
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(result.IsValid);
        }

        [TestCase(false, null, "receipt_missing")]
        [TestCase(true, "legacy", "legacy_receipt_unavailable")]
        public void ClientOnlyValidator_RejectsUnverifiableTransactions(
            bool legacy, string receipt, string expectedReason)
        {
            var validator = new ClientOnlyIAPReceiptValidator();
            var result = validator.ValidateAsync(new PurchaseTransaction
            {
                ProductId = "coins_100",
                TransactionId = "transaction-1",
                Receipt = receipt,
                IsLegacy = legacy,
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(expectedReason, result.FailureReason);
        }

        [Test]
        public void RewardFailure_ReplaysOnNextStartWithSameTransactionId()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            var store = new MemoryStore();
            string firstTransactionId;
            using (var service = MakeService(provider, store))
            {
                service.SetRewardHandler((_, __) => UniTask.FromResult(false));
                var result = service.PurchaseAsync("coins_100").GetAwaiter().GetResult();
                Assert.AreEqual("reward_pending", result.FailureReason);
                firstTransactionId = result.Transaction.TransactionId;
                Assert.AreEqual(firstTransactionId, service.PendingForTest()[0].TransactionId);
            }

            var replayed = new List<string>();
            using (var service = MakeService(provider, store))
            {
                service.SetRewardHandler((transaction, _) =>
                {
                    replayed.Add(transaction.TransactionId);
                    return UniTask.FromResult(true);
                });
                service.ReplayPendingAsync().GetAwaiter().GetResult();
                CollectionAssert.AreEqual(new[] { firstTransactionId }, replayed);
                CollectionAssert.IsEmpty(service.PendingForTest());
                CollectionAssert.Contains(provider.ConfirmedTransactionIds, firstTransactionId);
            }
        }

        [Test]
        public void DuplicateTransaction_IsIdempotentlyConfirmedWithoutSecondReward()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            var store = new MemoryStore();
            var rewards = 0;
            PurchaseTransaction transaction;
            using (var service = MakeService(provider, store))
            {
                service.SetRewardHandler((_, __) =>
                {
                    rewards++;
                    return UniTask.FromResult(true);
                });
                transaction = service.PurchaseAsync("coins_100").GetAwaiter().GetResult().Transaction;
            }

            provider.RestoreTransactions.Add(transaction);
            using (var service = MakeService(provider, store))
            {
                service.SetRewardHandler((_, __) =>
                {
                    rewards++;
                    return UniTask.FromResult(true);
                });
                service.RestoreAsync().GetAwaiter().GetResult();
            }

            Assert.AreEqual(1, rewards);
        }

        [Test]
        public void NonConsumableOwnership_PersistsInJournal()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            var store = new MemoryStore();
            using (var service = MakeService(provider, store))
            {
                service.SetRewardHandler((_, __) => UniTask.FromResult(true));
                Assert.IsTrue(service.PurchaseAsync("remove_ads").GetAwaiter().GetResult().Success);
                Assert.IsTrue(service.IsOwned("remove_ads"));
            }

            using var restoredService = MakeService(provider, store);
            Assert.IsTrue(restoredService.IsOwned("remove_ads"));
            var duplicate = restoredService.PurchaseAsync("remove_ads").GetAwaiter().GetResult();
            Assert.AreEqual("already_owned", duplicate.FailureReason);
        }

        [Test]
        public void BootInitialization_PreservesCatalogProductTypes()
        {
            var provider = new FakeIAPProvider { Initialized = false };
            using var service = MakeService(provider, new MemoryStore());
            var boot = new IAPBootTask(provider, MakeCatalog(), service);

            boot.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(3, provider.InitializedProducts.Count);
            Assert.AreEqual(ProductType.Consumable, provider.InitializedProducts[0].Type);
            Assert.AreEqual(ProductType.NonConsumable, provider.InitializedProducts[1].Type);
            Assert.AreEqual(ProductType.Subscription, provider.InitializedProducts[2].Type);
        }

        [Test]
        public void MissingRewardHandler_KeepsStoreApprovedPurchasePending()
        {
            var provider = new FakeIAPProvider { Initialized = true };
            using var service = MakeService(provider, new MemoryStore());

            var result = service.PurchaseAsync("coins_100").GetAwaiter().GetResult();

            Assert.IsTrue(result.StoreApproved);
            Assert.AreEqual("reward_handler_not_configured", result.FailureReason);
            Assert.AreEqual(1, service.PendingForTest().Count);
            CollectionAssert.IsEmpty(provider.ConfirmedTransactionIds);
        }

        [Test]
        public void LegacyPlayerPrefsOwnership_MigratesIntoJournal()
        {
            PlayerPrefs.SetString(LegacyOwnedKey, "{\"Ids\":[\"remove_ads\"]}");
            PlayerPrefs.Save();
            var store = new MemoryStore();

            using var service = MakeService(
                new FakeIAPProvider { Initialized = true }, store);

            Assert.IsTrue(service.IsOwned("remove_ads"));
            CollectionAssert.Contains(store.State.OwnedProductIds, "remove_ads");
            Assert.IsFalse(PlayerPrefs.HasKey(LegacyOwnedKey));
            Assert.IsTrue(store.State.LegacyPlayerPrefsMigrated);
        }
    }
}
