using System;
using Unity.Cinemachine;
using UnityEngine;

namespace EasyFramework.Services.Cameras
{
    /// <summary>Cinemachine 3.x 薄封装。所有宿主对象懒创建并 DontDestroyOnLoad。
    /// 注:Cinemachine 3.x 的精确类型/成员名以工程安装版本为准(验证代理 unity_reflect 核对)。</summary>
    public sealed class CinemachineCameraService : ICameraService, IDisposable
    {
        CinemachineCamera _vcam;
        CinemachineConfiner2D _confiner;
        CinemachineImpulseSource _impulse;
        PolygonCollider2D _boundsShape;
        GameObject _boundsHolder;
        bool _disposed;

        public void Follow(Transform target)
        {
            ThrowIfDisposed();
            EnsureCamera();
            _vcam.Follow = target;
        }

        public void SetBounds(Bounds bounds)
        {
            ThrowIfDisposed();
            EnsureCamera();
            if (_confiner == null)
                _confiner = _vcam.gameObject.AddComponent<CinemachineConfiner2D>();

            if (_boundsShape == null)
            {
                _boundsHolder = new GameObject("[EasyFramework.CameraBounds]");
                UnityEngine.Object.DontDestroyOnLoad(_boundsHolder);
                _boundsShape = _boundsHolder.AddComponent<PolygonCollider2D>();
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
            ThrowIfDisposed();
            if (duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            EnsureCamera();
            if (_impulse == null)
                _impulse = _vcam.gameObject.AddComponent<CinemachineImpulseSource>();
            _impulse.ImpulseDefinition.ImpulseDuration = duration;
            _impulse.GenerateImpulseWithForce(intensity);
        }

        void EnsureCamera()
        {
            if (_vcam != null) return;
            var go = new GameObject("[EasyFramework.Camera]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _vcam = go.AddComponent<CinemachineCamera>();
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CinemachineCameraService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_vcam != null) UnityEngine.Object.Destroy(_vcam.gameObject);
            if (_boundsHolder != null) UnityEngine.Object.Destroy(_boundsHolder);
            _vcam = null;
            _boundsShape = null;
            _boundsHolder = null;
            _confiner = null;
            _impulse = null;
        }
    }
}
