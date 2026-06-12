namespace Game
{
    /// <summary>TapRush 计分/倒计时纯逻辑。无 Unity 依赖,全 EditMode 单测。</summary>
    public sealed class TapRushSession
    {
        readonly int _initialHighScore;

        public int Score { get; private set; }
        public float TimeRemaining { get; private set; }
        public bool IsOver => TimeRemaining <= 0f;
        public int HighScore => Score > _initialHighScore ? Score : _initialHighScore;

        public TapRushSession(float durationSeconds, int highScore)
        {
            TimeRemaining = durationSeconds > 0f ? durationSeconds : 0f;
            _initialHighScore = highScore;
        }

        public void AddScore(int amount)
        {
            if (IsOver) return;
            Score += amount;
        }

        public void TickDown(float deltaSeconds)
        {
            if (IsOver) return;
            TimeRemaining -= deltaSeconds;
            if (TimeRemaining < 0f) TimeRemaining = 0f;
        }
    }
}
