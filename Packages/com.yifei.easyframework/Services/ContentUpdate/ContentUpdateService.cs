using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.ContentUpdate
{
    public sealed class ContentUpdateService : IContentUpdateService, IDisposable
    {
        readonly IAddressablesCatalogGateway _gateway;
        readonly IEventBus _events;
        readonly SemaphoreSlim _operationGate = new(1, 1);
        List<string> _pendingCatalogKeys = new();
        bool _disposed;

        public bool HasChecked { get; private set; }

        public ContentUpdateService(IAddressablesCatalogGateway gateway, IEventBus events)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public async UniTask<ContentUpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(ct);
            try
            {
                return await CheckCoreAsync(ct);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        async UniTask<ContentUpdateInfo> CheckCoreAsync(CancellationToken ct)
        {
            try
            {
                var fetched = await _gateway.CheckForCatalogUpdatesAsync(ct);
                _pendingCatalogKeys = fetched != null
                    ? new List<string>(fetched)
                    : new List<string>();
                HasChecked = true;

                var info = new ContentUpdateInfo(
                    _pendingCatalogKeys.Count > 0, -1L);
                if (info.IsAvailable)
                    _events.Publish(new ContentAvailableEvent(info));
                return info;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _events.Publish(new ContentUpdateFailedEvent(
                    $"Catalog check failed: {e.GetType().Name}"));
                throw;
            }
        }

        public async UniTask<bool> DownloadAndApplyAsync(
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(ct);
            try
            {
                if (!HasChecked)
                    await CheckCoreAsync(ct);
                if (_pendingCatalogKeys.Count == 0)
                    return false;

                try
                {
                    var keys = new List<string>(_pendingCatalogKeys);
                    var success = await _gateway.UpdateCatalogsAsync(keys, progress, ct);
                    if (success)
                    {
                        _pendingCatalogKeys.Clear();
                        _events.Publish(new ContentUpdateAppliedEvent());
                    }
                    else
                    {
                        _events.Publish(new ContentUpdateFailedEvent(
                            "Addressables catalog update returned failure."));
                    }
                    return success;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _events.Publish(new ContentUpdateFailedEvent(
                        $"Catalog update failed: {e.GetType().Name}"));
                    return false;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContentUpdateService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _operationGate.Dispose();
        }
    }
}
