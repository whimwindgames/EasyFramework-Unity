using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using UnityEngine;
using VContainer.Unity;

namespace EasyFramework.Services.Audio
{
    public sealed class AudioService : IAudioService, ITickable
    {
        const string BgmKey = "ef.audio.bgm";
        const string SfxKey = "ef.audio.sfx";
        const int SfxVoices = 8;

        readonly IAssetService _assets;

        float _bgmVolume;
        float _sfxVolume;

        GameObject _host;
        AudioSource _bgmA;
        AudioSource _bgmB;
        bool _aIsActive;        // 当前出声(active)的是 A 还是 B
        AudioSource[] _sfx;
        int _sfxCursor;

        CrossfadeState _fade;
        bool _fadingToStop;     // true:淡出为停止 BGM(无新 clip)

        public AudioService(IAssetService assets)
        {
            _assets = assets;
            _bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmKey, 1f));
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
        }

        public float BgmVolume
        {
            get => _bgmVolume;
            set
            {
                _bgmVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(BgmKey, _bgmVolume);
                PlayerPrefs.Save();
                ApplyBgmVolume();
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(SfxKey, _sfxVolume);
                PlayerPrefs.Save();
            }
        }

        public async UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f)
        {
            EnsureHost();
            var clip = await _assets.LoadAsync<AudioClip>(key, AssetScope.Global);

            var incoming = _aIsActive ? _bgmB : _bgmA;
            incoming.clip = clip;
            incoming.loop = true;
            incoming.volume = 0f;
            incoming.Play();

            _aIsActive = !_aIsActive;
            _fadingToStop = false;
            _fade = new CrossfadeState(fadeSeconds, 1f);
            ApplyBgmVolume();
        }

        public void StopBgm(float fadeSeconds = 0.3f)
        {
            if (_host == null) return; // 从未播放过
            _fadingToStop = true;
            _fade = new CrossfadeState(fadeSeconds, 1f);
        }

        public void PlaySfx(string key, float volume = 1f)
        {
            EnsureHost();
            // SFX clip 走 Scene 作用域加载;同步取已加载实例,未加载则异步取后播放
            PlaySfxAsync(key, Mathf.Clamp01(volume)).Forget();
        }

        async UniTaskVoid PlaySfxAsync(string key, float volume)
        {
            var clip = await _assets.LoadAsync<AudioClip>(key, AssetScope.Scene);
            var src = _sfx[_sfxCursor];
            _sfxCursor = (_sfxCursor + 1) % SfxVoices;
            src.PlayOneShot(clip, volume * _sfxVolume);
        }

        public void Tick()
        {
            if (_fade == null) return;
            _fade.Advance(Time.unscaledDeltaTime);
            ApplyBgmVolume();
            if (_fade.IsDone)
            {
                var outgoing = _aIsActive ? _bgmB : _bgmA;
                outgoing.Stop();
                if (_fadingToStop)
                {
                    (_aIsActive ? _bgmA : _bgmB).Stop(); // 停止当前 active(无新 clip 时即 active 本身)
                }
                _fade = null;
            }
        }

        void ApplyBgmVolume()
        {
            if (_host == null) return;
            var active = _aIsActive ? _bgmA : _bgmB;
            var inactive = _aIsActive ? _bgmB : _bgmA;
            if (_fade != null && !_fadingToStop)
            {
                active.volume = _fade.InVolume * _bgmVolume;
                inactive.volume = _fade.OutVolume * _bgmVolume;
            }
            else if (_fade != null && _fadingToStop)
            {
                active.volume = _fade.OutVolume * _bgmVolume;
            }
            else
            {
                active.volume = _bgmVolume;
            }
        }

        void EnsureHost()
        {
            if (_host != null) return;
            _host = new GameObject("[EasyFramework.Audio]");
            Object.DontDestroyOnLoad(_host);
            _bgmA = _host.AddComponent<AudioSource>();
            _bgmB = _host.AddComponent<AudioSource>();
            _bgmA.playOnAwake = _bgmB.playOnAwake = false;
            _sfx = new AudioSource[SfxVoices];
            for (var i = 0; i < SfxVoices; i++)
            {
                _sfx[i] = _host.AddComponent<AudioSource>();
                _sfx[i].playOnAwake = false;
            }
            _aIsActive = true; // 约定:active 初始指向 A(下一次 PlayBgm 切到 B 出声)
        }
    }
}
