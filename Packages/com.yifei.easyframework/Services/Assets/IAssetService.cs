using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Assets
{
    public enum AssetScope { Global, Scene }

    public interface IAssetService
    {
        UniTask<T> LoadAsync<T>(
            string key, AssetScope scope = AssetScope.Scene,
            CancellationToken ct = default) where T : UnityEngine.Object;
        void ReleaseScope(AssetScope scope);
    }
}
