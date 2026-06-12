using EasyFramework;
using EasyFramework.DevTools.Cheats;
using UnityEngine;

namespace Game
{
    /// <summary>TapRush 作弊命令示例(DEV/编辑器下经调试控制台可用)。</summary>
    public static class TapRushCheats
    {
        [Cheat("tap_reset_highscore", "Reset TapRush high score to 0")]
        public static void ResetHighScore()
        {
            if (!G.IsInitialized) { Debug.LogWarning("[TapRush] Framework not initialized."); return; }
            G.Save.Data<TapRushSaveData>().HighScore = 0;
            G.Save.Save();
            Debug.Log("[TapRush] High score reset.");
        }

        [Cheat("tap_set_highscore", "Set TapRush high score to value")]
        public static void SetHighScore(int value)
        {
            if (!G.IsInitialized) { Debug.LogWarning("[TapRush] Framework not initialized."); return; }
            G.Save.Data<TapRushSaveData>().HighScore = value;
            G.Save.Save();
            Debug.Log($"[TapRush] High score set to {value}.");
        }
    }
}
