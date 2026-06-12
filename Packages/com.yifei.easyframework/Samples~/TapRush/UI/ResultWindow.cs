using Cysharp.Threading.Tasks;
using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>结算界面:本局得分 + 最高分 + "再来一次" + "看广告翻倍"。</summary>
    public sealed class ResultWindow : UIPanel
    {
        TMP_Text _scoreText;
        TMP_Text _bestText;

        public sealed class Args
        {
            public int Score;
            public int HighScore;
            public System.Action OnReplay;
            public System.Action OnDoubleViaAd;   // 看广告翻倍(回调里走 G.Ads + 更新分数)
        }

        Args _args;

        protected override void OnSetup(object args)
        {
            _args = (Args)args;
            BuildUi();
        }

        void BuildUi()
        {
            var root = (RectTransform)transform;
            UiBuild.FullScreen(root, "Bg", new Color(0.10f, 0.08f, 0.14f, 1f));
            UiBuild.Label(root, "Title", "RESULT", new Vector2(0f, 480f), 88f);
            _scoreText = UiBuild.Label(root, "Score", $"Score: {_args.Score}", new Vector2(0f, 300f), 60f);
            _bestText = UiBuild.Label(root, "Best", $"Best: {_args.HighScore}", new Vector2(0f, 200f), 52f);
            UiBuild.Button(root, "DoubleBtn", "WATCH AD x2", new Vector2(0f, 20f), () => _args.OnDoubleViaAd?.Invoke());
            UiBuild.Button(root, "ReplayBtn", "REPLAY", new Vector2(0f, -160f), () => _args.OnReplay?.Invoke());
        }

        /// <summary>广告翻倍成功后由玩法回调刷新显示。</summary>
        public void RefreshScore(int newScore, int newHigh)
        {
            if (_scoreText != null) _scoreText.text = $"Score: {newScore}";
            if (_bestText != null) _bestText.text = $"Best: {newHigh}";
        }

        protected override UniTask PlayEnter() => UniTask.CompletedTask;
    }
}
