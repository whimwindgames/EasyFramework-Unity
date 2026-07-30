using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Scenes
{
    public readonly struct SceneWillUnloadEvent
    {
        public readonly string SceneName;
        public SceneWillUnloadEvent(string n) => SceneName = n;
    }

    public readonly struct SceneLoadedEvent
    {
        public readonly string SceneName;
        public SceneLoadedEvent(string n) => SceneName = n;
    }

    /// <summary>转场视觉接口;Phase 3 提供淡入淡出实现,Phase 2 用 Noop。</summary>
    public interface ISceneTransition
    {
        UniTask PlayOut(CancellationToken ct = default);
        UniTask PlayIn(CancellationToken ct = default);
    }

    public sealed class NoopSceneTransition : ISceneTransition
    {
        public UniTask PlayOut(CancellationToken ct = default)
            => ct.IsCancellationRequested ? UniTask.FromCanceled(ct) : UniTask.CompletedTask;
        public UniTask PlayIn(CancellationToken ct = default)
            => ct.IsCancellationRequested ? UniTask.FromCanceled(ct) : UniTask.CompletedTask;
    }

    public interface ISceneService
    {
        string CurrentScene { get; }
        UniTask LoadAsync(string sceneName, IProgress<float> progress = null,
            CancellationToken ct = default);
    }
}
