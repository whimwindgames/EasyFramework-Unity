using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyFramework.Services.Assets
{
    public sealed class AddressablesAssetService : IAssetService, IDisposable
    {
        sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        readonly Dictionary<AssetScope, Dictionary<string, Entry>> _byScope = new()
        {
            { AssetScope.Global, new Dictionary<string, Entry>() },
            { AssetScope.Scene, new Dictionary<string, Entry>() },
        };
        readonly IDisposable _sceneUnloadSub;

        public AddressablesAssetService(IEventBus events)
        {
            _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ReleaseScope(AssetScope.Scene));
        }

        public async UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene)
            where T : UnityEngine.Object
        {
            var group = _byScope[scope];
            if (group.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                await existing.Handle.ToUniTask();
                return (T)existing.Handle.Result;
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            var entry = new Entry { Handle = handle, RefCount = 1 };
            group[key] = entry;
            var result = await handle.ToUniTask();
            return result;
        }

        public void ReleaseScope(AssetScope scope)
        {
            var group = _byScope[scope];
            foreach (var entry in group.Values)
            {
                if (entry.Handle.IsValid())
                    Addressables.Release(entry.Handle);
            }
            group.Clear();
        }

        public void Dispose()
        {
            _sceneUnloadSub?.Dispose();
            ReleaseScope(AssetScope.Scene);
            ReleaseScope(AssetScope.Global);
        }
    }
}
