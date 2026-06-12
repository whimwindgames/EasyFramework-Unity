using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace EasyFramework.Services.UI
{
    public sealed class UIService : IUIService, ITickable, IDisposable
    {
        /// <summary>EditMode 测试可替换为 Object.DestroyImmediate;运行时为 Object.Destroy。</summary>
        internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

        readonly IAssetService _assets;

        UIRootHandle _root;                              // 懒加载
        readonly List<UIPanel> _windowStack = new();     // 栈底=[0],栈顶=末尾
        UIPanel _hud;                                    // 单实例

        // Popup 队列:每项是"显示一个 popup 并完成其请求"的待执行闭包
        readonly Queue<Func<UniTask>> _popupQueue = new();
        UIPanel _activePopup;
        bool _pumpingPopups;

        // Window 操作串行队列:防止 PushAsync/PopAsync 并发交错
        readonly Queue<Func<UniTask>> _windowQueue = new();
        bool _pumpingWindows;

        public int WindowCount => _windowStack.Count;

        public UIService(IAssetService assets) => _assets = assets;

        // ---------------- UIRoot 懒加载 ----------------

        UIRootHandle EnsureRoot() => _root ??= UIRootBuilder.Build();

        async UniTask<T> InstantiatePanelAsync<T>(UILayer layer, object args) where T : UIPanel
        {
            var root = EnsureRoot();
            var key = "ui/" + typeof(T).Name;
            var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Global);
            var go = UnityEngine.Object.Instantiate(prefab, root.Layer(layer), false);
            go.SetActive(true); // 模板 prefab 可能为非激活资产;实例化后确保面板可见/激活
            var panel = go.GetComponent<T>();
            if (panel == null)
                throw new InvalidOperationException(
                    $"UI prefab '{key}' has no component of type {typeof(T).Name}.");
            panel.OnSetup(args);
            return panel;
        }

        // ---------------- Window 串行泵 ----------------

        /// <summary>
        /// 入队一个 Window 操作闭包并驱动串行泵。
        /// 同一时刻只有一个 Push/Pop 闭包在执行;后续入队的在前一个完成后依次执行。
        /// </summary>
        UniTask EnqueueWindowOp(Func<UniTask> op)
        {
            var tcs = new UniTaskCompletionSource();
            _windowQueue.Enqueue(async () =>
            {
                try { await op(); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
            PumpWindows().Forget();
            return tcs.Task;
        }

        async UniTask PumpWindows()
        {
            if (_pumpingWindows) return;
            _pumpingWindows = true;
            try
            {
                while (_windowQueue.Count > 0)
                {
                    var next = _windowQueue.Dequeue();
                    await next();
                }
            }
            finally
            {
                _pumpingWindows = false;
            }
        }

        // ---------------- Window 栈 ----------------

        public UniTask<T> PushAsync<T>(object args = null) where T : UIPanel
        {
            var tcs = new UniTaskCompletionSource<T>();
            EnqueueWindowOp(async () =>
            {
                // 下层栈顶隐藏(全屏遮挡,省 overdraw + 避免误接收输入)
                if (_windowStack.Count > 0)
                    _windowStack[^1].gameObject.SetActive(false);

                var panel = await InstantiatePanelAsync<T>(UILayer.Window, args);
                _windowStack.Add(panel);
                await panel.PlayEnter();
                tcs.TrySetResult(panel);
            }).Forget();
            return tcs.Task;
        }

        public UniTask PopAsync()
        {
            return EnqueueWindowOp(async () =>
            {
                if (_windowStack.Count <= 1) return; // 栈底界面不弹出(避免空 Window 层)

                var top = _windowStack[^1];
                _windowStack.RemoveAt(_windowStack.Count - 1);
                await top.PlayExit();
                DestroyHandler(top.gameObject);

                var newTop = _windowStack[^1];
                newTop.gameObject.SetActive(true);
                await newTop.PlayEnter();
            });
        }

        public async UniTask PopAllAsync()
        {
            while (_windowStack.Count > 0)
            {
                var top = _windowStack[^1];
                _windowStack.RemoveAt(_windowStack.Count - 1);
                await top.PlayExit();
                DestroyHandler(top.gameObject);
            }
        }

        // ---------------- Popup 队列 ----------------

        public UniTask<TResult> ShowPopupAsync<TPopup, TResult>(object args = null)
            where TPopup : UIPopup<TResult>
        {
            var tcs = new UniTaskCompletionSource<TResult>();

            // 入队一个"显示该 popup 并把结果回传给 tcs"的闭包;排队中不 Instantiate/不激活
            _popupQueue.Enqueue(async () =>
            {
                var popup = await InstantiatePanelAsync<TPopup>(UILayer.Popup, args);
                _activePopup = popup;
                await popup.PlayEnter();

                var result = await popup.Result; // 等用户 SetResult
                tcs.TrySetResult(result);

                await popup.PlayExit();
                DestroyHandler(popup.gameObject);
                _activePopup = null;
            });

            PumpPopups().Forget();
            return tcs.Task;
        }

        async UniTask PumpPopups()
        {
            if (_pumpingPopups) return; // 同一时刻只有一个 popup 在显示
            _pumpingPopups = true;
            try
            {
                while (_popupQueue.Count > 0)
                {
                    var next = _popupQueue.Dequeue();
                    await next();
                }
            }
            finally
            {
                _pumpingPopups = false;
            }
        }

        // ---------------- HUD 单实例 ----------------

        public async UniTask<T> ShowHudAsync<T>(object args = null) where T : UIPanel
        {
            if (_hud != null) await HideHudAsync();
            var panel = await InstantiatePanelAsync<T>(UILayer.Hud, args);
            _hud = panel;
            await panel.PlayEnter();
            return panel;
        }

        public async UniTask HideHudAsync()
        {
            if (_hud == null) return;
            var hud = _hud;
            _hud = null;
            await hud.PlayExit();
            DestroyHandler(hud.gameObject);
        }

        // ---------------- 返回键 ----------------

        public void Tick()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                HandleBack();
        }

        /// <summary>返回键路由逻辑(不读真实按键,供单测)。返回 true 表示已消费。</summary>
        internal bool HandleBack()
        {
            // 1) 活跃 Popup 优先
            if (_activePopup != null)
                return _activePopup.OnBackRequested(); // UIPopup 默认 true(拦截)

            // 2) 栈顶 Window
            if (_windowStack.Count > 0)
            {
                var top = _windowStack[^1];
                if (top.OnBackRequested()) return true;

                // 3) 未消费 → 默认回退(栈深 > 1);若 Window 操作正在进行中则忽略本次按键,
                //    避免动画进行中连续按 Esc 交错弹出多层。
                if (_windowStack.Count > 1)
                {
                    if (_pumpingWindows) return true; // 消费按键但不再入队新 Pop
                    PopAsync().Forget();
                    return true;
                }
            }
            return false;
        }

        // ---------------- 清理 ----------------

        public void Dispose()
        {
            PopAllAsync().Forget();
            if (_hud != null) DestroyHandler(_hud.gameObject);
            if (_activePopup != null) DestroyHandler(_activePopup.gameObject);
            _popupQueue.Clear();
            if (_root != null)
            {
                if (_root.EventSystemObject != null) DestroyHandler(_root.EventSystemObject);
                DestroyHandler(_root.Root);
                _root = null;
            }
        }
    }
}
