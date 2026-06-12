using UnityEngine;

namespace EasyFramework.Services.UI
{
    /// <summary>把挂载对象的 RectTransform 锚点收进 Screen.safeArea(刘海屏适配)。挂在四个层根上。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void Awake() => _rect = GetComponent<RectTransform>();

        void OnEnable() => Apply();

        void Update()
        {
            // safeArea 或分辨率变化(旋转)时重算
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
            {
                Apply();
            }
        }

        void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();

            var safe = Screen.safeArea;
            var w = Screen.width;
            var h = Screen.height;
            if (w <= 0 || h <= 0) return;

            var anchorMin = safe.position;
            var anchorMax = safe.position + safe.size;
            anchorMin.x /= w; anchorMin.y /= h;
            anchorMax.x /= w; anchorMax.y /= h;

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(w, h);
        }
    }
}
