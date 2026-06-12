using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.Ads
{
    public enum AdResult { Completed, Skipped, NotReady, Failed }

    public enum BannerPosition { Top, Bottom }

    /// <summary>SDK 适配点。Fake / AdMob / LevelPlay 各实现一个;在 LifetimeScope 按平台/环境切换。</summary>
    public interface IAdsProvider
    {
        UniTask InitializeAsync(CancellationToken ct);
        bool IsRewardedReady { get; }
        UniTask<AdResult> ShowRewardedAsync(string placement);
        UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }

    /// <summary>业务层入口(G.Ads)。频控 / 冷却 / 打点在此层,SDK 细节藏在 IAdsProvider。</summary>
    public interface IAdsService
    {
        bool IsRewardedReady { get; }
        UniTask<AdResult> ShowRewardedAsync(string placement);
        UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }
}
