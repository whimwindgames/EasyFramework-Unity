using Unity.Cinemachine;
using UnityEngine;

namespace EasyFramework.Services.Cameras
{
    /// <summary>Cinemachine 3.x 薄封装。所有宿主对象懒创建并 DontDestroyOnLoad。
    /// 注:Cinemachine 3.x 的精确类型/成员名以工程安装版本为准(验证代理 unity_reflect 核对)。</summary>
    public sealed class CinemachineCameraService : ICameraService
    {
        CinemachineCamera _vcam;
        CinemachineConfiner2D _confiner;
        CinemachineImpulseSource _impulse;
        PolygonCollider2D _boundsShape;

        public void Follow(Transform target)
        {
            EnsureCamera();
            _vcam.Follow = target;
        }

        public void SetBounds(Bounds bounds)
        {
            EnsureCamera();
            if (_confiner == null)
                _confiner = _vcam.gameObject.AddComponent<CinemachineConfiner2D>();

            if (_boundsShape == null)
            {
                var holder = new GameObject("[EasyFramework.CameraBounds]");
                Object.DontDestroyOnLoad(holder);
                _boundsShape = holder.AddComponent<PolygonCollider2D>();
                _boundsShape.isTrigger = true;
            }

            var min = bounds.min;
            var max = bounds.max;
            _boundsShape.points = new[]
            {
                new Vector2(min.x, min.y),
                new Vector2(max.x, min.y),
                new Vector2(max.x, max.y),
                new Vector2(min.x, max.y),
            };
            _confiner.BoundingShape2D = _boundsShape;
            _confiner.InvalidateBoundingShapeCache();
        }

        public void Shake(float intensity, float duration)
        {
            EnsureCamera();
            if (_impulse == null)
                _impulse = _vcam.gameObject.AddComponent<CinemachineImpulseSource>();
            // 方向向量 * 强度;duration 由 ImpulseDefinition 配置承载(薄封装,以默认包络为主)
            _impulse.GenerateImpulseWithForce(intensity);
        }

        void EnsureCamera()
        {
            if (_vcam != null) return;
            var go = new GameObject("[EasyFramework.Camera]");
            Object.DontDestroyOnLoad(go);
            _vcam = go.AddComponent<CinemachineCamera>();
        }
    }
}
