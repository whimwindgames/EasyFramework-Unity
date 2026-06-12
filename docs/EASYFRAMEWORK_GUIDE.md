# EasyFramework 集成与使用指南

> 面向**用本框架做游戏的开发者 / AI 助手**。读完这一篇即可在任意 Unity 6 工程里用 EasyFramework 开发 2D 小游戏。
> 框架仓库:https://github.com/whimwindgames/EasyFramework-Unity · 包名 `com.yifei.easyframework` · Unity 6000.0+

---

## 0. 这是什么

EasyFramework 是一个 **Unity 2D 移动小游戏复用底座**。它把每个小游戏都要重写的东西(启动装配、UI 栈、存档、对象池、音频、输入、广告/内购/统计…)做成**接口化、可单测、SDK 可整体替换**的服务,你写游戏只管通过静态门面 `G.Xxx` 调用。

设计目标:**从空工程到能跑的新游戏 < 10 分钟**。

---

## 1. 安装(接进当前工程)

### 方式一:OpenUPM CLI(推荐,需 Node.js)
```bash
openupm add com.yifei.easyframework
```

### 方式二:手动配置 manifest.json
把下面合并进工程的 `Packages/manifest.json`(已有 OpenUPM 条目就只补缺的 scope):
```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": ["com.yifei", "com.cysharp", "jp.hadashikick", "com.kyrylokuzyk", "com.yasirkula"]
  }
]
```
然后 Package Manager → **Add package by name** → `com.yifei.easyframework`;或在 OpenUPM 收录前直接用 git URL(依赖仍由上面 registry 解析):
```
"com.yifei.easyframework": "https://github.com/whimwindgames/EasyFramework-Unity.git?path=Packages/com.yifei.easyframework#v0.1.0"
```

装好后,Package Manager 选中 EasyFramework → **Samples** 标签可导入 **TapRush**(完整示例)和 **Template**(新游戏模板)。

---

## 2. 架构与三条铁律

```
你的游戏代码(Game 层)
   │  用 G.Xxx 门面调用,或构造注入
   ▼
EasyFramework.Services(服务层,全接口化)   +   EasyFramework.Monetization
   │
   ▼
EasyFramework.Core(纯 C# 核心,零场景依赖,可单测)
   │
   ▼
第三方库(VContainer / UniTask / MessagePipe / Addressables / PrimeTween / Cinemachine / Input System / TMP)

EasyFramework.Boot = 组合根(RootLifetimeScope / G 门面 / FrameworkInstaller)
```

**三条铁律(写代码前必读):**
1. **只能向下依赖**:Core 不认识 Service,Service 不认识你的 Game 代码。
2. **服务先接口后实现**:`IAdsService` → `FakeAdsProvider` / 真实 SDK 适配器,在 LifetimeScope 按平台切换。
3. **框架内部禁用 `G.` 门面**(模块间一律构造注入);`G` 只给你的 Game 层用。

**启动时序**(`RootLifetimeScope` 挂在 Boot 场景的常驻节点上):
```
FrameworkInstaller.Install   注册全部服务
   → GameBootstrap          按 IBootTask.Priority 升序异步初始化(Save→Config→表现层→商业化)
   → G.Initialize           绑定门面
   → 发布 BootCompletedEvent  ← 你的游戏在这之后才开始
```
> ⚠️ **`G.Xxx` 在 `BootCompletedEvent` 之后才保证可用。** 早期入口(如作弊命令)用 `G.IsInitialized` 守卫。

---

## 3. G 门面完整 API

> 业务代码直接用 `G.Xxx`。命名空间 `using EasyFramework;`。所有 await 返回 `UniTask`。

| 门面 | 接口 | 方法 |
|---|---|---|
| `G.Events` | `IEventBus` | `Publish<T>(evt)` · `Subscribe<T>(handler)` → `IDisposable`(Dispose 退订) |
| `G.Timer` | `ITimerService` | `Schedule(delay, callback, repeat=false, useUnscaledTime=false)` → `TimerHandle` · `Cancel(handle)` |
| `G.Asset` | `IAssetService` | `await LoadAsync<T>(key, scope=AssetScope.Scene)` · `ReleaseScope(scope)` |
| `G.Scene` | `ISceneService` | `await LoadAsync(sceneName, progress?)` · `CurrentScene` |
| `G.Save` | `ISaveService` | `await LoadAsync()` · `Save()` · `Data<T>()`(T : SaveData) |
| `G.Config` | `IConfigService` | `Get<T>(key, defaultValue)` · `Has(key)` |
| `G.Pool` | `IPoolService` | `await SpawnAsync(key, pos?, rot?, parent?)` → GameObject · `Despawn(go)` · `await PrewarmAsync(key, count)` |
| `G.UI` | `IUIService` | `await PushAsync<W>()` · `PopAsync()` · `PopAllAsync()` · `await ShowPopupAsync<P, R>()` · `await ShowHudAsync<H>()` · `HideHudAsync()` · `WindowCount` |
| `G.Audio` | `IAudioService` | `await PlayBgmAsync(key, fade=0.5f)` · `StopBgm(fade=0.3f)` · `PlaySfx(key, vol=1f)` · `BgmVolume` · `SfxVolume` |
| `G.Input` | `IInputService` | `MoveAxis`(摇杆+WASD 合流)· `IsPointerOverUI` |
| `G.Camera` | `ICameraService` | `Follow(transform)` · `SetBounds(bounds)` · `Shake(intensity, duration)` |
| `G.Juice` | `IJuiceService` | `PunchScale(t, strength=0.2f, dur=0.25f)` · `Flash(spriteRenderer, color, dur=0.1f)` · `await HitStopAsync(dur=0.05f)` |
| `G.Loc` | `ILocalizationService` | `Get(key)` · `await SetLocaleAsync(localeCode)` · `CurrentLocale` |
| `G.Haptics` | `IHapticsService` | `Vibrate(HapticStrength.Light/Medium/Heavy)` · `Enabled` |
| `G.Ads` | `IAdsService` | `await ShowRewardedAsync(placement)` → `AdResult` · `await ShowInterstitialAsync(placement)` · `ShowBanner(pos)` · `HideBanner()` · `IsRewardedReady` |
| `G.IAP` | `IIAPService` | `await PurchaseAsync(productId)` → `PurchaseResult` · `await RestoreAsync()` · `IsOwned(productId)` |
| `G.Analytics` | `IAnalyticsService` | `Track(name)` · `Track(name, (key, value)...)` · `SetUserProperty(key, value)` |

**输入手势**经 `G.Events` 广播(不是 `G.Input` 上的方法):
```csharp
G.Events.Subscribe<TapEvent>(e => Shoot(e.ScreenPosition));
// 其它:LongPressEvent, SwipeEvent(.Start/.End/.Direction), DragEvent(.Phase/.Position/.Delta), PinchEvent(.DeltaScale)
```

---

## 4. 新游戏起手式(标准流程)

1. **导入 Template Sample**(Package Manager → EasyFramework → Samples → Import "Template"),把导入后的目录复制改名为 `Assets/<YourGame>/`。
2. 改命名空间 `EasyFramework.Template` → `<YourGame>`,类名 `Template*` → `<YourGame>*`。
3. **建 Boot 场景**:新建场景,放一个 `[EasyFramework]` GameObject 挂 `RootLifetimeScope`;**注册你的存档类型**(见 §5 关键坑)。在其下建子 GameObject 挂你的 `<YourGame>LifetimeScope`(嵌套 `LifetimeScope` 自动成为 Root 的子作用域,在这里注册你游戏特有的服务/RewardHandler)。把 Boot 场景设为 Build Settings 第 0 个。
4. 用 `StateMachine<TContext>` 写游戏流程(Menu / Gameplay / Result),用 `G.UI` 建界面,`G.Pool` 管玩法对象,`G.Save` 存档。
5. Play。`BootCompletedEvent` 发布后进入 Menu。

> **最省事的学习法**:导入 TapRush Sample,直接读它的代码——它是上述所有能力的完整可运行范例。

---

## 5. 关键坑与约定(务必知道)

### 5.1 存档类型必须注册(最常见的崩溃点)
`G.Save.Data<T>()` 默认用框架的 `DefaultSaveData`。如果你 `G.Save.Data<MyGameSave>()` 但没注册 `MyGameSave` 类型,会**强转崩溃**。解决:让你的 `RootLifetimeScope` 子类覆写 `GetSaveProfile()` 返回你的 profile,或通过 `FrameworkOptions.SaveProfile` 传入。参考 TapRush 的 `TapRushRootLifetimeScope`。

```csharp
public sealed class MyRootLifetimeScope : RootLifetimeScope
{
    protected override SaveProfile GetSaveProfile() => new SaveProfile
    {
        DataType = typeof(MyGameSave),
        CurrentVersion = 1,
        CreateNew = () => new MyGameSave(),
        Migrations = new List<ISaveMigration>(),
        FileName = "save.json",
    };
}
```

### 5.2 UI 面板约定
面板 = 继承 `UIPanel`(或带返回值的 `UIPopup<TResult>`)的脚本 + **同名 prefab**。Addressables 按 `"ui/<类名>"` 约定加载(如 `MenuWindow` → key `"ui/MenuWindow"`),**无注册表**。面板生命周期:`OnSetup(args)` → `PlayEnter()` →(显示)→ `PlayExit()` →(销毁)。
- **Window** = 栈(Push 盖上、Pop 回退,Push 时隐藏下层)
- **Popup** = 队列 + 带返回值(`await G.UI.ShowPopupAsync<ConfirmPopup, bool>()` 拿用户选择)
- **HUD** = 单实例常驻

### 5.3 商业化只 await 结果,Completed 才发奖
```csharp
var r = await G.Ads.ShowRewardedAsync("double_coins");
if (r == AdResult.Completed) GrantReward();   // Skipped/NotReady/Failed 都不发
```
编辑器里 Fake 实现秒回成功,**不接真 SDK 也能跑通**。接真 SDK:`#if EF_ADMOB`(广告)/ `#if EF_FIREBASE`(统计),在 `GameLifetimeScope` 覆盖注册对应 Provider。

### 5.4 事件订阅要退订
`Subscribe` 返回 `IDisposable`。在 `OnDisable`/`OnDestroy` 里 Dispose,或用场景作用域事件(场景卸载自动退订)。

### 5.5 对象池
`G.Pool.SpawnAsync(key, ...)` 首次会经 Asset 服务加载 prefab。实例上实现 `IPoolable.OnSpawn/OnDespawn` 做重置。场景卸载自动清池。

---

## 6. 常用配方(copy-paste)

```csharp
using EasyFramework;
using Cysharp.Threading.Tasks;

// —— 存档:读最高分、写回 ——
int hi = G.Save.Data<MyGameSave>().HighScore;
G.Save.Data<MyGameSave>().HighScore = newScore;
G.Save.Save();

// —— UI:开始按钮 push 游戏 HUD ——
await G.UI.ShowHudAsync<GameHud>();
bool quit = await G.UI.ShowPopupAsync<ConfirmPopup, bool>("确定退出?");

// —— 对象池 + 手势:点哪生成哪 ——
G.Events.Subscribe<TapEvent>(async e => {
    var go = await G.Pool.SpawnAsync("Circle", ScreenToWorld(e.ScreenPosition));
});

// —— 音效 + 手感 + 震动:打击反馈 ——
G.Audio.PlaySfx("hit");
G.Juice.PunchScale(transform);
G.Haptics.Vibrate(HapticStrength.Light);

// —— 配置:可远程覆盖的数值 ——
float cooldown = G.Config.Get("skill_cooldown", 3f);

// —— 打点 ——
G.Analytics.Track("level_complete", ("level", level), ("score", score));

// —— 游戏流程状态机骨架 ——
public sealed class GameFlow
{
    readonly StateMachine<GameFlow> _fsm;
    public GameFlow() {
        _fsm = new StateMachine<GameFlow>(this);
        _fsm.AddState(new MenuState());
        _fsm.AddState(new GameplayState());
        _fsm.AddState(new ResultState());
    }
    public UniTask ToMenu() => _fsm.ChangeState<MenuState>();
    public UniTask ToGameplay() => _fsm.ChangeState<GameplayState>();
    public UniTask ToResult() => _fsm.ChangeState<ResultState>();
    public State<GameFlow> Current => _fsm.Current;
    public void Tick(float dt) => _fsm.Update(dt);
}
```

---

## 7. 开发期工具(DevTools)

DEV / 编辑器构建下自动启用(发布版零开销):调试控制台(三指下滑)、FPS/内存角标、作弊命令。加作弊:
```csharp
[Cheat("add_gold", "加金币")]
public static void AddGold(int n) { /* ... */ }   // 静态方法,框架自动扫描注册
```

---

## 8. 模块清单速查

- **Core**:EventBus · StateMachine · ObjectPool · Timer · Boot 管线
- **Services**:Asset(Addressables) · Scene(过渡) · Save(HMAC+迁移+原子写) · Config(远程覆盖) · UI(四层栈) · Audio(交叉淡入) · Input(手势+虚拟摇杆) · Camera(Cinemachine) · Juice(手感) · Localization · Haptics
- **Monetization**:Ads(频控) · IAP(恢复购买+掉单补发) · Analytics(多后端)
- **DevTools**:Cheat · PerfOverlay · 调试控制台

完整 API 与设计细节见包内 `README.md`(Package Manager 详情页)和源码接口文件。
