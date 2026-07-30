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
            public int CancelCallCount { get; private set; }

            public TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)
            {
                var entry = new Entry { Callback = callback, Repeat = repeat };
                _entries.Add(entry);
                return default; // 测试不需要真实 handle 值,仅需可调用 Advance 手动触发
            }

            public void Cancel(TimerHandle handle) => CancelCallCount++;

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
        public void Spawn_PurgesExternallyDestroyedIdleInstance()
        {
            var first = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(first);
            UnityEngine.Object.DestroyImmediate(first);

            GameObject replacement = null;
            Assert.DoesNotThrow(() =>
                replacement = _pool.SpawnAsync("bullet").GetAwaiter().GetResult());
            Assert.IsNotNull(replacement);
        }

        [Test]
        public void Prewarm_RejectsNegativeCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _pool.PrewarmAsync("bullet", -1).GetAwaiter().GetResult());
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
        public void Dispose_CancelsPeriodicScanTimer()
        {
            _pool.Dispose();
            Assert.AreEqual(1, _timer.CancelCallCount,
                "Dispose 应取消构造时注册的周期缩容扫描定时器,避免定时器在实例销毁后继续持有回调");
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

        [Test]
        public void SetIdleTimeout_DisableThenReenable_DoesNotUseStaleTimestamp()
        {
            var now = 100f;
            float NowProvider() => now;
            var pool = new PoolService(_assets, _bus, _timer, NowProvider);
            pool.SetIdleTimeout("bullet", 5f);

            var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            pool.Despawn(a); // _idleSince[a] 记录为 now(100)

            pool.SetIdleTimeout("bullet", null); // 关闭缩容;_idleSince[a] 的旧时间戳不应再被使用

            now += 1000f; // 远超原先 5 秒阈值的时间流逝,但因缩容已关闭,不应影响 a
            _timer.Fire(); // 缩容关闭期间的扫描不应销毁 a(现有行为,IdleTimeout_NotConfigured_* 已覆盖类似场景)

            pool.SetIdleTimeout("bullet", 5f); // 重新开启;应视为全新计时起点,而不是沿用旧的 _idleSince[a]

            _timer.Fire(); // 重新开启后的第一次扫描:若未清理旧时间戳,now - 旧时间戳(=1000)>= 5,会被误杀

            var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(a, b, "重新开启缩容后不应立即销毁实例——应获得全新的闲置宽限期,而不是被陈旧时间戳误杀");
        }

        [Test]
        public void IdleTimeout_RemovingMiddleInstance_PreservesLifoOrderOfRemaining()
        {
            var now = 100f;
            float NowProvider() => now;
            var pool = new PoolService(_assets, _bus, _timer, NowProvider);
            pool.SetIdleTimeout("bullet", 5f); // 必须先配置阈值,Despawn 时才会记录 _idleSince

            // 三个不同实例:栈为空,每次 SpawnAsync 都会实例化新对象。
            var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            var c = pool.SpawnAsync("bullet").GetAwaiter().GetResult();

            // 先让 a 很早就 Despawn 并闲置很久(它将是本次唯一超时的实例)。
            pool.Despawn(a);

            now += 100f; // a 闲置时间大幅增加,后面用它来确保只有 a 超时
            // 此时若立即扫描,a 会被销毁;先不扫描,继续按 c、b 的顺序 Despawn,
            // 使栈自底向上为 a(最先入栈) -> c -> b(最后入栈,在栈顶)。
            pool.Despawn(c);
            pool.Despawn(b);
            // 栈自顶向下(出栈顺序): b, c, a。a 在栈底,闲置时间远超 c、b。

            now += 2f; // a 的闲置时间已远超 5 秒阈值;c、b 刚 Despawn 不久(闲置 2 秒),未超时
            _timer.Fire(); // 触发缩容扫描:应仅销毁 a,c 与 b 的相对 LIFO 顺序应保持不变

            Assert.IsTrue(a == null, "a 应已被缩容扫描销毁(前置条件,确保下面验证的是移除后的顺序)");

            // 缩容前,出栈顺序应为 b(最后 Despawn,最先复用)、然后 c。
            // 该断言验证:移除处于栈底的 a 后,剩余的 b、c 相对顺序未被破坏性地反转。
            var first = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(b, first, "最后一个存活的 Despawn 实例应最先被复用(LIFO)");

            var second = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(c, second, "次新的存活 Despawn 实例应第二个被复用");

            // a 已被缩容销毁,不应再出现在池中被复用。
            Assert.AreNotSame(a, first);
            Assert.AreNotSame(a, second);
        }
    }
}
