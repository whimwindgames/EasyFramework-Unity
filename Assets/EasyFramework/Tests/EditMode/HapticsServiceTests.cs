using System;
using System.Collections.Generic;
using EasyFramework.Services.Haptics;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class HapticsServiceTests
    {
        const string Key = "ef.haptics";
        Action<HapticStrength> _originalHook;
        List<HapticStrength> _calls;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            _originalHook = HapticsService.PlatformVibrate;
            _calls = new List<HapticStrength>();
            HapticsService.PlatformVibrate = s => _calls.Add(s);
        }

        [TearDown]
        public void TearDown()
        {
            HapticsService.PlatformVibrate = _originalHook;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [Test]
        public void Default_EnabledIsTrue()
        {
            var svc = new HapticsService();
            Assert.IsTrue(svc.Enabled);
        }

        [Test]
        public void Vibrate_WhenEnabled_CallsPlatformHook()
        {
            var svc = new HapticsService();
            svc.Vibrate(HapticStrength.Medium);
            Assert.AreEqual(1, _calls.Count);
            Assert.AreEqual(HapticStrength.Medium, _calls[0]);
        }

        [Test]
        public void Vibrate_WhenDisabled_DoesNothing()
        {
            var svc = new HapticsService { Enabled = false };
            svc.Vibrate(HapticStrength.Heavy);
            Assert.AreEqual(0, _calls.Count);
        }

        [Test]
        public void Enabled_Setter_PersistsToPlayerPrefs()
        {
            var svc = new HapticsService();
            svc.Enabled = false;
            Assert.AreEqual(0, PlayerPrefs.GetInt(Key, -1));
            svc.Enabled = true;
            Assert.AreEqual(1, PlayerPrefs.GetInt(Key, -1));
        }

        [Test]
        public void Enabled_RoundTripsThroughPlayerPrefs()
        {
            var a = new HapticsService();
            a.Enabled = false;
            var b = new HapticsService();
            Assert.IsFalse(b.Enabled);
        }
    }
}
