using EasyFramework.Core.Pooling;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 可点击圆圈。经 G.Pool 生成回收(IPoolable 重置);被点击时回调玩法加分 + 播音效。
    /// 点击判定:玩法订阅 TapEvent 做屏幕坐标命中检测后调 Hit();或本组件挂 Collider 由射线命中(选前者,见 TapRushFlow)。
    /// </summary>
    public sealed class TapRushCircle : MonoBehaviour, IPoolable
    {
        public System.Action<TapRushCircle> OnHit;   // 玩法注入:命中时加分 + 回收
        public float Radius { get; private set; } = 0.6f;

        public void OnSpawn()
        {
            // 重置可见性/缩放(池复用)。
            transform.localScale = Vector3.one;
        }

        public void OnDespawn()
        {
            OnHit = null;
        }

        /// <summary>由玩法命中检测调用。</summary>
        public void Hit() => OnHit?.Invoke(this);
    }
}
