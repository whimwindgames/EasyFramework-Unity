# EasyFramework 设计文档

- 日期:2026-06-12
- 状态:已与所有者确认,待实现
- Unity 版本:6000.3.15f1(URP + 新 Input System)

## 1. 定位与需求

EasyFramework 是一个 **Unity 2D 小游戏复用底座**,目标是用同一套框架快速做多个小游戏。

| 维度 | 决定 |
|---|---|
| 开发场景 | 多个小游戏共用,做完一个直接复用到下一个 |
| 游戏类型 | 不限定玩法类型,保持通用 |
| 目标平台 | 移动端 iOS / Android |
| 商业化 | 完整商业化层:广告 + 内购 + 数据统计 + 远程配置 |
| 第三方库 | 拥抱成熟库,框架只写胶水层和业务抽象 |
| 使用者 | 高级 Unity/C# 开发者 |

成功标准:**从空项目到能跑的新游戏 < 10 分钟**(复制模板即跑);框架核心层可单元测试;广告/内购等 SDK 可整体替换而不改业务代码。

## 2. 总体架构

### 2.1 骨架风格:DI 内核 + 静态门面(混合方案)

- 底层用 **VContainer** 管理所有模块的注册、装配和生命周期。依赖图在启动时构建完成,初始化顺序确定,缺注册立即报错。
- 框架启动完成后,把核心服务绑定到静态门面 `G`(`G.UI` / `G.Audio` / `G.Save` …),小游戏业务代码通过门面快速开发;需要单测的核心逻辑仍可走构造注入。
- 门面为**手写薄层**(每服务一行属性 + 一行 Resolve),不引入代码生成。编辑器下对"未初始化即访问"给出友好报错。

### 2.2 分层与依赖规则

```
Game 层(每个小游戏自己的代码,不属于框架)
  ↓ 通过 G.Xxx 门面 或 构造注入
EasyFramework.Services(服务层,全部接口化)
  ↓
EasyFramework.Core(核心层,零 Unity 场景依赖,纯 C# 可单测)
  ↓
第三方库
```

三条铁律:

1. **只能向下依赖**:Core 不知道任何 Service,Service 不知道任何 Game 代码。
2. **服务先接口后实现**:`IAdsService` → `AdMobAdsService` / `FakeAdsService`,实现在 LifetimeScope 按平台/环境切换。
3. **框架内部禁用 `G.` 门面**,模块之间只走构造注入;门面只服务 Game 层。

### 2.3 目录与程序集

```
Assets/
├── EasyFramework/                   ← 框架本体,跨项目整体复制/将来发 UPM 包
│   ├── Core/         (EasyFramework.Core.asmdef)
│   ├── Services/     (EasyFramework.Services.asmdef)
│   ├── Monetization/ (EasyFramework.Monetization.asmdef)
│   ├── DevTools/     (EasyFramework.DevTools.asmdef)
│   └── _Template/    ← 新游戏模板(启动场景、GameLifetimeScope 模板、目录骨架)
├── Game/                            ← 当前小游戏代码(Game.asmdef)
└── ...
```

asmdef 编译隔离强制执行依赖规则;改业务代码不重编框架。

### 2.4 第三方库清单

| 库 | 用途 | 理由 |
|---|---|---|
| VContainer | DI 容器 | Unity 最快 DI,支持纯 C# 类生命周期(IStartable/ITickable/IDisposable) |
| UniTask | 异步 | 零分配 async/await |
| MessagePipe | 事件 | 与 VContainer 同生态,强类型,支持作用域 |
| PrimeTween | 动画/缓动 | 零分配,API 现代 |
| Addressables | 资源管理 | 官方,移动端内存管理必备 |
| Cinemachine | 相机 | 官方,2D 跟随/震屏开箱即用 |
| Unity Localization | 本地化 | 官方,文本表 + 资源表 |
| Newtonsoft Json(官方包) | 存档序列化 | 成熟稳定 |
| IngameDebugConsole | 真机调试 | 免费开源 |
| Unity IAP | 内购 | 官方 |
| 广告聚合 SDK(LevelPlay 或 AdMob,实现阶段定) | 广告 | 接口不锁死,适配器可换 |
| Firebase Analytics(首个统计后端) | 统计 | 行业标准 |

## 3. Core 核心层

纯 C#、无场景依赖、全部 EditMode 单测。

### 3.1 Bootstrap 启动管线

唯一入口为启动场景的 `RootLifetimeScope`(DontDestroyOnLoad):

```
RootLifetimeScope.Configure()   注册所有框架服务
→ GameBootstrap.StartAsync()    按序异步初始化:
    1. Save.LoadAsync()             存档最先
    2. Config / Localization        配置与语言
    3. Audio / UI / Input           表现层服务
    4. Ads / IAP / Analytics        商业化 SDK(并行初始化,失败不阻塞启动)
→ G.Initialize(resolver)        静态门面绑定
→ SceneService.LoadAsync(首场景)
```

关键决策:

- **商业化 SDK 初始化失败不阻塞游戏启动**:服务内部标记不可用,业务调用时返回明确失败结果。
- 每个小游戏只写一个 `GameLifetimeScope`(继承框架基类),作为 Root 的子作用域注册游戏特有服务。
- **分层自洽**:Core 只定义启动契约 `IBootTask`(含优先级与"失败是否阻塞"标记)和执行器 `GameBootstrap`;各服务在 Services/Monetization 层实现 `IBootTask`,由 `RootLifetimeScope` 注册时声明顺序。Core 始终不引用任何具体服务。

### 3.2 EventBus(MessagePipe 封装)

- 事件即类型,一律 `readonly struct`,零 GC。
- **全局事件**(Root Scope)与**场景事件**(场景 Scope)分离;订阅返回 IDisposable,场景卸载自动退订,从机制上堵死忘退订泄漏。

### 3.3 StateMachine

轻量泛型状态机 `StateMachine<TContext>`,状态为类(Enter/Exit/Update,支持异步 Enter)。两种用途:

1. 游戏流程:内置 `GameFlowMachine`(Boot → Menu → Gameplay → Pause → Result),每个游戏可增删状态,切换伴随场景/UI 联动。
2. 角色/Boss AI:Game 层复用同一实现。

**不做可视化编辑器**(YAGNI)。

### 3.4 ObjectPool

- `G.Pool.Spawn(key, pos)` / `Despawn(go)`;池子首次使用时经 AssetService 加载 Prefab,预热数量可配。
- `IPoolable.OnSpawn/OnDespawn` 钩子处理重置;场景卸载自动清池。

### 3.5 Timer

定时器/冷却;可选是否受 timeScale 影响;场景卸载自动清理。

## 4. Services 服务层

### 4.1 Asset(Addressables 封装)

- 每次加载记录句柄,**按场景分组**,场景卸载自动 Release 整组,解决忘释放导致内存上涨的问题。

### 4.2 Scene

- 异步场景加载:过渡遮罩(淡入淡出)、Loading 进度、加载前后钩子。

### 4.3 Save

- 单文件 JSON,`Application.persistentDataPath`,**临时文件 + 原子替换**防写坏。
- **版本迁移**:`Version` 字段 + 迁移函数链(v1→v2→v3),更新不丢档。
- **HMAC 校验**防手改(不上加密)。
- 自动落盘:`OnApplicationPause(true)`(移动端切后台)+ 关键节点手动 Save。
- 云存档 v1 不做,留 `ISaveBackend` 接口。

### 4.4 Config(含 RemoteConfig)

- ScriptableObject 本地默认值 + 远程配置覆盖:`G.Config.Get<float>("interstitial_cooldown", 30f)`。
- 远程拉取失败用本地值,**永不阻塞启动**。数值调优、广告频控、活动开关全走它。

### 4.5 UI 框架

四层结构(自上而下):

| 层 | 内容 |
|---|---|
| Overlay | 转场遮罩、飘字、全局 Toast |
| Popup | 确认框、奖励弹窗(队列化展示) |
| Window | 全屏界面,栈管理 |
| HUD | 战斗中常驻 UI |

- API:`await G.UI.Push<T>()` / `Pop()`;**弹窗带返回值**:`await G.UI.ShowPopupAsync<ConfirmPopup, bool>(...)`。
- **约定优于配置**:面板 = Prefab + 同名脚本,Addressables 按名加载,无注册表。
- 面板基类内置:安全区适配(刘海屏)、PrimeTween 入出场动画钩子、Android 返回键栈顶响应。
- **不上 MVVM**(YAGNI),需要刷新的地方用事件驱动。

### 4.6 Audio

- BGM/SFX 双通道、AudioSource 池、淡入淡出、音量设置持久化(存 Save)。

### 4.7 Input

- 新 Input System 之上提供**手势识别**(Tap / LongPress / Swipe 带方向 / Drag / Pinch),事件形式发布,自动处理 UI 穿透。
- **虚拟摇杆** UI 组件,与键盘/手柄输入合流为同一 Action,业务不感知输入来源。

### 4.8 Camera

- Cinemachine 2D:跟随、边界限制、震屏(Impulse)。

### 4.9 Tween / Juice

- PrimeTween 封装 + 手感套件:punch、闪白、顿帧(hitstop)、飘字。

### 4.10 Localization

- 封装 Unity Localization:文本表 + 资源表(图/音按语言切换)、运行时切语言广播事件、UI 组件自动刷新。
- 默认提供中英双语骨架。

### 4.11 Vibration

- iOS Haptic / Android Vibrator 统一接口,轻/中/重三档。

## 5. Monetization 商业化层

全部"接口 + 适配器",编辑器一律 Fake 实现。SDK 回调统一切回主线程。

### 5.1 Ads

```csharp
public interface IAdsService
{
    bool IsRewardedReady { get; }
    UniTask<AdResult> ShowRewardedAsync(string placement);
    UniTask<AdResult> ShowInterstitialAsync(string placement);
    void ShowBanner(BannerPosition pos);
    void HideBanner();
}
public enum AdResult { Completed, Skipped, NotReady, Failed }
```

- 业务只 `await` 结果,`Completed` 才发奖励;加载/重试/线程切换藏在适配器。
- 第一个适配器在 LevelPlay 与 AdMob 中按发行地区选定(实现阶段决定),接口不锁死。
- `FakeAdsService`:编辑器弹模拟弹窗,1 秒回调成功。
- 内置**插屏频控**(冷却 + 关卡间隔,可被远程配置覆盖)。

### 5.2 IAP

- 商品目录:ScriptableObject(id、消耗/非消耗、价格档、奖励内容)。
- `await G.IAP.PurchaseAsync(id)` 返回结果对象;发货可由目录配置的 Reward 自动执行,也可手写。
- 内置**恢复购买**(iOS 上架必须)与**掉单补发**(Pending 队列持久化到存档)。
- 票据校验 v1 本地校验,留 `IReceiptValidator` 接口。
- `FakeIAPService`:模拟成功/取消/失败可切换。

### 5.3 Analytics

- `G.Analytics.Track(name, params…)` → `IAnalyticsBackend` 列表广播;新增后端不改打点代码。
- **框架自动打标准事件**:启动、关卡开始/结束/失败、广告链路(请求→展示→完成)、支付链路。
- 编辑器 Backend 为控制台打印;真机在 DevTools 看事件流。

## 6. DevTools 与新游戏工作流

- 真机调试:IngameDebugConsole(三指下滑呼出)+ 作弊命令(`[Cheat("add_gold")]` 特性注册)+ FPS/内存角标。
- 新游戏 = 复制 `_Template/`(启动场景、GameLifetimeScope 模板、目录骨架),改名即跑。

## 7. 测试策略

| 层 | 策略 |
|---|---|
| Core | 全部 EditMode 单测(状态机、对象池、存档迁移、事件总线) |
| Services 关键路径 | PlayMode 测试(存档读写、UI 栈) |
| Monetization | Fake 实现覆盖业务流转 + 真机冒烟 |

## 8. 错误处理约定

1. 框架服务**不向业务层抛异常**,返回结果对象(如 `AdResult`、`PurchaseResult`)。
2. 所有 async 入口 try/catch 并上报 Analytics。
3. 第三方 SDK 异常只降级该服务,不崩游戏。

## 9. YAGNI 裁剪与扩展点

v1 明确不做,但留好接口:

| 不做 | 预留扩展点 |
|---|---|
| 云存档 | `ISaveBackend` |
| 服务端票据校验 | `IReceiptValidator` |
| MVVM/响应式 UI 绑定 | 面板基类事件钩子 |
| 状态机可视化编辑器 | —(代码即配置) |
| 热更新 | —(小游戏整包更新) |

## 10. 实现顺序建议(供计划阶段参考)

1. **Phase 1 骨架**:asmdef 结构、VContainer 接入、Bootstrap、G 门面、EventBus、StateMachine、Timer
2. **Phase 2 资源与数据**:Asset、Scene、Save、Config、ObjectPool
3. **Phase 3 表现层**:UI、Audio、Input、Camera、Tween/Juice、Localization、Vibration
4. **Phase 4 商业化**:Ads、IAP、Analytics、RemoteConfig 对接
5. **Phase 5 开发体验**:DevTools、_Template、示例小游戏验收(用框架做一个最小完整游戏)

## 11. 实现偏差摘要(各 Phase 累计)

本节汇总实现阶段相对本设计规格 / 各 Phase 计划的累计偏差,逐条注明 Phase、偏差内容、理由与影响。
所有偏差均为构建依赖修正、第三方 API/版本对齐、或测试可行性补缝,**未改动任何公共接口契约**(`G.*` 门面、各服务接口、`FrameworkInstaller`/`FrameworkOptions`、`IBootTask`、`StateMachine`/`State`、`UIPanel`/`UIPopup` 等签名一律不变)。
详细记录见各 Phase 计划文档末尾的 "Deviations" 小节,评审遗留的 minor 问题清单见 `docs/superpowers/known-minors.md`。

| Phase | 偏差 | 理由 / 影响 |
|---|---|---|
| Phase 1 | Tests asmdef 追加 `MessagePipe.VContainer` 引用(`EventBusTests` 调 `builder.RegisterMessagePipe()`,扩展方法所在程序集未引用导致 CS1061)。 | 仅构建依赖图修正;契约/测试/实现代码均未改,修复后 22 用例全绿。 |
| Phase 2 | (1) `Services.asmdef` 因 `overrideReferences: true` 关闭自动引用,追加 `UniTask.Addressables`(否则 `ToUniTask<T>()` 泛型重载不可见,`AddressablesAssetService` 拿到 void)。(2) `Tests.EditMode.asmdef` 同因追加 `MessagePipe.VContainer`。 | 两处均为第三方 API 接线层的 asmdef 引用修正;接口契约 / 实现逻辑 / 测试代码未动,源码未改,计划预留回退未采用。EditMode 48/48(框架自有)全绿。 |
| Phase 3a (UI) | (1) `FadeSceneTransition` 改工厂 lambda 注册(VContainer 不识别 C# 可选参数默认值,会去解析未注册的 `System.Single`)。(2) `UIRootBuilder` 新增 `DontDestroyHandler` 测试缝(`DontDestroyOnLoad` 在 EditMode 非法)。(3) Popup 计数测试按 `activeInHierarchy` 过滤、伪 prefab 模板 `SetActive(false)`、实例化后 `SetActive(true)`,贴近真实 prefab 语义。 | 均为 DI 注册写法 / EditMode 运行可行性补缝,`IUIService`/`ISceneTransition`/`UIPanel`/`UIPopup` 契约不变。EditMode 109/109 全绿。 |
| Phase 3b (表现层) | (1) Cinemachine 3.1.7 首次未落地 PackageCache,经 `resolve_packages` 重解析(非代码问题)。(2) PrimeTween 运行时程序集名是 `PrimeTween.Runtime`(非 `PrimeTween`),asmdef 引用名改正。(3) `LocalizedText` 不能直接引用 Boot 层 `G`(会致 `Services→Boot` 循环依赖),新增 Services 层 `LocalizationRuntime` 静态访问点由 `G.Initialize/Reset` 填充。(4) 音量按锁定契约存 `PlayerPrefs "ef.audio.*"`(spec 原文措辞为「存 Save」)。 | 第三方版本/程序集名对齐 + 分层契约疏漏补正;公共接口契约不变,`LocalizedText` 公共 API 与语义保持。安装版本:PrimeTween 1.3.3 / Cinemachine 3.1.7 / Input System 1.19.0。EditMode 109/109 全绿。 |
| Phase 4 (商业化) | (1) `AdsService` 改工厂 lambda 显式调 3 参生产构造(VContainer 自动选「参数最多」构造选中 internal 测试构造,去解析未注册的 `Func<float>` 抛异常)—— 同 Phase 3 的可选参数默认值模式。(2) Unity IAP 程序集名核对为 `Unity.Purchasing`,与计划一致无需调整;真实广告 / Firebase SDK 不接入,只交付「接口 + 业务层 + Fake + 接入槽(`#if EF_ADMOB`/`#if EF_FIREBASE`)」,`UnityIAPProvider` 仅真机路径、不写 EditMode 单测。 | DI 注册写法修正 + 无人值守模式无法配置原生 SDK;业务逻辑(频控/掉单/广播)全 Fake 覆盖。EditMode 132/132 全绿。 |
| Phase 5 (DevTools/模板/示例) | (1) DevTools 发布版零开销用代码内 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` 而非 asmdef `defineConstraints`(保证程序集始终编译、引用恒定有效,公共 API 在 `#if` 内外签名一致)。(2) TapRush UI 全部代码构建(无美术 prefab),经 prefab 化标 Addressable(`"ui/<TypeName>"`)走真实加载链路。(3) TapRush 点击音效用 Editor 程序化生成的 0.1s 正弦波 `AudioClip`,标 Addressable(`"audio/tap"`),补上 Phase 2 推迟的 Addressables 真实加载冒烟。(4) Cheat 控制台桥接(`IngameDebugConsole.DebugLogConsole.AddCommand`)、console prefab 实例化、Addressables Editor 标记 API 名以验证阶段 `unity_reflect` 核对实际安装版本为准。 | 示例游戏目标是打通框架各能力与 Addressables 真实加载,非美术产品;DevTools 零开销策略避免 `defineConstraints` 断引用。第三方 API 名最终以验证代理核对结果收口(见 Phase 5 计划末尾 "Deviations")。 |

### 评审遗留 minor 问题(不阻塞,记录在案)

各 Phase 评审发现、约定 Phase 5 收尾统一权衡的非阻塞问题,完整清单见 `docs/superpowers/known-minors.md`,要点摘录:

- **Phase 1**:StateMachine 同步 Enter 下版本守卫不生效(异步场景才有意义,保留);`TimerService.Cancel` O(n) 扫描;公共类型 XML 文档注释普遍缺失。
- **Phase 2**:`AddressablesAssetService` 的 `RefCount` 为死字段(`ReleaseScope` 直接整组释放);`PoolService.SpawnAsync` 以 `rotation == default` 判默认值而 `default(Quaternion)` 非法旋转;`JsonSaveService` 原子写未 fsync;`SaveOnPauseListener` 未挂 `OnApplicationQuit`(编辑器停播 / 桌面 Alt+F4 不落盘)。
- **Phase 3**:Audio/Input 走显式 Ticker 桥接而 UIService 直接实现 `ITickable`,风格不对称;`UIService.Dispose` 中 `PopAllAsync().Forget()` 与同步 Destroy 有顺序隐患;`InputService.IsPointerOverUI` 无参重载多点触控下只对最后指针有效;`CinemachineCameraService.Shake` 的 `duration` 被丢弃(由 Impulse 包络承载);`LocalizedText` 首帧文本可能延迟(已有 `IsInitialized` 守卫)。
- **Phase 4**:`IAPService` pending 队列以 `productId` 去重,同商品多次 Consumable 掉单只补一次;`UnityIAPProvider` 商店回调线程/时序假设未做主线程切换兜底(真机接入时验证)。

> 备注:本表 Phase 5 行记录的是计划锁定的设计性偏差;其中第三方 API 名的最终核对结果(IngameDebugConsole/Addressables Editor/AudioClip 持久化)由 Phase 5 验证阶段在该 Phase 计划文档末尾 "Deviations" 小节据实补全。
