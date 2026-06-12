using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Assets
{
    public enum AssetScope { Global, Scene }

    public interface IAssetService
    {
        UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene) where T : UnityEngine.Object;
        void ReleaseScope(AssetScope scope);
    }
}
