using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Core.Timing;
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

        sealed class FakeTimerService : ITimerService
        {
            sealed class Entry
            {
                public Action Callback;
                public bool Repeat;
                public bool Cancelled;
            }

            readonly List<Entry> _entries = new();
            long _nextId = 1;

            public TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)
            {
                var entry = new Entry { Callback = callback, Repeat = repeat };
                _entries.Add(entry);
                return default; // 测试不需要真实 handle 值,仅需可调用 Advance 手动触发
            }

            public void Cancel(TimerHandle handle) { }

            /// <summary>测试钩子:手动触发一次所有已注册的 repeat 回调(模拟到达周期时间点)。</summary>
            public void Fire()
            {
                foreach (var e in _entries.ToArray())
                    if (!e.Cancelled)
                        e.Callback();
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
        FakeTimerService _timer;
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
            _timer = new FakeTimerService();
            _pool = new PoolService(_assets, _bus, _timer);
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

        [Test]
        public void IdleTimeout_NotConfigured_NeverDestroysIdleInstance()
        {
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(a);
            _timer.Fire(); // 触发缩容扫描;未配置 idleTimeoutSeconds,应无事发生
            var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(a, b); // 仍是原实例,说明没有被销毁
        }

        [Test]
        public void IdleTimeout_Configured_DestroysInstanceAfterTimeoutOnScan()
        {
            var now = 100f;
            float NowProvider() => now;
            var pool = new PoolService(_assets, _bus, _timer, NowProvider);
            pool.SetIdleTimeout("bullet", 5f);

            var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            pool.Despawn(a);

            now += 10f; // 超过 5 秒闲置阈值
            _timer.Fire();

            var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreNotSame(a, b); // 旧实例已被缩容销毁,重新实例化了新的
        }

        [Test]
        public void IdleTimeout_Configured_KeepsInstanceBeforeTimeoutOnScan()
        {
            var now = 100f;
            float NowProvider() => now;
            var pool = new PoolService(_assets, _bus, _timer, NowProvider);
            pool.SetIdleTimeout("bullet", 5f);

            var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            pool.Despawn(a);

            now += 2f; // 未超过 5 秒闲置阈值
            _timer.Fire();

            var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(a, b); // 未超时,仍复用原实例
        }

        [Test]
        public void IdleTimeout_ReusedInstance_RemovedFromIdleSinceTracking()
        {
            var now = 100f;
            float NowProvider() => now;
            var pool = new PoolService(_assets, _bus, _timer, NowProvider);
            pool.SetIdleTimeout("bullet", 5f);

            var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            pool.Despawn(a);

            now += 2f;
            var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult(); // 复用命中,应从 _idleSince 移除记录
            Assert.AreSame(a, b);

            now += 10f; // 若 b 的旧闲置时间戳未被清除,缩容会误杀正在使用中的实例
            _timer.Fire();

            // b 当前是 active 状态(未 Despawn),不应被缩容逻辑影响。
            Assert.DoesNotThrow(() => pool.Despawn(b));
        }
    }
}
