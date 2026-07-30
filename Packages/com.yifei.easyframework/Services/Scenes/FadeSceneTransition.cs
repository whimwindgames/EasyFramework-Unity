using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Services.Scenes
{
    /// <summary>淡入淡出转场:在一个独立 DontDestroyOnLoad 的全屏黑幕 CanvasGroup 上用 PrimeTween 补间 alpha。视觉薄层,不单测。</summary>
    public sealed class FadeSceneTransition : ISceneTransition, IDisposable
    {
        readonly float _duration;
        CanvasGroup _group; // 懒加载

        public FadeSceneTransition(float duration = 0.25f)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            _duration = duration;
        }

        CanvasGroup EnsureGroup()
        {
            if (_group != null) return _group;

            var go = new GameObject("[SceneFade]");
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // 盖在所有 UI 之上(含 UIRoot Overlay 层)

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // 全屏黑色 Image
            var imgGo = new GameObject("Black", typeof(RectTransform));
            imgGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = imgGo.AddComponent<Image>();
            img.color = Color.black;

            return _group = group;
        }

        public async UniTask PlayOut(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var group = EnsureGroup();
            group.blocksRaycasts = true;
            await Tween.Alpha(group, endValue: 1f, duration: _duration).ToUniTask();
            ct.ThrowIfCancellationRequested();
        }

        public async UniTask PlayIn(CancellationToken ct = default)
        {
            var group = EnsureGroup();
            try
            {
                await Tween.Alpha(group, endValue: 0f, duration: _duration).ToUniTask();
                ct.ThrowIfCancellationRequested();
            }
            finally
            {
                if (group != null)
                {
                    group.alpha = 0f;
                    group.blocksRaycasts = false;
                }
            }
        }

        public void Dispose()
        {
            if (_group == null) return;
            Tween.StopAll(_group);
            var root = _group.gameObject;
            _group = null;
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }
    }
}
