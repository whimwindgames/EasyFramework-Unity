using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.UI
{
    /// <summary>所有面板的基类。生命周期由 IUIService 驱动:OnSetup → PlayEnter →(显示)→ PlayExit →(销毁)。</summary>
    public abstract class UIPanel : MonoBehaviour
    {
        /// <summary>面板创建后、入场前调用,接收 Push/Show 传入的参数。默认空实现。</summary>
        protected internal virtual void OnSetup(object args) { }

        /// <summary>入场动画。默认立即完成。子类可用 PrimeTween 等实现淡入/缩放。</summary>
        protected internal virtual UniTask PlayEnter() => UniTask.CompletedTask;

        /// <summary>出场动画。默认立即完成。</summary>
        protected internal virtual UniTask PlayExit() => UniTask.CompletedTask;

        /// <summary>Android/Esc 返回键路由到本面板时调用。返回 true 表示已消费(拦截默认行为)。默认不消费。</summary>
        protected internal virtual bool OnBackRequested() => false;
    }
}
