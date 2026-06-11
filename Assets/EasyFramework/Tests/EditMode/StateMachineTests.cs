using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Fsm;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class StateMachineTests
    {
        class Ctx { public List<string> Log = new(); }

        class StateA : State<Ctx>
        {
            public override UniTask Enter() { Context.Log.Add("A.Enter"); return UniTask.CompletedTask; }
            public override void Update(float dt) => Context.Log.Add($"A.Update:{dt}");
            public override void Exit() => Context.Log.Add("A.Exit");
        }

        class StateB : State<Ctx>
        {
            public override UniTask Enter() { Context.Log.Add("B.Enter"); return UniTask.CompletedTask; }
        }

        [Test]
        public void ChangeState_RunsExitThenEnter()
        {
            var ctx = new Ctx();
            var fsm = new StateMachine<Ctx>(ctx);
            fsm.AddState(new StateA());
            fsm.AddState(new StateB());

            fsm.ChangeState<StateA>().GetAwaiter().GetResult();
            fsm.Update(0.5f);
            fsm.ChangeState<StateB>().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[] { "A.Enter", "A.Update:0.5", "A.Exit", "B.Enter" }, ctx.Log);
            Assert.IsInstanceOf<StateB>(fsm.Current);
        }

        [Test]
        public void Update_WithoutState_DoesNothing()
        {
            var fsm = new StateMachine<Ctx>(new Ctx());
            Assert.DoesNotThrow(() => fsm.Update(0.1f));
        }

        [Test]
        public void ChangeState_ToUnregistered_Throws()
        {
            var fsm = new StateMachine<Ctx>(new Ctx());
            Assert.Throws<KeyNotFoundException>(() =>
                fsm.ChangeState<StateA>().GetAwaiter().GetResult());
        }
    }
}
