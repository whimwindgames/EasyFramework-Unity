using System;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Scenes
{
    /// <summary>对 Unity SceneManager 的薄抽象,便于单测替换。</summary>
    internal interface ISceneLoader
    {
        UniTask LoadAsync(string sceneName, IProgress<float> progress);
    }
}
