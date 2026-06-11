using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Scenes;
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    public sealed class PoolService : IPoolService, IDisposable
    {
        /// <summary>EditMode 测试可替换为 Object.DestroyImmediate;运行时为 Object.Destroy。</summary>
        internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

        readonly IAssetService _assets;
        readonly IDisposable _sceneUnloadSub;
        readonly Dictionary<string, Stack<GameObject>> _idle = new();
        readonly HashSet<GameObject> _active = new();
        readonly Dictionary<GameObject, IPoolable[]> _poolablesCache = new();

        GameObject _root;
        GameObject Root => _root != null ? _root : (_root = new GameObject("[Pools]"));

        public PoolService(IAssetService assets, IEventBus events)
        {
            _assets = assets;
            _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ClearAll());
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
