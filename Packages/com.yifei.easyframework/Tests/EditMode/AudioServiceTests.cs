using System;
using System.Collections.Generic;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class AudioServiceTests
    {
        const string BgmKey = "ef.audio.bgm";
        const string SfxKey = "ef.audio.sfx";

        Action<GameObject> _originalDontDestroy;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();

            // DontDestroyOnLoad 只在 Play 模式合法;EditMode 下 no-op,宿主 GameObject 仍正常构建。
            _originalDontDestroy = AudioService.DontDestroyHandler;
            AudioService.DontDestroyHandler = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();
            AudioService.DontDestroyHandler = _originalDontDestroy;
        }

        static AudioService Make()
            => new AudioService(new FakeAssetService(new Dictionary<string, UnityEngine.Object>()));

        [Test]
        public void BgmVolume_Setter_Clamps01()
        {
            var svc = Make();
            svc.BgmVolume = 1.5f;
            Assert.AreEqual(1f, svc.BgmVolume, 1e-4f);
            svc.BgmVolume = -0.2f;
            Assert.AreEqual(0f, svc.BgmVolume, 1e-4f);
        }

        [Test]
        public void SfxVolume_Setter_Clamps01()
        {
            var svc = Make();
            svc.SfxVolume = 2f;
            Assert.AreEqual(1f, svc.SfxVolume, 1e-4f);
            svc.SfxVolume = -1f;
            Assert.AreEqual(0f, svc.SfxVolume, 1e-4f);
        }

        [Test]
        public void BgmVolume_Setter_PersistsToPlayerPrefs()
        {
            var svc = Make();
            svc.BgmVolume = 0.42f;
            Assert.AreEqual(0.42f, PlayerPrefs.GetFloat(BgmKey, -1f), 1e-4f);
        }

        [Test]
        public void Volumes_RoundTripThroughPlayerPrefs()
        {
            var svc = Make();
            svc.BgmVolume = 0.3f;
            svc.SfxVolume = 0.7f;

            var svc2 = Make(); // 新实例从 PlayerPrefs 读回
            Assert.AreEqual(0.3f, svc2.BgmVolume, 1e-4f);
            Assert.AreEqual(0.7f, svc2.SfxVolume, 1e-4f);
        }

        [Test]
        public void DefaultVolumes_AreOneWhenNoPrefs()
        {
            var svc = Make();
            Assert.AreEqual(1f, svc.BgmVolume, 1e-4f);
            Assert.AreEqual(1f, svc.SfxVolume, 1e-4f);
        }

        [Test]
        public void PlaySfx_SameKeyWithinMinInterval_SecondCallIsSkipped()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, UnityEngine.Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click")); // 同一时刻重复触发,应被限流跳过
        }

        [Test]
        public void PlaySfx_SameKeyAfterMinInterval_IsNotSkipped()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, UnityEngine.Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            t += AudioService.MinSfxIntervalSeconds + 0.001f;
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click")); // 已超过最小间隔,应正常播放
        }

        [Test]
        public void PlaySfx_DifferentKeys_AreNotThrottledAgainstEachOther()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, UnityEngine.Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
                { "sfx_hit", AudioClip.Create("sfx_hit", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_hit")); // 不同 key,同一时刻也不应互相限流
        }
    }
}
