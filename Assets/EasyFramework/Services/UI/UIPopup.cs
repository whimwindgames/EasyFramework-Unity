using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.UI
{
    /// <summary>带返回值的弹窗基类。await ShowPopupAsync 得到的 UniTask 在 SetResult 后完成。</summary>
    public abstract class UIPopup<TResult> : UIPanel
    {
        readonly UniTaskCompletionSource<TResult> _tcs = new();

        /// <summary>弹窗结果。SetResult 调用后完成。</summary>
        public UniTask<TResult> Result => _tcs.Task;

        /// <summary>子类在用户操作(确认/取消等)时调用,设置结果并触发关闭。重复调用安全(仅首次生效)。</summary>
        protected void SetResult(TResult result) => _tcs.TrySetResult(result);

        /// <summary>Popup 默认拦截返回键(等待用户显式选择)。子类可覆盖为 SetResult(默认值) 实现"返回即取消"。</summary>
        protected internal override bool OnBackRequested() => true;
    }
}
