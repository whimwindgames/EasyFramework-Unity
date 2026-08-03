using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EasyFramework.Services.UI
{
    /// <summary>按配置构建 UIRoot(Canvas + 四层根 + 按需 EventSystem)。</summary>
    internal static class UIRootBuilder
    {
        /// <summary>EditMode 测试可替换为 no-op;运行时为 Object.DontDestroyOnLoad
        /// (DontDestroyOnLoad 仅在 Play 模式合法,EditMode 调用会抛异常)。</summary>
        internal static System.Action<GameObject> DontDestroyHandler = Object.DontDestroyOnLoad;

        public static UIRootHandle Build(UIRootProfile profile)
        {
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            profile.Validate();

            Camera worldCamera = null;
            if (profile.RenderMode != RenderMode.ScreenSpaceOverlay)
            {
                worldCamera = profile.WorldCamera != null
                    ? profile.WorldCamera
                    : profile.UseMainCameraWhenWorldCameraMissing ? Camera.main : null;
                if (profile.RenderMode == RenderMode.ScreenSpaceCamera && worldCamera == null)
                    throw new System.InvalidOperationException(
                        "ScreenSpaceCamera UI requires UIRootProfile.WorldCamera, Camera.main, " +
                        "or a custom IUIRootFactory.");
            }

            var handle = new UIRootHandle();

            var root = new GameObject("[UIRoot]");
            if (profile.DontDestroyOnLoad)
                DontDestroyHandler(root);
            handle.Root = root;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = profile.RenderMode;
            canvas.planeDistance = profile.PlaneDistance;
            canvas.sortingLayerName = profile.SortingLayerName;
            canvas.sortingOrder = profile.SortingOrder;
            if (profile.RenderMode != RenderMode.ScreenSpaceOverlay)
                canvas.worldCamera = worldCamera;
            handle.Canvas = canvas;

            if (profile.RenderMode == RenderMode.WorldSpace)
            {
                var rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta = profile.ReferenceResolution;
                rootRect.localScale = Vector3.one * profile.WorldSpaceScale;
            }

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = profile.ScaleMode;
            scaler.referenceResolution = profile.ReferenceResolution;
            scaler.screenMatchMode = profile.ScreenMatchMode;
            scaler.matchWidthOrHeight = Mathf.Clamp01(profile.MatchWidthOrHeight);
            scaler.referencePixelsPerUnit = profile.ReferencePixelsPerUnit;

            root.AddComponent<GraphicRaycaster>();

            // 四层根:sibling 顺序 = 绘制叠放顺序(后者在上)
            // Hud(0) < Window(1) < Popup(2) < Overlay(3)
            foreach (var layer in new[] { UILayer.Hud, UILayer.Window, UILayer.Popup, UILayer.Overlay })
            {
                var layerGo = new GameObject(layer.ToString(), typeof(RectTransform));
                var rt = (RectTransform)layerGo.transform;
                rt.SetParent(root.transform, false);
                Stretch(rt);
                rt.SetAsLastSibling(); // 保持创建顺序即叠放顺序
                if (profile.ApplySafeArea)
                    layerGo.AddComponent<SafeAreaFitter>();
                if (profile.UseIndependentLayerSorting)
                {
                    var layerCanvas = layerGo.AddComponent<Canvas>();
                    layerCanvas.overrideSorting = true;
                    layerCanvas.sortingLayerName = profile.SortingLayerName;
                    layerCanvas.sortingOrder = profile.SortingOrder + (int)layer * profile.LayerSortingStep;
                    layerGo.AddComponent<GraphicRaycaster>();
                }
                handle.Layers[layer] = rt;
            }

            if (profile.EnsureEventSystem)
                EnsureEventSystem(handle, profile.DisableNavigationEvents);
            handle.Validate();
            return handle;
        }

        static void EnsureEventSystem(UIRootHandle handle, bool disableNavigationEvents)
        {
            if (EventSystem.current != null)
            {
                if (disableNavigationEvents)
                    EventSystem.current.sendNavigationEvents = false;
                return;
            }
#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<EventSystem>();
#else
            var existing = Object.FindObjectOfType<EventSystem>();
#endif
            if (existing != null)
            {
                if (disableNavigationEvents)
                    existing.sendNavigationEvents = false;
                return;
            }

            var es = new GameObject("[EventSystem]");
            var eventSystem = es.AddComponent<EventSystem>();
            eventSystem.sendNavigationEvents = !disableNavigationEvents;
            es.AddComponent<InputSystemUIInputModule>();
            if (handle.Root != null && handle.Root.scene.IsValid())
                es.transform.SetParent(handle.Root.transform, false);
            handle.EventSystemObject = es;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
    }
}
