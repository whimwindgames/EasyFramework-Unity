using System;
using UnityEngine;

namespace EasyFramework.Services.Haptics
{
    public sealed class HapticsService : IHapticsService
    {
        const string PrefsKey = "ef.haptics";

        /// <summary>平台振动钩子。默认调真实平台实现;EditMode 测试替换为记录型。</summary>
        internal static Action<HapticStrength> PlatformVibrate = DefaultPlatformVibrate;

        bool _enabled;

        public HapticsService()
        {
            _enabled = PlayerPrefs.GetInt(PrefsKey, 1) != 0;
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Vibrate(HapticStrength strength)
        {
            if (!_enabled) return;
            PlatformVibrate(strength);
        }

        static void DefaultPlatformVibrate(HapticStrength strength)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            long ms = strength switch
            {
                HapticStrength.Light => 50L,
                HapticStrength.Medium => 100L,
                HapticStrength.Heavy => 200L,
                _ => 50L,
            };
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator != null) vibrator.Call("vibrate", ms);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Android vibrate failed: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate(); // iOS 三档同振(系统未暴露强度)
#else
            Debug.Log($"[EasyFramework] Haptic ({strength}) — no device vibration in this environment.");
#endif
        }
    }
}
