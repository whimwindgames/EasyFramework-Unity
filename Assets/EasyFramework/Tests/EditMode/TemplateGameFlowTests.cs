using EasyFramework.Template;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class TemplateGameFlowTests
    {
        [Test]
        public void Flow_TransitionsThroughThreeStates()
        {
            var flow = new TemplateGameFlow();

            flow.ToMenu().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.MenuState>(flow.Current);

            flow.ToGameplay().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.GameplayState>(flow.Current);

            flow.ToResult().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.ResultState>(flow.Current);
        }

        [Test]
        public void Flow_CanReturnToMenuFromResult()
        {
            var flow = new TemplateGameFlow();
            flow.ToGameplay().GetAwaiter().GetResult();
            flow.ToResult().GetAwaiter().GetResult();
            flow.ToMenu().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.MenuState>(flow.Current);
        }

        [Test]
        public void Flow_TickDoesNotThrow()
        {
            var flow = new TemplateGameFlow();
            flow.ToGameplay().GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => flow.Tick(0.016f));
        }
    }
}
