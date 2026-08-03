using System;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Services.UI
{
    /// <summary>
    /// UI 根节点的布局与渲染配置。它描述 Canvas 如何适配屏幕，不限制游戏必须使用横屏或竖屏。
    /// </summary>
    [Serializable]
    public sealed class UIRootProfile
    {
        [Tooltip("UI 设计基准分辨率。竖屏常用 1080×1920，横屏常用 1920×1080。")]
        public Vector2 ReferenceResolution = new(1080f, 1920f);

        [Tooltip("0 以宽度为缩放基准，1 以高度为缩放基准。")]
        [Range(0f, 1f)]
        public float MatchWidthOrHeight = 0.5f;

        public CanvasScaler.ScaleMode ScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        public CanvasScaler.ScreenMatchMode ScreenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        public float ReferencePixelsPerUnit = 100f;

        [Header("Canvas")]
        public RenderMode RenderMode = RenderMode.ScreenSpaceOverlay;
        [Tooltip("ScreenSpaceCamera/WorldSpace 使用的相机。为空时可回退到 Camera.main。")]
        public Camera WorldCamera;
        public bool UseMainCameraWhenWorldCameraMissing = true;
        public float PlaneDistance = 100f;
        [Tooltip("WorldSpace 模式下根 Canvas 的世界缩放。0.001 表示 1920 像素对应 1.92 世界单位。")]
        public float WorldSpaceScale = 0.001f;
        public string SortingLayerName = "Default";
        public int SortingOrder;

        [Header("Layers")]
        [Tooltip("为 Hud/Window/Popup/Overlay 创建独立排序 Canvas。")]
        public bool UseIndependentLayerSorting;
        public int LayerSortingStep = 100;

        [Header("Runtime")]
        public bool ApplySafeArea = true;
        public bool EnsureEventSystem = true;
        public bool DisableNavigationEvents;
        public bool DontDestroyOnLoad = true;

        /// <summary>向后兼容的竖屏配置。</summary>
        public static UIRootProfile Portrait(
            float width = 1080f, float height = 1920f,
            float matchWidthOrHeight = 0.5f)
            => Create(width, height, matchWidthOrHeight);

        /// <summary>横屏配置。默认以高度为缩放基准，适合宽高比不同的横屏设备。</summary>
        public static UIRootProfile Landscape(
            float width = 1920f, float height = 1080f,
            float matchWidthOrHeight = 1f)
            => Create(width, height, matchWidthOrHeight);

        public static UIRootProfile Create(
            float width, float height, float matchWidthOrHeight = 0.5f)
        {
            if (width <= 0f || height <= 0f)
                throw new ArgumentOutOfRangeException(nameof(width),
                    "Reference resolution must be positive.");
            return new UIRootProfile
            {
                ReferenceResolution = new Vector2(width, height),
                MatchWidthOrHeight = Mathf.Clamp01(matchWidthOrHeight),
            };
        }

        internal void Validate()
        {
            if (ReferenceResolution.x <= 0f || ReferenceResolution.y <= 0f)
                throw new InvalidOperationException("UI reference resolution must be positive.");
            if (ReferencePixelsPerUnit <= 0f)
                throw new InvalidOperationException("UI reference pixels per unit must be positive.");
            if (PlaneDistance <= 0f && RenderMode == RenderMode.ScreenSpaceCamera)
                throw new InvalidOperationException("UI plane distance must be positive in ScreenSpaceCamera mode.");
            if (WorldSpaceScale <= 0f && RenderMode == RenderMode.WorldSpace)
                throw new InvalidOperationException("UI world-space scale must be positive in WorldSpace mode.");
            if (LayerSortingStep < 0)
                throw new InvalidOperationException("UI layer sorting step cannot be negative.");
        }
    }
}
