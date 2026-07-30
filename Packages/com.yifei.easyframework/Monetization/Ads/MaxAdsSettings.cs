using System;

namespace EasyFramework.Monetization.Ads
{
    [Serializable]
    public sealed class MaxAdsPlatformSettings
    {
        public string RewardedAdUnitId;
        public string InterstitialAdUnitId;
        public string BannerAdUnitId;

        public bool HasAnyAdUnit =>
            !string.IsNullOrWhiteSpace(RewardedAdUnitId) ||
            !string.IsNullOrWhiteSpace(InterstitialAdUnitId) ||
            !string.IsNullOrWhiteSpace(BannerAdUnitId);
    }

    /// <summary>
    /// AppLovin MAX 广告位配置。SDK Key 必须在 AppLovin Integration Manager 中配置;
    /// 广告位 ID 必须使用 MAX 后台中对应平台的真实 ID。
    /// </summary>
    [Serializable]
    public sealed class MaxAdsSettings
    {
        public MaxAdsPlatformSettings Android = new();
        public MaxAdsPlatformSettings IOS = new();
        public string BannerBackgroundColor = "#000000";
        public int InitializationTimeoutMs = 15_000;

        public MaxAdsPlatformSettings CurrentPlatform
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return IOS;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return Android;
#else
                // 编辑器测试优先 Android;仅配置 iOS 时仍可预览配置完整性。
                return Android?.HasAnyAdUnit == true ? Android : IOS;
#endif
            }
        }

        public bool IsConfiguredForCurrentPlatform =>
            CurrentPlatform?.HasAnyAdUnit == true;
    }
}
