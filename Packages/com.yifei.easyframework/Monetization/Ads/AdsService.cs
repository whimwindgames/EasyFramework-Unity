using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Configs;
using UnityEngine;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdsService : IAdsService
    {
        const string CooldownKey = "ads.interstitial_cooldown";
        const string MinGapKey = "ads.interstitial_min_gap";
        const float DefaultCooldown = 30f;
        const int DefaultMinGap = 1;

        readonly IAdsProvider _provider;
        readonly IConfigService _config;
        readonly IAnalyticsService _analytics;
        readonly Func<float> _now;

        float _lastInterstitialTime = float.NegativeInfinity;
        int _interstitialCallsSinceShown;

        /// <summary>生产构造:时间源默认 Time.realtimeSinceStartup。</summary>
        public AdsService(IAdsProvider provider, IConfigService config, IAnalyticsService analytics)
            : this(provider, config, analytics, () => Time.realtimeSinceStartup) { }

        /// <summary>测试构造:可注入时间源。</summary>
        internal AdsService(IAdsProvider provider, IConfigService config,
            IAnalyticsService analytics, Func<float> nowProvider)
        {
            _provider = provider;
            _config = config;
            _analytics = analytics;
            _now = nowProvider;
            // 首次允许展示:间隔计数从达到态起。
            _interstitialCallsSinceShown = Mathf.Max(0, _config.Get(MinGapKey, DefaultMinGap) - 1);
        }

        public bool IsRewardedReady => _provider.IsRewardedReady;

        public async UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            _analytics.Track("ad_request", ("placement", placement), ("type", "rewarded"));
            if (!_provider.IsRewardedReady)
                return AdResult.NotReady;

            _analytics.Track("ad_show", ("placement", placement), ("type", "rewarded"));
            var result = await _provider.ShowRewardedAsync(placement);
            _analytics.Track("ad_complete", ("placement", placement), ("type", "rewarded"),
                ("result", result.ToString()));
            return result;
        }

        public async UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            _analytics.Track("ad_request", ("placement", placement), ("type", "interstitial"));

            if (!PassesFrequencyGate())
                return AdResult.NotReady;

            _analytics.Track("ad_show", ("placement", placement), ("type", "interstitial"));
            var result = await _provider.ShowInterstitialAsync(placement);
            _analytics.Track("ad_complete", ("placement", placement), ("type", "interstitial"),
                ("result", result.ToString()));

            if (result == AdResult.Completed)
            {
                _lastInterstitialTime = _now();
                _interstitialCallsSinceShown = 0;
            }
            return result;
        }

        bool PassesFrequencyGate()
        {
            var cooldown = _config.Get(CooldownKey, DefaultCooldown);
            var minGap = Mathf.Max(1, _config.Get(MinGapKey, DefaultMinGap));

            // 关卡间隔:每 minGap 次调用允许一次展示。
            _interstitialCallsSinceShown++;
            if (_interstitialCallsSinceShown < minGap)
                return false;

            // 冷却:距上次成功展示需超过 cooldown 秒。
            if (_now() - _lastInterstitialTime < cooldown)
                return false;

            return true;
        }

        public void ShowBanner(BannerPosition position) => _provider.ShowBanner(position);

        public void HideBanner() => _provider.HideBanner();
    }
}
