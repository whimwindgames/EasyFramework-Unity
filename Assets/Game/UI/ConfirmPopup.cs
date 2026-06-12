using EasyFramework.Services.UI;
using UnityEngine;

namespace Game.UI
{
    /// <summary>退出确认弹窗。await ShowPopupAsync&lt;ConfirmPopup, bool&gt; 得到用户选择。</summary>
    public sealed class ConfirmPopup : UIPopup<bool>
    {
        protected override void OnSetup(object args)
        {
            var message = args as string ?? "Quit to menu?";
            var root = (RectTransform)transform;

            // 半透明遮罩 + 对话框
            UiBuild.FullScreen(root, "Dim", new Color(0f, 0f, 0f, 0.6f));
            UiBuild.Label(root, "Msg", message, new Vector2(0f, 120f), 52f);
            UiBuild.Button(root, "YesBtn", "YES", new Vector2(-160f, -80f), () => SetResult(true));
            UiBuild.Button(root, "NoBtn", "NO", new Vector2(160f, -80f), () => SetResult(false));
        }

        // 返回键:等价于"取消"(覆盖基类默认的"拦截不动"),返回即取消退出。
        protected override bool OnBackRequested()
        {
            SetResult(false);
            return true;
        }
    }
}
