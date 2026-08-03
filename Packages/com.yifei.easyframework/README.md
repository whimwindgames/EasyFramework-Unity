# EasyFramework 使用文档

Unity 2D、2.5D 与 3D 游戏复用底座。DI 内核(VContainer)+ 可选服务模块 + 静态门面(`G`)。
新项目可以开箱即用，已有项目可以只接入依赖管理与需要的服务，不必替换现有相机、输入或对象池。

- 要求 Unity 6000.0+(URP + 新 Input System)
- 设计文档与完整仓库:https://github.com/whimwindgames/EasyFramework-Unity
- 两个 Sample(经 Package Manager → Samples 导入):**TapRush**(完整示例游戏)、**Template**(新游戏空白模板)

## 安装

全部依赖由 OpenUPM 解析。**OpenUPM CLI(推荐):**

```bash
openupm add com.yifei.easyframework
```

**手动:** 把下面合并进 `Packages/manifest.json`,再在 Package Manager 按名安装 `com.yifei.easyframework`:

````json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": [
      "com.yifei",
      "com.cysharp",
      "jp.hadashikick",
      "com.kyrylokuzyk",
      "com.yasirkula"
    ]
  }
]
````

---

## 一、架构总览

```
Game 层(你的小游戏,经 G 门面 / 构造注入)
  │
  ▼
EasyFramework.Services(服务层,全接口化)          EasyFramework.Monetization
  Asset / Scene / Save / Config / Pool /              Ads / IAP / Analytics
  UI / Audio / Input / Camera / Juice /
  Localization / Haptics
  │
  ▼
EasyFramework.Core(纯 C# 核心,零 Unity 场景依赖,可单测)
  EventBus / StateMachine / ObjectPool / Timer / Boot 管线 / Pooling
  │
  ▼
第三方库(VContainer / UniTask / MessagePipe / Addressables / TMP / Cinemachine / PrimeTween / Input System)

EasyFramework.Boot     = 组合根(RootLifetimeScope / G 门面 / FrameworkInstaller)
EasyFramework.DevTools = [Cheat] 作弊 + FPS/内存角标 + 调试控制台
```

**三条铁律**

1. **只能向下依赖**:Core 不认识 Service,Service 不认识 Game。
2. **服务先接口后实现**:`IAdsService` → `FakeAdsProvider` / 真实 SDK 适配器,在 LifetimeScope 按平台/环境切换。
3. **框架内部禁用 `G.` 门面**(模块间一律构造注入);`G` 只服务 Game 层。

**启动管线**(`RootLifetimeScope`,挂在 Boot 场景的常驻 `DontDestroyOnLoad` 节点上):

```
FrameworkInstaller.Install(builder, FrameworkOptions)   只注册启用的服务(接口 + 实现)
        │
        ▼
GameBootstrap   按 IBootTask.Priority 升序异步初始化各服务
        │       (Save → Config → 表现层 → 商业化 SDK;IsCritical=false 的失败不阻塞启动)
        ▼
G.Initialize(resolver)   把已解析服务绑定到 G 门面
        │
        ▼
发布 BootCompletedEvent   业务层据此进入主菜单
```

> 商业化 SDK / 远程配置等非关键初始化失败只降级该服务、不崩游戏。关闭的模块不会构造、启动，
> 对应 `G.Xxx` 为 `null`；`G.Events` 与 `G.Timer` 始终可用。

### 已有项目的模块化接入

`FrameworkOptions.Features` 默认是 `FrameworkFeatures.All`，保持旧版开箱即用行为。已有大型项目建议从
`FrameworkFeatureSets.ExistingProject` 起步，再逐个加入需要的模块：

```csharp
protected override void ConfigureFrameworkOptions(FrameworkOptions options)
{
    options.Features = FrameworkFeatureSets.ExistingProject |
                       FrameworkFeatures.UI |
                       FrameworkFeatures.Input |
                       FrameworkFeatures.Camera |
                       FrameworkFeatures.Pooling;

    // 横屏不是特殊版本，只是 UI Root 的一种配置。
    options.UIRootProfile = UIRootProfile.Landscape(1920f, 1080f);

    // 项目已有特殊系统时，在根容器构建前替换；未启用的模块无需提供。
    options.InputServiceFactory = resolver => new MyInputService();
    options.CameraServiceFactory = resolver => new MyCameraService();
    options.PoolServiceFactory = resolver => new MyPoolService();
}
```

如果项目需要 URP Camera Stack、多相机、已有 Canvas 或特殊安全区层级，提供
`options.UIRootFactoryFactory = resolver => new MyUIRootFactory()`；如果页面系统也完全自有，则使用
`UIServiceFactory` 整体替换。两者不能同时设置。

3D 场景中的 World Space UI 可直接使用同一 Profile。`ReferenceResolution` 决定根 RectTransform 的像素尺寸，
`WorldSpaceScale` 决定像素到世界单位的换算（默认 `0.001`，即 1920 像素为 1.92 世界单位）：

```csharp
var profile = UIRootProfile.Landscape();
profile.RenderMode = RenderMode.WorldSpace;
profile.WorldCamera = worldUiCamera;
profile.WorldSpaceScale = 0.001f;
```

需要把 Canvas 挂到角色、座舱或 3D 锚点时，使用自定义 `IUIRootFactory` 设置父节点与世界姿态。

---

## 二、G 门面速查表

> 业务代码(Game 层)直接用 `G.Xxx`。框架内部禁用,走构造注入。所有签名以接口文件为准。

| 门面 | 接口 | 常用调用 |
|---|---|---|
| `G.Events` | `IEventBus` | `Publish<T>(evt)` / `Subscribe<T>(handler)`(返回 `IDisposable`,Dispose 退订) |
| `G.Timer` | `ITimerService` | `Schedule(delay, callback, repeat=false, useUnscaledTime=false)` → `TimerHandle` / `Cancel(handle)` |
| `G.Asset` | `IAssetService` | `await LoadAsync<T>(key, scope=Scene)` / `ReleaseScope(scope)` |
| `G.Scene` | `ISceneService` | `await LoadAsync(sceneName, progress?)` / `CurrentScene` |
| `G.Save` | `ISaveService` | `await LoadAsync()` / `Save()` / `Data<T>()` |
| `G.Config` | `IConfigService` | `Get<T>(key, defaultValue)` / `Has(key)` / `await RefreshRemoteAsync(ct)` |
| `G.Pool` | `IPoolService` | `await SpawnAsync(key, pos?, rot?, parent?)` / `Despawn(go)` / `await PrewarmAsync(key, count)` |
| `G.UI` | `IUIService` | `await PushAsync<W>()` / `ShowPopupAsync<P, R>()` / `ShowHudAsync<H>()` / `ShowOverlayAsync<O>()` |
| `G.Audio` | `IAudioService` | `await PlayBgmAsync(key, fade=0.5f)` / `StopBgm(fade=0.3f)` / `PlaySfx(key, vol=1f)` |
| `G.Input` | `IInputService` | `MoveAxis`(摇杆 + WASD 合流)/ `IsPointerOverUI` |
| `G.Camera` | `ICameraService` | 跟随 / 边界 / 震屏(Cinemachine 2D) |
| `G.Juice` | `IJuiceService` | `PunchScale` / `Flash` / `await HitStopAsync()`(PrimeTween 封装) |
| `G.Loc` | `ILocalizationService` | `Get(key)` / `await SetLocaleAsync(localeCode)` / `CurrentLocale` |
| `G.Haptics` | `IHapticsService` | `Vibrate(strength)` / `Enabled`(轻/中/重三档) |
| `G.Ads` | `IAdsService` | `await ShowRewardedAsync(placement)` / `await ShowInterstitialAsync(placement)` / `ShowBanner(pos)` / `HideBanner()` / `IsRewardedReady` |
| `G.IAP` | `IIAPService` | `await PurchaseAsync(id)` / `await RestoreAsync()` / `IsOwned(id)` |
| `G.Analytics` | `IAnalyticsService` | `Track(name)` / `Track(name, (key, value)...)` / `SetUserProperty(key, value)` |

> **输入手势**(Tap / LongPress / Swipe / Drag / Pinch)经 `G.Events` 广播,订阅对应事件结构体:
> `G.Events.Subscribe<TapEvent>(e => ...)`(`TapEvent.ScreenPosition` 为屏幕坐标)。`G.Input` 只提供
> 摇杆轴 `MoveAxis` 与 `IsPointerOverUI`。

> **访问时机**:`G.Xxx` 在 `BootCompletedEvent` 之后才保证可用。`G.IsInitialized` 可在作弊命令等
> 早期入口处守卫。

---

## 三、新游戏十分钟上手

1. 经 Package Manager → Samples 导入 **Template**,把导入后的目录复制改名为你的游戏(详细改名清单见 Template 的 `README.md`)。
2. 改命名空间 `EasyFramework.Template` → `<YourGame>`,改类名 `Template*` → `<YourGame>*`。
3. 打开 `Assets/Scenes/Boot.unity`,在 `[EasyFramework]` 上挂游戏自己的 `RootLifetimeScope` 子类,
   并在其下新建子 GameObject,
   挂你的 `<YourGame>LifetimeScope`(嵌套 `LifetimeScope` 自动成为 Root 的子作用域)。
4. 用 `StateMachine<TContext>` 写流程(Menu / Gameplay / Result),用 `G.UI` 建界面,
   用 `G.Pool` 管玩法对象,用 `G.Save` 存档。
5. Play。`BootCompletedEvent` 发布后进入 Menu。

完整可运行参考:导入 **TapRush** Sample(**TapRush** —— 点圈得分、60 秒倒计时的最小完整游戏,逐一打通了上表所有能力,
含 Addressables 真实加载、Fake 商业化与存档读回)。

---

## 四、面板约定(UI)

面板 = 继承 `UIPanel`(或带返回值的 `UIPopup<TResult>`)的脚本 + 同名 prefab。
Addressables 按 `"ui/<TypeName>"` 约定加载,**无注册表**(约定优于配置)。

生命周期由 `IUIService` 驱动:

```
OnSetup(args)  →  PlayEnter()  →  (显示)  →  PlayExit()  →  (销毁)
```

- **Window** = 栈(`PushAsync` 盖在上面、`PopAsync` 回退;Push 时隐藏下层栈顶)。
- **Popup** = 队列(同时只显示一个,带返回值;`await ShowPopupAsync<ConfirmPopup, bool>()` 拿用户选择)。
- **HUD** = 按类型缓存常驻；多个不同类型 HUD 可以同时显示，使用 `HideHudAsync<T>()` 精确关闭，或无泛型版本一次关闭全部。
- **Overlay** = 覆盖全部 UI 的全局状态层；同类型复用并置顶，不同类型按最近调用顺序叠放。使用 `HideOverlayAsync<T>()` 精确关闭，或无泛型版本全部关闭。适合 Loading、断线重连、登录遮罩和场景过场。

四层绘制叠放自下而上:`Hud < Window < Popup < Overlay`(`UILayer`)。
返回键(Android / Esc)路由到栈顶面板的 `OnBackRequested()`;`UIPopup` 默认拦截返回键等待显式选择。
不上 MVVM —— 面板刷新由业务用 `IEventBus` 事件或直接调面板方法驱动。

### 面板生命周期与跨程序集继承

- 游戏程序集继承 `UIPanel` 时，使用 `protected override` 实现 `OnSetup`、`PlayEnter`、`PlayExit` 和 `OnBackRequested`；框架同一程序集内的测试或实现才使用 `protected internal override`。
- 生命周期钩子只由 `IUIService` 调用。业务代码负责调用 Push/Show/Hide/Pop API，不直接调用钩子，也不自行销毁受服务管理的面板。
- `OnSetup(args)` 每次 Show 都会调用；同类型 HUD/Overlay 被复用时也会收到新参数并重新执行 `PlayEnter()`。
- Window、HUD 与 Overlay 在 `PlayExit()` 完成后销毁；Popup 在 `SetResult` 后退出并销毁。`SetResult` 仅供 `UIPopup<TResult>` 子类处理用户选择，重复调用只有第一次生效。
- 所有 Window、HUD 与 Overlay 操作进入同一串行队列。调用方应传入所属页面或会话的 `CancellationToken`，不要用 `Forget()` 隐藏加载失败。

默认 `UIRootProfile.Portrait()` 保持 `1080×1920`；横屏使用 `UIRootProfile.Landscape()`，默认
`1920×1080` 且以高度为缩放基准。两者都依赖 CanvasScaler 连续适配不同宽高比，并由
`SafeAreaFitter` 在分辨率或设备方向变化时重新计算安全区。框架不会强制设置设备方向。

---

## 五、SDK 接入槽

商业化层为「接口 + 适配器」:编辑器 / 开发包默认用 Fake,正式包接真实 SDK **不改业务代码**。
所有根服务适配器必须在 `RootLifetimeScope.ConfigureFrameworkOptions` 中配置。VContainer 的父容器
看不到子 `GameLifetimeScope` 的注册,因此不能在子作用域覆盖广告、IAP、统计或远程配置。

| 槽 | 默认(编辑器 / 测试) | 接入真实 SDK |
|---|---|---|
| 广告 `IAdsProvider` | 编辑器/开发包 `Fake`;正式包 `Unavailable` | 可选安装 `com.yifei.easyframework.max`,调用 `options.UseAppLovinMax(...)` |
| 内购 `IIAPProvider` | 编辑器/开发包 `Fake`;正式包 `Unavailable` | 可选安装 `com.yifei.easyframework.iap`,调用 `options.UseUnityIAP()` |
| 收据 `IIAPReceiptValidator` | 编辑器/开发包放行 Fake;正式包拒绝 | Unity IAP 扩展默认客户端确认,也可传服务器 validator factory |
| 统计后端 | 编辑器/开发包 Console;正式包空列表 | `options.AnalyticsBackendsFactory = ...` |
| 远程配置 | `NoopRemoteConfigProvider` | `options.RemoteConfigProviderFactory = ...` |
| 存档后端 `ISaveBackend`(云存档,v1 未做) | 本地文件 | 预留接口 |

```csharp
public sealed class MyRootLifetimeScope : RootLifetimeScope
{
    protected override void ConfigureFrameworkOptions(FrameworkOptions options)
    {
        // 安装 MAX 扩展后:
        // options.UseAppLovinMax(new MaxAdsSettings { ... });
        // 安装 Unity IAP 扩展后(当前无服务端验签):
        // options.UseUnityIAP();
        options.RemoteConfigProviderFactory = resolver => new MyRemoteConfigProvider();
        options.AnalyticsBackendsFactory = resolver =>
            new IAnalyticsBackend[] { new MyAnalyticsBackend() };
    }
}
```

核心包不依赖 MAX 或 Unity Purchasing。两个独立扩展仓库分别为:

- `https://git.whimwindgames.cn/gitadmin/easyframework-max.git`
- `https://git.whimwindgames.cn/gitadmin/easyframework-iap.git`

只安装实际需要的扩展,即可避免未使用 SDK 增大包体、引入原生依赖或影响构建。

IAP 的 `ProcessPurchase` 会保持 Pending。交易先写入 `iap-transactions.json`,通过客户端交易
结构/收据存在性校验并由
`SetRewardHandler((transaction, ct) => ...)` 成功发奖后才向商店确认。发奖实现必须用
`transaction.TransactionId` 做幂等;否则应用恰好在发奖后、journal 落盘前退出时仍可能重复发奖。
当前方案不做服务端密码学验签,不能抵御已控制客户端的攻击者;以后增加服务端时只需替换
`IIAPReceiptValidator`,交易和发奖链路无需重写。旧版无收据 pending 不会自动放行。

---

## 六、DevTools

游戏 `GameLifetimeScope` 注册 `DevToolsBootTask` 后,**DEV / 编辑器构建**下自动启用:

- **调试控制台**(IngameDebugConsole,三指下滑呼出)。
- **FPS / 内存角标**(`PerfOverlay`,左上角)。
- **作弊命令**:在任意 **静态方法** 上标 `[Cheat("命令名", "说明")]`(参数支持无参 / `int` / `float` / `string` / `bool`),
  框架扫描程序集自动注册到控制台。命令名重复或参数类型不支持的方法会被跳过并 `LogWarning`。

示例(见 TapRush Sample 的 `TapRushCheats.cs`):

```csharp
[Cheat("tap_set_highscore", "Set TapRush high score to value")]
public static void SetHighScore(int value) { /* ... */ }
```

**发布版**(非 `DEVELOPMENT_BUILD`)下,DevTools 逻辑被 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 编出,
公共 API 保留空壳 / no-op —— **零运行时开销**,且引用方编译恒定有效(不靠 `defineConstraints`,程序集始终参与编译)。

---

## 七、测试

| 层 | 策略 |
|---|---|
| Core / 服务业务逻辑 | 222 项 EditMode 单测(状态机、对象池、存档迁移、事件、UI、HTTP 幂等重试、IAP Pending/补单等) |
| 运行时生命周期 | 3 项 PlayMode 冒烟(淡出遮罩、音频宿主释放、正式广告安全关闭) |
| SDK 真机路径 | Fake 覆盖业务流转 + iOS/Android 商店沙盒与广告测试设备验收 |

运行:Unity Test Runner 的 EditMode / PlayMode 选项卡;CI 或本机可执行:

```bash
UNITY_EDITOR="/path/to/Unity" ./scripts/run-unity-tests.sh
```
薄视觉 / 平台层(`OnGUI` 角标、Cinemachine / PrimeTween 调用、UI 代码构建外观)不写单测,靠 PlayMode 冒烟把关。
