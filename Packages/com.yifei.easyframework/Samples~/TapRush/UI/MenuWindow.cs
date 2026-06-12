using Cysharp.Threading.Tasks;
using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>主菜单:显示最高分 + 开始按钮。点击开始 → 通过回调驱动 TapRushFlow 进 Gameplay。</summary>
    public sealed class MenuWindow : UIPanel
    {
        TMP_Text _highScoreText;

        /// <summary>由 OnSetup 接收的参数:最高分 + 开始回调。</summary>
        public sealed class Args
        {
            public int HighScore;
            public System.Action OnStart;
        }

        protected override void OnSetup(object args)
        {
            var a = (Args)args;
            BuildUi(a);
        }

        void BuildUi(Args a)
        {
            var root = (RectTransform)transform;
            UiBuild.FullScreen(root, "Bg", new Color(0.08f, 0.10f, 0.16f, 1f));
            UiBuild.Label(root, "Title", "TAP RUSH", new Vector2(0f, 420f), 96f);
            _highScoreText = UiBuild.Label(root, "HighScore", $"Best: {a.HighScore}", new Vector2(0f, 240f), 52f);
            UiBuild.Button(root, "StartBtn", "START", new Vector2(0f, -40f), () => a.OnStart?.Invoke());
        }

        protected override UniTask PlayEnter() => UniTask.CompletedTask;
    }
}
