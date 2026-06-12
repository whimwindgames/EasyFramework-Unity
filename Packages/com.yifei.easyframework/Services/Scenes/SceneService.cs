using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.Scenes
{
    public sealed class SceneService : ISceneService
    {
        readonly ISceneLoader _loader;
        readonly ISceneTransition _transition;
        readonly IEventBus _events;

        public string CurrentScene { get; private set; }

        internal SceneService(ISceneLoader loader, ISceneTransition transition,
            IEventBus events, string initialScene)
        {
            _loader = loader;
            _transition = transition;
            _events = events;
            CurrentScene = initialScene;
        }

        public async UniTask LoadAsync(string sceneName, IProgress<float> progress = null)
        {
            await _transition.PlayOut();
            _events.Publish(new SceneWillUnloadEvent(CurrentScene));
            await _loader.LoadAsync(sceneName, progress);
            CurrentScene = sceneName;
            _events.Publish(new SceneLoadedEvent(sceneName));
            await _transition.PlayIn();
        }
    }
}
