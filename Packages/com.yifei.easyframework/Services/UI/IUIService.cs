using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.UI
{
    /// <summary>
    /// UI 服务门面。三种语义:
    /// Window = 栈(Push 盖在上面,Pop 回退);Popup = 队列(同时只显示一个,带返回值);Hud = 单实例常驻。
    /// </summary>
    public interface IUIService
    {
        /// <summary>压入一个 Window(全屏栈)。完成后返回面板实例。args 透传 OnSetup。</summary>
        UniTask<T> PushAsync<T>(object args = null, CancellationToken ct = default) where T : UIPanel;

        /// <summary>弹出栈顶 Window,回退到下一个;栈空或仅一个时行为见实现(默认仅 1 个不弹)。</summary>
        UniTask PopAsync(CancellationToken ct = default);

        /// <summary>清空 Window 栈。</summary>
        UniTask PopAllAsync(CancellationToken ct = default);

        /// <summary>显示一个带返回值的弹窗;await 得到用户选择结果。多个并发请求排队,逐个展示。</summary>
        UniTask<TResult> ShowPopupAsync<TPopup, TResult>(
            object args = null, CancellationToken ct = default) where TPopup : UIPopup<TResult>;

        /// <summary>显示 HUD(单实例);已有 HUD 时先隐藏旧的。</summary>
        UniTask<T> ShowHudAsync<T>(
            object args = null, CancellationToken ct = default) where T : UIPanel;

        /// <summary>隐藏当前 HUD。</summary>
        UniTask HideHudAsync(CancellationToken ct = default);

        /// <summary>当前 Window 栈深。</summary>
        int WindowCount { get; }
    }
}
