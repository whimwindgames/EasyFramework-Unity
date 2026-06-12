using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.Ads
{
    /// <summary>编辑器 / 测试用广告 provider。模拟弹窗:可配结果与延迟。</summary>
    public sealed class FakeAdsProvider : IAdsProvider
    {
        public AdResult NextResult = AdResult.Completed;
        public bool IsRewardedReady { get; set; } = true;
        /// <summary>模拟展示耗时(秒);测试设 0 以同步完成。</summary>
        public float Delay = 0.1f;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            Debug.Log("[EasyFramework] FakeAdsProvider initialized.");
            return UniTask.CompletedTask;
        }

        public async UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            Debug.Log($"[EasyFramework] Fake rewarded ad @ '{placement}' -> {NextResult}");
            await DelayIfNeeded();
            return NextResult;
        }

        public async UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            Debug.Log($"[EasyFramework] Fake interstitial @ '{placement}' -> {NextResult}");
            await DelayIfNeeded();
            return NextResult;
        }

        UniTask DelayIfNeeded()
            => Delay > 0f
                ? UniTask.Delay(TimeSpan.FromSeconds(Delay), DelayType.Realtime)
                : UniTask.CompletedTask;

        public void ShowBanner(BannerPosition position)
            => Debug.Log($"[EasyFramework] Fake banner @ {position}");

        public void HideBanner() => Debug.Log("[EasyFramework] Fake banner hidden.");
    }
}
