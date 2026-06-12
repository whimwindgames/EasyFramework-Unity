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

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();
        }

        static AudioService Make()
            => new AudioService(new FakeAssetService(new Dictionary<string, Object>()));

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
    }
}
