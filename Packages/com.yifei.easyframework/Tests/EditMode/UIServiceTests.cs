using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using EasyFramework.Services.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public class UIServiceTests
    {
        // ---------------- 测试面板类型 ----------------

        sealed class WindowA : UIPanel
        {
            public object ReceivedArgs;
            public int EnterCount, ExitCount;
            protected internal override void OnSetup(object args) => ReceivedArgs = args;
            protected internal override UniTask PlayEnter() { EnterCount++; return UniTask.CompletedTask; }
            protected internal override UniTask PlayExit() { ExitCount++; return UniTask.CompletedTask; }
        }

        sealed class WindowB : UIPanel { }
        sealed class MissingWindow : UIPanel { }

        // 消费返回键的 Window(返回 true)
        sealed class BackConsumingWindow : UIPanel
        {
            public int BackCount;
            protected internal override bool OnBackRequested() { BackCount++; return true; }
        }

        sealed class ConfirmPopup : UIPopup<bool>
        {
            public void Confirm() => SetResult(true);
            public void Cancel() => SetResult(false);
        }

        sealed class HudPanel : UIPanel { }
        sealed class SecondaryHudPanel : UIPanel { }
        sealed class OverlayPanel : UIPanel { }
        sealed class SecondaryOverlayPanel : UIPanel { }

        // ---------------- 伪 prefab 工厂 ----------------

        readonly List<GameObject> _spawned = new();

        GameObject MakePrefab<T>() where T : UIPanel
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.AddComponent<T>();
            go.SetActive(false); // 模拟真实 prefab 资产:模板本身非激活场景对象(实例化后由 UIService 激活)
            _spawned.Add(go);
            return go;
        }

        FakeAssetService _assets;
        UIService _ui;
        System.Action<GameObject> _originalDestroy;
        System.Action<GameObject> _originalDontDestroy;

        [SetUp]
        public void SetUp()
        {
            _originalDestroy = UIService.DestroyHandler;
            UIService.DestroyHandler = UnityEngine.Object.DestroyImmediate;

            // DontDestroyOnLoad 只在 Play 模式合法;EditMode 下 no-op,UIRoot 仍正常构建。
            _originalDontDestroy = UIRootBuilder.DontDestroyHandler;
            UIRootBuilder.DontDestroyHandler = _ => { };

            var dict = new Dictionary<string, UnityEngine.Object>
            {
                { "ui/WindowA", MakePrefab<WindowA>() },
                { "ui/WindowB", MakePrefab<WindowB>() },
                { "ui/BackConsumingWindow", MakePrefab<BackConsumingWindow>() },
                { "ui/ConfirmPopup", MakePrefab<ConfirmPopup>() },
                { "ui/HudPanel", MakePrefab<HudPanel>() },
                { "ui/SecondaryHudPanel", MakePrefab<SecondaryHudPanel>() },
                { "ui/OverlayPanel", MakePrefab<OverlayPanel>() },
                { "ui/SecondaryOverlayPanel", MakePrefab<SecondaryOverlayPanel>() },
            };
            var wrongPrefab = MakePrefab<WindowA>();
            dict.Add("ui/MissingWindow", wrongPrefab);
            _assets = new FakeAssetService(dict);
            _ui = new UIService(_assets);
        }

        [TearDown]
        public void TearDown()
        {
            _ui.Dispose();
            UIService.DestroyHandler = _originalDestroy;
            UIRootBuilder.DontDestroyHandler = _originalDontDestroy;
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // ---------------- Window 栈 ----------------

        [Test]
        public void Push_IncrementsWindowCount_AndPlaysEnter()
        {
            var a = _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            Assert.AreEqual(1, _ui.WindowCount);
            Assert.AreEqual(1, a.EnterCount);
        }

        [Test]
        public void Push_PassesArgsToOnSetup()
        {
            var args = new object();
            var a = _ui.PushAsync<WindowA>(args).GetAwaiter().GetResult();
            Assert.AreSame(args, a.ReceivedArgs);
        }

        [Test]
        public void Push_HidesPreviousTop()
        {
            var a = _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PushAsync<WindowB>().GetAwaiter().GetResult();
            Assert.IsFalse(a.gameObject.activeSelf, "下层栈顶应被隐藏");
        }

        [Test]
        public void Push_Failure_CompletesCallerAndReactivatesPrevious()
        {
            var previous = _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            Assert.Throws<System.InvalidOperationException>(() =>
                _ui.PushAsync<MissingWindow>().GetAwaiter().GetResult());
            Assert.AreEqual(1, _ui.WindowCount);
            Assert.IsTrue(previous.gameObject.activeSelf);
        }

        [Test]
        public void Pop_RemovesTop_AndReactivatesPrevious()
        {
            var a = _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PushAsync<WindowB>().GetAwaiter().GetResult();
            _ui.PopAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, _ui.WindowCount);
            Assert.IsTrue(a.gameObject.activeSelf, "回退后下层重新激活");
            Assert.AreEqual(2, a.EnterCount, "回退到 a 时再次 PlayEnter");
        }

        [Test]
        public void Pop_OnSingleWindow_DoesNothing()
        {
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PopAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, _ui.WindowCount, "栈底界面不弹出");
        }

        [Test]
        public void PopAll_ClearsStack()
        {
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PushAsync<WindowB>().GetAwaiter().GetResult();
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PopAllAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, _ui.WindowCount);
        }

        // ---------------- Popup 队列 ----------------

        [Test]
        public void Popup_AwaitReturnsResult()
        {
            var task = _ui.ShowPopupAsync<ConfirmPopup, bool>();
            // 同步泵出:popup 已创建并等待 SetResult
            var popup = (ConfirmPopup)FindActivePopup();
            Assert.IsNotNull(popup);
            popup.Confirm();
            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result);
        }

        [Test]
        public void Popup_SecondQueuesUntilFirstResolves()
        {
            var first = _ui.ShowPopupAsync<ConfirmPopup, bool>();
            var second = _ui.ShowPopupAsync<ConfirmPopup, bool>();

            // 第一个活跃,第二个排队中:场景里此刻只有一个 ConfirmPopup 实例
            Assert.AreEqual(1, CountActivePopupInstances(), "第二个 popup 在第一个 SetResult 前不应被实例化/激活");
            Assert.IsFalse(second.Status.IsCompleted(), "第二个尚未完成");

            // 解决第一个 → 第二个出队显示
            ((ConfirmPopup)FindActivePopup()).Confirm();
            first.GetAwaiter().GetResult();

            Assert.AreEqual(1, CountActivePopupInstances(), "现在轮到第二个显示");
            ((ConfirmPopup)FindActivePopup()).Cancel();
            var r2 = second.GetAwaiter().GetResult();
            Assert.IsFalse(r2);
        }

        [Test]
        public void Dispose_CancelsActivePopupCaller()
        {
            var task = _ui.ShowPopupAsync<ConfirmPopup, bool>();
            _ui.Dispose();
            Assert.IsTrue(task.Status.IsCompleted());
            Assert.Throws<System.OperationCanceledException>(() =>
                task.GetAwaiter().GetResult());
        }

        // ---------------- HUD ----------------

        [Test]
        public void ShowHud_ReusesSameType()
        {
            var h1 = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            var h2 = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            Assert.AreSame(h1, h2);
            Assert.AreEqual(1, _ui.HudCount);
        }

        [Test]
        public void ShowHud_AllowsDifferentTypesAtTheSameTime()
        {
            var primary = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            var secondary = _ui.ShowHudAsync<SecondaryHudPanel>().GetAwaiter().GetResult();

            Assert.NotNull(primary);
            Assert.NotNull(secondary);
            Assert.AreEqual(2, _ui.HudCount);
            Assert.IsTrue(primary.gameObject.activeSelf);
            Assert.IsTrue(secondary.gameObject.activeSelf);
        }

        [Test]
        public void HideHudOfType_LeavesOtherHudAlive()
        {
            var primary = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            var secondary = _ui.ShowHudAsync<SecondaryHudPanel>().GetAwaiter().GetResult();
            _ui.HideHudAsync<HudPanel>().GetAwaiter().GetResult();

            Assert.IsTrue(primary == null || primary.gameObject == null);
            Assert.IsNotNull(secondary);
            Assert.AreEqual(1, _ui.HudCount);
        }

        [Test]
        public void HideHud_RemovesAllHuds()
        {
            var primary = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            var secondary = _ui.ShowHudAsync<SecondaryHudPanel>().GetAwaiter().GetResult();
            _ui.HideHudAsync().GetAwaiter().GetResult();
            Assert.IsTrue(primary == null || primary.gameObject == null);
            Assert.IsTrue(secondary == null || secondary.gameObject == null);
            Assert.AreEqual(0, _ui.HudCount);
        }

        // ---------------- Overlay ----------------

        [Test]
        public void ShowOverlay_ReusesSameTypeAndUsesOverlayLayer()
        {
            var first = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            var second = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, _ui.OverlayCount);
            Assert.AreEqual(UILayer.Overlay.ToString(), first.transform.parent.name);
        }

        [Test]
        public void ShowOverlay_DifferentTypesFollowLatestShowOrder()
        {
            var first = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            var second = _ui.ShowOverlayAsync<SecondaryOverlayPanel>().GetAwaiter().GetResult();

            Assert.AreEqual(2, _ui.OverlayCount);
            Assert.AreSame(first.transform.parent, second.transform.parent);
            Assert.Greater(second.transform.GetSiblingIndex(), first.transform.GetSiblingIndex());

            var reusedFirst = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            Assert.AreSame(first, reusedFirst);
            Assert.Greater(first.transform.GetSiblingIndex(), second.transform.GetSiblingIndex());
        }

        [Test]
        public void HideOverlayOfType_LeavesOtherOverlayAlive()
        {
            var primary = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            var secondary = _ui.ShowOverlayAsync<SecondaryOverlayPanel>().GetAwaiter().GetResult();
            _ui.HideOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();

            Assert.IsTrue(primary == null || primary.gameObject == null);
            Assert.IsNotNull(secondary);
            Assert.AreEqual(1, _ui.OverlayCount);
        }

        [Test]
        public void HideOverlay_RemovesAllOverlays()
        {
            var primary = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            var secondary = _ui.ShowOverlayAsync<SecondaryOverlayPanel>().GetAwaiter().GetResult();
            _ui.HideOverlayAsync().GetAwaiter().GetResult();

            Assert.IsTrue(primary == null || primary.gameObject == null);
            Assert.IsTrue(secondary == null || secondary.gameObject == null);
            Assert.AreEqual(0, _ui.OverlayCount);
        }

        [Test]
        public void Dispose_DestroysOverlays()
        {
            var overlay = _ui.ShowOverlayAsync<OverlayPanel>().GetAwaiter().GetResult();
            _ui.Dispose();

            Assert.IsTrue(overlay == null || overlay.gameObject == null);
            Assert.AreEqual(0, _ui.OverlayCount);
        }

        [Test]
        public void LandscapeProfile_ConfiguresReferenceResolutionAndLayerSorting()
        {
            var profile = UIRootProfile.Landscape();
            profile.ApplySafeArea = false;
            profile.EnsureEventSystem = false;
            profile.DontDestroyOnLoad = false;
            profile.UseIndependentLayerSorting = true;

            var handle = new DefaultUIRootFactory(profile).Create();
            try
            {
                var scaler = handle.Root.GetComponent<CanvasScaler>();
                Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
                Assert.AreEqual(1f, scaler.matchWidthOrHeight);
                Assert.AreEqual(0, handle.Layer(UILayer.Hud).GetComponent<Canvas>().sortingOrder);
                Assert.AreEqual(300, handle.Layer(UILayer.Overlay).GetComponent<Canvas>().sortingOrder);
                Assert.IsNull(handle.Layer(UILayer.Hud).GetComponent<SafeAreaFitter>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(handle.Root);
            }
        }

        [Test]
        public void ScreenSpaceCameraProfile_BindsConfiguredCamera()
        {
            var cameraObject = new GameObject("UI Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = UIRootProfile.Landscape();
            profile.RenderMode = RenderMode.ScreenSpaceCamera;
            profile.WorldCamera = camera;
            profile.EnsureEventSystem = false;
            profile.DontDestroyOnLoad = false;

            var handle = new DefaultUIRootFactory(profile).Create();
            try
            {
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, handle.Canvas.renderMode);
                Assert.AreSame(camera, handle.Canvas.worldCamera);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(handle.Root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void WorldSpaceProfile_AppliesReferenceSizeScaleAndCamera()
        {
            var cameraObject = new GameObject("World UI Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = UIRootProfile.Landscape();
            profile.RenderMode = RenderMode.WorldSpace;
            profile.WorldCamera = camera;
            profile.WorldSpaceScale = 0.002f;
            profile.ApplySafeArea = false;
            profile.EnsureEventSystem = false;
            profile.DontDestroyOnLoad = false;

            var handle = new DefaultUIRootFactory(profile).Create();
            try
            {
                var rootRect = (RectTransform)handle.Root.transform;
                Assert.AreEqual(RenderMode.WorldSpace, handle.Canvas.renderMode);
                Assert.AreSame(camera, handle.Canvas.worldCamera);
                Assert.AreEqual(new Vector2(1920f, 1080f), rootRect.sizeDelta);
                Assert.AreEqual(Vector3.one * 0.002f, rootRect.localScale);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(handle.Root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        // ---------------- 返回键 ----------------

        [Test]
        public void HandleBack_WithActivePopup_PopupConsumes()
        {
            _ui.ShowPopupAsync<ConfirmPopup, bool>();
            // ConfirmPopup.OnBackRequested 默认 true(拦截)
            Assert.IsTrue(_ui.HandleBack());
            // 收尾:解决 popup 以免 TearDown 残留
            ((ConfirmPopup)FindActivePopup()).Cancel();
        }

        [Test]
        public void HandleBack_WindowConsumes_StopsHere()
        {
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            var w = _ui.PushAsync<BackConsumingWindow>().GetAwaiter().GetResult();
            Assert.IsTrue(_ui.HandleBack());
            Assert.AreEqual(1, w.BackCount);
            Assert.AreEqual(2, _ui.WindowCount, "Window 消费了返回键,不应回退");
        }

        [Test]
        public void HandleBack_Unconsumed_DefaultPops()
        {
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            _ui.PushAsync<WindowB>().GetAwaiter().GetResult();
            // WindowB 未覆盖 OnBackRequested(默认 false)→ 默认 Pop
            Assert.IsTrue(_ui.HandleBack());
            Assert.AreEqual(1, _ui.WindowCount, "默认回退一层(同步完成的面板,Forget 延续即时跑完)");
        }

        [Test]
        public void HandleBack_SingleWindow_NotConsumed()
        {
            _ui.PushAsync<WindowA>().GetAwaiter().GetResult();
            // 栈深 1 且 WindowA 不消费 → 返回 false(无可回退)
            Assert.IsFalse(_ui.HandleBack());
            Assert.AreEqual(1, _ui.WindowCount);
        }

        // ---------------- 测试辅助:从 Popup 层根找活跃 popup ----------------

        static UIPanel FindActivePopup()
        {
#if UNITY_2023_1_OR_NEWER
            var popups = UnityEngine.Object.FindObjectsByType<ConfirmPopup>(FindObjectsSortMode.None);
#else
            var popups = UnityEngine.Object.FindObjectsOfType<ConfirmPopup>();
#endif
            foreach (var p in popups)
                if (p != null && p.gameObject.activeInHierarchy) return p;
            return popups.Length > 0 ? popups[0] : null;
        }

        static int CountActivePopupInstances()
        {
#if UNITY_2023_1_OR_NEWER
            var popups = UnityEngine.Object.FindObjectsByType<ConfirmPopup>(FindObjectsSortMode.None);
#else
            var popups = UnityEngine.Object.FindObjectsOfType<ConfirmPopup>();
#endif
            // 只数活跃实例:伪 prefab 模板本身也是场景中的 ConfirmPopup,需排除(它非激活实例)。
            var count = 0;
            foreach (var p in popups)
                if (p != null && p.gameObject.activeInHierarchy) count++;
            return count;
        }
    }
}
