// ===========================================================================
// AdMobAdsProvider —— 真实广告 SDK 接入槽(NOT a placeholder)。
//
// 这是设计好的「接入位置」:本阶段(无人值守)不接入真实 AdMob/LevelPlay SDK,
// 因其需要开发者账号、App ID、原生插件与编辑器侧配置。接入步骤:
//   1. 导入 Google Mobile Ads (AdMob) Unity 插件;
//   2. 在 Player Settings > Scripting Define Symbols 定义 EF_ADMOB;
//   3. 填充下方各方法体(签名已与 IAdsProvider 锁定,业务层零改动);
//   4. 在 GameLifetimeScope 覆盖注册 IAdsProvider -> AdMobAdsProvider。
// IAdsService(AdsService 业务层)、FakeAdsProvider、AdsServiceTests 全部不变。
// ===========================================================================
#if EF_ADMOB
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdMobAdsProvider : IAdsProvider
    {
        // TODO(接入时): 注入并初始化 GoogleMobileAds,缓存 RewardedAd / InterstitialAd / BannerView。

        public UniTask InitializeAsync(CancellationToken ct)
        {
            // MobileAds.Initialize(...); 预加载首个奖励/插屏。
            return UniTask.CompletedTask;
        }

        public bool IsRewardedReady => false; // 接入时: 返回缓存的 RewardedAd != null && CanShow

        public UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            // 接入时: rewardedAd.Show(reward => ...); 把 OnUserEarnedReward/OnAdClosed 映射为 AdResult。
            return UniTask.FromResult(AdResult.NotReady);
        }

        public UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            // 接入时: interstitialAd.Show(); 映射关闭/失败为 AdResult。
            return UniTask.FromResult(AdResult.NotReady);
        }

        public void ShowBanner(BannerPosition position) { /* 接入时: 创建/显示 BannerView。 */ }

        public void HideBanner() { /* 接入时: bannerView.Hide()。 */ }
    }
}
#endif
