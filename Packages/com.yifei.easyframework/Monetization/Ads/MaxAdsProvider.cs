using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.Ads
{
    internal interface IMaxAdsClient : IDisposable
    {
        event Action<bool> SdkInitialized;
        event Action<string> RewardedLoaded;
        event Action<string> RewardedLoadFailed;
        event Action<string> RewardedHidden;
        event Action<string> RewardedDisplayFailed;
        event Action<string> RewardReceived;
        event Action<string> InterstitialLoaded;
        event Action<string> InterstitialLoadFailed;
        event Action<string> InterstitialHidden;
        event Action<string> InterstitialDisplayFailed;

        void Initialize();
        bool IsRewardedReady(string adUnitId);
        bool IsInterstitialReady(string adUnitId);
        void LoadRewarded(string adUnitId);
        void ShowRewarded(string adUnitId, string placement);
        void LoadInterstitial(string adUnitId);
        void ShowInterstitial(string adUnitId, string placement);
        void CreateBanner(string adUnitId, BannerPosition position, string backgroundColor);
        void ShowBanner(string adUnitId);
        void HideBanner(string adUnitId);
        void DestroyBanner(string adUnitId);
    }

    /// <summary>
    /// AppLovin MAX 正式广告适配器。奖励以 OnAdReceivedRewardEvent 为唯一完成信号;
    /// 全屏广告关闭或展示失败后立即预加载下一条,加载失败按 2~64 秒指数退避。
    /// </summary>
    public sealed class MaxAdsProvider : IAdsProvider, IDisposable
    {
        readonly MaxAdsSettings _settings;
        readonly MaxAdsPlatformSettings _platform;
        readonly IMaxAdsClient _client;
        readonly CancellationTokenSource _lifetime = new();
        readonly Func<TimeSpan, CancellationToken, UniTask> _delay;

        UniTaskCompletionSource<bool> _initialization;
        UniTaskCompletionSource<AdResult> _rewardedShow;
        UniTaskCompletionSource<AdResult> _interstitialShow;
        int _rewardedRetryAttempt;
        int _interstitialRetryAttempt;
        bool _rewardEarned;
        bool _initialized;
        bool _bannerCreated;
        BannerPosition _bannerPosition;
        bool _disposed;

        public MaxAdsProvider(MaxAdsSettings settings)
            : this(settings, new MaxSdkAdsClient(),
                (delay, ct) => UniTask.Delay(delay, DelayType.Realtime, cancellationToken: ct)) { }

        internal MaxAdsProvider(MaxAdsSettings settings, IMaxAdsClient client,
            Func<TimeSpan, CancellationToken, UniTask> delay = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _platform = settings.CurrentPlatform ??
                        throw new ArgumentException("MAX platform settings are missing.", nameof(settings));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _delay = delay ??
                     ((duration, ct) => UniTask.Delay(
                         duration, DelayType.Realtime, cancellationToken: ct));
            Subscribe();
        }

        public bool IsRewardedReady =>
            !_disposed &&
            _initialized &&
            !string.IsNullOrWhiteSpace(_platform.RewardedAdUnitId) &&
            _client.IsRewardedReady(_platform.RewardedAdUnitId);

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            if (_initialized) return;
            if (!_settings.IsConfiguredForCurrentPlatform)
                throw new InvalidOperationException(
                    "AppLovin MAX has no ad unit configured for the current platform.");
            if (_initialization != null)
                throw new InvalidOperationException("AppLovin MAX initialization is already in progress.");

            var completion = new UniTaskCompletionSource<bool>();
            _initialization = completion;
            try
            {
                _client.Initialize();
                var timeoutMs = Math.Max(1_000, _settings.InitializationTimeoutMs);
                var (timedOut, succeeded) = await completion.Task
                    .AttachExternalCancellation(ct)
                    .TimeoutWithoutException(TimeSpan.FromMilliseconds(timeoutMs));
                if (timedOut)
                    throw new TimeoutException("AppLovin MAX initialization timed out.");
                if (!succeeded)
                    throw new InvalidOperationException("AppLovin MAX initialization failed.");
            }
            finally
            {
                if (ReferenceEquals(_initialization, completion))
                    _initialization = null;
            }
        }

        public UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            ThrowIfDisposed();
            if (!IsRewardedReady || _rewardedShow != null)
                return UniTask.FromResult(AdResult.NotReady);

            _rewardEarned = false;
            _rewardedShow = new UniTaskCompletionSource<AdResult>();
            try
            {
                _client.ShowRewarded(_platform.RewardedAdUnitId, placement);
                return _rewardedShow.Task;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] MAX rewarded show failed: {e.Message}");
                CompleteRewarded(AdResult.Failed);
                LoadRewarded();
                return UniTask.FromResult(AdResult.Failed);
            }
        }

        public UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            ThrowIfDisposed();
            if (!_initialized ||
                string.IsNullOrWhiteSpace(_platform.InterstitialAdUnitId) ||
                !_client.IsInterstitialReady(_platform.InterstitialAdUnitId) ||
                _interstitialShow != null)
                return UniTask.FromResult(AdResult.NotReady);

            _interstitialShow = new UniTaskCompletionSource<AdResult>();
            try
            {
                _client.ShowInterstitial(_platform.InterstitialAdUnitId, placement);
                return _interstitialShow.Task;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] MAX interstitial show failed: {e.Message}");
                CompleteInterstitial(AdResult.Failed);
                LoadInterstitial();
                return UniTask.FromResult(AdResult.Failed);
            }
        }

        public void ShowBanner(BannerPosition position)
        {
            ThrowIfDisposed();
            var adUnitId = _platform.BannerAdUnitId;
            if (!_initialized || string.IsNullOrWhiteSpace(adUnitId))
                return;

            if (_bannerCreated && _bannerPosition != position)
            {
                _client.DestroyBanner(adUnitId);
                _bannerCreated = false;
            }

            if (!_bannerCreated)
            {
                _client.CreateBanner(adUnitId, position, _settings.BannerBackgroundColor);
                _bannerCreated = true;
                _bannerPosition = position;
            }
            _client.ShowBanner(adUnitId);
        }

        public void HideBanner()
        {
            if (_disposed || !_bannerCreated ||
                string.IsNullOrWhiteSpace(_platform.BannerAdUnitId))
                return;
            _client.HideBanner(_platform.BannerAdUnitId);
        }

        void OnSdkInitialized(bool succeeded)
        {
            if (_disposed) return;
            _initialized = succeeded;
            _initialization?.TrySetResult(succeeded);
            if (!succeeded) return;
            LoadRewarded();
            LoadInterstitial();
        }

        void OnRewardedLoaded(string adUnitId)
        {
            if (adUnitId == _platform.RewardedAdUnitId)
                _rewardedRetryAttempt = 0;
        }

        void OnRewardedLoadFailed(string adUnitId)
        {
            if (adUnitId != _platform.RewardedAdUnitId || _disposed) return;
            _rewardedRetryAttempt++;
            RetryRewardedAsync(RetryDelay(_rewardedRetryAttempt)).Forget();
        }

        void OnRewardedHidden(string adUnitId)
        {
            if (adUnitId != _platform.RewardedAdUnitId) return;
            CompleteRewarded(_rewardEarned ? AdResult.Completed : AdResult.Skipped);
            LoadRewarded();
        }

        void OnRewardedDisplayFailed(string adUnitId)
        {
            if (adUnitId != _platform.RewardedAdUnitId) return;
            CompleteRewarded(AdResult.Failed);
            LoadRewarded();
        }

        void OnRewardReceived(string adUnitId)
        {
            if (adUnitId == _platform.RewardedAdUnitId)
                _rewardEarned = true;
        }

        void OnInterstitialLoaded(string adUnitId)
        {
            if (adUnitId == _platform.InterstitialAdUnitId)
                _interstitialRetryAttempt = 0;
        }

        void OnInterstitialLoadFailed(string adUnitId)
        {
            if (adUnitId != _platform.InterstitialAdUnitId || _disposed) return;
            _interstitialRetryAttempt++;
            RetryInterstitialAsync(RetryDelay(_interstitialRetryAttempt)).Forget();
        }

        void OnInterstitialHidden(string adUnitId)
        {
            if (adUnitId != _platform.InterstitialAdUnitId) return;
            CompleteInterstitial(AdResult.Completed);
            LoadInterstitial();
        }

        void OnInterstitialDisplayFailed(string adUnitId)
        {
            if (adUnitId != _platform.InterstitialAdUnitId) return;
            CompleteInterstitial(AdResult.Failed);
            LoadInterstitial();
        }

        void LoadRewarded()
        {
            if (_disposed || !_initialized ||
                string.IsNullOrWhiteSpace(_platform.RewardedAdUnitId))
                return;
            _client.LoadRewarded(_platform.RewardedAdUnitId);
        }

        void LoadInterstitial()
        {
            if (_disposed || !_initialized ||
                string.IsNullOrWhiteSpace(_platform.InterstitialAdUnitId))
                return;
            _client.LoadInterstitial(_platform.InterstitialAdUnitId);
        }

        async UniTaskVoid RetryRewardedAsync(TimeSpan delay)
        {
            try
            {
                await _delay(delay, _lifetime.Token);
                LoadRewarded();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] MAX rewarded retry failed: {e.Message}");
            }
        }

        async UniTaskVoid RetryInterstitialAsync(TimeSpan delay)
        {
            try
            {
                await _delay(delay, _lifetime.Token);
                LoadInterstitial();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] MAX interstitial retry failed: {e.Message}");
            }
        }

        static TimeSpan RetryDelay(int attempt)
            => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(6, Math.Max(1, attempt))));

        void CompleteRewarded(AdResult result)
        {
            var completion = _rewardedShow;
            _rewardedShow = null;
            completion?.TrySetResult(result);
            _rewardEarned = false;
        }

        void CompleteInterstitial(AdResult result)
        {
            var completion = _interstitialShow;
            _interstitialShow = null;
            completion?.TrySetResult(result);
        }

        void Subscribe()
        {
            _client.SdkInitialized += OnSdkInitialized;
            _client.RewardedLoaded += OnRewardedLoaded;
            _client.RewardedLoadFailed += OnRewardedLoadFailed;
            _client.RewardedHidden += OnRewardedHidden;
            _client.RewardedDisplayFailed += OnRewardedDisplayFailed;
            _client.RewardReceived += OnRewardReceived;
            _client.InterstitialLoaded += OnInterstitialLoaded;
            _client.InterstitialLoadFailed += OnInterstitialLoadFailed;
            _client.InterstitialHidden += OnInterstitialHidden;
            _client.InterstitialDisplayFailed += OnInterstitialDisplayFailed;
        }

        void Unsubscribe()
        {
            _client.SdkInitialized -= OnSdkInitialized;
            _client.RewardedLoaded -= OnRewardedLoaded;
            _client.RewardedLoadFailed -= OnRewardedLoadFailed;
            _client.RewardedHidden -= OnRewardedHidden;
            _client.RewardedDisplayFailed -= OnRewardedDisplayFailed;
            _client.RewardReceived -= OnRewardReceived;
            _client.InterstitialLoaded -= OnInterstitialLoaded;
            _client.InterstitialLoadFailed -= OnInterstitialLoadFailed;
            _client.InterstitialHidden -= OnInterstitialHidden;
            _client.InterstitialDisplayFailed -= OnInterstitialDisplayFailed;
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MaxAdsProvider));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _initialized = false;
            _lifetime.Cancel();
            CompleteRewarded(AdResult.Failed);
            CompleteInterstitial(AdResult.Failed);
            _initialization?.TrySetResult(false);
            if (_bannerCreated && !string.IsNullOrWhiteSpace(_platform.BannerAdUnitId))
                _client.DestroyBanner(_platform.BannerAdUnitId);
            Unsubscribe();
            _client.Dispose();
            _lifetime.Dispose();
        }
    }

    /// <summary>MAX 8.6.x SDK 薄封装;集中管理静态回调订阅,避免场景切换后重复回调。</summary>
    internal sealed class MaxSdkAdsClient : IMaxAdsClient
    {
        public event Action<bool> SdkInitialized;
        public event Action<string> RewardedLoaded;
        public event Action<string> RewardedLoadFailed;
        public event Action<string> RewardedHidden;
        public event Action<string> RewardedDisplayFailed;
        public event Action<string> RewardReceived;
        public event Action<string> InterstitialLoaded;
        public event Action<string> InterstitialLoadFailed;
        public event Action<string> InterstitialHidden;
        public event Action<string> InterstitialDisplayFailed;

        bool _disposed;

        public MaxSdkAdsClient()
        {
            MaxSdkCallbacks.OnSdkInitializedEvent += OnSdkInitialized;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardReceived;
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialDisplayFailed;
        }

        public void Initialize() => MaxSdk.InitializeSdk();

        public bool IsRewardedReady(string adUnitId)
            => MaxSdk.IsRewardedAdReady(adUnitId);

        public bool IsInterstitialReady(string adUnitId)
            => MaxSdk.IsInterstitialReady(adUnitId);

        public void LoadRewarded(string adUnitId)
            => MaxSdk.LoadRewardedAd(adUnitId);

        public void ShowRewarded(string adUnitId, string placement)
            => MaxSdk.ShowRewardedAd(adUnitId, placement);

        public void LoadInterstitial(string adUnitId)
            => MaxSdk.LoadInterstitial(adUnitId);

        public void ShowInterstitial(string adUnitId, string placement)
            => MaxSdk.ShowInterstitial(adUnitId, placement);

        public void CreateBanner(
            string adUnitId, BannerPosition position, string backgroundColor)
        {
            var maxPosition = position == BannerPosition.Top
                ? MaxSdkBase.AdViewPosition.TopCenter
                : MaxSdkBase.AdViewPosition.BottomCenter;
            MaxSdk.CreateBanner(adUnitId, new MaxSdkBase.AdViewConfiguration(maxPosition));
            MaxSdk.SetBannerPlacement(adUnitId,
                position == BannerPosition.Top ? "banner_top" : "banner_bottom");
            var color = Color.black;
            if (!string.IsNullOrWhiteSpace(backgroundColor) &&
                !ColorUtility.TryParseHtmlString(backgroundColor, out color))
                color = Color.black;
            MaxSdk.SetBannerBackgroundColor(adUnitId, color);
        }

        public void ShowBanner(string adUnitId) => MaxSdk.ShowBanner(adUnitId);
        public void HideBanner(string adUnitId) => MaxSdk.HideBanner(adUnitId);
        public void DestroyBanner(string adUnitId) => MaxSdk.DestroyBanner(adUnitId);

        void OnSdkInitialized(MaxSdkBase.SdkConfiguration configuration)
            => SdkInitialized?.Invoke(configuration?.IsSuccessfullyInitialized == true);

        void OnRewardedLoaded(string adUnitId, MaxSdkBase.AdInfo _)
            => RewardedLoaded?.Invoke(adUnitId);

        void OnRewardedLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo _)
            => RewardedLoadFailed?.Invoke(adUnitId);

        void OnRewardedHidden(string adUnitId, MaxSdkBase.AdInfo _)
            => RewardedHidden?.Invoke(adUnitId);

        void OnRewardedDisplayFailed(
            string adUnitId, MaxSdkBase.ErrorInfo _, MaxSdkBase.AdInfo __)
            => RewardedDisplayFailed?.Invoke(adUnitId);

        void OnRewardReceived(
            string adUnitId, MaxSdkBase.Reward _, MaxSdkBase.AdInfo __)
            => RewardReceived?.Invoke(adUnitId);

        void OnInterstitialLoaded(string adUnitId, MaxSdkBase.AdInfo _)
            => InterstitialLoaded?.Invoke(adUnitId);

        void OnInterstitialLoadFailed(string adUnitId, MaxSdkBase.ErrorInfo _)
            => InterstitialLoadFailed?.Invoke(adUnitId);

        void OnInterstitialHidden(string adUnitId, MaxSdkBase.AdInfo _)
            => InterstitialHidden?.Invoke(adUnitId);

        void OnInterstitialDisplayFailed(
            string adUnitId, MaxSdkBase.ErrorInfo _, MaxSdkBase.AdInfo __)
            => InterstitialDisplayFailed?.Invoke(adUnitId);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MaxSdkCallbacks.OnSdkInitializedEvent -= OnSdkInitialized;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= OnRewardedLoaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= OnRewardedLoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= OnRewardedHidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= OnRewardedDisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= OnRewardReceived;
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= OnInterstitialLoaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= OnInterstitialLoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= OnInterstitialHidden;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= OnInterstitialDisplayFailed;
        }
    }
}
