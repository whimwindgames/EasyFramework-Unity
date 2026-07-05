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
            try
            {
                var result = await handle.ToUniTask();
                return result ?? new List<string>();
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
        {
            // Addressables.UpdateCatalogs 返回 AsyncOperationHandle<List<IResourceLocator>>(更新后的 locator 列表),
            // 而非 bool;成功与否通过 handle.Status 判断,而不是 Result 本身。
            AsyncOperationHandle<List<IResourceLocator>> handle = Addressables.UpdateCatalogs(catalogKeys, false);
            try
            {
                await handle.ToUniTask(progress: progress);

                var succeeded = handle.Status == AsyncOperationStatus.Succeeded;
                if (succeeded) progress?.Report(1f);
                return succeeded;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }
    }
}
