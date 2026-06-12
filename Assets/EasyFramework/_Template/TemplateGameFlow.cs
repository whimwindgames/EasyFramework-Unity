using Cysharp.Threading.Tasks;
using EasyFramework.Core.Fsm;

namespace EasyFramework.Template
{
    /// <summary>
    /// 游戏流程骨架:Menu → Gameplay → Result。复制后在各 State 里接你的 UI/玩法。
    /// 用法:new TemplateGameFlow().ToMenu()(或由 LifetimeScope 的入口点在 BootCompleted 后驱动)。
    /// </summary>
    public sealed class TemplateGameFlow
    {
        readonly StateMachine<TemplateGameFlow> _fsm;

        // 公开当前状态便于查询/测试。
        public State<TemplateGameFlow> Current => _fsm.Current;

        public TemplateGameFlow()
        {
            _fsm = new StateMachine<TemplateGameFlow>(this);
            _fsm.AddState(new MenuState());
            _fsm.AddState(new GameplayState());
            _fsm.AddState(new ResultState());
        }

        // ---- 流程切换 API(供 UI 按钮 / 玩法事件调用)----
        public UniTask ToMenu() => _fsm.ChangeState<MenuState>();
        public UniTask ToGameplay() => _fsm.ChangeState<GameplayState>();
        public UniTask ToResult() => _fsm.ChangeState<ResultState>();

        public void Tick(float deltaTime) => _fsm.Update(deltaTime);

        // ---- 三态骨架:扩展点在注释里 ----

        public sealed class MenuState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:G.UI.PushAsync<MenuWindow>();播放菜单 BGM 等。
                return UniTask.CompletedTask;
            }
            // Exit / Update 按需覆盖。
        }

        public sealed class GameplayState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:加载关卡场景、G.UI.ShowHudAsync<GameHud>()、开始计时/生成对象。
                return UniTask.CompletedTask;
            }

            public override void Update(float deltaTime)
            {
                // 扩展点:玩法每帧逻辑(倒计时、刷怪)。结束时调 Context.ToResult()。
            }
        }

        public sealed class ResultState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:G.UI.PushAsync<ResultWindow>()、结算打点 G.Analytics.Track("level_end")、存档 G.Save.Save()。
                return UniTask.CompletedTask;
            }
        }
    }
}
