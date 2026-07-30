using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.ContentUpdate
{
    /// <summary>
    /// Addressables.CheckForCatalogUpdates() / Addressables.UpdateCatalogs() 的薄封装。
    /// 真实实现见 AddressablesCatalogGateway;EditMode 测试用 FakeAddressablesCatalogGateway 替身。
    /// </summary>
    public interface IAddressablesCatalogGateway
    {
        /// <summary>返回有更新的 catalog key 列表;无更新时返回空列表。</summary>
        UniTask<List<string>> CheckForCatalogUpdatesAsync(CancellationToken ct);

        /// <summary>下载并应用指定 catalog 的更新;成功返回 true。</summary>
        UniTask<bool> UpdateCatalogsAsync(
            List<string> catalogKeys, IProgress<float> progress, CancellationToken ct);
    }
}
