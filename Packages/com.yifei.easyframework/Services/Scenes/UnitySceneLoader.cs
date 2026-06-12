using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace EasyFramework.Services.Scenes
{
    internal sealed class UnitySceneLoader : ISceneLoader
    {
        public async UniTask LoadAsync(string sceneName, IProgress<float> progress)
        {
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = true;
            while (!op.isDone)
            {
                // LoadSceneAsync 进度上限 0.9,归一化到 0..1
                progress?.Report(op.progress >= 0.9f ? 1f : op.progress / 0.9f);
                await UniTask.Yield();
            }
            progress?.Report(1f);
        }
    }
}
