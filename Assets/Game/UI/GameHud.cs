using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>战斗 HUD:分数 + 倒计时。由玩法每帧调 SetScore/SetTime 刷新(事件驱动,无 MVVM)。</summary>
    public sealed class GameHud : UIPanel
    {
        TMP_Text _scoreText;
        TMP_Text _timeText;

        protected override void OnSetup(object args)
        {
            var root = (RectTransform)transform;
            _scoreText = UiBuild.Label(root, "Score", "0", new Vector2(0f, 760f), 72f);
            _timeText = UiBuild.Label(root, "Time", "60.0", new Vector2(0f, 640f), 56f);
        }

        public void SetScore(int score)
        {
            if (_scoreText != null) _scoreText.text = score.ToString();
        }

        public void SetTime(float seconds)
        {
            if (_timeText != null) _timeText.text = seconds.ToString("00.0");
        }
    }
}
