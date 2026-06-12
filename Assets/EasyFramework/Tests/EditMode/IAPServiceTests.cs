using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class IAPServiceTests
    {
        const string OwnedKey = "ef.iap.owned";
        const string PendingKey = "ef.iap.pending";

        sealed class FakeAnalytics : IAnalyticsService
        {
            public readonly List<string> Names = new();
            public void Track(string eventName) => Names.Add(eventName);
            public void Track(string eventName, params (string key, object value)[] parameters) => Names.Add(eventName);
            public void SetUserProperty(string key, string value) { }
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
        }

        static ProductCatalog MakeCatalog()
        {
            var c = ScriptableObject.CreateInstance<ProductCatalog>();
            c.AddEntryForTest("coins_100", ProductType.Consumable, new List<string> { "+100 coins" });
            c.AddEntryForTest("remove_ads", ProductType.NonConsumable, new List<string> { "no more ads" });
            return c;
        }

        [Test]
        public void Purchase_Success_InvokesRewardHandlerAndTracks()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var an = new FakeAnalytics();
            var rewarded = new List<string>();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(rewarded.Add);

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsTrue(r.Success);
            Assert.AreEqual("coins_100", r.ProductId);
            CollectionAssert.AreEqual(new[] { "coins_100" }, rewarded);
            CollectionAssert.AreEqual(new[] { "iap_purchase_start", "iap_purchase_success" }, an.Names);
        }

        [Test]
        public void Purchase_Cancelled_DoesNotReward_AndTracksFail()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Cancelled };
            var an = new FakeAnalytics();
            var rewarded = new List<string>();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(rewarded.Add);

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsFalse(r.Success);
            Assert.AreEqual("cancelled", r.FailureReason);
            CollectionAssert.IsEmpty(rewarded);
            CollectionAssert.AreEqual(new[] { "iap_purchase_start", "iap_purchase_fail" }, an.Names);
        }

        [Test]
        public void NonConsumable_Owned_PersistsAcrossInstances()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => { });
            svc.PurchaseAsync("remove_ads").GetAwaiter().GetResult();
            Assert.IsTrue(svc.IsOwned("remove_ads"));

            // 重建 service:从 PlayerPrefs 恢复已购集合。
            var svc2 = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            Assert.IsTrue(svc2.IsOwned("remove_ads"));
        }

        [Test]
        public void Consumable_NotMarkedOwned()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => { });
            svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsFalse(svc.IsOwned("coins_100"), "消耗型不记入已购");
        }

        [Test]
        public void RewardThrows_EnqueuesPending_AndReplaysOnNextStart()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => throw new Exception("reward boom")); // 发奖失败

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsTrue(r.Success, "购买本身成功(发奖失败不回滚购买)");
            // 掉单入 pending 队列(持久化)。
            CollectionAssert.Contains(svc.PendingForTest(), "coins_100");

            // 重建 service + 正常 RewardHandler,重放 pending。
            var replayed = new List<string>();
            var svc2 = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc2.SetRewardHandler(replayed.Add);
            svc2.ReplayPending();
            CollectionAssert.AreEqual(new[] { "coins_100" }, replayed);
            CollectionAssert.IsEmpty(svc2.PendingForTest(), "重放成功后清空 pending");
        }

        [Test]
        public void Purchase_NotInitialized_ReturnsFailureResult_NoThrow()
        {
            var prov = new FakeIAPProvider { Initialized = false };
            var an = new FakeAnalytics();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(_ => { });

            PurchaseResult r = null;
            Assert.DoesNotThrow(() => r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult());
            Assert.IsFalse(r.Success);
            Assert.AreEqual("not_initialized", r.FailureReason);
            CollectionAssert.Contains(an.Names, "iap_purchase_fail");
        }

        [Test]
        public void Restore_DoesNotThrow()
        {
            var prov = new FakeIAPProvider { Initialized = true };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            Assert.DoesNotThrow(() => svc.RestoreAsync().GetAwaiter().GetResult());
        }
    }
}
