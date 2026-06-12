# EasyFramework Phase 3a(UI 框架)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase 1/2 之上实现设计文档 §4.5 的 UI 框架:四层 UIRoot(HUD < Window < Popup < Overlay)、`UIPanel`/`UIPopup<TResult>` 面板基类、`IUIService`(Window 栈 / Popup 队列 / HUD 单实例)、Android/Esc 返回键路由、安全区适配 `SafeAreaFitter`、以及替换 Phase 2 `NoopSceneTransition` 的 `FadeSceneTransition` 淡入淡出转场。面板逻辑全带 EditMode 单测(用 Phase 2 的 `FakeAssetService` 注入伪 prefab);转场为视觉薄层,不单测。最后接线进 `FrameworkInstaller` / `G` 门面。

**Architecture:** 沿用 Phase 1/2 的「VContainer DI 内核 + 静态门面 `G`」。新代码落在 `EasyFramework.Services` 程序集,命名空间 `EasyFramework.Services.UI`,目录与命名空间一一对应。服务先接口后实现(`IUIService` → `UIService`)。**UIService 必须满足 Phase 2 的纯容器约束**:UIRoot(Canvas/层根/EventSystem)程序化构建为**懒加载**,绝不在构造函数里 `new GameObject` / 访问 `Screen`,因此 `FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)` 仍可在纯 `ContainerBuilder` 下 Build。返回键检测走 VContainer 的 `ITickable`,判定逻辑抽 `internal` 方法,不依赖真实按键即可单测。面板 prefab 按约定 `"ui/" + typeof(T).Name` 经 `IAssetService.LoadAsync<GameObject>(...)` 加载,无注册表(约定优于配置)。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / Addressables / 新 Input System(`UnityEngine.InputSystem`)/ TextMeshPro(UI 默认依赖)/ PrimeTween(转场动画,由 Phase 3b Task 1 统一安装)/ Unity Test Framework 1.6

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包 `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`)。
- **并行实现代理只允许用 Write/Edit 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(`FakeAssetService.LoadAsync` 同步返回;面板默认 `PlayEnter`/`PlayExit` 立即完成的 `UniTask`),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。EditMode 下 `Object.Destroy` / `Object.Instantiate` 生效时机问题:沿用 Phase 2 `PoolService` 的 `DestroyHandler` 模式——`UIService` 暴露 `internal static Action<GameObject> DestroyHandler`(默认 `Object.Destroy`),测试在 `[SetUp]` 替换为 `Object.DestroyImmediate`、`[TearDown]` 还原。

**⚠️ 依赖关系与前置要求(必读):**

1. **不改 asmdef、不装包。** 本计划**不修改任何 `.asmdef`**。`UIService` 用到的新引用——`UnityEngine.UI`(Canvas/CanvasScaler/GraphicRaycaster)、`UnityEngine.InputSystem`(`InputSystemUIInputModule`/`Keyboard`)、`Unity.TextMeshPro`、`PrimeTween`——由 **Phase 3b Task 1** 统一安装包并升级 `EasyFramework.Services.asmdef` / `EasyFramework.Tests.EditMode.asmdef` 的 references。**本计划执行前,Phase 3b Task 1 必须先完成 asmdef 升级**(或由编排者保证 references 已含上述程序集);否则本计划的实现文件无法编译。`UnityEngine.UI`/`UnityEngine.InputSystem`/`Unity.TextMeshPro` 随 Engine/包提供,`PrimeTween` 仅 `FadeSceneTransition`(Task 6)一个文件用到。
   - **临时降级路径(若 Phase 3b 尚未就绪而需先验证 Task 1-5、7):** `FadeSceneTransition`(Task 6)是唯一引用 `PrimeTween` 的文件;若 PrimeTween 包尚未安装,实现代理可暂缓落地 Task 6 单文件,Task 1-5、7 不依赖 PrimeTween,可独立编译验证。Task 6 落地后再统一重验。**这是执行顺序协调,不是占位符**——`FadeSceneTransition` 代码在本计划中完整给出。

2. **接线 Task 改 Phase 1/2 落地文件。** Task 7 要修改 `Boot/G.cs`、`Boot/FrameworkInstaller.cs`。**Phase 1/2 正由其他代理实现中。执行 Task 7 前,实现代理必须先 `Read` 这两个文件的实际落地内容核对**(`G.Initialize`/`Reset` 的具体写法、`FrameworkInstaller.Install` 的签名与各服务注册块、`NoopSceneTransition` 的注册行)。**本计划给出「修改后的完整文件内容」,但若实际文件与 Phase 1/2 计划有偏差,以实际文件为基线做等效增量合并,不要机械覆盖。** 具体三处增量:(a) `G` 加 `UI` 属性;(b) `FrameworkInstaller` 注册 `IUIService`;(c) `FrameworkInstaller` 把 Scene 块的 `ISceneTransition` 注册从 `NoopSceneTransition` 换成 `FadeSceneTransition`。

3. **锁定契约不可改。** 下列公共签名为锁定契约(命名空间 `EasyFramework.Services.UI`),实现细节可展开但**签名不可变**:
   - `enum UILayer { Hud, Window, Popup, Overlay }`
   - `UIPanel`:`protected internal virtual void OnSetup(object args)`、`protected internal virtual UniTask PlayEnter()`、`protected internal virtual UniTask PlayExit()`、`protected internal virtual bool OnBackRequested()`
   - `UIPopup<TResult> : UIPanel`:`UniTask<TResult> Result { get; }`、`protected void SetResult(TResult result)`
   - `IUIService`:`PushAsync<T>(object args = null) where T : UIPanel`、`PopAsync()`、`PopAllAsync()`、`ShowPopupAsync<TPopup, TResult>(object args = null) where TPopup : UIPopup<TResult>`、`ShowHudAsync<T>(object args = null) where T : UIPanel`、`HideHudAsync()`、`int WindowCount { get; }`

---

## 关键设计决策(展开自任务书)

执行前请通读,避免实现偏离:

- **prefab 加载约定:** 面板 prefab 经 `IAssetService.LoadAsync<GameObject>("ui/" + typeof(T).Name, AssetScope.Global)` 加载。约定优于配置:面板类型名即 key(如 `SettingsWindow` → `"ui/SettingsWindow"`),无注册表。用 `AssetScope.Global` 是因为 UI 面板跨场景常驻可复用,不随场景卸载被释放;面板实例由 UIService 自己 `Instantiate` 并管理生命周期(关闭即 Destroy)。
- **UIRoot 懒加载结构:** 服务首次需要某层根时调 `EnsureRoot()` 程序化构建,**绝不在构造函数里建**:
  - 一个 `DontDestroyOnLoad` 的根 GameObject `[UIRoot]`,挂 `Canvas`(`renderMode = ScreenSpaceOverlay`)+ `CanvasScaler`(`ScaleWithScreenSize`, `referenceResolution = (1080,1920)`, `matchWidthOrHeight = 0.5`)+ `GraphicRaycaster`。
  - 四个层根 GameObject:`Hud`、`Window`、`Popup`、`Overlay`,作为 `[UIRoot]` 子节点,**按 sibling 顺序 Hud(0) < Window(1) < Popup(2) < Overlay(3)** 决定绘制叠放(后者在上)。每层根挂 `SafeAreaFitter`。
  - 若场景中无 `EventSystem`,自动补建一个挂 `EventSystem` + `InputSystemUIInputModule`(新 Input System)的 GameObject,同样 `DontDestroyOnLoad`。
- **Window = 栈语义:** `PushAsync<T>` 把新 Window 压栈、Instantiate 到 Window 层、`OnSetup(args)` → `PlayEnter()`。**默认决策:Push 时把下层栈顶 Window `SetActive(false)`**(全屏界面遮挡下层,省 overdraw 且避免下层误接收输入),`PopAsync` 弹出栈顶并 Destroy、把新栈顶 `SetActive(true)` 再 `PlayEnter()`(重新入场)。`PopAllAsync` 逐个弹空。`WindowCount` = 栈深。
- **Popup = 队列语义:** 同一时刻只显示一个 Popup。`ShowPopupAsync<TPopup,TResult>` 把请求入 `Queue`;若当前无活跃 Popup 则立即显示,否则排队(**排队中的 Popup 不 Instantiate、不激活**,只持有一个待执行的工厂闭包),返回的 `UniTask<TResult>` 在该 Popup 最终 `SetResult` 后完成。当前 Popup `SetResult` → `PlayExit` → Destroy → 从队列取下一个显示。
- **HUD = 单实例:** `ShowHudAsync<T>` 同时只有一个 HUD;再次 Show 先 `HideHudAsync()`(Destroy 旧的)再建新的。`HideHudAsync` 移除当前 HUD。
- **返回键路由(`ITickable.Tick`):** 每帧检测 `Keyboard.current.escapeKey.wasPressedThisFrame`(新 Input System;Android 返回键在新 Input System 下映射为 `escapeKey`)。检测到则调 `internal bool HandleBack()`,优先级:**活跃 Popup(若它 `OnBackRequested()` 未消费,默认不做任何事——Popup 需用户显式选择,不被返回键随意关闭,返回 true 表示已拦截)→ 栈顶 Window 的 `OnBackRequested()` → 未消费则默认 `PopAsync()`(栈深 > 1 时)**。`HandleBack` 不读真实按键,纯逻辑,可单测;`Tick` 只负责把按键事件转交 `HandleBack`。
- **`UIPopup<TResult>` 结果:** 内部持 `UniTaskCompletionSource<TResult>`,`Result` 暴露其 `.Task`;`SetResult` 调 `_tcs.TrySetResult(result)`(用 `Try*` 避免重复 set 抛异常)。

---

## 文件结构总览

```
Assets/EasyFramework/
├── Services/
│   ├── UI/
│   │   ├── UILayer.cs                                  (enum UILayer)
│   │   ├── UIPanel.cs                                  (面板基类)
│   │   ├── UIPopup.cs                                  (UIPopup<TResult> : UIPanel)
│   │   ├── IUIService.cs                               (IUIService 接口)
│   │   ├── UIRootBuilder.cs                            (internal,程序化构建 Canvas/层根/EventSystem)
│   │   ├── UIService.cs                                (实现 IUIService + ITickable + IDisposable)
│   │   └── SafeAreaFitter.cs                           (RectTransform 安全区适配组件)
│   └── Scenes/
│       └── FadeSceneTransition.cs                      (新增:ISceneTransition 淡入淡出实现,用 PrimeTween)
├── Boot/
│   ├── G.cs                                            (修改:新增 UI 属性)
│   └── FrameworkInstaller.cs                           (修改:注册 IUIService + 换 ISceneTransition 为 FadeSceneTransition)
└── Tests/EditMode/
    ├── UIServiceTests.cs                               (新增:栈/队列/HUD/返回键/PopAll 全覆盖)
    └── FrameworkInstallerTests.cs                      (修改:断言 IUIService 可解析、G.UI 绑定)
```

依赖方向不变:`Tests → Boot → {Core, Services, Monetization}`;`Services → Core`。Phase 3a 只在 `Services` 内部新增 `UI/` 目录与 `Scenes/FadeSceneTransition.cs`,并修改 Boot 的两个文件。**不改任何 asmdef**(见前置要求 1)。

---

### Task 1: UILayer / UIPanel / UIPopup 基类(可并行)

> 这三者是 Service、测试与后续全部 Task 的契约底座,先落地。无外部依赖,纯类型 + UniTask。

**Files:**
- Create: `Assets/EasyFramework/Services/UI/UILayer.cs`, `UIPanel.cs`, `UIPopup.cs`
- Test: 基类的行为(默认 `PlayEnter`/`PlayExit` 立即完成、`OnBackRequested` 默认 false、`UIPopup` 结果)在 Task 5 的 `UIServiceTests` 中通过测试面板间接覆盖;此处不单独建测试文件。

- [ ] **Step 1: 实现 UILayer** — `UILayer.cs`

```csharp
namespace EasyFramework.Services.UI
{
    /// <summary>UI 四层(绘制叠放自下而上:Hud < Window < Popup < Overlay)。</summary>
    public enum UILayer
    {
        Hud,
        Window,
        Popup,
        Overlay,
    }
}
```

- [ ] **Step 2: 实现 UIPanel 基类** — `UIPanel.cs`

`protected internal` 钩子:对子类(`protected`)和同程序集的 `UIService`(`internal`)可见,对业务层不可见。默认 `PlayEnter`/`PlayExit` 立即完成,`OnBackRequested` 默认未消费。

```csharp
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
```

- [ ] **Step 3: 实现 UIPopup&lt;TResult&gt;** — `UIPopup.cs`

内部 `UniTaskCompletionSource<TResult>`;`Result` 暴露 `.Task`;`SetResult` 用 `TrySetResult`(避免重复 set 抛异常)。`OnBackRequested` 默认返回 `true`——Popup 拦截返回键(需用户显式选择结果,不被返回键随意关闭),子类可覆盖。

```csharp
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
```

> 说明:`UniTaskCompletionSource<T>.Task` 返回 `UniTask<T>`,与 `Result` 类型一致。`TrySetResult` 在已完成时返回 `false` 而不抛异常,符合"重复 set 安全"。`UIService` 在显示 Popup 时通过 `internal` 钩子读取 `Result` 等待完成(下 Task 经反射/基类协助实现,见 Task 3 说明)。

- [ ] **Step 4: 验证编译**(验证代理统一在 Task 5 后编译;本 Task 无独立测试)
- [ ] **Step 5: Commit**(编排者)`git commit -m "feat(ui): add UIPanel, UIPopup and UILayer base types"`

---

### Task 2: IUIService 接口(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/UI/IUIService.cs`

- [ ] **Step 1: 实现接口** — `IUIService.cs`

锁定契约,逐字落地。

```csharp
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
        UniTask<T> PushAsync<T>(object args = null) where T : UIPanel;

        /// <summary>弹出栈顶 Window,回退到下一个;栈空或仅一个时行为见实现(默认仅 1 个不弹)。</summary>
        UniTask PopAsync();

        /// <summary>清空 Window 栈。</summary>
        UniTask PopAllAsync();

        /// <summary>显示一个带返回值的弹窗;await 得到用户选择结果。多个并发请求排队,逐个展示。</summary>
        UniTask<TResult> ShowPopupAsync<TPopup, TResult>(object args = null) where TPopup : UIPopup<TResult>;

        /// <summary>显示 HUD(单实例);已有 HUD 时先隐藏旧的。</summary>
        UniTask<T> ShowHudAsync<T>(object args = null) where T : UIPanel;

        /// <summary>隐藏当前 HUD。</summary>
        UniTask HideHudAsync();

        /// <summary>当前 Window 栈深。</summary>
        int WindowCount { get; }
    }
}
```

- [ ] **Step 2: Commit**(编排者)`git commit -m "feat(ui): add IUIService contract"`

---

### Task 3: UIRootBuilder + SafeAreaFitter(可并行;Service 依赖 UIRootBuilder)

**Files:**
- Create: `Assets/EasyFramework/Services/UI/UIRootBuilder.cs`, `SafeAreaFitter.cs`

- [ ] **Step 1: 实现 SafeAreaFitter** — `SafeAreaFitter.cs`

挂在每个层根上,按 `Screen.safeArea` 把自身 `RectTransform` 的 `anchorMin`/`anchorMax` 收进安全区(刘海屏)。`OnEnable` + 缓存上次 safeArea,变化时重算(屏幕旋转/分辨率变化)。

```csharp
using UnityEngine;

namespace EasyFramework.Services.UI
{
    /// <summary>把挂载对象的 RectTransform 锚点收进 Screen.safeArea(刘海屏适配)。挂在四个层根上。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        void Awake() => _rect = GetComponent<RectTransform>();

        void OnEnable() => Apply();

        void Update()
        {
            // safeArea 或分辨率变化(旋转)时重算
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
            {
                Apply();
            }
        }

        void Apply()
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();

            var safe = Screen.safeArea;
            var w = Screen.width;
            var h = Screen.height;
            if (w <= 0 || h <= 0) return;

            var anchorMin = safe.position;
            var anchorMax = safe.position + safe.size;
            anchorMin.x /= w; anchorMin.y /= h;
            anchorMax.x /= w; anchorMax.y /= h;

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(w, h);
        }
    }
}
```

- [ ] **Step 2: 实现 UIRootBuilder** — `UIRootBuilder.cs`(internal)

程序化构建 UIRoot 结构与按需 EventSystem。**纯静态构建方法,只在 UIService 首次使用时被调**(懒加载),返回构建好的句柄供 Service 持有。

```csharp
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
        public static UIRootHandle Build()
        {
            var handle = new UIRootHandle();

            var root = new GameObject("[UIRoot]");
            Object.DontDestroyOnLoad(root);
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
            Object.DontDestroyOnLoad(es);
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
```

> 说明:`InputSystemUIInputModule` 来自 `UnityEngine.InputSystem.UI`(新 Input System 包,Phase 3b Task 1 装/引用)。EventSystem 补建仅当场景内确无 EventSystem 时执行,避免与既有 EventSystem 冲突。`UIRootBuilder.Build()` 触碰 `GameObject`/`Screen`,**因此只能在 Service 懒加载时调用,不能在构造函数里调**(满足纯容器 Build 约束)。

- [ ] **Step 3: Commit**(编排者)`git commit -m "feat(ui): add UIRootBuilder and SafeAreaFitter"`

---

### Task 4: UIService 实现(依赖 Task 1-3)

**Files:**
- Create: `Assets/EasyFramework/Services/UI/UIService.cs`

> 失败测试在 Task 5 统一写(`UIServiceTests` 覆盖本 Service 的栈/队列/HUD/返回键/PopAll)。本 Task 先把实现落地,Task 5 写测试后由验证代理统一编译运行。

- [ ] **Step 1: 实现 UIService** — `UIService.cs`

构造注入 `IAssetService`;实现 `IUIService` + `VContainer.Unity.ITickable`(返回键)+ `IDisposable`(清理)。UIRoot 懒加载(`EnsureRoot`)。`DestroyHandler` 可注入(EditMode 测试换 `DestroyImmediate`)。

```csharp
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
            var panel = go.GetComponent<T>();
            if (panel == null)
                throw new InvalidOperationException(
                    $"UI prefab '{key}' has no component of type {typeof(T).Name}.");
            panel.OnSetup(args);
            return panel;
        }

        // ---------------- Window 栈 ----------------

        public async UniTask<T> PushAsync<T>(object args = null) where T : UIPanel
        {
            // 下层栈顶隐藏(全屏遮挡,省 overdraw + 避免误接收输入)
            if (_windowStack.Count > 0)
                _windowStack[^1].gameObject.SetActive(false);

            var panel = await InstantiatePanelAsync<T>(UILayer.Window, args);
            _windowStack.Add(panel);
            await panel.PlayEnter();
            return panel;
        }

        public async UniTask PopAsync()
        {
            if (_windowStack.Count <= 1) return; // 栈底界面不弹出(避免空 Window 层)

            var top = _windowStack[^1];
            _windowStack.RemoveAt(_windowStack.Count - 1);
            await top.PlayExit();
            DestroyHandler(top.gameObject);

            var newTop = _windowStack[^1];
            newTop.gameObject.SetActive(true);
            await newTop.PlayEnter();
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

                // 3) 未消费 → 默认回退(栈深 > 1)
                if (_windowStack.Count > 1)
                {
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
```

> 实现要点:
> - **`Result` 跨程序集可读:** `UIPopup<TResult>.Result` 是 `public`,`UIService` 在同程序集泛型方法 `ShowPopupAsync<TPopup,TResult>` 里直接 `popup.Result`(`popup` 静态类型 `TPopup : UIPopup<TResult>`),无需反射。
> - **Popup 排队不激活:** 第二个请求入队后,闭包未执行 → 不 Instantiate → 实例不存在(自然"不激活");`_pumpingPopups` 保证串行泵出。第一个 `SetResult` 后其闭包继续(`PlayExit`+Destroy),while 循环取下一个才 Instantiate 第二个。
> - **`HandleBack` 调 `PopAsync().Forget()`:** 返回键触发的 Pop 是 fire-and-forget(`Tick` 同步上下文不 await)。单测对 `HandleBack` 的栈/消费断言不依赖 Pop 的异步完成,只断言返回值与"是否触发了 Pop 路径";若需断言 Pop 后栈深,测试用同步完成的面板(默认 `PlayExit` 立即完成),`Forget` 的延续同步跑完,栈深即时更新(见 Task 5 备注)。
> - **`EnsureRoot` 懒加载:** 构造函数只存 `_assets`,不碰 `GameObject`/`Screen`,满足 `FrameworkInstaller` 纯 `ContainerBuilder.Build()` 约束。

- [ ] **Step 2: 验证编译**(验证代理 Task 5 后统一)
- [ ] **Step 3: Commit**(编排者)`git commit -m "feat(ui): add UIService with window stack, popup queue, hud and back routing"`

---

### Task 5: UIService EditMode 测试(依赖 Task 1-4)

**Files:**
- Test: `Assets/EasyFramework/Tests/EditMode/UIServiceTests.cs`

> 用 Phase 2 的 `FakeAssetService` 注入"伪 prefab":测试里 `new GameObject` + `AddComponent<测试面板>`,放进 `FakeAssetService` 字典(key = `"ui/" + 面板类型名`)。`UIService.DestroyHandler` 在 `[SetUp]` 换 `Object.DestroyImmediate`、`[TearDown]` 还原(沿用 Phase 2 PoolService 模式)。所有面板用同步完成的 `PlayEnter`/`PlayExit`,`.GetAwaiter().GetResult()` 阻塞。

- [ ] **Step 1: 写失败测试** — `UIServiceTests.cs`

覆盖:Push/Pop 栈序与 `WindowCount`、`OnSetup` 收到 args、Popup await 结果、Popup 排队(第二个在第一个 `SetResult` 前不激活/不存在)、HUD 单实例、返回键消费逻辑(`HandleBack` Popup 优先 / Window 消费 / 默认 Pop)、`PopAllAsync` 清空。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using EasyFramework.Services.UI;
using NUnit.Framework;
using UnityEngine;

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

        // ---------------- 伪 prefab 工厂 ----------------

        readonly List<GameObject> _spawned = new();

        GameObject MakePrefab<T>() where T : UIPanel
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.AddComponent<T>();
            _spawned.Add(go);
            return go;
        }

        FakeAssetService _assets;
        UIService _ui;
        System.Action<GameObject> _originalDestroy;

        [SetUp]
        public void SetUp()
        {
            _originalDestroy = UIService.DestroyHandler;
            UIService.DestroyHandler = UnityEngine.Object.DestroyImmediate;

            var dict = new Dictionary<string, UnityEngine.Object>
            {
                { "ui/WindowA", MakePrefab<WindowA>() },
                { "ui/WindowB", MakePrefab<WindowB>() },
                { "ui/BackConsumingWindow", MakePrefab<BackConsumingWindow>() },
                { "ui/ConfirmPopup", MakePrefab<ConfirmPopup>() },
                { "ui/HudPanel", MakePrefab<HudPanel>() },
            };
            _assets = new FakeAssetService(dict);
            _ui = new UIService(_assets);
        }

        [TearDown]
        public void TearDown()
        {
            _ui.Dispose();
            UIService.DestroyHandler = _originalDestroy;
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

        // ---------------- HUD ----------------

        [Test]
        public void ShowHud_ReplacesPrevious_SingleInstance()
        {
            var h1 = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            var h2 = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            Assert.AreNotSame(h1, h2);
            Assert.IsTrue(h1 == null || h1.gameObject == null, "旧 HUD 应已销毁");
        }

        [Test]
        public void HideHud_RemovesCurrent()
        {
            var h = _ui.ShowHudAsync<HudPanel>().GetAwaiter().GetResult();
            _ui.HideHudAsync().GetAwaiter().GetResult();
            Assert.IsTrue(h == null || h.gameObject == null);
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
            return UnityEngine.Object.FindObjectsByType<ConfirmPopup>(FindObjectsSortMode.None).Length;
#else
            return UnityEngine.Object.FindObjectsOfType<ConfirmPopup>().Length;
#endif
        }
    }
}
```

> 测试备注:
> - **同步完成假设让 `Forget` 延续即时跑完:** 测试面板的 `PlayEnter`/`PlayExit` 返回 `UniTask.CompletedTask`,`FakeAssetService.LoadAsync` 同步返回。`ShowPopupAsync` 内 `PumpPopups().Forget()` 与 `HandleBack` 内 `PopAsync().Forget()` 的 async 方法体在首个真正挂起点前同步执行;因全程无挂起,延续在调用返回前即完成。故 `HandleBack_Unconsumed_DefaultPops` 可同步断言栈深、`Popup_AwaitReturnsResult` 可在 `ShowPopupAsync` 返回后立即 `FindActivePopup`。
> - **`FindObjectsByType`/`FindObjectsOfType` 找伪 prefab 实例化出的 popup:** `UIService` 把 popup Instantiate 到 Popup 层根(`DontDestroyOnLoad` 的 `[UIRoot]` 下),EditMode 下这些是真实 `GameObject`,可被 `FindObjects*` 命中。`#if UNITY_2023_1_OR_NEWER` 兼容新旧 API(项目为 Unity 6,走新 API)。

- [ ] **Step 2: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.UIServiceTests` 过滤)Expected: 全 PASS。
- [ ] **Step 3: Commit**(编排者)`git commit -m "test(ui): add UIService editmode tests for stack/queue/hud/back"`

---

### Task 6: FadeSceneTransition(依赖 PrimeTween;视觉薄层,不单测)

**Files:**
- Create: `Assets/EasyFramework/Services/Scenes/FadeSceneTransition.cs`

> **依赖 PrimeTween(程序集名 `PrimeTween`),由 Phase 3b Task 1 统一安装并升级 asmdef references。** 本文件是 Phase 3a 唯一引用 PrimeTween 的文件;若 PrimeTween 尚未就绪,本 Task 可暂缓落地(见前置要求 1 的临时降级路径),Task 1-5、7 不受影响。**视觉薄层,标注不单测(非占位符)**:它只是把一个全屏黑幕 `CanvasGroup` 的 alpha 用 PrimeTween 在 0↔1 间补间,行为靠真机/Play 模式肉眼验证,逻辑分支极少,EditMode 单测价值低(沿用 Phase 2 对 `UnitySceneLoader` 的"薄实现不单测"约定)。

- [ ] **Step 1: 实现 FadeSceneTransition** — `FadeSceneTransition.cs`

实现 Phase 2 的 `EasyFramework.Services.Scenes.ISceneTransition`(`PlayOut`/`PlayIn`)。Overlay 层全屏黑幕 `CanvasGroup`:`PlayOut` 淡入(alpha 0→1,挡住切场景过程)、`PlayIn` 淡出(1→0)。黑幕程序化构建为 `DontDestroyOnLoad`,懒加载。

```csharp
using Cysharp.Threading.Tasks;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Services.Scenes
{
    /// <summary>淡入淡出转场:在一个独立 DontDestroyOnLoad 的全屏黑幕 CanvasGroup 上用 PrimeTween 补间 alpha。视觉薄层,不单测。</summary>
    public sealed class FadeSceneTransition : ISceneTransition
    {
        readonly float _duration;
        CanvasGroup _group; // 懒加载

        public FadeSceneTransition(float duration = 0.25f) => _duration = duration;

        CanvasGroup EnsureGroup()
        {
            if (_group != null) return _group;

            var go = new GameObject("[SceneFade]");
            Object.DontDestroyOnLoad(go);

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

        public async UniTask PlayOut()
        {
            var group = EnsureGroup();
            group.blocksRaycasts = true;
            await Tween.Alpha(group, endValue: 1f, duration: _duration).ToUniTask();
        }

        public async UniTask PlayIn()
        {
            var group = EnsureGroup();
            await Tween.Alpha(group, endValue: 0f, duration: _duration).ToUniTask();
            group.blocksRaycasts = false;
        }
    }
}
```

> 说明:`Tween.Alpha(CanvasGroup, ...)` 是 PrimeTween 内置重载;`.ToUniTask()` 是 PrimeTween 的 UniTask 集成(PrimeTween 检测到 UniTask 程序集时启用)。若该集成 API 名不符(版本差异),实现代理用 `await tween.ToYieldInstruction()` 或 `await UniTask.WaitUntil(() => !tween.isAlive)` 等效替换;接口签名与视觉行为不变。`sortingOrder = short.MaxValue` 确保黑幕在 UIRoot Overlay 之上,转场期间盖住一切。

- [ ] **Step 2: 验证编译**(验证代理:确认 PrimeTween 引用就绪后编译 0 error;不写单测)
- [ ] **Step 3: Commit**(编排者)`git commit -m "feat(scenes): add fade scene transition with PrimeTween"`

---

### Task 7: 接线 —— FrameworkInstaller / G(串行,依赖 Task 1-6)

> **执行前必读前置要求 2。先 `Read` Phase 1/2 落地的 `Boot/G.cs`、`Boot/FrameworkInstaller.cs` 核对实际签名,再按下方完整内容等效增量合并。** 本计划给出的是「修改后的完整文件」,但若实际文件与 Phase 2 计划有偏差,以实际文件为基线只做三处增量:(a) `G` 加 `UI` 属性 + `Initialize`/`Reset` 同步;(b) `FrameworkInstaller` 注册 `IUIService`;(c) `FrameworkInstaller` 把 `ISceneTransition` 注册从 `NoopSceneTransition` 改为 `FadeSceneTransition`。

**Files:**
- Modify: `Assets/EasyFramework/Boot/G.cs`(新增 `UI` 属性)
- Modify: `Assets/EasyFramework/Boot/FrameworkInstaller.cs`(注册 `IUIService` + 换转场实现)
- Modify: `Assets/EasyFramework/Tests/EditMode/FrameworkInstallerTests.cs`(扩展断言)

- [ ] **Step 1: 升级失败测试** — `FrameworkInstallerTests.cs`

在 Phase 2 基础上扩展:断言 `IUIService` 可解析、`G.UI` 绑定。**先 Read Phase 2 实际文件**,在其上增量修改。下方仅给出**新增的测试方法与 using**,合并进现有 `FrameworkInstallerTests` 类(保留 Phase 2 已有的 `MakeOptions`/`Build`/`TearDown` 与既有断言)。

新增 using(若缺):
```csharp
using EasyFramework.Services.UI;
```

新增/扩展断言:
```csharp
        [Test]
        public void Install_ResolvesUIService()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IUIService>());
        }

        [Test]
        public void GFacade_BindsUIService()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.AreSame(c.Resolve<IUIService>(), EasyFramework.G.UI);
        }
```

并在 Phase 2 已有的 `GFacade_ResetClearsBindings` 断言中补一行(可选,保持一致性):
```csharp
            Assert.IsNull(EasyFramework.G.UI);
```

- [ ] **Step 2: 升级 G 门面** — `Assets/EasyFramework/Boot/G.cs`

在 Phase 2 的 Events/Timer/Asset/Scene/Save/Config/Pool 基础上新增 `UI` 属性,`Initialize`/`Reset` 同步更新。下方为合并后完整内容(**以实际 Phase 2 文件为基线,只增 UI 三处**):

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using EasyFramework.Services.UI;
using VContainer;

namespace EasyFramework
{
    /// <summary>业务层快速访问门面。框架内部禁止使用,模块间一律构造注入。</summary>
    public static class G
    {
        public static IEventBus Events { get; private set; }
        public static ITimerService Timer { get; private set; }
        public static IAssetService Asset { get; private set; }
        public static ISceneService Scene { get; private set; }
        public static ISaveService Save { get; private set; }
        public static IConfigService Config { get; private set; }
        public static IPoolService Pool { get; private set; }
        public static IUIService UI { get; private set; }
        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            Asset = resolver.Resolve<IAssetService>();
            Scene = resolver.Resolve<ISceneService>();
            Save = resolver.Resolve<ISaveService>();
            Config = resolver.Resolve<IConfigService>();
            Pool = resolver.Resolve<IPoolService>();
            UI = resolver.Resolve<IUIService>();
            IsInitialized = true;
        }

        internal static void Reset()
        {
            Events = null;
            Timer = null;
            Asset = null;
            Scene = null;
            Save = null;
            Config = null;
            Pool = null;
            UI = null;
            IsInitialized = false;
        }
    }
}
```

- [ ] **Step 3: 升级 FrameworkInstaller** — `Assets/EasyFramework/Boot/FrameworkInstaller.cs`

两处增量:(a) Scene 块的 `ISceneTransition` 注册从 `NoopSceneTransition` 改为 `FadeSceneTransition`;(b) 新增 UI 块注册 `UIService` 为 `IUIService`,且因 `UIService` 同时实现 `ITickable` 返回键,需把它挂进入口点。**关键约束:`UIService` 构造只存 `IAssetService`、不碰 GameObject,纯 `ContainerBuilder.Build()` 仍可成功。** 下方为合并后完整内容(**以实际 Phase 2 文件为基线,只改两处**):

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using EasyFramework.Services.UI;
using MessagePipe;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架启动选项:把 Unity 静态依赖从组合根传入,使 Install 可脱离 MonoBehaviour 单测。</summary>
    public sealed class FrameworkOptions
    {
        /// <summary>存档目录;生产传 Application.persistentDataPath,测试传临时目录。</summary>
        public string SaveDirectory;
        /// <summary>本地配置表;可空。</summary>
        public IReadOnlyList<ConfigTable> ConfigTables;
        /// <summary>可空:游戏自定义存档 profile;为 null 时用框架默认 DefaultSaveData profile。</summary>
        public SaveProfile SaveProfile;
        /// <summary>初始场景名(SceneService.CurrentScene 初值);默认 "Boot"。</summary>
        public string InitialScene = "Boot";
    }

    /// <summary>框架服务注册(纯逻辑,便于脱离 MonoBehaviour 测试)。入口点注册在 RootLifetimeScope。</summary>
    public static class FrameworkInstaller
    {
        public static void Install(IContainerBuilder builder, FrameworkOptions options)
        {
            // ---- Core(Phase 1)----
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            builder.Register<TimerService>(Lifetime.Singleton).As<ITimerService>().AsSelf();

            // ---- Asset ----
            builder.Register<AddressablesAssetService>(Lifetime.Singleton)
                .As<IAssetService>().AsSelf();

            // ---- Scene ----
            // Phase 3a:转场从 NoopSceneTransition 换成 FadeSceneTransition(淡入淡出黑幕)
            builder.Register<ISceneTransition, FadeSceneTransition>(Lifetime.Singleton);
            builder.RegisterInstance<ISceneLoader>(new UnitySceneLoader());
            builder.Register<ISceneService>(c => new SceneService(
                c.Resolve<ISceneLoader>(),
                c.Resolve<ISceneTransition>(),
                c.Resolve<IEventBus>(),
                options.InitialScene), Lifetime.Singleton);

            // ---- Save ----
            var profile = options.SaveProfile ?? CreateDefaultSaveProfile();
            builder.RegisterInstance(profile);
            builder.Register<ISaveService>(c => new JsonSaveService(profile, options.SaveDirectory),
                Lifetime.Singleton);
            builder.Register<SaveBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Config ----
            builder.Register<IRemoteConfigProvider, NoopRemoteConfigProvider>(Lifetime.Singleton);
            var tables = options.ConfigTables ?? new List<ConfigTable>();
            builder.Register<ConfigService>(c => new ConfigService(tables, c.Resolve<IRemoteConfigProvider>()),
                Lifetime.Singleton).As<IConfigService>().AsSelf();
            builder.Register<ConfigBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Pool ----
            builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();

            // ---- UI(Phase 3a)----
            // UIService 同时实现 IUIService(门面)与 ITickable(返回键检测)。
            // 构造只存 IAssetService、不碰 GameObject/Screen,UIRoot 懒加载,纯容器 Build 安全。
            builder.Register<UIService>(Lifetime.Singleton)
                .As<IUIService>()
                .As<ITickable>()
                .AsSelf();
        }

        static SaveProfile CreateDefaultSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(DefaultSaveData),
                CurrentVersion = 1,
                CreateNew = () => new DefaultSaveData { Version = 1 },
                Migrations = new List<ISaveMigration>(),
                FileName = "save.json",
                HmacSalt = "easyframework",
            };
    }
}
```

> 注册要点:
> - **`UIService` 的 `ITickable`:** 用 `.As<ITickable>()` 注册,VContainer 自动把它纳入 Tick 入口点循环(与 Phase 1 的 `TimerTicker`、`GameBootstrap` 同机制),每帧调 `UIService.Tick()` 检测返回键。**纯容器单测**(`FrameworkInstallerTests` 用 `ContainerBuilder.Build()`,无 PlayerLoop)只验证 `Resolve<IUIService>()` 成功——构造不碰 GameObject 故 Build 不抛;`Tick` 不会在无 PlayerLoop 的测试里被调,安全。
> - **`FadeSceneTransition` 默认时长:** `builder.Register<ISceneTransition, FadeSceneTransition>` 用无参/默认参构造(`duration = 0.25f`)。若游戏要自定义时长,在 `GameLifetimeScope` 覆盖注册 `RegisterInstance<ISceneTransition>(new FadeSceneTransition(0.4f))` 即可(子作用域覆盖)。
> - **若实际 Phase 2 文件的 Scene 块写法不同**(如未用工厂 lambda),实现代理只把 `ISceneTransition` 的具体类型从 `NoopSceneTransition` 换成 `FadeSceneTransition`,其余保持实际文件原样;并在末尾追加 UI 块。

- [ ] **Step 4: 验证测试通过**(验证代理:`FrameworkInstallerTests` 全过,含新增 UI 断言)
- [ ] **Step 5: Commit**(编排者)`git commit -m "feat(boot): wire UIService into facade and installer, swap to fade transition"`

---

### Task 8: 全量验证(串行,使用 UnityMCP)

> **前置:确认 Phase 3b Task 1 已升级 asmdef references(`UnityEngine.UI`/`UnityEngine.InputSystem`/`Unity.TextMeshPro`/`PrimeTween`)且包已装。** 否则编译会缺引用——此时按前置要求 1 临时降级路径先验 Task 1-5、7,Task 6 待包就绪后补验。

- [ ] **Step 1: 刷新编译** — `refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false`。
- [ ] **Step 2: 0 error** — `read_console(types=["error"])`。Expected: 0 errors。
- [ ] **Step 3: EditMode 全量测试** — `run_tests(mode="EditMode")` → `get_test_job` 轮询。Expected: Phase 1/2 全部用例 + Phase 3a 新增(`UIServiceTests` 约 15、`FrameworkInstallerTests` 升级后 +2)全 PASS。
- [ ] **Step 4: 无新警告** — `read_console(types=["warning"])`。Expected: 无框架相关新警告。
- [ ] **Step 5: Play 模式冒烟(转场目视)** — 进 Boot 场景 Play,调一次 `G.Scene.LoadAsync(...)` 或在临时按钮触发,目视黑幕淡入淡出无报错(`FadeSceneTransition` 不单测,靠此步把关);`G.UI.PushAsync` 一个最小面板看是否正确叠在 Window 层。Expected: 无 NRE/无 EventSystem 缺失警告。
- [ ] **Step 6: 偏差记录** — 若实现与计划有偏差(如 PrimeTween `.ToUniTask()` API 名、VContainer `As<ITickable>` 写法、`InputSystemUIInputModule` 命名空间),在本计划文档末尾追加 "Deviations" 小节记录。
- [ ] **Step 7: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- **Spec 覆盖:** Phase 3a 范围 = 设计文档 §4.5 UI 框架(四层结构、`await G.UI.Push<T>()`/`Pop()`、弹窗带返回值 `ShowPopupAsync<ConfirmPopup,bool>`、约定优于配置按名加载无注册表、面板基类内置安全区 + PrimeTween 入出场钩子 + Android 返回键栈顶响应、不上 MVVM)。逐项核对:
  - 四层(Overlay/Popup/Window/HUD)→ `UILayer` + `UIRootBuilder` 四层根,sibling 顺序定叠放。✓
  - Window 栈 + Popup 队列化 + HUD 常驻 → `UIService` 栈/队列/单实例三语义。✓
  - 弹窗带返回值 → `UIPopup<TResult>` + `UniTaskCompletionSource.TrySetResult` + `ShowPopupAsync<TPopup,TResult>`。✓
  - 约定优于配置、Addressables 按名加载、无注册表 → `"ui/" + typeof(T).Name` 经 `IAssetService.LoadAsync<GameObject>(..., AssetScope.Global)`。✓
  - 面板基类:安全区适配 → `SafeAreaFitter`(挂四层根);PrimeTween 入出场钩子 → `PlayEnter`/`PlayExit` 虚方法(默认立即完成,子类可用 PrimeTween);Android 返回键 → `Tick` 检测 `escapeKey.wasPressedThisFrame` + `HandleBack` 路由(Popup → Window.OnBackRequested → 默认 Pop)。✓
  - 转场遮罩(§4.2 Scene 留的 `ISceneTransition`,Phase 2 用 Noop)→ `FadeSceneTransition` 实现并在 Installer 替换 Noop。✓
  - 不上 MVVM(YAGNI)→ 未引入任何绑定层,刷新留给业务用 `IEventBus` 事件驱动。✓
  - §2.2 分层铁律:UI 代码全在 `EasyFramework.Services.UI`,只向下依赖 Core(`IEventBus` 未强依赖)与同层 `IAssetService`/`ISceneTransition`;框架内部不碰 `G` 门面(`UIService` 构造注入 `IAssetService`,不走 `G`);先接口后实现(`IUIService`→`UIService`、`ISceneTransition`→`FadeSceneTransition`)。✓
- **类型一致性:** 跨 Task 核对锁定契约在测试/实现/Installer 三处一致:`UILayer{Hud,Window,Popup,Overlay}`;`UIPanel.OnSetup/PlayEnter/PlayExit/OnBackRequested`(`protected internal virtual`,签名逐字对齐任务书);`UIPopup<TResult>.Result(UniTask<TResult>)/SetResult(protected)`;`IUIService.PushAsync<T>/PopAsync/PopAllAsync/ShowPopupAsync<TPopup,TResult>/ShowHudAsync<T>/HideHudAsync/WindowCount`(逐条对齐)。衔接 Phase 1/2 类型:`IAssetService.LoadAsync<T>(key, AssetScope)`/`AssetScope.Global`、`ISceneTransition.PlayOut/PlayIn`、`FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)`、`G.Initialize/Reset`、`UIService.DestroyHandler`(沿用 `PoolService.DestroyHandler` 模式)、`VContainer.Unity.ITickable` 均与 Phase 1/2 计划一致。`UniTaskCompletionSource.TrySetResult` 用法核对正确(Try* 防重复 set 抛异常,`Result => _tcs.Task` 类型为 `UniTask<TResult>`)。✓
- **占位符:** 无 TBD。两处"不单测"均为任务书/spec 明确许可的延后,非占位符:(a) `FadeSceneTransition` 视觉薄层不单测(沿用 Phase 2 `UnitySceneLoader` 约定,代码完整给出,Play 模式目视把关);(b) Addressables 真实加载在 Phase 5 冒烟,EditMode 用 `FakeAssetService` 伪 prefab。可能漂移处均给出具体回退:PrimeTween `.ToUniTask()` API 名不符 → `ToYieldInstruction()`/`WaitUntil(!isAlive)` 等效替换;VContainer `As<ITickable>` 入口点写法不符 → 以实际 Phase 1 `TimerTicker` 注册写法为准等效注册;`InputSystemUIInputModule` 命名空间差异 → 以实际包为准;Phase 1/2 文件签名偏差 → Read-then-merge 流程(Task 7 已声明)。asmdef/包依赖明确委托 Phase 3b Task 1,本计划注明依赖并给临时降级路径。✓

---

## 自查发现并修正的问题(起草阶段)

1. **UIService 构造若懒加载没做对会破坏纯容器单测** —— 任务书硬性要求"UIRoot 构建必须懒加载,不在构造函数里碰 GameObject"。修正:构造函数只存 `IAssetService`;UIRoot 经 `EnsureRoot()`→`UIRootBuilder.Build()` 在首次 `Push/Show` 时才构建。`FrameworkInstallerTests` 用纯 `ContainerBuilder.Build()` + `Resolve<IUIService>()` 验证不抛(构造不碰 GameObject)。
2. **Popup "第二个在第一个 SetResult 前不激活"需要明确数据结构与时序** —— 修正:用 `Queue<Func<UniTask>>` 存"显示并解析一个 popup"的闭包,排队项**不 Instantiate**(实例不存在即天然未激活);`_pumpingPopups` 串行泵出,第一个 `Result` 完成后其闭包才 `PlayExit`+Destroy 并取下一个。测试 `Popup_SecondQueuesUntilFirstResolves` 用 `CountActivePopupInstances()==1` 断言时序。
3. **返回键判定必须可单测且不依赖真实按键** —— 修正:`Tick()` 只读 `Keyboard.current.escapeKey.wasPressedThisFrame` 并转交;判定全在 `internal bool HandleBack()`(纯逻辑,Popup→Window.OnBackRequested→默认 Pop),测试直接调 `HandleBack()` 断言返回值与栈深,不触按键。
4. **`UniTaskCompletionSource` 用法**(任务书点名) —— 修正:`UIPopup<TResult>` 内部 `UniTaskCompletionSource<TResult>`,`Result => _tcs.Task`,`SetResult` 用 `_tcs.TrySetResult(result)`(`Try*` 在重复 set 时返回 false 而非抛异常)。`ShowPopupAsync` 内对回传 tcs 同样用 `TrySetResult`。
5. **EditMode 下 Object.Destroy 不即时生效导致 HUD 替换/PopAll 断言不稳** —— 沿用 Phase 2 `PoolService.DestroyHandler` 模式:`UIService.DestroyHandler` = `internal static Action<GameObject>`(默认 `Object.Destroy`),测试 `[SetUp]` 换 `DestroyImmediate`、`[TearDown]` 还原,使"旧 HUD 已销毁""PopAll 清空"可即时断言。
6. **`asmdef`/包依赖与 Phase 3b 的边界** —— 任务书要求本计划不改 asmdef、PrimeTween/TMP 引用由 Phase 3b Task 1 统一升级。修正:在「前置要求 1」明确声明依赖关系与执行顺序;`FadeSceneTransition`(唯一用 PrimeTween 的文件)给临时降级路径,使 Task 1-5、7 可在 Phase 3b 未就绪时先行验证。
7. **`FadeSceneTransition` 替换 `NoopSceneTransition` 的接线点** —— 修正:Task 7 Step 3 明确把 `FrameworkInstaller` Scene 块的 `ISceneTransition` 注册从 `NoopSceneTransition` 改为 `FadeSceneTransition`,并提示若实际 Phase 2 文件写法不同只换具体类型;黑幕用独立 `sortingOrder = short.MaxValue` 的 Canvas,保证盖在 UIRoot Overlay 之上。
8. **Window Push 是否隐藏下层未定** —— 任务书要求"你定一个默认并说明"。修正:默认 Push 时把下层栈顶 `SetActive(false)`(全屏遮挡省 overdraw + 避免下层误接收输入),Pop 时把新栈顶 `SetActive(true)` 并重新 `PlayEnter`;在「关键设计决策」与代码注释中说明,测试 `Push_HidesPreviousTop`/`Pop_RemovesTop_AndReactivatesPrevious` 覆盖。

---

## Deviations (Task 8 验证代理记录 / 2026-06-12)

验证代理在编译 + EditMode 全量测试阶段发现并修复以下偏差。公共接口契约(`IUIService`/`ISceneTransition`/`UIPanel`/`UIPopup<TResult>`)均未改动,仅修运行/测试可行性问题。

1. **`FadeSceneTransition` 的 DI 注册无法解析 `System.Single`(编译通过但测试运行期抛异常)。**
   - 现象:`FrameworkInstallerTests.*` 5 个用例报 `VContainerException : Failed to resolve FadeSceneTransition : No such registration of type: System.Single`。
   - 根因:计划用 `builder.Register<ISceneTransition, FadeSceneTransition>(...)`,但 `FadeSceneTransition` 构造为 `FadeSceneTransition(float duration = 0.25f)`;VContainer **不识别 C# 可选参数默认值**,会尝试从容器解析未注册的 `System.Single` 而失败(计划注释「用无参/默认参构造」的假设对 VContainer 不成立)。
   - 修复:`Boot/FrameworkInstaller.cs` 改为工厂 lambda 注册 `builder.Register<ISceneTransition>(_ => new FadeSceneTransition(), Lifetime.Singleton);`,由 C# 直接走默认时长 `0.25f`。运行时行为与意图一致(子作用域仍可覆盖自定义时长)。

2. **`UIServiceTests` 大量用例在 EditMode 抛 `DontDestroyOnLoad can only be used in play mode`。**
   - 现象:14 个 `UIServiceTests` 中约 11 个报上述 `InvalidOperationException`,源自 `[UIRoot]` / `[EventSystem]` 构建。
   - 根因:`UIRootBuilder.Build()` 硬调 `Object.DontDestroyOnLoad`,在 EditMode 测试(非 Play 模式)非法。计划只为 `Object.Destroy` 留了 `DestroyHandler` 测试缝,未为 `DontDestroyOnLoad` 留对称缝;计划风险 #1 只覆盖「构造不碰 GameObject」,未覆盖 `Push/Show` 触发的懒构建路径。
   - 修复:`Services/UI/UIRootBuilder.cs` 新增对称测试缝 `internal static Action<GameObject> DontDestroyHandler = Object.DontDestroyOnLoad;`,两处 `DontDestroyOnLoad` 改走该钩子。测试 `[SetUp]` 将其替换为 no-op、`[TearDown]` 还原(与既有 `DestroyHandler` 模式一致)。运行时行为不变。

3. **`Popup_SecondQueuesUntilFirstResolves` 计数把伪 prefab 模板算进活跃实例。**
   - 现象:`CountActivePopupInstances()` 期望 1、实得 2。
   - 根因:计划的 `MakePrefab<T>()` 创建的伪 prefab 模板本身是场景中一个**活跃**的 `ConfirmPopup` GameObject;`InstantiatePanelAsync` 实例化后,`FindObjectsByType<ConfirmPopup>` 同时命中「模板 + 实例」= 2。计划的 `CountActivePopupInstances` 名为「Active」却未按 `activeInHierarchy` 过滤,且 `MakePrefab` 未停用模板——属计划测试代码的潜在缺陷。
   - 修复(三处,贴近真实 prefab 语义):
     - 测试 `MakePrefab<T>()`:模板 `go.SetActive(false)`(真实 Addressable prefab 不是活跃场景对象);
     - 测试 `CountActivePopupInstances()`:按 `gameObject.activeInHierarchy` 过滤后计数;
     - 生产 `UIService.InstantiatePanelAsync`:实例化后 `go.SetActive(true)`,确保从非激活模板克隆出的面板可见(Window 的 `Push_HidesPreviousTop`/`Pop_*` 仍依赖实例默认激活,故此为行为正确的必要补充)。

**验证结果:** 编译 0 错误;EditMode 109/109 通过(含 Addressables doc-stub 1 个,EasyFramework 自有 108 个全绿)。
