using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Configs;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class AdsServiceTests
    {
        // ---- Fakes ----
        sealed class FakeConfig : IConfigService
        {
            readonly Dictionary<string, object> _values = new();
            public void Set(string key, object value) => _values[key] = value;
            public T Get<T>(string key, T defaultValue)
                => _values.TryGetValue(key, out var v) ? (T)v : defaultValue;
            public bool Has(string key) => _values.ContainsKey(key);
            public UniTask RefreshRemoteAsync(CancellationToken ct = default)
                => UniTask.CompletedTask;
        }

        sealed class FakeAnalytics : IAnalyticsService
        {
            public readonly List<(string name, Dictionary<string, object> p)> Events = new();
            public void Track(string eventName) => Events.Add((eventName, new Dictionary<string, object>()));
            public void Track(string eventName, params (string key, object value)[] parameters)
            {
                var d = new Dictionary<string, object>();
                foreach (var (k, val) in parameters) d[k] = val;
                Events.Add((eventName, d));
            }
            public void SetUserProperty(string key, string value) { }
            public List<string> Names()
            {
                var n = new List<string>();
                foreach (var e in Events) n.Add(e.name);
                return n;
            }
        }

        static (AdsService svc, FakeAdsProvider prov, FakeAnalytics an, FakeConfig cfg, float[] clock)
            Build(float cooldown = 30f, int minGap = 1)
        {
            var prov = new FakeAdsProvider { Delay = 0f, IsRewardedReady = true, NextResult = AdResult.Completed };
            var an = new FakeAnalytics();
            var cfg = new FakeConfig();
            cfg.Set("ads.interstitial_cooldown", cooldown);
            cfg.Set("ads.interstitial_min_gap", minGap);
            var clock = new[] { 0f };
            var svc = new AdsService(prov, cfg, an, () => clock[0]);
            return (svc, prov, an, cfg, clock);
        }

        [Test]
        public void Interstitial_FirstShow_RunsAndTracksFullChain()
        {
            var (svc, _, an, _, _) = Build();
            var r = svc.ShowInterstitialAsync("level_end").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r);
            CollectionAssert.AreEqual(new[] { "ad_request", "ad_show", "ad_complete" }, an.Names());
            Assert.AreEqual("level_end", an.Events[0].p["placement"]);
            Assert.AreEqual("interstitial", an.Events[0].p["type"]);
        }

        [Test]
        public void Interstitial_WithinCooldown_ReturnsNotReadyAndSkipsShow()
        {
            var (svc, _, an, _, clock) = Build(cooldown: 30f, minGap: 0);
            svc.ShowInterstitialAsync("a").GetAwaiter().GetResult(); // t=0, completes, cooldown set
            clock[0] = 10f; // still within 30s cooldown
            an.Events.Clear();
            var r = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r);
            CollectionAssert.DoesNotContain(an.Names(), "ad_show", "冷却内不应展示");
        }

        [Test]
        public void Interstitial_AfterCooldown_ShowsAgain()
        {
            var (svc, _, _, _, clock) = Build(cooldown: 30f, minGap: 0);
            svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            clock[0] = 31f; // past cooldown
            var r = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r);
        }

        [Test]
        public void Interstitial_NotReady_DoesNotConsumeCooldown()
        {
            var (svc, prov, an, _, clock) = Build(cooldown: 30f, minGap: 0);
            prov.NextResult = AdResult.NotReady;
            var r1 = svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r1);
            // cooldown 未刷新,下次(同一时刻)Completed 应能展示
            prov.NextResult = AdResult.Completed;
            var r2 = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r2, "NotReady 不计冷却,后续可立即展示");
            // NotReady 一路只打 ad_request(无 ad_show/ad_complete)
            Assert.AreEqual("ad_request", an.Events[0].name);
        }

        [Test]
        public void Interstitial_MinGap_BlocksBeforeEnoughCalls()
        {
            // min_gap=2:每 2 次 ShowInterstitial 才允许一次真实展示
            var (svc, _, _, _, clock) = Build(cooldown: 0f, minGap: 2);
            var r1 = svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r1, "首次满足间隔(计数从允许态起)");
            var r2 = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r2, "间隔未到");
            var r3 = svc.ShowInterstitialAsync("c").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r3, "达到间隔");
        }

        [Test]
        public void Rewarded_ResultPassesThrough_AndTracks()
        {
            var (svc, prov, an, _, _) = Build();
            prov.NextResult = AdResult.Skipped;
            var r = svc.ShowRewardedAsync("double_coins").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Skipped, r, "奖励视频结果原样透传");
            CollectionAssert.AreEqual(new[] { "ad_request", "ad_show", "ad_complete" }, an.Names());
            Assert.AreEqual("rewarded", an.Events[0].p["type"]);
        }

        [Test]
        public void Rewarded_NotReady_ReturnsNotReady()
        {
            var (svc, prov, an, _, _) = Build();
            prov.IsRewardedReady = false;
            Assert.IsFalse(svc.IsRewardedReady);
            var r = svc.ShowRewardedAsync("p").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r);
            Assert.AreEqual("ad_request", an.Events[0].name);
            CollectionAssert.DoesNotContain(an.Names(), "ad_show");
        }
    }
}
