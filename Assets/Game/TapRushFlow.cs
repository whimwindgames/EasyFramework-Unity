using System;
using Cysharp.Threading.Tasks;
using EasyFramework;
using EasyFramework.Core.Fsm;
using EasyFramework.Monetization.Ads;
using EasyFramework.Services.Inputs;
using Game.UI;
using UnityEngine;

namespace Game
{
    /// <summary>TapRush 流程:Menu → Gameplay → Result。用 G 门面打通框架各能力。</summary>
    public sealed class TapRushFlow
    {
        const float Duration = 60f;
        const string CircleKey = "circle";
        const string TapSfxKey = "audio/tap";

        readonly StateMachine<TapRushFlow> _fsm;

        TapRushSession _session;
        GameHud _hud;
        IDisposable _tapSub;
        readonly System.Collections.Generic.List<GameObject> _liveCircles = new();
        float _spawnTimer;

        public State<TapRushFlow> Current => _fsm.Current;

        public TapRushFlow()
        {
            _fsm = new StateMachine<TapRushFlow>(this);
            _fsm.AddState(new MenuState());
            _fsm.AddState(new GameplayState());
            _fsm.AddState(new ResultState());
        }

        public UniTask ToMenu() => _fsm.ChangeState<MenuState>();
        public UniTask ToGameplay() => _fsm.ChangeState<GameplayState>();
        public UniTask ToResult() => _fsm.ChangeState<ResultState>();
        public void Tick(float dt) => _fsm.Update(dt);

        int LoadHighScore() => G.Save.Data<TapRushSaveData>().HighScore;

        // ---------------- Menu ----------------
        public sealed class MenuState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                await G.UI.PushAsync<MenuWindow>(new MenuWindow.Args
                {
                    HighScore = f.LoadHighScore(),
                    OnStart = () => f.ToGameplay().Forget(),
                });
            }

            public override void Exit() => G.UI.PopAllAsync().Forget();
        }

        // ---------------- Gameplay ----------------
        public sealed class GameplayState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                f._session = new TapRushSession(Duration, f.LoadHighScore());
                f._spawnTimer = 0f;
                f._hud = await G.UI.ShowHudAsync<GameHud>();
                f._hud.SetScore(0);
                f._hud.SetTime(Duration);

                G.Analytics.Track("level_start", ("game", "tap_rush"));

                // 订阅点击:屏幕坐标命中检测最近圆圈。
                f._tapSub = G.Events.Subscribe<TapEvent>(f.OnTap);
            }

            public override void Update(float dt)
            {
                var f = Context;
                // _hud 在 Enter 的 await ShowHudAsync 之后才赋值;在异步 Enter 完成前的帧
                // 守卫 _hud,避免 NRE。_session 已结束也直接退出。
                if (f._session == null || f._hud == null) return;

                if (f._session.IsOver)
                {
                    f.ToResult().Forget();
                    return;
                }

                f._session.TickDown(dt);
                f._hud.SetTime(f._session.TimeRemaining);

                // 周期性生成圆圈
                f._spawnTimer -= dt;
                if (f._spawnTimer <= 0f)
                {
                    f._spawnTimer = 0.7f;
                    f.SpawnCircle().Forget();
                }

                if (f._session.IsOver)
                    f.ToResult().Forget();
            }

            public override void Exit()
            {
                var f = Context;
                f._tapSub?.Dispose();
                f._tapSub = null;
                f.DespawnAllCircles();
                G.UI.HideHudAsync().Forget();
            }
        }

        // ---------------- Result ----------------
        public sealed class ResultState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                var score = f._session.Score;
                var high = f._session.HighScore;

                // 存档:写回最高分。
                G.Save.Data<TapRushSaveData>().HighScore = high;
                G.Save.Save();

                G.Analytics.Track("level_end", ("game", "tap_rush"), ("score", score), ("high_score", high));

                ResultWindow win = null;
                win = await G.UI.PushAsync<ResultWindow>(new ResultWindow.Args
                {
                    Score = score,
                    HighScore = high,
                    OnReplay = () => f.ToGameplay().Forget(),
                    OnDoubleViaAd = () => f.DoubleViaAd(win).Forget(),
                });
            }
        }

        // ---------------- 玩法辅助 ----------------

        async UniTask SpawnCircle()
        {
            var pos = new Vector3(UnityEngine.Random.Range(-2.2f, 2.2f), UnityEngine.Random.Range(-3.5f, 3.5f), 0f);
            var go = await G.Pool.SpawnAsync(CircleKey, pos);
            var circle = go.GetComponent<TapRushCircle>();
            if (circle != null) circle.OnHit = OnCircleHit;
            _liveCircles.Add(go);
        }

        void OnCircleHit(TapRushCircle circle)
        {
            _session.AddScore(1);
            _hud.SetScore(_session.Score);
            G.Audio.PlaySfx(TapSfxKey);
            _liveCircles.Remove(circle.gameObject);
            G.Pool.Despawn(circle.gameObject);
        }

        void OnTap(TapEvent e)
        {
            // 屏幕坐标 → 世界坐标命中最近圆圈(主相机正交)。
            var cam = Camera.main;
            if (cam == null) return;
            var world = cam.ScreenToWorldPoint(new Vector3(e.ScreenPosition.x, e.ScreenPosition.y, -cam.transform.position.z));
            TapRushCircle hit = null;
            var best = float.MaxValue;
            foreach (var go in _liveCircles)
            {
                if (go == null) continue;
                var c = go.GetComponent<TapRushCircle>();
                var d = Vector2.Distance(world, go.transform.position);
                if (d <= c.Radius && d < best) { best = d; hit = c; }
            }
            hit?.Hit();
        }

        void DespawnAllCircles()
        {
            foreach (var go in _liveCircles)
                if (go != null) G.Pool.Despawn(go);
            _liveCircles.Clear();
        }

        async UniTask DoubleViaAd(ResultWindow win)
        {
            var result = await G.Ads.ShowRewardedAsync("double_score");
            if (result != AdResult.Completed) return;

            var doubled = _session.Score * 2;
            // 用翻倍后分数刷新存档与显示(以翻倍分参与最高分)。
            var newHigh = doubled > G.Save.Data<TapRushSaveData>().HighScore
                ? doubled : G.Save.Data<TapRushSaveData>().HighScore;
            G.Save.Data<TapRushSaveData>().HighScore = newHigh;
            G.Save.Save();
            G.Analytics.Track("ad_double_score", ("score", doubled));
            win?.RefreshScore(doubled, newHigh);
        }
    }
}
