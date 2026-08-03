using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace EasyFramework.Services.UI
{
    public sealed class UIService : IUIService, ITickable, IDisposable
    {
        internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

        readonly IAssetService _assets;
        readonly IUIRootFactory _rootFactory;
        readonly CancellationTokenSource _disposeCts = new();
        readonly List<UIPanel> _windowStack = new();
        readonly Dictionary<Type, UIPanel> _huds = new();
        readonly Dictionary<Type, UIPanel> _overlays = new();
        readonly Queue<Func<UniTask>> _uiQueue = new();
        readonly Queue<Func<UniTask>> _popupQueue = new();

        UIRootHandle _root;
        UIPanel _activePopup;
        bool _pumpingUi;
        bool _pumpingPopups;
        bool _disposed;

        public int WindowCount => _windowStack.Count;
        public int HudCount => _huds.Count;
        public int OverlayCount => _overlays.Count;

        public UIService(IAssetService assets)
            : this(assets, new DefaultUIRootFactory(UIRootProfile.Portrait())) { }

        public UIService(IAssetService assets, IUIRootFactory rootFactory)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
        }

        UIRootHandle EnsureRoot()
        {
            ThrowIfDisposed();
            if (_root != null) return _root;
            _root = _rootFactory.Create()
                ?? throw new InvalidOperationException("UI root factory returned null.");
            _root.Validate();
            return _root;
        }

        async UniTask<T> InstantiatePanelAsync<T>(
            UILayer layer, object args, CancellationToken ct) where T : UIPanel
        {
            var root = EnsureRoot();
            var key = "ui/" + typeof(T).Name;
            var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Global);
            ct.ThrowIfCancellationRequested();

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, root.Layer(layer), false);
                var panel = instance.GetComponent<T>();
                if (panel == null)
                    throw new InvalidOperationException(
                        $"UI prefab '{key}' has no component of type {typeof(T).Name}.");
                panel.OnSetup(args);
                ct.ThrowIfCancellationRequested();
                instance.SetActive(true);
                return panel;
            }
            catch
            {
                if (instance != null)
                    DestroyHandler(instance);
                throw;
            }
        }

        async UniTask EnqueueUiOp(
            Func<CancellationToken, UniTask> operation, CancellationToken callerCt)
        {
            await EnqueueUiOp(async ct =>
            {
                await operation(ct);
                return true;
            }, callerCt);
        }

        UniTask<T> EnqueueUiOp<T>(
            Func<CancellationToken, UniTask<T>> operation, CancellationToken callerCt)
        {
            ThrowIfDisposed();
            var completion = new UniTaskCompletionSource<T>();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                callerCt, _disposeCts.Token);
            _uiQueue.Enqueue(async () =>
            {
                try
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    completion.TrySetResult(await operation(linkedCts.Token));
                }
                catch (OperationCanceledException e)
                {
                    completion.TrySetCanceled(e.CancellationToken);
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
                finally
                {
                    linkedCts.Dispose();
                }
            });
            PumpUi().Forget();
            return completion.Task;
        }

        async UniTask PumpUi()
        {
            if (_pumpingUi) return;
            _pumpingUi = true;
            try
            {
                while (_uiQueue.Count > 0)
                    await _uiQueue.Dequeue()();
            }
            finally
            {
                _pumpingUi = false;
            }
        }

        public UniTask<T> PushAsync<T>(
            object args = null, CancellationToken ct = default) where T : UIPanel
            => EnqueueUiOp(async operationCt =>
            {
                var previous = _windowStack.Count > 0 ? _windowStack[^1] : null;
                if (previous != null)
                    previous.gameObject.SetActive(false);

                T panel = null;
                try
                {
                    panel = await InstantiatePanelAsync<T>(UILayer.Window, args, operationCt);
                    await panel.PlayEnter();
                    operationCt.ThrowIfCancellationRequested();
                    _windowStack.Add(panel);
                    return panel;
                }
                catch
                {
                    if (panel != null)
                        DestroyHandler(panel.gameObject);
                    if (previous != null)
                        previous.gameObject.SetActive(true);
                    throw;
                }
            }, ct);

        public UniTask PopAsync(CancellationToken ct = default)
            => EnqueueUiOp(async operationCt =>
            {
                if (_windowStack.Count <= 1) return;

                var top = _windowStack[^1];
                await top.PlayExit();
                operationCt.ThrowIfCancellationRequested();
                _windowStack.RemoveAt(_windowStack.Count - 1);
                DestroyHandler(top.gameObject);

                var newTop = _windowStack[^1];
                newTop.gameObject.SetActive(true);
                await newTop.PlayEnter();
                operationCt.ThrowIfCancellationRequested();
            }, ct);

        public UniTask PopAllAsync(CancellationToken ct = default)
            => EnqueueUiOp(operationCt => ClearWindowsAsync(true, operationCt), ct);

        async UniTask ClearWindowsAsync(bool animate, CancellationToken ct)
        {
            Exception firstError = null;
            while (_windowStack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var top = _windowStack[^1];
                _windowStack.RemoveAt(_windowStack.Count - 1);
                if (animate)
                {
                    try { await top.PlayExit(); }
                    catch (Exception e) { firstError ??= e; }
                }
                if (top != null)
                    DestroyHandler(top.gameObject);
            }
            if (firstError != null) throw firstError;
        }

        public UniTask<TResult> ShowPopupAsync<TPopup, TResult>(
            object args = null, CancellationToken ct = default)
            where TPopup : UIPopup<TResult>
        {
            ThrowIfDisposed();
            var completion = new UniTaskCompletionSource<TResult>();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct, _disposeCts.Token);
            _popupQueue.Enqueue(async () =>
            {
                TPopup popup = null;
                try
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    popup = await InstantiatePanelAsync<TPopup>(
                        UILayer.Popup, args, linkedCts.Token);
                    _activePopup = popup;
                    await popup.PlayEnter();
                    linkedCts.Token.ThrowIfCancellationRequested();
                    var result = await popup.Result.AttachExternalCancellation(linkedCts.Token);
                    completion.TrySetResult(result);
                    await popup.PlayExit();
                }
                catch (OperationCanceledException e)
                {
                    completion.TrySetCanceled(e.CancellationToken);
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
                finally
                {
                    if (popup != null)
                        DestroyHandler(popup.gameObject);
                    if (ReferenceEquals(_activePopup, popup))
                        _activePopup = null;
                    linkedCts.Dispose();
                }
            });
            PumpPopups().Forget();
            return completion.Task;
        }

        async UniTask PumpPopups()
        {
            if (_pumpingPopups) return;
            _pumpingPopups = true;
            try
            {
                while (_popupQueue.Count > 0)
                    await _popupQueue.Dequeue()();
            }
            finally
            {
                _pumpingPopups = false;
            }
        }

        public UniTask<T> ShowHudAsync<T>(
            object args = null, CancellationToken ct = default) where T : UIPanel
            => ShowCachedPanelAsync<T>(_huds, UILayer.Hud, false, args, ct);

        public UniTask HideHudAsync<T>(CancellationToken ct = default) where T : UIPanel
            => HideCachedPanelAsync(_huds, typeof(T), ct);

        public UniTask HideHudAsync(CancellationToken ct = default)
            => HideAllCachedPanelsAsync(_huds, ct);

        public UniTask<T> ShowOverlayAsync<T>(
            object args = null, CancellationToken ct = default) where T : UIPanel
            => ShowCachedPanelAsync<T>(_overlays, UILayer.Overlay, true, args, ct);

        public UniTask HideOverlayAsync<T>(CancellationToken ct = default) where T : UIPanel
            => HideCachedPanelAsync(_overlays, typeof(T), ct);

        public UniTask HideOverlayAsync(CancellationToken ct = default)
            => HideAllCachedPanelsAsync(_overlays, ct);

        UniTask<T> ShowCachedPanelAsync<T>(
            Dictionary<Type, UIPanel> cache,
            UILayer layer,
            bool bringToFrontOnReuse,
            object args,
            CancellationToken ct) where T : UIPanel
            => EnqueueUiOp(async operationCt =>
            {
                if (cache.TryGetValue(typeof(T), out var existing) && existing != null)
                {
                    existing.OnSetup(args);
                    existing.gameObject.SetActive(true);
                    if (bringToFrontOnReuse)
                        existing.transform.SetAsLastSibling();
                    await existing.PlayEnter();
                    operationCt.ThrowIfCancellationRequested();
                    return (T)existing;
                }

                var panel = await InstantiatePanelAsync<T>(layer, args, operationCt);
                try
                {
                    await panel.PlayEnter();
                    operationCt.ThrowIfCancellationRequested();
                    cache[typeof(T)] = panel;
                    return panel;
                }
                catch
                {
                    DestroyHandler(panel.gameObject);
                    throw;
                }
            }, ct);

        UniTask HideCachedPanelAsync(
            Dictionary<Type, UIPanel> cache, Type panelType, CancellationToken ct)
            => EnqueueUiOp(
                operationCt => HideCachedPanelCoreAsync(cache, panelType, true, operationCt), ct);

        UniTask HideAllCachedPanelsAsync(
            Dictionary<Type, UIPanel> cache, CancellationToken ct)
            => EnqueueUiOp(
                operationCt => HideAllCachedPanelsCoreAsync(cache, true, operationCt), ct);

        static async UniTask HideCachedPanelCoreAsync(
            Dictionary<Type, UIPanel> cache,
            Type panelType,
            bool animate,
            CancellationToken ct)
        {
            if (!cache.TryGetValue(panelType, out var panel)) return;
            cache.Remove(panelType);
            try
            {
                if (animate) await panel.PlayExit();
                ct.ThrowIfCancellationRequested();
            }
            finally
            {
                if (panel != null) DestroyHandler(panel.gameObject);
            }
        }

        static async UniTask HideAllCachedPanelsCoreAsync(
            Dictionary<Type, UIPanel> cache, bool animate, CancellationToken ct)
        {
            Exception firstError = null;
            var panels = new List<UIPanel>(cache.Values);
            cache.Clear();
            panels.Sort((left, right) =>
            {
                if (left == null) return right == null ? 0 : 1;
                if (right == null) return -1;
                return right.transform.GetSiblingIndex().CompareTo(
                    left.transform.GetSiblingIndex());
            });
            foreach (var panel in panels)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (animate && panel != null) await panel.PlayExit();
                }
                catch (Exception e)
                {
                    firstError ??= e;
                }
                finally
                {
                    if (panel != null) DestroyHandler(panel.gameObject);
                }
            }
            if (firstError != null) throw firstError;
        }

        public void Tick()
        {
            if (_disposed) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                HandleBack();
        }

        internal bool HandleBack()
        {
            if (_disposed) return false;
            if (_activePopup != null)
                return _activePopup.OnBackRequested();

            if (_windowStack.Count > 0)
            {
                var top = _windowStack[^1];
                if (top.OnBackRequested()) return true;
                if (_windowStack.Count > 1)
                {
                    if (_pumpingUi) return true;
                    PopAsync().Forget();
                    return true;
                }
            }
            return false;
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeCts.Cancel();

            foreach (var panel in _windowStack)
                if (panel != null) DestroyHandler(panel.gameObject);
            _windowStack.Clear();

            foreach (var hud in _huds.Values)
                if (hud != null) DestroyHandler(hud.gameObject);
            _huds.Clear();

            foreach (var overlay in _overlays.Values)
                if (overlay != null) DestroyHandler(overlay.gameObject);
            _overlays.Clear();
            if (_activePopup != null)
            {
                DestroyHandler(_activePopup.gameObject);
                _activePopup = null;
            }
            if (_root != null)
            {
                if (_root.OwnsEventSystem && _root.EventSystemObject != null)
                    DestroyHandler(_root.EventSystemObject);
                if (_root.OwnsRoot && _root.Root != null)
                    DestroyHandler(_root.Root);
                _root = null;
            }

            _disposeCts.Dispose();
        }
    }
}
