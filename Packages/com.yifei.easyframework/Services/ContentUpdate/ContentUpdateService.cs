using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.ContentUpdate
{
    public sealed class ContentUpdateService : IContentUpdateService
    {
        readonly IAddressablesCatalogGateway _gateway;
        readonly IEventBus _events;

        List<string> _pendingCatalogKeys = new();

        public bool HasChecked { get; private set; }

        public ContentUpdateService(IAddressablesCatalogGateway gateway, IEventBus events)
        {
            _gateway = gateway;
            _events = events;
        }

        public async UniTask<ContentUpdateInfo> CheckAsync()
        {
            _pendingCatalogKeys = await _gateway.CheckForCatalogUpdatesAsync() ?? new List<string>();
            HasChecked = true;

            var isAvailable = _pendingCatalogKeys.Count > 0;
            var info = new ContentUpdateInfo(isAvailable, -1L);

            if (isAvailable)
                _events.Publish(new ContentAvailableEvent(info));

            return info;
        }

        public async UniTask<bool> DownloadAndApplyAsync(IProgress<float> progress = null)
        {
            if (!HasChecked)
                await CheckAsync();

            if (_pendingCatalogKeys.Count == 0)
                return false;

            var success = await _gateway.UpdateCatalogsAsync(_pendingCatalogKeys, progress);
            if (success)
            {
                _events.Publish(new ContentUpdateAppliedEvent());
            }
            else
            {
                _events.Publish(new ContentUpdateFailedEvent("Addressables.UpdateCatalogs returned failure."));
            }

            return success;
        }
    }
}
