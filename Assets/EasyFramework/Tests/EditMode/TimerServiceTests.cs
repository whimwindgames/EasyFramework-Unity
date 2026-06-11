using EasyFramework.Core.Timing;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class TimerServiceTests
    {
        [Test]
        public void Schedule_FiresAfterDelay()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++);
            svc.Advance(0.5f, 0.5f);
            Assert.AreEqual(0, fired);
            svc.Advance(0.6f, 0.6f);
            Assert.AreEqual(1, fired);
            svc.Advance(5f, 5f);
            Assert.AreEqual(1, fired, "一次性定时器只触发一次");
        }

        [Test]
        public void Schedule_Repeat_FiresEveryInterval()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++, repeat: true);
            svc.Advance(3.05f, 3.05f);
            Assert.AreEqual(3, fired);
        }

        [Test]
        public void Cancel_PreventsFiring()
        {
            var svc = new TimerService();
            var fired = 0;
            var handle = svc.Schedule(1.0f, () => fired++);
            svc.Cancel(handle);
            svc.Advance(2f, 2f);
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void UnscaledTimer_UsesUnscaledDelta()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++, useUnscaledTime: true);
            svc.Advance(scaledDelta: 0f, unscaledDelta: 1.5f); // timeScale=0 模拟暂停
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Callback_CanScheduleAnotherTimer()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(0.5f, () => svc.Schedule(0.5f, () => fired++));
            svc.Advance(0.6f, 0.6f);
            svc.Advance(0.6f, 0.6f);
            Assert.AreEqual(1, fired);
        }
    }
}
