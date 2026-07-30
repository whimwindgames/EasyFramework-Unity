using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.Ads
{
    /// <summary>
    /// 正式环境未接入广告 SDK 时的安全关闭实现。奖励广告永远不会返回 Completed,
    /// 避免因为遗漏 SDK 配置而免费发奖。
    /// </summary>
    public sealed class UnavailableAdsProvider : IAdsProvider
    {
        public bool IsRewardedReady => false;

        public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        public UniTask<AdResult> ShowRewardedAsync(string placement)
            => UniTask.FromResult(AdResult.NotReady);

        public UniTask<AdResult> ShowInterstitialAsync(string placement)
            => UniTask.FromResult(AdResult.NotReady);

        public void ShowBanner(BannerPosition position) { }

        public void HideBanner() { }
    }
}
