using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyFramework.Services.Assets
{
    public sealed class AddressablesAssetService : IAssetService, IDisposable
    {
        readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly string Key;
            public readonly Type Type;
            public CacheKey(string key, Type type) { Key = key; Type = type; }
            public bool Equals(CacheKey other) => Key == other.Key && Type == other.Type;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Key, Type);
        }

        sealed class Entry
        {
            public AsyncOperationHandle Handle;
        }

        readonly Dictionary<AssetScope, Dictionary<CacheKey, Entry>> _byScope = new()
        {
            { AssetScope.Global, new Dictionary<CacheKey, Entry>() },
            { AssetScope.Scene, new Dictionary<CacheKey, Entry>() },
        };
        readonly IDisposable _sceneUnloadSub;
        bool _disposed;

        public AddressablesAssetService(IEventBus events)
            => _sceneUnloadSub = (events ?? throw new ArgumentNullException(nameof(events)))
                .Subscribe<SceneWillUnloadEvent>(_ => ReleaseScope(AssetScope.Scene));

        public async UniTask<T> LoadAsync<T>(
            string key, AssetScope scope = AssetScope.Scene,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Asset key is required.", nameof(key));
            if (!_byScope.TryGetValue(scope, out var group))
                throw new ArgumentOutOfRangeException(nameof(scope));

            var cacheKey = new CacheKey(key, typeof(T));
            if (group.TryGetValue(cacheKey, out var existing))
            {
                await existing.Handle.ToUniTask(cancellationToken: ct);
                return (T)existing.Handle.Result;
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            var entry = new Entry { Handle = handle };
            group[cacheKey] = entry;
            try
            {
                return await handle.ToUniTask(cancellationToken: ct);
            }
            catch
            {
                if (group.TryGetValue(cacheKey, out var current) &&
                    ReferenceEquals(current, entry))
                    group.Remove(cacheKey);
                if (handle.IsValid())
                    Addressables.Release(handle);
                throw;
            }
        }

        public void ReleaseScope(AssetScope scope)
        {
            if (!_byScope.TryGetValue(scope, out var group))
                throw new ArgumentOutOfRangeException(nameof(scope));
            foreach (var entry in group.Values)
                if (entry.Handle.IsValid())
                    Addressables.Release(entry.Handle);
            group.Clear();
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AddressablesAssetService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sceneUnloadSub?.Dispose();
            ReleaseScope(AssetScope.Scene);
            ReleaseScope(AssetScope.Global);
        }
    }
}
