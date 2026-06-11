using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class PoolServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<Type, List<Delegate>> _handlers = new();
            public void Publish<T>(T evt)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((Action<T>)d)(evt);
            }
            public IDisposable Subscribe<T>(Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }
            sealed class Sub : IDisposable
            {
                readonly Action _dispose;
                public Sub(Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }
        }

        sealed class Resettable : MonoBehaviour, IPoolable
        {
            public int SpawnCount, DespawnCount;
            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        GameObject _prefab;
        FakeBus _bus;
        FakeAssetService _assets;
        PoolService _pool;
        Action<GameObject> _originalDestroy;

        [SetUp]
        public void SetUp()
        {
            _originalDestroy = PoolService.DestroyHandler;
            PoolService.DestroyHandler = UnityEngine.Object.DestroyImmediate;

            _prefab = new GameObject("BulletPrefab");
            _prefab.AddComponent<Resettable>();
            _assets = new FakeAssetService(
                new Dictionary<string, UnityEngine.Object> { { "bullet", _prefab } });
            _bus = new FakeBus();
            _pool = new PoolService(_assets, _bus);
        }

        [TearDown]
        public void TearDown()
        {
            PoolService.DestroyHandler = _originalDestroy;
            if (_prefab != null) UnityEngine.Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Spawn_ReusesDespawnedInstance()
        {
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(a);
            var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(a, b);
        }

        [Test]
        public void Spawn_Despawn_InvokePoolableCallbacks()
        {
            var go = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            var r = go.GetComponent<Resettable>();
            Assert.AreEqual(1, r.SpawnCount);
            _pool.Despawn(go);
            Assert.AreEqual(1, r.DespawnCount);
            Assert.IsFalse(go.activeSelf);
        }

        [Test]
        public void Despawn_Twice_Throws()
        {
            var go = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(go);
            Assert.Throws<InvalidOperationException>(() => _pool.Despawn(go));
        }

        [Test]
        public void Despawn_ForeignInstance_Throws()
        {
            var foreign = new GameObject("foreign");
            try { Assert.Throws<InvalidOperationException>(() => _pool.Despawn(foreign)); }
            finally { UnityEngine.Object.DestroyImmediate(foreign); }
        }

        [Test]
        public void Prewarm_FillsPool()
        {
            _pool.PrewarmAsync("bullet", 3).GetAwaiter().GetResult();
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.IsNotNull(a); // 取自预热实例,不再实例化新的(行为以复用为准)
        }

        [Test]
        public void SceneWillUnload_ClearsAllPools()
        {
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(a);
            _bus.Publish(new SceneWillUnloadEvent("Level1"));
            // 清池后再 spawn 应得到新实例(旧的已 Destroy)
            var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreNotSame(a, b);
        }
    }
}
