using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EasyFramework.Services.UI
{
    /// <summary>持有 UIRoot 各层根 Transform 的句柄。</summary>
    internal sealed class UIRootHandle
    {
        public GameObject Root;
        public Canvas Canvas;
        public readonly Dictionary<UILayer, RectTransform> Layers = new();
        public GameObject EventSystemObject; // 仅当本服务补建时非空

        public RectTransform Layer(UILayer layer) => Layers[layer];
    }

    /// <summary>程序化构建 DontDestroyOnLoad 的 UIRoot(Canvas + 四层根 + 按需 EventSystem)。</summary>
    internal static class UIRootBuilder
    {
        /// <summary>EditMode 测试可替换为 no-op;运行时为 Object.DontDestroyOnLoad
        /// (DontDestroyOnLoad 仅在 Play 模式合法,EditMode 调用会抛异常)。</summary>
        internal static System.Action<GameObject> DontDestroyHandler = Object.DontDestroyOnLoad;

        public static UIRootHandle Build()
        {
            var handle = new UIRootHandle();

            var root = new GameObject("[UIRoot]");
            DontDestroyHandler(root);
            handle.Root = root;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            handle.Canvas = canvas;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

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
                layerGo.AddComponent<SafeAreaFitter>();
                handle.Layers[layer] = rt;
            }

            EnsureEventSystem(handle);
            return handle;
        }

        static void EnsureEventSystem(UIRootHandle handle)
        {
            if (EventSystem.current != null) return;
#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<EventSystem>();
#else
            var existing = Object.FindObjectOfType<EventSystem>();
#endif
            if (existing != null) return;

            var es = new GameObject("[EventSystem]");
            DontDestroyHandler(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
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
