using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    public interface IPoolService
    {
        UniTask PrewarmAsync(string key, int count);
        UniTask<GameObject> SpawnAsync(string key, Vector3 position = default, Quaternion rotation = default, Transform parent = null);
        void Despawn(GameObject instance);
    }
}
