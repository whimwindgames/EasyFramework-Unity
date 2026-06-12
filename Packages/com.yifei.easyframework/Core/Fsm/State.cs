using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Fsm
{
    public abstract class State<TContext>
    {
        protected TContext Context { get; private set; }
        protected StateMachine<TContext> Machine { get; private set; }

        internal void Attach(StateMachine<TContext> machine, TContext context)
        {
            Machine = machine;
            Context = context;
        }

        public virtual UniTask Enter() => UniTask.CompletedTask;
        public virtual void Update(float deltaTime) { }
        public virtual void Exit() { }
    }
}
