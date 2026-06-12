using Game;
using NUnit.Framework;

namespace Game.Tests
{
    public class TapRushSessionTests
    {
        [Test]
        public void AddScore_AccumulatesWhileRunning()
        {
            var s = new TapRushSession(60f, highScore: 0);
            s.AddScore(1);
            s.AddScore(2);
            Assert.AreEqual(3, s.Score);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void TickDown_ReducesTime_AndEndsAtZero()
        {
            var s = new TapRushSession(2f, 0);
            s.TickDown(1f);
            Assert.AreEqual(1f, s.TimeRemaining, 1e-4);
            Assert.IsFalse(s.IsOver);
            s.TickDown(1.5f);                 // 越界扣到 0
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
            Assert.IsTrue(s.IsOver);
        }

        [Test]
        public void AddScore_AfterOver_IsIgnored()
        {
            var s = new TapRushSession(1f, 0);
            s.TickDown(1f);
            Assert.IsTrue(s.IsOver);
            s.AddScore(5);
            Assert.AreEqual(0, s.Score, "结束后加分无效");
        }

        [Test]
        public void HighScore_TracksMaxOfInitialAndCurrent()
        {
            var s = new TapRushSession(60f, highScore: 10);
            Assert.AreEqual(10, s.HighScore);   // 还没超过历史
            s.AddScore(7);
            Assert.AreEqual(10, s.HighScore);   // 7 < 10
            s.AddScore(5);                      // 12 > 10
            Assert.AreEqual(12, s.HighScore);
        }

        [Test]
        public void TimeRemaining_NeverNegative_AndTickAfterOverNoOp()
        {
            var s = new TapRushSession(0.5f, 0);
            s.TickDown(10f);
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
            Assert.IsTrue(s.IsOver);
            Assert.DoesNotThrow(() => s.TickDown(1f));   // 结束后再 Tick 安全
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
        }
    }
}
