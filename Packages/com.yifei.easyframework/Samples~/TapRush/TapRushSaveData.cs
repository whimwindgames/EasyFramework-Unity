using EasyFramework.Services.Saves;

namespace Game
{
    /// <summary>TapRush 存档:仅最高分。经 SaveProfile 替换框架默认 DefaultSaveData。</summary>
    public sealed class TapRushSaveData : SaveData
    {
        public int HighScore;
    }
}
