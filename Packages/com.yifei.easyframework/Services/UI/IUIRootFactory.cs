using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Services.UI
{
    /// <summary>
    /// UIService 使用的根节点句柄。项目可提供自己的工厂，以复用现有 Canvas、URP Overlay Camera
    /// 或特殊安全区结构。
    /// </summary>
    public sealed class UIRootHandle
    {
        public GameObject Root { get; set; }
        public Canvas Canvas { get; set; }
        public IDictionary<UILayer, RectTransform> Layers { get; } =
            new Dictionary<UILayer, RectTransform>();
        public GameObject EventSystemObject { get; set; }

        /// <summary>UIService.Dispose 时是否销毁 Root。</summary>
        public bool OwnsRoot { get; set; } = true;

        /// <summary>UIService.Dispose 时是否销毁本工厂创建的 EventSystem。</summary>
        public bool OwnsEventSystem { get; set; } = true;

        public RectTransform Layer(UILayer layer)
        {
            if (!Layers.TryGetValue(layer, out var root) || root == null)
                throw new InvalidOperationException($"UI root does not provide layer '{layer}'.");
            return root;
        }

        internal void Validate()
        {
            if (Root == null)
                throw new InvalidOperationException("UI root factory returned a handle without Root.");
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
                Layer(layer);
        }
    }

    /// <summary>创建 UI 根节点。捕鱼等项目可替换它来接入自己的相机栈与 Canvas 结构。</summary>
    public interface IUIRootFactory
    {
        UIRootHandle Create();
    }

    /// <summary>使用 UIRootProfile 构建标准 UGUI 根节点。</summary>
    public sealed class DefaultUIRootFactory : IUIRootFactory
    {
        readonly UIRootProfile _profile;

        public DefaultUIRootFactory(UIRootProfile profile)
            => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        public UIRootHandle Create() => UIRootBuilder.Build(_profile);
    }
}
