using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Scenes;
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    public sealed class PoolService : IPoolService, IDisposable
    {
        /// <summary>EditMode 测试可替换为 Object.DestroyImmediate;运行时为 Object.Destroy。</summary>
        internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

        /// <summary>闲置缩容扫描周期(秒)。默认 10 秒,足够低频不影响性能。</summary>
        const float DefaultScanIntervalSeconds = 10f;

        readonly IAssetService _assets;
        readonly ITimerService _timer;
        readonly Func<float> _now;
        readonly IDisposable _sceneUnloadSub;
        readonly Dictionary<string, Stack<GameObject>> _idle = new();
        readonly HashSet<GameObject> _active = new();
        readonly Dictionary<GameObject, IPoolable[]> _poolablesCache = new();

        // 闲置缩容:默认关闭(不调用 SetIdleTimeout 则这两个字典恒为空,行为与缩容功能上线前完全一致)。
        readonly Dictionary<string, float> _idleTimeoutSeconds = new();
        readonly Dictionary<GameObject, float> _idleSince = new();

        GameObject _root;
        GameObject Root => _root != null ? _root : (_root = new GameObject("[Pools]"));

        /// <summary>生产构造:时间源默认 Time.realtimeSinceStartup。</summary>
        public PoolService(IAssetService assets, IEventBus events, ITimerService timer = null)
            : this(assets, events, timer, () => Time.realtimeSinceStartup) { }

        /// <summary>测试构造:可注入时间源。</summary>
        internal PoolService(IAssetService assets, IEventBus events, ITimerService timer, Func<float> nowProvider)
        {
            _assets = assets;
            _timer = timer;
            _now = nowProvider;
            _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ClearAll());
            _timer?.Schedule(DefaultScanIntervalSeconds, ScanIdleTimeouts, repeat: true);
        }

        /// <summary>
        /// 按 key 开启/调整闲置自动缩容:该 key 下的实例闲置超过 idleTimeoutSeconds 秒后,
        /// 下一次缩容扫描会销毁并从池中移除。传 null 或 0 关闭该 key 的缩容(默认即为关闭)。
        /// 不影响现有调用方——不调用本方法时行为与缩容功能上线前完全一致。
        /// </summary>
        public void SetIdleTimeout(string key, float? idleTimeoutSeconds)
        {
            if (idleTimeoutSeconds.HasValue && idleTimeoutSeconds.Value > 0f)
                _idleTimeoutSeconds[key] = idleTimeoutSeconds.Value;
            else
                _idleTimeoutSeconds.Remove(key);
        }

        public async UniTask PrewarmAsync(string key, int count)
        {
            var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Scene);
            var stack = GetStack(key);
            for (var i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, key);
                go.SetActive(false);
                stack.Push(go);
            }
        }

        public async UniTask<GameObject> SpawnAsync(string key, Vector3 position = default,
            Quaternion rotation = default, Transform parent = null)
        {
            var stack = GetStack(key);
            GameObject go;
            if (stack.Count > 0)
            {
                go = stack.Pop();
                _idleSince.Remove(go);
            }
            else
            {
                var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Scene);
                go = Instantiate(prefab, key);
            }

            var t = go.transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation == default ? Quaternion.identity : rotation);
            go.SetActive(true);
            _active.Add(go);

            foreach (var p in GetPoolables(go)) p.OnSpawn();
            return go;
        }

        public void Despawn(GameObject instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var marker = instance.GetComponent<PooledMarker>();
            if (marker == null)
                throw new InvalidOperationException("Instance was not spawned by this pool.");
            if (!_active.Remove(instance))
                throw new InvalidOperationException("Instance already despawned or not active.");

            foreach (var p in GetPoolables(instance)) p.OnDespawn();

            // Remove cache entry so a stale destroyed-GameObject key cannot linger.
            // The next SpawnAsync/Despawn for this instance will re-populate via
            // GetComponentsInChildren, which is correct after potential hierarchy changes.
            _poolablesCache.Remove(instance);

            instance.SetActive(false);
            instance.transform.SetParent(Root.transform, false);
            GetStack(marker.Key).Push(instance);

            if (_idleTimeoutSeconds.ContainsKey(marker.Key))
                _idleSince[instance] = _now();
        }

        Stack<GameObject> GetStack(string key)
        {
            if (!_idle.TryGetValue(key, out var stack))
                _idle[key] = stack = new Stack<GameObject>();
            return stack;
        }

        GameObject Instantiate(GameObject prefab, string key)
        {
            var go = UnityEngine.Object.Instantiate(prefab, Root.transform);
            var marker = go.AddComponent<PooledMarker>();
            marker.Key = key;
            return go;
        }

        IPoolable[] GetPoolables(GameObject go)
        {
            if (!_poolablesCache.TryGetValue(go, out var arr))
                _poolablesCache[go] = arr = go.GetComponentsInChildren<IPoolable>(true);
            return arr;
        }

        /// <summary>定时器周期回调:扫描 _idleSince,销毁超过各自 key 的 idleTimeoutSeconds 的实例。</summary>
        void ScanIdleTimeouts()
        {
            if (_idleSince.Count == 0) return;

            var now = _now();
            List<GameObject> toDestroy = null;
            foreach (var kv in _idleSince)
            {
                var go = kv.Key;
                if (go == null) continue; // 已被外部销毁,交给下面的清理兜底
                var marker = go.GetComponent<PooledMarker>();
                if (marker == null || !_idleTimeoutSeconds.TryGetValue(marker.Key, out var timeout))
                    continue;
                if (now - kv.Value < timeout) continue;

                (toDestroy ??= new List<GameObject>()).Add(go);
            }

            if (toDestroy == null) return;

            foreach (var go in toDestroy)
            {
                var marker = go.GetComponent<PooledMarker>();
                if (marker != null && _idle.TryGetValue(marker.Key, out var stack))
                {
                    // Stack 不支持随机移除,重建剩余元素(缩容是低频操作,重建成本可忽略)。
                    var remaining = new Stack<GameObject>();
                    foreach (var item in stack)
                        if (!ReferenceEquals(item, go))
                            remaining.Push(item);
                    // 上面按出栈顺序 push 会反转顺序,用临时数组还原原始 LIFO 顺序。
                    var arr = remaining.ToArray();
                    stack.Clear();
                    for (var i = arr.Length - 1; i >= 0; i--)
                        stack.Push(arr[i]);
                }
                _idleSince.Remove(go);
                _poolablesCache.Remove(go);
                DestroyHandler(go);
            }
        }

        void ClearAll()
        {
            foreach (var stack in _idle.Values)
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go != null)
                    {
                        _poolablesCache.Remove(go);
                        DestroyHandler(go);
                    }
                }
            _idle.Clear();
            _idleSince.Clear();
            foreach (var go in _active)
                if (go != null)
                {
                    _poolablesCache.Remove(go);
                    DestroyHandler(go);
                }
            _active.Clear();
            _poolablesCache.Clear(); // sweep any remaining entries (e.g. already-null keys)
        }

        public void Dispose()
        {
            _sceneUnloadSub?.Dispose();
            ClearAll();
            if (_root != null) DestroyHandler(_root);
        }
    }
}
