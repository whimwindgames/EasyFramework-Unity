using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using UnityEngine;

namespace EasyFramework.Services.Scenes
{
    public sealed class SceneService : ISceneService, IDisposable
    {
        readonly ISceneLoader _loader;
        readonly ISceneTransition _transition;
        readonly IEventBus _events;
        readonly SemaphoreSlim _loadGate = new(1, 1);
        bool _disposed;

        public string CurrentScene { get; private set; }

        internal SceneService(ISceneLoader loader, ISceneTransition transition,
            IEventBus events, string initialScene)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _transition = transition ?? throw new ArgumentNullException(nameof(transition));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            CurrentScene = initialScene;
        }

        public async UniTask LoadAsync(string sceneName, IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("Scene name is required.", nameof(sceneName));

            await _loadGate.WaitAsync(ct);
            var transitionStarted = false;
            try
            {
                ThrowIfDisposed();
                transitionStarted = true;
                await _transition.PlayOut(ct);
                ct.ThrowIfCancellationRequested();

                var previousScene = CurrentScene;
                _events.Publish(new SceneWillUnloadEvent(previousScene));
                await _loader.LoadAsync(sceneName, progress, ct);

                CurrentScene = sceneName;
                _events.Publish(new SceneLoadedEvent(sceneName));
                await _transition.PlayIn(CancellationToken.None);
            }
            catch
            {
                if (transitionStarted)
                {
                    try { await _transition.PlayIn(CancellationToken.None); }
                    catch (Exception cleanupError)
                    {
                        Debug.LogException(cleanupError);
                    }
                }
                throw;
            }
            finally
            {
                _loadGate.Release();
            }
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SceneService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _loadGate.Dispose();
            if (_transition is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
