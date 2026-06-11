using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    /// <summary>池内实例标记,记录所属 key,用于 Despawn 校验。</summary>
    internal sealed class PooledMarker : MonoBehaviour
    {
        public string Key;
    }
}
