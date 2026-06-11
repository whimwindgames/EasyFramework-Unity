using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Assets
{
    public sealed class FakeAssetService : IAssetService
    {
        readonly IReadOnlyDictionary<string, UnityEngine.Object> _assets;
        readonly Dictionary<AssetScope, Dictionary<string, int>> _refs = new()
        {
            { AssetScope.Global, new Dictionary<string, int>() },
            { AssetScope.Scene, new Dictionary<string, int>() },
        };

        /// <summary>测试可读:某 scope 当前活跃的 key 数(ReleaseScope 后归零)。</summary>
        public int ActiveCount(AssetScope scope) => _refs[scope].Count;

        public FakeAssetService(IReadOnlyDictionary<string, UnityEngine.Object> assets)
        {
            _assets = assets;
        }

        public UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene)
            where T : UnityEngine.Object
        {
            if (!_assets.TryGetValue(key, out var obj))
                throw new KeyNotFoundException($"FakeAssetService has no asset for key '{key}'.");

            var group = _refs[scope];
            group[key] = group.TryGetValue(key, out var c) ? c + 1 : 1;
            return UniTask.FromResult((T)obj);
        }

        public void ReleaseScope(AssetScope scope) => _refs[scope].Clear();
    }
}
