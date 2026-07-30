using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Ads;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyFramework.Tests.PlayMode
{
    public sealed class RuntimeLifecycleSmokeTests
    {
        [UnityTest]
        public IEnumerator FadeTransition_PlayInAlwaysLeavesOverlayNonBlocking()
        {
            var transition = new FadeSceneTransition(0f);
            yield return transition.PlayOut().ToCoroutine();
            yield return transition.PlayIn().ToCoroutine();

            var overlay = GameObject.Find("[SceneFade]");
            Assert.IsNotNull(overlay);
            var group = overlay.GetComponent<CanvasGroup>();
            Assert.AreEqual(0f, group.alpha, 0.0001f);
            Assert.IsFalse(group.blocksRaycasts);

            transition.Dispose();
            yield return null;
            Assert.IsNull(GameObject.Find("[SceneFade]"));
        }

        [UnityTest]
        public IEnumerator AudioService_DisposeRemovesPersistentHost()
        {
            var clip = AudioClip.Create("test-bgm", 32, 1, 8_000, false);
            var assets = new FakeAssetService(
                new Dictionary<string, Object> { ["bgm"] = clip });
            var audio = new AudioService(assets);

            yield return audio.PlayBgmAsync("bgm", 0f).ToCoroutine();
            Assert.IsNotNull(GameObject.Find("[EasyFramework.Audio]"));

            audio.Dispose();
            yield return null;
            Assert.IsNull(GameObject.Find("[EasyFramework.Audio]"));
            Object.Destroy(clip);
        }

        [Test]
        public void ProductionFallbackAds_CannotCompleteReward()
        {
            var provider = new UnavailableAdsProvider();
            Assert.IsFalse(provider.IsRewardedReady);
            Assert.AreEqual(AdResult.NotReady,
                provider.ShowRewardedAsync("smoke").GetAwaiter().GetResult());
        }
    }
}
