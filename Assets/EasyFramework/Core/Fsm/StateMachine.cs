using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Fsm
{
    public sealed class StateMachine<TContext>
    {
        readonly Dictionary<Type, State<TContext>> _states = new();
        readonly TContext _context;
        int _version;

        public State<TContext> Current { get; private set; }

        public StateMachine(TContext context) => _context = context;

        public void AddState(State<TContext> state)
        {
            state.Attach(this, _context);
            _states[state.GetType()] = state;
        }

        public async UniTask ChangeState<TState>() where TState : State<TContext>
        {
            if (!_states.TryGetValue(typeof(TState), out var next))
                throw new KeyNotFoundException($"State {typeof(TState).Name} not registered.");

            // 版本守卫:Enter await 期间若有更晚的 ChangeState,本次后续不再生效
            var version = ++_version;
            Current?.Exit();
            Current = next;
            await next.Enter();
            if (version != _version) return;
        }

        public void Update(float deltaTime) => Current?.Update(deltaTime);
    }
}
