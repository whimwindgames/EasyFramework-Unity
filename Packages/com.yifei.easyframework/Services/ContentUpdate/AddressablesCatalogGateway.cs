using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyFramework.Services.ContentUpdate
{
    /// <summary>
    /// IAddressablesCatalogGateway 的真实实现,直接转发到 Addressables 静态 API。
    /// 本类是唯一调用 Addressables.CheckForCatalogUpdates / Addressables.UpdateCatalogs 的地方;
    /// ContentUpdateService 的全部业务逻辑测试改走 FakeAddressablesCatalogGateway,不依赖本类。
    /// </summary>
    public sealed class AddressablesCatalogGateway : IAddressablesCatalogGateway
    {
        public async UniTask<List<string>> CheckForCatalogUpdatesAsync()
        {
            var handle = Addressables.CheckForCatalogUpdates(false);
            var result = await handle.ToUniTask();
            Addressables.Release(handle);
            return result ?? new List<string>();
        }

        public async UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
        {
            // Addressables.UpdateCatalogs 返回 AsyncOperationHandle<List<IResourceLocator>>(更新后的 locator 列表),
            // 而非 bool;成功与否通过 handle.Status 判断,而不是 Result 本身。
            AsyncOperationHandle<List<IResourceLocator>> handle = Addressables.UpdateCatalogs(catalogKeys, false);
            await handle.ToUniTask(progress: progress);
            progress?.Report(1f);

            var succeeded = handle.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(handle);
            return succeeded;
        }
    }
}
