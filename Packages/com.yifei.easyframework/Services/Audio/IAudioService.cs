using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Audio
{
    public interface IAudioService
    {
        UniTask PlayBgmAsync(
            string key, float fadeSeconds = 0.5f, CancellationToken ct = default);
        void StopBgm(float fadeSeconds = 0.3f);
        void PlaySfx(string key, float volume = 1f);
        float BgmVolume { get; set; }   // 0~1, 持久化 PlayerPrefs "ef.audio.bgm"
        float SfxVolume { get; set; }   // 0~1, 持久化 PlayerPrefs "ef.audio.sfx"
    }
}
