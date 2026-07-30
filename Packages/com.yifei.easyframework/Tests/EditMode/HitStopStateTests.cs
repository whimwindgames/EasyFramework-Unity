using EasyFramework.Services.Juice;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class HitStopStateTests
    {
        [Test]
        public void FirstRequest_IsEntering()
        {
            var s = new HitStopState();
            var entering = s.Request(0.05f, now: 0f);
            Assert.IsTrue(entering, "首次请求应触发进入(置 timeScale=0)");
            Assert.IsTrue(s.IsActive);
        }

        [Test]
        public void ReentrantRequest_DoesNotReEnter_ExtendsDeadline()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);               // deadline = 0.05
            var entering2 = s.Request(0.05f, now: 0.03f); // 恢复前重入,deadline -> 0.08
            Assert.IsFalse(entering2, "重入不应再次进入");
            Assert.IsFalse(s.ShouldRestore(0.06f), "原 deadline 已过但被延长,不应恢复");
            Assert.IsTrue(s.ShouldRestore(0.08f), "到延长后的 deadline 才恢复");
        }

        [Test]
        public void ShouldRestore_FalseBeforeDeadline_TrueAfter()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);
            Assert.IsFalse(s.ShouldRestore(0.04f));
            Assert.IsTrue(s.ShouldRestore(0.05f));
        }

        [Test]
        public void Restore_DeactivatesAndAllowsNewRound()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);
            s.Restore();
            Assert.IsFalse(s.IsActive);
            var enteringAgain = s.Request(0.05f, now: 1f);
            Assert.IsTrue(enteringAgain, "恢复后再请求是新一轮进入");
        }

        [Test]
        public void ShorterReentrant_DoesNotShortenDeadline()
        {
            var s = new HitStopState();
            s.Request(0.1f, now: 0f);                 // deadline = 0.1
            s.Request(0.02f, now: 0.01f);             // now+0.02=0.03 < 0.1,不缩短
            Assert.IsFalse(s.ShouldRestore(0.05f));
            Assert.IsTrue(s.ShouldRestore(0.1f));
        }

        [Test]
        public void Remaining_ReflectsExtendedDeadline()
        {
            var state = new HitStopState();
            state.Request(0.1f, 1f);
            state.Request(0.2f, 1.05f);
            Assert.AreEqual(0.15f, state.Remaining(1.1f), 0.0001f);
        }
    }
}
