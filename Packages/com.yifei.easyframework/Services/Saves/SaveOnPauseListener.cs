using UnityEngine;

namespace EasyFramework.Services.Saves
{
    /// <summary>移动端切后台(OnApplicationPause(true))自动落盘。由 RootLifetimeScope 持有。</summary>
    public sealed class SaveOnPauseListener : MonoBehaviour
    {
        ISaveService _save;

        public void Bind(ISaveService save) => _save = save;

        void OnApplicationPause(bool paused)
        {
            if (paused) _save?.Save();
        }
    }
}
