using EasyFramework.Services.Audio;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class CrossfadeStateTests
    {
        [Test]
        public void Advance_AtMidpoint_VolumesAreHalf()
        {
            // 从静默淡入到 targetVolume=1,用时 1s;出声道从 1 淡出
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(0.5f);
            Assert.AreEqual(0.5f, s.InVolume, 1e-4f);
            Assert.AreEqual(0.5f, s.OutVolume, 1e-4f);
            Assert.IsFalse(s.IsDone);
        }

        [Test]
        public void Advance_PastDuration_ClampsToEndpoints()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(2f);
            Assert.AreEqual(1f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
            Assert.IsTrue(s.IsDone);
        }

        [Test]
        public void TargetVolume_ScalesInChannel()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 0.6f);
            s.Advance(1f);
            Assert.AreEqual(0.6f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
        }

        [Test]
        public void ZeroFade_CompletesImmediately()
        {
            var s = new CrossfadeState(fadeSeconds: 0f, targetVolume: 1f);
            s.Advance(0f);
            Assert.IsTrue(s.IsDone);
            Assert.AreEqual(1f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
        }

        [Test]
        public void Advance_AccumulatesAcrossMultipleSteps()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(0.25f);
            s.Advance(0.25f);
            Assert.AreEqual(0.5f, s.InVolume, 1e-4f);
            Assert.AreEqual(0.5f, s.OutVolume, 1e-4f);
        }
    }
}
