using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.Profiling;
#endif

namespace EasyFramework.DevTools.Overlay
{
    /// <summary>左上角 FPS(滑动平均)+ 已分配内存(MB)简易角标。仅 UNITY_EDITOR || DEVELOPMENT_BUILD 下绘制,发布版零开销。</summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float Smoothing = 0.1f;   // 指数滑动平均系数
        float _avgDeltaTime;
        GUIStyle _style;

        void Update()
        {
            // 指数滑动平均,避免逐帧抖动
            _avgDeltaTime += (Time.unscaledDeltaTime - _avgDeltaTime) * Smoothing;
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                normal = { textColor = Color.green },
                alignment = TextAnchor.UpperLeft,
            };

            var fps = _avgDeltaTime > 0f ? 1f / _avgDeltaTime : 0f;
            var memMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            var text = $"FPS {fps:00.0}\nMEM {memMb:000.0} MB";

            // 安全区内偏移一点,避开刘海
            var rect = new Rect(10f + Screen.safeArea.x, 10f + (Screen.height - Screen.safeArea.yMax), 260f, 60f);
            GUI.Label(rect, text, _style);
        }
#endif
    }
}
