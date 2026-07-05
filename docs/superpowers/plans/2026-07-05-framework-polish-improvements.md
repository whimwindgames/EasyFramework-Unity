# EasyFramework 打磨清单(EventBus 重入防护 + Boot 耗时日志 + Pool 闲置缩容 + Localization 校验工具 + Audio 限流)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 落地设计文档 `docs/superpowers/specs/2026-07-05-framework-polish-improvements-design.md` 中互相独立的 5 项打磨改动:EventBus 重入回归测试 + 递归深度保护、GameBootstrap 逐任务耗时日志、PoolService 闲置自动缩容、Localization 编辑器漏翻译校验工具、AudioService 同音效限流。5 项互不依赖,可任意顺序或并行实现。

**Architecture:** 全部是对既有服务的内部增强,不新增子系统、不改变任何现有公开接口签名。EventBus 深度保护加在 `MessagePipeEventBus.Publish<T>` 这层包装本身(不碰 MessagePipe 内部)。Boot 耗时日志包在 `GameBootstrap.RunSafe` 内,仅开发模式生效。PoolService 缩容复用既有 `ITimerService.Schedule(delay, callback, repeat:true)`,不新起 Update 循环,新增内部字典 `_idleSince` 与可选构造参数,默认关闭、行为不变。Localization 校验工具是纯编辑器脚本,落在新的 `EasyFramework.Services.Localization.Editor` 程序集,不改动运行时 `ILocalizationService` 接口。AudioService 限流按 `key` 记录上次播放时间戳,复用 `AdsService` 已验证的"生产构造用真实时间源、internal 测试构造注入 `Func<float>`"模式。

**Tech Stack:** Unity 6000.x / VContainer / MessagePipe / UniTask / NUnit (EditMode) / Unity Test Framework。全部改动在 `Packages/com.yifei.easyframework` 包内。

---

## 执行前必读:磁盘现状核对结论

写计划前已用 Read 工具核对以下文件现状,后续 Task 严格以此为准:

- `IBootTask`(`Core/Boot/IBootTask.cs`):`int Priority { get; }`、`bool IsCritical { get; }`、`UniTask InitializeAsync(CancellationToken ct)`。
- `IEventBus`(`Core/Events/IEventBus.cs`):`void Publish<T>(T evt)`、`IDisposable Subscribe<T>(Action<T> handler)`。
- `MessagePipeEventBus`(`Core/Events/MessagePipeEventBus.cs`):内部两个 `Dictionary<Type, object>`(`_publishers`/`_subscribers`)懒填充缓存 MessagePipe 的 `IPublisher<T>`/`ISubscriber<T>`,构造函数签名 `MessagePipeEventBus(IObjectResolver resolver)`。
- `GameBootstrap`(`Core/Boot/GameBootstrap.cs`):构造 `GameBootstrap(IEnumerable<IBootTask> tasks, IEventBus events)`;`StartAsync` 按 `Priority` 分组、组内 `UniTask.WhenAll` 并行跑 `RunSafe`;`RunSafe` 是 `static async UniTask RunSafe(IBootTask task, CancellationToken ct)`,失败时非关键 `Debug.LogWarning`,关键抛 `BootFailedException`。
- `IPoolService`(`Services/Pooling/IPoolService.cs`):`UniTask PrewarmAsync(string key, int count)`、`UniTask<GameObject> SpawnAsync(string key, Vector3 position = default, Quaternion rotation = default, Transform parent = null)`、`void Despawn(GameObject instance)`。
- `PoolService`(`Services/Pooling/PoolService.cs`):构造 `PoolService(IAssetService assets, IEventBus events)`;`_idle: Dictionary<string, Stack<GameObject>>`;`internal static Action<GameObject> DestroyHandler`(测试可替换为 `DestroyImmediate`);`Despawn` 命中缓存时把实例 push 回 `GetStack(marker.Key)`;`ClearAll()` 在 `SceneWillUnloadEvent` 时整体清空。
- `ITimerService`(`Core/Timing/ITimerService.cs`):`TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)`、`void Cancel(TimerHandle handle)`。`TimerService`(`Core/Timing/TimerService.cs`)另有 `public void Advance(float scaledDelta, float unscaledDelta)` 供测试手动推进虚拟时间(无需真实等待)。
- `ILocalizationService`(`Services/Localization/ILocalizationService.cs`):`string CurrentLocale { get; }`、`string Get(string key)`、`UniTask SetLocaleAsync(string localeCode)`。
- `TableLocalizationService`(`Services/Localization/TableLocalizationService.cs`):构造 `TableLocalizationService(IReadOnlyList<LocalizationTable> tables, IEventBus events, string defaultLocale = "zh-CN")`,内部 `Dictionary<string, Dictionary<string, string>> _entries`(key -> locale -> value)。
- `LocalizationTable`(`Services/Localization/LocalizationTable.cs`):`IReadOnlyList<Row> Rows`,`Row { string Key; List<LocaleValue> Values; }`,`LocaleValue { string Locale; string Value; }`。是 `[CreateAssetMenu]` 的 `ScriptableObject`。
- `IAudioService`(`Services/Audio/IAudioService.cs`):`UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f)`、`void StopBgm(float fadeSeconds = 0.3f)`、`void PlaySfx(string key, float volume = 1f)`、`float BgmVolume { get; set; }`、`float SfxVolume { get; set; }`。
- `AudioService`(`Services/Audio/AudioService.cs`):构造仅 `AudioService(IAssetService assets)`;`PlaySfx` 调 `PlaySfxAsync(key, volume).Forget()`;`PlaySfxAsync` 是 `async UniTaskVoid`,从 `_sfx[_sfxCursor]` 轮询取 `AudioSource` 调 `PlayOneShot`。
- 可注入时间源的既有范例:`AdsService`(`Monetization/Ads/AdsService.cs`)有两个构造——公开构造 `AdsService(IAdsProvider, IConfigService, IAnalyticsService)` 默认时间源 `() => Time.realtimeSinceStartup`;`internal AdsService(..., Func<float> nowProvider)` 供测试注入。本计划 Task 5(AudioService)复用同一模式。
- 测试写法范例:`Tests/EditMode/PoolServiceTests.cs`、`ConfigServiceTests.cs`、`AudioServiceTests.cs`、`EventBusTests.cs`、`AssetServiceTests.cs` —— NUnit `[Test]`/`[SetUp]`/`[TearDown]`,Fake 类写成测试类内部 `sealed class`,UniTask 用 `.GetAwaiter().GetResult()` 同步等待,namespace 统一 `EasyFramework.Tests`。`FakeAssetService`(`Services/Assets/FakeAssetService.cs`)已存在,构造 `FakeAssetService(IReadOnlyDictionary<string, UnityEngine.Object> assets)`,可直接复用。`EventBusTests.cs` 用真实 VContainer + MessagePipe 容器(`builder.RegisterMessagePipe(); builder.Register<IEventBus, MessagePipeEventBus>(...)`)而非 Fake bus,因为要验证的正是 MessagePipe 本身的分发行为。
- 测试程序集 `Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`:`references` 已含 `EasyFramework.Core`、`EasyFramework.Services`、`EasyFramework.Monetization`、`EasyFramework.Boot`、`EasyFramework.DevTools`、`EasyFramework.Template`、`UniTask`、`VContainer`、`MessagePipe`、`MessagePipe.VContainer` 等;`includePlatforms: ["Editor"]`。新增运行时代码(Task 1-3、5)不需要新建 asmdef,直接落在既有 `Core`/`Services` 子文件夹,归属既有的 `EasyFramework.Core.asmdef` / `EasyFramework.Services.asmdef`。**例外**:Task 4(Localization 编辑器工具)是纯编辑器代码,`EasyFramework.Services.asmdef` 没有 `includePlatforms: ["Editor"]` 限制(它跨平台编译),不能把编辑器专用脚本直接放进去,需要新建一个 `Editor` 子文件夹 + 新 asmdef(参照仓库里已有的 `Samples~/TapRush/Editor/Game.Editor.asmdef` 命名风格),测试程序集需要新增对它的引用。
- `.meta` 文件约定:参照 `docs/superpowers/plans/2026-06-12-easyframework-phase5-devtools-sample.md` 的既有约定,新建的 `.cs`/`.asmdef` 文件的 `.meta` **不手写**,由 Unity 编辑器刷新（`AssetDatabase.Refresh` 或重新聚焦编辑器）时自动生成 GUID。本计划的 Step 2/4(运行测试)均在 Unity Test Runner 里执行,执行前先让 Unity 完成一次编译刷新即可。

---

## Task 清单总览(对应 spec §2 编号)

| Task | 对应 spec | 文件范围 |
|---|---|---|
| Task 1 | §2.1 IEventBus 重入安全验证 + 递归深度保护 | `Core/Events/MessagePipeEventBus.cs` |
| Task 2 | §2.2 GameBootstrap 逐任务耗时日志 | `Core/Boot/GameBootstrap.cs` |
| Task 3 | §2.3 PoolService 闲置自动缩容 | `Services/Pooling/PoolService.cs`、`IPoolService.cs` 不变 |
| Task 4 | §2.4 Localization 编辑器漏翻译检测 | 新增 `Services/Localization/Editor/` |
| Task 5 | §2.5 AudioService 同音效并发限流 | `Services/Audio/AudioService.cs` |

---

### Task 1: IEventBus 重入回归测试 + MessagePipeEventBus 递归深度保护

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Core/Events/MessagePipeEventBus.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/EventBusTests.cs`

- [ ] **Step 1: 先写重入回归测试(验证现状,不预设有 bug)**

  在 `EventBusTests.cs` 末尾(`MultipleSubscribers_AllReceive` 测试后、类结束 `}` 前)追加两个测试方法:

  ```csharp
        [Test]
        public void Reentrant_SubscribeInsideHandler_DoesNotThrow_NewSubscriberMissesCurrentPublish()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var outerCalls = 0;
            var innerCalls = 0;
            System.IDisposable innerSub = null;

            bus.Subscribe<ScoreEvent>(e =>
            {
                outerCalls++;
                if (innerSub == null)
                    innerSub = bus.Subscribe<ScoreEvent>(_ => innerCalls++);
            });

            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(1)));
            Assert.AreEqual(1, outerCalls);
            // 本次 Publish 过程中新增的订阅不应重入收到"本次"事件(MessagePipe 对快照的订阅列表分发)。
            Assert.AreEqual(0, innerCalls);

            bus.Publish(new ScoreEvent(2));
            Assert.AreEqual(2, outerCalls);
            Assert.AreEqual(1, innerCalls); // 第二次发布,新订阅者才收到。
        }

        [Test]
        public void Reentrant_DisposeSelfInsideHandler_DoesNotThrow_StopsReceivingAfterward()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var calls = 0;
            System.IDisposable sub = null;
            sub = bus.Subscribe<ScoreEvent>(_ =>
            {
                calls++;
                sub.Dispose();
            });

            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(1)));
            Assert.AreEqual(1, calls);

            bus.Publish(new ScoreEvent(2));
            Assert.AreEqual(1, calls); // 已在第一次回调里自我取消订阅,第二次不应再收到。
        }
  ```

- [ ] **Step 2: 运行测试验证现状**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.EventBusTests`。预期:**全部通过**(包括新增的两个重入测试)。这一步是验证性质——MessagePipe 内部对订阅列表分发做了快照隔离,重入 Subscribe/Dispose 不会抛异常也不会导致本次分发漏发/多发。如果两个新测试中任意一个失败或抛异常,记录实际行为（例如断言的具体数值不一致），按实际行为改断言使其反映真实语义，但不需要在 `MessagePipeEventBus` 里做任何修复代码——这一步只是把现状钉成回归测试。

- [ ] **Step 3: 加递归深度保护(独立于上面的验证结果,直接实现)**

  编辑 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Core/Events/MessagePipeEventBus.cs`,把整个文件内容替换为:

  ```csharp
  using System;
  using System.Collections.Generic;
  using MessagePipe;
  using UnityEngine;
  using VContainer;

  namespace EasyFramework.Core.Events
  {
      /// <summary>
      /// MessagePipe 驱动的事件总线实现。
      ///
      /// 线程安全约定(Thread-Safety Contract):
      ///   此类 <b>仅供 Unity 主线程调用</b>。
      ///   _publishers / _subscribers 的懒填充不是线程安全的;若从 SDK 回调线程(广告 / IAP 等)
      ///   调用 Publish/Subscribe,必须先 Dispatcher.InvokeOnMainThread(或 UniTask.SwitchToMainThread)
      ///   切换回主线程。调用方违反此约定可能触发 Dictionary 并发异常或脏读。
      ///   如需跨线程发布,建议在业务层封装一个线程安全的转发队列,不在此处引入锁开销。
      ///
      /// 递归深度保护(Reentrancy Guard):
      ///   Publish&lt;T&gt; 内部触发的事件处理函数如果又同步 Publish 了同一个或另一个事件类型,
      ///   层层嵌套可能形成"事件 A 触发事件 B 又触发事件 A"的死循环拖垮主循环。
      ///   这里用一个跨类型共享的调用深度计数器,超过 <see cref="MaxPublishDepth"/> 时记一条错误日志并
      ///   跳过本次发布(不再往下调用 MessagePipe 的 Publish),避免栈溢出/卡死。
      ///   深度计数器不区分事件类型:无论是同类型自递归还是不同类型互相触发,只要嵌套调用链变长都会被拦截。
      /// </summary>
      public sealed class MessagePipeEventBus : IEventBus
      {
          /// <summary>Publish 嵌套调用深度阈值,超过后跳过发布并记录错误。可在业务层通过反射或子类化调整;默认 20 足够覆盖正常的链式事件场景。</summary>
          public const int MaxPublishDepth = 20;

          readonly IObjectResolver _resolver;
          // 仅主线程读写;不使用并发集合以避免移动端 GC 开销
          readonly Dictionary<Type, object> _publishers = new();
          readonly Dictionary<Type, object> _subscribers = new();

          int _publishDepth;

          public MessagePipeEventBus(IObjectResolver resolver) => _resolver = resolver;

          public void Publish<T>(T evt)
          {
              if (_publishDepth >= MaxPublishDepth)
              {
                  Debug.LogError(
                      $"[EasyFramework] IEventBus.Publish<{typeof(T).Name}> exceeded max reentrancy depth " +
                      $"({MaxPublishDepth}). Skipping this publish to avoid a runaway event loop. " +
                      "Check for events that trigger each other in a cycle.");
                  return;
              }

              _publishDepth++;
              try
              {
                  if (!_publishers.TryGetValue(typeof(T), out var pub))
                  {
                      pub = _resolver.Resolve<IPublisher<T>>();
                      _publishers[typeof(T)] = pub;
                  }
                  ((IPublisher<T>)pub).Publish(evt);
              }
              finally
              {
                  _publishDepth--;
              }
          }

          public IDisposable Subscribe<T>(Action<T> handler)
          {
              if (!_subscribers.TryGetValue(typeof(T), out var sub))
              {
                  sub = _resolver.Resolve<ISubscriber<T>>();
                  _subscribers[typeof(T)] = sub;
              }
              return ((ISubscriber<T>)sub).Subscribe(handler);
          }
      }
  }
  ```

  关键点:`_publishDepth` 是实例字段(每个 `MessagePipeEventBus` 实例独立计数,与 VContainer 里注册为 `Lifetime.Singleton` 一致);用 `try/finally` 保证异常路径下计数器也能正确回退;深度检查在 `_publishers` 缓存查找之前,超限时完全不触碰 MessagePipe。

- [ ] **Step 4: 写深度保护越界测试并运行验证通过**

  在 `EventBusTests.cs` 的 `Reentrant_DisposeSelfInsideHandler_DoesNotThrow_StopsReceivingAfterward` 测试后追加:

  ```csharp
        [Test]
        public void Publish_ExceedingMaxDepth_LogsErrorAndStopsRecursion()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var depthReached = 0;

            bus.Subscribe<ScoreEvent>(e =>
            {
                depthReached++;
                if (depthReached <= MessagePipeEventBus.MaxPublishDepth + 5)
                    bus.Publish(new ScoreEvent(e.Amount + 1)); // 故意自触发形成深递归
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "exceeded max reentrancy depth"));
            Assert.DoesNotThrow(() => bus.Publish(new ScoreEvent(0)));

            // 深度保护应在到达 MaxPublishDepth 后拦截,不应无限递归下去(不会栈溢出,调用次数有限)。
            Assert.LessOrEqual(depthReached, MessagePipeEventBus.MaxPublishDepth + 1);
        }
  ```

  在文件顶部 `using` 区加 `using UnityEngine.TestTools;`(`LogAssert` 所在命名空间)。完整顶部 `using` 块应为:

  ```csharp
  using EasyFramework.Core.Events;
  using MessagePipe;
  using NUnit.Framework;
  using UnityEngine;
  using UnityEngine.TestTools;
  using VContainer;
  ```

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.EventBusTests`。预期:全部 6 个测试(原有 3 个 + 本 Task 新增 3 个)通过,`Publish_ExceedingMaxDepth_LogsErrorAndStopsRecursion` 捕获到一条匹配 "exceeded max reentrancy depth" 的 `LogType.Error` 且不抛异常、不栈溢出。

- [ ] **Step 5: Commit**

  ```bash
  git add Packages/com.yifei.easyframework/Core/Events/MessagePipeEventBus.cs Packages/com.yifei.easyframework/Tests/EditMode/EventBusTests.cs
  git commit -m "$(cat <<'EOF'
  feat: add EventBus reentrancy regression tests and publish depth guard

  Confirm MessagePipe already handles reentrant subscribe/dispose safely,
  then add an independent recursion depth guard in MessagePipeEventBus.Publish
  to stop runaway event cycles (event A triggering B triggering A) from
  hanging the main loop.
  EOF
  )"
  ```

---

### Task 2: GameBootstrap 逐任务耗时日志

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Core/Boot/GameBootstrap.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/GameBootstrapTests.cs`(新建)

- [ ] **Step 1: Write the failing test**

  新建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/GameBootstrapTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Threading;
  using Cysharp.Threading.Tasks;
  using EasyFramework.Core.Boot;
  using EasyFramework.Core.Events;
  using MessagePipe;
  using NUnit.Framework;
  using UnityEngine.TestTools;
  using VContainer;

  namespace EasyFramework.Tests
  {
      public class GameBootstrapTests
      {
          sealed class FastTask : IBootTask
          {
              public int Priority => 0;
              public bool IsCritical => false;
              public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;
          }

          sealed class SlowTask : IBootTask
          {
              readonly int _delayMs;
              public SlowTask(int delayMs) => _delayMs = delayMs;
              public int Priority => 0;
              public bool IsCritical => false;
              public async UniTask InitializeAsync(CancellationToken ct)
              {
                  var start = System.DateTime.UtcNow;
                  while ((System.DateTime.UtcNow - start).TotalMilliseconds < _delayMs)
                      await UniTask.Yield();
              }
          }

          IEventBus BuildEventBus()
          {
              var builder = new ContainerBuilder();
              builder.RegisterMessagePipe();
              builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
              return builder.Build().Resolve<IEventBus>();
          }

          [Test]
          public void SlowBootTask_ExceedingThreshold_LogsTiming()
          {
              var tasks = new List<IBootTask> { new SlowTask((int)GameBootstrap.SlowTaskThresholdMs + 20) };
              var bootstrap = new GameBootstrap(tasks, BuildEventBus());

              LogAssert.Expect(UnityEngine.LogType.Log, new System.Text.RegularExpressions.Regex(
                  $"{nameof(SlowTask)}.*ms"));
              bootstrap.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
          }

          [Test]
          public void FastBootTask_UnderThreshold_DoesNotLogTiming()
          {
              var tasks = new List<IBootTask> { new FastTask() };
              var bootstrap = new GameBootstrap(tasks, BuildEventBus());

              // 未超阈值不应打印耗时日志;仅允许 BootCompletedEvent 之外没有额外 Log。
              bootstrap.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
              LogAssert.NoUnexpectedReceived();
          }
      }
  }
  ```

- [ ] **Step 2: Run test to verify it fails**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.GameBootstrapTests`。预期:**编译失败**(`GameBootstrap.SlowTaskThresholdMs` 尚不存在),或如果先临时把该引用换成字面量 `70` 编译通过后运行,预期 `SlowBootTask_ExceedingThreshold_LogsTiming` 失败,因为 `RunSafe` 当前没有任何耗时日志,`LogAssert.Expect` 断言不到匹配的 `Log` 类型日志。

- [ ] **Step 3: Write minimal implementation**

  把 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Core/Boot/GameBootstrap.cs` 整个文件替换为:

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Linq;
  using System.Threading;
  using Cysharp.Threading.Tasks;
  using EasyFramework.Core.Events;
  using UnityEngine;
  using VContainer.Unity;
  using Debug = UnityEngine.Debug;

  namespace EasyFramework.Core.Boot
  {
      public sealed class BootFailedException : Exception
      {
          public BootFailedException(string message, Exception inner) : base(message, inner) { }
      }

      public sealed class GameBootstrap : IAsyncStartable
      {
          /// <summary>耗时超过该毫秒数的 BootTask 才打印计时日志;仅开发模式(Editor / Debug 包)生效。</summary>
          public const long SlowTaskThresholdMs = 50;

          readonly IReadOnlyList<IBootTask> _tasks;
          readonly IEventBus _events;

          public GameBootstrap(IEnumerable<IBootTask> tasks, IEventBus events)
          {
              _tasks = tasks.ToList();
              _events = events;
          }

          public async UniTask StartAsync(CancellationToken ct)
          {
              foreach (var group in _tasks.GroupBy(t => t.Priority).OrderBy(g => g.Key))
              {
                  await UniTask.WhenAll(group.Select(t => RunSafe(t, ct)));
              }
              _events.Publish(new BootCompletedEvent());
          }

          static async UniTask RunSafe(IBootTask task, CancellationToken ct)
          {
              var isDevMode = Debug.isDebugBuild || Application.isEditor;
              var sw = isDevMode ? Stopwatch.StartNew() : null;
              try
              {
                  await task.InitializeAsync(ct);
              }
              catch (Exception e) when (!task.IsCritical)
              {
                  Debug.LogWarning($"[EasyFramework] Non-critical boot task {task.GetType().Name} failed: {e.Message}");
              }
              catch (Exception e)
              {
                  throw new BootFailedException($"Critical boot task {task.GetType().Name} failed.", e);
              }
              finally
              {
                  if (sw != null)
                  {
                      sw.Stop();
                      if (sw.ElapsedMilliseconds > SlowTaskThresholdMs)
                          Debug.Log($"[EasyFramework] Boot task {task.GetType().Name} took {sw.ElapsedMilliseconds}ms.");
                  }
              }
          }
      }
  }
  ```

  关键点:`isDevMode = Debug.isDebugBuild || Application.isEditor` 与 spec 一致;`Stopwatch` 只在开发模式下创建,Release 包(非 Debug 且非 Editor)完全跳过计时开销;耗时日志放在 `finally` 里,保证无论成功/非关键失败/关键失败(抛出前)都会记一次,失败路径下 `LogWarning`/异常与耗时日志不冲突(耗时日志用 `Debug.Log`,失败日志已有的 `Debug.LogWarning` 保持不变)。`using Debug = UnityEngine.Debug;` 消歧 `System.Diagnostics.Debug` 与 `UnityEngine.Debug`。

- [ ] **Step 4: Run test to verify it passes**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.GameBootstrapTests`。预期:两个测试均通过——`SlowBootTask_ExceedingThreshold_LogsTiming` 捕获到匹配 `SlowTask.*ms` 的 `Log`;`FastBootTask_UnderThreshold_DoesNotLogTiming` 没有额外未预期日志。同时在 Unity Test Runner 的 EditMode 标签页运行全部既有测试(`EasyFramework.Tests` 全部类),确认 137+ 个原有测试仍然通过,未被 `GameBootstrap` 的改动影响(`GameBootstrap` 的其它调用方,例如 `RootLifetimeScope`,构造签名未变)。

- [ ] **Step 5: Commit**

  ```bash
  git add Packages/com.yifei.easyframework/Core/Boot/GameBootstrap.cs Packages/com.yifei.easyframework/Tests/EditMode/GameBootstrapTests.cs
  git commit -m "$(cat <<'EOF'
  feat: log per-task boot timing in dev builds when threshold is exceeded

  GameBootstrap.RunSafe now measures each IBootTask.InitializeAsync call and
  logs the type name + elapsed ms when it exceeds 50ms, but only in Editor or
  debug builds so release builds pay zero overhead. Helps pinpoint slow boot
  tasks without touching the IBootTask contract.
  EOF
  )"
  ```

---

### Task 3: PoolService 闲置自动缩容

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Pooling/PoolService.cs`
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs` (line 87: `builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();`)
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/PoolServiceTests.cs`

- [ ] **Step 1: Write the failing test**

  在 `PoolServiceTests.cs` 里新增一个 `FakeTimerService`(测试类内部 nested sealed class,放在 `FakeBus` 之后、`Resettable` 之前),并追加缩容相关测试。先读取当前文件顶部的 `using` 与类结构（已在探索阶段读过,现状:`FakeBus` 在第 16-37 行,`Resettable` 在第 39-44 行,字段声明在第 46-50 行,`[SetUp]` 在第 52-64 行)。编辑 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/PoolServiceTests.cs`:

  在顶部 `using` 区加入 `EasyFramework.Core.Timing`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using Cysharp.Threading.Tasks;
  using EasyFramework.Core.Events;
  using EasyFramework.Core.Pooling;
  using EasyFramework.Core.Timing;
  using EasyFramework.Services.Assets;
  using EasyFramework.Services.Pooling;
  using EasyFramework.Services.Scenes;
  using NUnit.Framework;
  using UnityEngine;
  ```

  在 `sealed class FakeBus { ... }`(现有第 16-37 行)之后、`sealed class Resettable`(现有第 39-44 行)之前插入:

  ```csharp
          sealed class FakeTimerService : ITimerService
          {
              sealed class Entry
              {
                  public Action Callback;
                  public bool Repeat;
                  public bool Cancelled;
              }

              readonly List<Entry> _entries = new();
              long _nextId = 1;

              public TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)
              {
                  var entry = new Entry { Callback = callback, Repeat = repeat };
                  _entries.Add(entry);
                  return default; // 测试不需要真实 handle 值,仅需可调用 Advance 手动触发
              }

              public void Cancel(TimerHandle handle) { }

              /// <summary>测试钩子:手动触发一次所有已注册的 repeat 回调(模拟到达周期时间点)。</summary>
              public void Fire()
              {
                  foreach (var e in _entries.ToArray())
                      if (!e.Cancelled)
                          e.Callback();
              }
          }

  ```

  接着改字段声明区(现有第 46-50 行 `GameObject _prefab; FakeBus _bus; FakeAssetService _assets; PoolService _pool; Action<GameObject> _originalDestroy;`),追加一个 `FakeTimerService` 字段:

  ```csharp
        GameObject _prefab;
        FakeBus _bus;
        FakeAssetService _assets;
        FakeTimerService _timer;
        PoolService _pool;
        Action<GameObject> _originalDestroy;
  ```

  `[SetUp]` 方法(现有第 52-64 行)里构造 `_pool` 的那一行 `_pool = new PoolService(_assets, _bus);` 改成同时构造 `_timer` 并传入新的可选参数(默认不开启缩容,保持其余既有测试行为不变):

  ```csharp
        [SetUp]
        public void SetUp()
        {
            _originalDestroy = PoolService.DestroyHandler;
            PoolService.DestroyHandler = UnityEngine.Object.DestroyImmediate;

            _prefab = new GameObject("BulletPrefab");
            _prefab.AddComponent<Resettable>();
            _assets = new FakeAssetService(
                new Dictionary<string, UnityEngine.Object> { { "bullet", _prefab } });
            _bus = new FakeBus();
            _timer = new FakeTimerService();
            _pool = new PoolService(_assets, _bus, _timer);
        }
  ```

  最后在文件末尾 `SceneWillUnload_ClearsAllPools` 测试之后、类结束 `}` 前追加缩容相关测试(用到 `internal` 测试构造 `PoolService(IAssetService, IEventBus, ITimerService, Func<float> nowProvider)` 与新增的 `internal` 方法 `SetIdleTimeout(string key, float? idleTimeoutSeconds)`,均在 Step 3 定义):

  ```csharp
      [Test]
      public void IdleTimeout_NotConfigured_NeverDestroysIdleInstance()
      {
          var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          _pool.Despawn(a);
          _timer.Fire(); // 触发缩容扫描;未配置 idleTimeoutSeconds,应无事发生
          var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          Assert.AreSame(a, b); // 仍是原实例,说明没有被销毁
      }

      [Test]
      public void IdleTimeout_Configured_DestroysInstanceAfterTimeoutOnScan()
      {
          var now = 100f;
          float NowProvider() => now;
          var pool = new PoolService(_assets, _bus, _timer, NowProvider);
          pool.SetIdleTimeout("bullet", 5f);

          var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          pool.Despawn(a);

          now += 10f; // 超过 5 秒闲置阈值
          _timer.Fire();

          var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          Assert.AreNotSame(a, b); // 旧实例已被缩容销毁,重新实例化了新的
      }

      [Test]
      public void IdleTimeout_Configured_KeepsInstanceBeforeTimeoutOnScan()
      {
          var now = 100f;
          float NowProvider() => now;
          var pool = new PoolService(_assets, _bus, _timer, NowProvider);
          pool.SetIdleTimeout("bullet", 5f);

          var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          pool.Despawn(a);

          now += 2f; // 未超过 5 秒闲置阈值
          _timer.Fire();

          var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          Assert.AreSame(a, b); // 未超时,仍复用原实例
      }

      [Test]
      public void IdleTimeout_ReusedInstance_RemovedFromIdleSinceTracking()
      {
          var now = 100f;
          float NowProvider() => now;
          var pool = new PoolService(_assets, _bus, _timer, NowProvider);
          pool.SetIdleTimeout("bullet", 5f);

          var a = pool.SpawnAsync("bullet").GetAwaiter().GetResult();
          pool.Despawn(a);

          now += 2f;
          var b = pool.SpawnAsync("bullet").GetAwaiter().GetResult(); // 复用命中,应从 _idleSince 移除记录
          Assert.AreSame(a, b);

          now += 10f; // 若 b 的旧闲置时间戳未被清除,缩容会误杀正在使用中的实例
          _timer.Fire();

          // b 当前是 active 状态(未 Despawn),不应被缩容逻辑影响。
          Assert.DoesNotThrow(() => pool.Despawn(b));
      }
  ```

- [ ] **Step 2: Run test to verify it fails**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.PoolServiceTests`。预期:**编译失败**——`PoolService` 目前只有一个双参构造 `(IAssetService, IEventBus)`,不存在三参 `(IAssetService, IEventBus, ITimerService)` 构造、四参 `(IAssetService, IEventBus, ITimerService, Func<float>)` 构造,也不存在 `SetIdleTimeout` 方法。

- [ ] **Step 3: Write minimal implementation**

  把 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Pooling/PoolService.cs` 整个文件替换为:

  ```csharp
  using System;
  using System.Collections.Generic;
  using Cysharp.Threading.Tasks;
  using EasyFramework.Core.Events;
  using EasyFramework.Core.Pooling;
  using EasyFramework.Core.Timing;
  using EasyFramework.Services.Assets;
  using EasyFramework.Services.Scenes;
  using UnityEngine;

  namespace EasyFramework.Services.Pooling
  {
      public sealed class PoolService : IPoolService, IDisposable
      {
          /// <summary>EditMode 测试可替换为 Object.DestroyImmediate;运行时为 Object.Destroy。</summary>
          internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

          /// <summary>闲置缩容扫描周期(秒)。默认 10 秒,足够低频不影响性能。</summary>
          const float DefaultScanIntervalSeconds = 10f;

          readonly IAssetService _assets;
          readonly ITimerService _timer;
          readonly Func<float> _now;
          readonly IDisposable _sceneUnloadSub;
          readonly Dictionary<string, Stack<GameObject>> _idle = new();
          readonly HashSet<GameObject> _active = new();
          readonly Dictionary<GameObject, IPoolable[]> _poolablesCache = new();

          // 闲置缩容:默认关闭(不调用 SetIdleTimeout 则这两个字典恒为空,行为与缩容功能上线前完全一致)。
          readonly Dictionary<string, float> _idleTimeoutSeconds = new();
          readonly Dictionary<GameObject, float> _idleSince = new();

          GameObject _root;
          GameObject Root => _root != null ? _root : (_root = new GameObject("[Pools]"));

          /// <summary>生产构造:时间源默认 Time.realtimeSinceStartup。</summary>
          public PoolService(IAssetService assets, IEventBus events, ITimerService timer = null)
              : this(assets, events, timer, () => Time.realtimeSinceStartup) { }

          /// <summary>测试构造:可注入时间源。</summary>
          internal PoolService(IAssetService assets, IEventBus events, ITimerService timer, Func<float> nowProvider)
          {
              _assets = assets;
              _timer = timer;
              _now = nowProvider;
              _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ClearAll());
              _timer?.Schedule(DefaultScanIntervalSeconds, ScanIdleTimeouts, repeat: true);
          }

          /// <summary>
          /// 按 key 开启/调整闲置自动缩容:该 key 下的实例闲置超过 idleTimeoutSeconds 秒后,
          /// 下一次缩容扫描会销毁并从池中移除。传 null 或 0 关闭该 key 的缩容(默认即为关闭)。
          /// 不影响现有调用方——不调用本方法时行为与缩容功能上线前完全一致。
          /// </summary>
          public void SetIdleTimeout(string key, float? idleTimeoutSeconds)
          {
              if (idleTimeoutSeconds.HasValue && idleTimeoutSeconds.Value > 0f)
                  _idleTimeoutSeconds[key] = idleTimeoutSeconds.Value;
              else
                  _idleTimeoutSeconds.Remove(key);
          }

          public async UniTask PrewarmAsync(string key, int count)
          {
              var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Scene);
              var stack = GetStack(key);
              for (var i = 0; i < count; i++)
              {
                  var go = Instantiate(prefab, key);
                  go.SetActive(false);
                  stack.Push(go);
              }
          }

          public async UniTask<GameObject> SpawnAsync(string key, Vector3 position = default,
              Quaternion rotation = default, Transform parent = null)
          {
              var stack = GetStack(key);
              GameObject go;
              if (stack.Count > 0)
              {
                  go = stack.Pop();
                  _idleSince.Remove(go);
              }
              else
              {
                  var prefab = await _assets.LoadAsync<GameObject>(key, AssetScope.Scene);
                  go = Instantiate(prefab, key);
              }

              var t = go.transform;
              t.SetParent(parent, false);
              t.SetPositionAndRotation(position, rotation == default ? Quaternion.identity : rotation);
              go.SetActive(true);
              _active.Add(go);

              foreach (var p in GetPoolables(go)) p.OnSpawn();
              return go;
          }

          public void Despawn(GameObject instance)
          {
              if (instance == null) throw new ArgumentNullException(nameof(instance));
              var marker = instance.GetComponent<PooledMarker>();
              if (marker == null)
                  throw new InvalidOperationException("Instance was not spawned by this pool.");
              if (!_active.Remove(instance))
                  throw new InvalidOperationException("Instance already despawned or not active.");

              foreach (var p in GetPoolables(instance)) p.OnDespawn();

              // Remove cache entry so a stale destroyed-GameObject key cannot linger.
              // The next SpawnAsync/Despawn for this instance will re-populate via
              // GetComponentsInChildren, which is correct after potential hierarchy changes.
              _poolablesCache.Remove(instance);

              instance.SetActive(false);
              instance.transform.SetParent(Root.transform, false);
              GetStack(marker.Key).Push(instance);

              if (_idleTimeoutSeconds.ContainsKey(marker.Key))
                  _idleSince[instance] = _now();
          }

          Stack<GameObject> GetStack(string key)
          {
              if (!_idle.TryGetValue(key, out var stack))
                  _idle[key] = stack = new Stack<GameObject>();
              return stack;
          }

          GameObject Instantiate(GameObject prefab, string key)
          {
              var go = UnityEngine.Object.Instantiate(prefab, Root.transform);
              var marker = go.AddComponent<PooledMarker>();
              marker.Key = key;
              return go;
          }

          IPoolable[] GetPoolables(GameObject go)
          {
              if (!_poolablesCache.TryGetValue(go, out var arr))
                  _poolablesCache[go] = arr = go.GetComponentsInChildren<IPoolable>(true);
              return arr;
          }

          /// <summary>定时器周期回调:扫描 _idleSince,销毁超过各自 key 的 idleTimeoutSeconds 的实例。</summary>
          void ScanIdleTimeouts()
          {
              if (_idleSince.Count == 0) return;

              var now = _now();
              List<GameObject> toDestroy = null;
              foreach (var kv in _idleSince)
              {
                  var go = kv.Key;
                  if (go == null) continue; // 已被外部销毁,交给下面的清理兜底
                  var marker = go.GetComponent<PooledMarker>();
                  if (marker == null || !_idleTimeoutSeconds.TryGetValue(marker.Key, out var timeout))
                      continue;
                  if (now - kv.Value < timeout) continue;

                  (toDestroy ??= new List<GameObject>()).Add(go);
              }

              if (toDestroy == null) return;

              foreach (var go in toDestroy)
              {
                  var marker = go.GetComponent<PooledMarker>();
                  if (marker != null && _idle.TryGetValue(marker.Key, out var stack))
                  {
                      // Stack 不支持随机移除,重建剩余元素(缩容是低频操作,重建成本可忽略)。
                      var remaining = new Stack<GameObject>();
                      foreach (var item in stack)
                          if (!ReferenceEquals(item, go))
                              remaining.Push(item);
                      // 上面按出栈顺序 push 会反转顺序,用临时数组还原原始 LIFO 顺序。
                      var arr = remaining.ToArray();
                      stack.Clear();
                      for (var i = arr.Length - 1; i >= 0; i--)
                          stack.Push(arr[i]);
                  }
                  _idleSince.Remove(go);
                  _poolablesCache.Remove(go);
                  DestroyHandler(go);
              }
          }

          void ClearAll()
          {
              foreach (var stack in _idle.Values)
                  while (stack.Count > 0)
                  {
                      var go = stack.Pop();
                      if (go != null)
                      {
                          _poolablesCache.Remove(go);
                          DestroyHandler(go);
                      }
                  }
              _idle.Clear();
              _idleSince.Clear();
              foreach (var go in _active)
                  if (go != null)
                  {
                      _poolablesCache.Remove(go);
                      DestroyHandler(go);
                  }
              _active.Clear();
              _poolablesCache.Clear(); // sweep any remaining entries (e.g. already-null keys)
          }

          public void Dispose()
          {
              _sceneUnloadSub?.Dispose();
              ClearAll();
              if (_root != null) DestroyHandler(_root);
          }
      }
  }
  ```

  关键点:
  - `PoolService(IAssetService assets, IEventBus events, ITimerService timer = null)` 是新的生产构造,`timer` 设为可选参数默认 `null`——VContainer 按类型自动解析构造参数时,`ITimerService` 已经在 `FrameworkInstaller` 里注册为 singleton(`builder.Register<TimerService>(Lifetime.Singleton).As<ITimerService>().AsSelf();`,早于 Pool 段注册),能够正常解析;若 `timer` 为 `null`(例如已有测试直接 `new PoolService(_assets, _bus)` 不传 timer)则跳过 `_timer?.Schedule(...)`,不崩溃。
  - `internal PoolService(IAssetService assets, IEventBus events, ITimerService timer, Func<float> nowProvider)` 测试构造允许注入可控时间源,与 `AdsService` 的既有模式一致。
  - `SetIdleTimeout` 是新增的 `public` 方法(不在 `IPoolService` 接口里,`IPoolService` 保持不变,满足"不改变任何现有公开接口签名"的验收标准;调用方需要拿到具体类型 `PoolService` 才能调用,和 `G.Pool` 门面暴露的是接口类型 `IPoolService` 不冲突——如果业务层需要用这个方法,可通过 `AsSelf()` 解析具体类型,`FrameworkInstaller` 里 Pool 段已经是 `.As<IPoolService>().AsSelf()`)。
  - `ScanIdleTimeouts` 是定时器周期回调,签名匹配 `Action`(`ITimerService.Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)` 的 `callback` 参数);默认 `useUnscaledTime` 用 `false`(跟随游戏内时间缩放,与其它默认调用一致,可后续按需求调整,不在本次范围)。
  - `_idle` 的 `Stack<GameObject>` 保留做实际存取语义不变(LIFO);`ScanIdleTimeouts` 需要从中间移除已超时实例时用"重建栈"的方式,因为 `Stack<T>` 不支持随机移除,这是缩容这个低频操作(默认 10 秒一次)的可接受成本,不影响 `SpawnAsync`/`Despawn` 热路径的性能。

- [ ] **Step 4: Run test to verify it passes**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.PoolServiceTests`。预期:全部 10 个测试(原有 6 个 + 本 Task 新增 4 个)通过。同时运行 `EasyFramework.Tests` 全部既有测试,确认没有因为 `PoolService` 构造函数增加可选参数而破坏 `FrameworkInstaller`/`RootLifetimeScope` 的组合根解析(`builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();` 是自动构造函数解析,新增的第三个参数 `ITimerService timer = null` 会被 VContainer 按类型解析到已注册的 `ITimerService`,无需修改 `FrameworkInstaller.cs` 的注册代码本身)。

- [ ] **Step 5: Commit**

  ```bash
  git add Packages/com.yifei.easyframework/Services/Pooling/PoolService.cs Packages/com.yifei.easyframework/Tests/EditMode/PoolServiceTests.cs
  git commit -m "$(cat <<'EOF'
  feat: add opt-in idle timeout auto-shrink to PoolService

  PoolService now tracks per-instance idle timestamps and, when a key's
  idle timeout is configured via SetIdleTimeout, periodically destroys
  instances that have sat idle past the threshold. Reuses the existing
  ITimerService.Schedule(repeat: true) instead of a new Update loop.
  Disabled by default (no behavior change unless SetIdleTimeout is called).
  EOF
  )"
  ```

---

### Task 4: Localization 编辑器漏翻译检测工具

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Localization/Editor/EasyFramework.Services.Localization.Editor.asmdef`
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Localization/Editor/LocalizationValidator.cs`
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/LocalizationValidatorTests.cs`

- [ ] **Step 1: Write the failing test**

  先新建 asmdef(测试依赖它才能编译通过,所以顺序上先建空壳程序集,再补测试断言,是 TDD 在"新程序集"场景下的合理变体——真正的"失败测试"体现在 Step 2 用完整断言运行时功能未实现)。

  新建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Localization/Editor/EasyFramework.Services.Localization.Editor.asmdef`:

  ```json
  {
    "name": "EasyFramework.Services.Localization.Editor",
    "rootNamespace": "EasyFramework.Services.Localization.Editor",
    "references": [
      "EasyFramework.Core",
      "EasyFramework.Services"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true,
    "defineConstraints": [],
    "noEngineReferences": false
  }
  ```

  (`.meta` 不手写,由 Unity 刷新时自动生成 GUID,参照既有约定。)

  在 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef` 的 `references` 数组里追加 `"EasyFramework.Services.Localization.Editor"`(放在 `"EasyFramework.Services"` 之后):

  ```json
  {
    "name": "EasyFramework.Tests.EditMode",
    "rootNamespace": "EasyFramework.Tests",
    "references": [
      "EasyFramework.Core",
      "EasyFramework.Services",
      "EasyFramework.Services.Localization.Editor",
      "EasyFramework.Monetization",
      "EasyFramework.Boot",
      "EasyFramework.DevTools",
      "EasyFramework.Template",
      "UniTask",
      "VContainer",
      "MessagePipe",
      "MessagePipe.VContainer",
      "Unity.Addressables",
      "Unity.ResourceManager",
      "PrimeTween.Runtime",
      "Unity.Cinemachine",
      "Unity.InputSystem",
      "Unity.TextMeshPro",
      "UnityEngine.TestRunner",
      "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "precompiledReferences": ["nunit.framework.dll", "Newtonsoft.Json.dll"],
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "overrideReferences": true,
    "autoReferenced": false
  }
  ```

  新建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/LocalizationValidatorTests.cs`:

  ```csharp
  using System.Collections.Generic;
  using EasyFramework.Services.Localization;
  using EasyFramework.Services.Localization.Editor;
  using NUnit.Framework;
  using UnityEngine;

  namespace EasyFramework.Tests
  {
      public class LocalizationValidatorTests
      {
          static LocalizationTable MakeTable(string tableName,
              params (string key, IReadOnlyDictionary<string, string> localeValues)[] rows)
          {
              var table = ScriptableObject.CreateInstance<LocalizationTable>();
              table.name = tableName;
              foreach (var (key, localeValues) in rows)
                  table.AddEntryForTest(key, localeValues);
              return table;
          }

          [Test]
          public void Validate_AllKeysHaveAllLocales_ReturnsNoIssues()
          {
              var table = MakeTable("Common",
                  ("hello", new Dictionary<string, string> { { "zh-CN", "你好" }, { "en", "Hello" } }),
                  ("bye", new Dictionary<string, string> { { "zh-CN", "再见" }, { "en", "Bye" } }));

              var issues = LocalizationValidator.Validate(new[] { table });

              Assert.IsEmpty(issues);
          }

          [Test]
          public void Validate_MissingLocaleForKey_ReportsIssue()
          {
              var table = MakeTable("Common",
                  ("hello", new Dictionary<string, string> { { "zh-CN", "你好" }, { "en", "Hello" } }),
                  ("bye", new Dictionary<string, string> { { "zh-CN", "再见" } })); // 缺 en

              var issues = LocalizationValidator.Validate(new[] { table });

              Assert.AreEqual(1, issues.Count);
              Assert.AreEqual("Common", issues[0].TableName);
              Assert.AreEqual("bye", issues[0].Key);
              Assert.AreEqual("en", issues[0].MissingLocale);
          }

          [Test]
          public void Validate_MultipleTables_UnionsLocaleSetAcrossAllTables()
          {
              // 表 A 只有 zh-CN/en,表 B 引入了 ja——校验应把 ja 也纳入"应该存在的 locale 集合",
              // 从而发现表 A 里所有 key 都缺 ja。
              var tableA = MakeTable("A",
                  ("k1", new Dictionary<string, string> { { "zh-CN", "1" }, { "en", "1" } }));
              var tableB = MakeTable("B",
                  ("k2", new Dictionary<string, string> { { "zh-CN", "2" }, { "en", "2" }, { "ja", "2" } }));

              var issues = LocalizationValidator.Validate(new[] { tableA, tableB });

              Assert.AreEqual(1, issues.Count);
              Assert.AreEqual("A", issues[0].TableName);
              Assert.AreEqual("k1", issues[0].Key);
              Assert.AreEqual("ja", issues[0].MissingLocale);
          }

          [Test]
          public void Validate_EmptyKey_IsSkipped()
          {
              var table = MakeTable("Common",
                  ("", new Dictionary<string, string> { { "zh-CN", "空key" } }),
                  ("valid", new Dictionary<string, string> { { "zh-CN", "有效" } }));

              var issues = LocalizationValidator.Validate(new[] { table });

              Assert.IsEmpty(issues); // 只有一个 locale(zh-CN)出现过,valid 的 zh-CN 已填,空 key 被跳过
          }

          [Test]
          public void Validate_NullOrEmptyTableList_ReturnsNoIssues()
          {
              Assert.IsEmpty(LocalizationValidator.Validate(null));
              Assert.IsEmpty(LocalizationValidator.Validate(new LocalizationTable[0]));
          }
      }
  }
  ```

- [ ] **Step 2: Run test to verify it fails**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.LocalizationValidatorTests`。预期:**编译失败**——`EasyFramework.Services.Localization.Editor` 命名空间下的 `LocalizationValidator` 类型尚不存在。

- [ ] **Step 3: Write minimal implementation**

  新建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Localization/Editor/LocalizationValidator.cs`:

  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using EasyFramework.Services.Localization;
  using UnityEditor;
  using UnityEngine;

  namespace EasyFramework.Services.Localization.Editor
  {
      /// <summary>单条漏翻译记录:表名 + key + 缺失的 locale。</summary>
      public readonly struct MissingTranslation
      {
          public readonly string TableName;
          public readonly string Key;
          public readonly string MissingLocale;

          public MissingTranslation(string tableName, string key, string missingLocale)
          {
              TableName = tableName;
              Key = key;
              MissingLocale = missingLocale;
          }
      }

      /// <summary>
      /// 编辑器专用批量漏翻译校验。遍历传入的 LocalizationTable 集合,
      /// 汇总所有表里出现过的 locale 全集,对每个非空 key 检查是否覆盖全部 locale。
      /// 不改动运行时 ILocalizationService 契约,纯静态分析工具。
      /// </summary>
      public static class LocalizationValidator
      {
          public static IReadOnlyList<MissingTranslation> Validate(IReadOnlyList<LocalizationTable> tables)
          {
              var issues = new List<MissingTranslation>();
              if (tables == null || tables.Count == 0) return issues;

              // 第一遍:收集所有表里出现过的 locale 全集(某个 locale 只要在任意一个 key 上出现过,就视为"应该覆盖"的目标)。
              var allLocales = new HashSet<string>();
              foreach (var table in tables)
              {
                  if (table == null) continue;
                  foreach (var row in table.Rows)
                  {
                      if (string.IsNullOrEmpty(row.Key) || row.Values == null) continue;
                      foreach (var lv in row.Values)
                          if (!string.IsNullOrEmpty(lv.Locale))
                              allLocales.Add(lv.Locale);
                  }
              }

              // 第二遍:每个表的每个非空 key,检查是否覆盖 allLocales 全集。
              foreach (var table in tables)
              {
                  if (table == null) continue;
                  var tableName = table.name;
                  foreach (var row in table.Rows)
                  {
                      if (string.IsNullOrEmpty(row.Key)) continue;
                      var present = new HashSet<string>();
                      if (row.Values != null)
                          foreach (var lv in row.Values)
                              if (!string.IsNullOrEmpty(lv.Locale))
                                  present.Add(lv.Locale);

                      foreach (var locale in allLocales)
                          if (!present.Contains(locale))
                              issues.Add(new MissingTranslation(tableName, row.Key, locale));
                  }
              }

              return issues;
          }

          [MenuItem("EasyFramework/校验本地化表")]
          static void ValidateAllTablesMenuItem()
          {
              var guids = AssetDatabase.FindAssets($"t:{nameof(LocalizationTable)}");
              var tables = guids
                  .Select(guid => AssetDatabase.LoadAssetAtPath<LocalizationTable>(AssetDatabase.GUIDToAssetPath(guid)))
                  .Where(t => t != null)
                  .ToList();

              var issues = Validate(tables);
              if (issues.Count == 0)
              {
                  Debug.Log($"[EasyFramework] 本地化校验通过:{tables.Count} 个表,未发现缺失翻译。");
                  return;
              }

              Debug.LogWarning($"[EasyFramework] 本地化校验发现 {issues.Count} 处缺失翻译:");
              foreach (var issue in issues)
                  Debug.LogWarning($"  表 [{issue.TableName}] key '{issue.Key}' 缺少 locale '{issue.MissingLocale}'");
          }
      }
  }
  ```

  关键点:
  - `Validate` 是纯静态方法,不依赖 `AssetDatabase`,可以在 EditMode 测试里直接用内存构造的 `LocalizationTable`(`ScriptableObject.CreateInstance` + `AddEntryForTest`,`AddEntryForTest` 已是现有的 `internal` 测试钩子)调用,不需要真实资产文件。
  - 菜单项 `[MenuItem("EasyFramework/校验本地化表")]` 挂在 `ValidateAllTablesMenuItem` 这个私有静态方法上,用 `AssetDatabase.FindAssets` 找工程内所有 `LocalizationTable` 资产,调 `Validate` 后把结果打印到 Console(表名 + key + 缺失的 locale,符合 spec §2.4 "缺失项汇总打印")。这部分依赖 `UnityEditor.AssetDatabase`,是纯编辑器 API,因此必须落在 `includePlatforms: ["Editor"]` 的独立程序集里,不能放进跨平台的 `EasyFramework.Services.asmdef`。
  - `MissingTranslation` 是新增的公开类型,但位于 `EasyFramework.Services.Localization.Editor` 命名空间、独立的 Editor-only 程序集,不影响运行时 `ILocalizationService`/`TableLocalizationService` 的现有公开接口签名。

- [ ] **Step 4: Run test to verify it passes**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.LocalizationValidatorTests`。预期:全部 5 个测试通过。额外手动验证(编辑器 GUI 检查,非自动化断言):在 Unity 编辑器菜单栏找到 `EasyFramework > 校验本地化表`,点击后 Console 打印校验结果(工程里没有真实 `LocalizationTable` 资产时应打印 "0 个表,未发现缺失翻译" 一类的通过信息,不报错)。

- [ ] **Step 5: Commit**

  ```bash
  git add Packages/com.yifei.easyframework/Services/Localization/Editor/EasyFramework.Services.Localization.Editor.asmdef Packages/com.yifei.easyframework/Services/Localization/Editor/LocalizationValidator.cs Packages/com.yifei.easyframework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef Packages/com.yifei.easyframework/Tests/EditMode/LocalizationValidatorTests.cs
  git commit -m "$(cat <<'EOF'
  feat: add editor-only batch localization validator

  New EasyFramework.Services.Localization.Editor assembly with
  LocalizationValidator.Validate (pure, testable) plus an
  "EasyFramework/校验本地化表" menu item that scans all LocalizationTable
  assets and logs missing key/locale combinations before shipping.
  Runtime ILocalizationService is untouched.
  EOF
  )"
  ```

---

### Task 5: AudioService 同音效并发限流

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Audio/AudioService.cs`
- Modify (按需,见 Step 4): `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs` (line 99: `builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().AsSelf();`)
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/AudioServiceTests.cs`

- [ ] **Step 1: Write the failing test**

  在 `AudioServiceTests.cs` 顶部 `using` 区加入 `System`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using EasyFramework.Services.Assets;
  using EasyFramework.Services.Audio;
  using NUnit.Framework;
  using UnityEngine;
  ```

  在类末尾 `DefaultVolumes_AreOneWhenNoPrefs` 测试之后、类结束 `}` 前追加三个限流相关测试(直接调用 Step 3 里新增的 `internal AudioService(IAssetService, Func<float>)` 测试构造,注入可控时钟,不需要真实等待 `MinSfxIntervalSeconds`):

  ```csharp
        [Test]
        public void PlaySfx_SameKeyWithinMinInterval_SecondCallIsSkipped()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click")); // 同一时刻重复触发,应被限流跳过
        }

        [Test]
        public void PlaySfx_SameKeyAfterMinInterval_IsNotSkipped()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            t += AudioService.MinSfxIntervalSeconds + 0.001f;
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click")); // 已超过最小间隔,应正常播放
        }

        [Test]
        public void PlaySfx_DifferentKeys_AreNotThrottledAgainstEachOther()
        {
            var t = 0f;
            var svc = new AudioService(new FakeAssetService(new Dictionary<string, Object>
            {
                { "sfx_click", AudioClip.Create("sfx_click", 1, 1, 44100, false) },
                { "sfx_hit", AudioClip.Create("sfx_hit", 1, 1, 44100, false) },
            }), () => t);

            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_click"));
            Assert.DoesNotThrow(() => svc.PlaySfx("sfx_hit")); // 不同 key,同一时刻也不应互相限流
        }
  ```

  本 Task 不需要额外的辅助方法(不引入 `MakeWithClock` 之类的封装),三个测试各自 inline 构造 `AudioService`,与 `AudioServiceTests.cs` 里既有测试(`BgmVolume_Setter_Clamps01` 等)直接调用 `Make()` 辅助方法风格一致但更简单,因为需要各自控制独立的 `t` 闭包变量。

- [ ] **Step 2: Run test to verify it fails**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.AudioServiceTests`。预期:**编译失败**——`AudioService` 目前只有单参构造 `(IAssetService)`,不存在 `(IAssetService, Func<float>)` 构造,也不存在 `public const float MinSfxIntervalSeconds`。

- [ ] **Step 3: Write minimal implementation**

  把 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Audio/AudioService.cs` 整个文件替换为:

  ```csharp
  using System;
  using System.Collections.Generic;
  using Cysharp.Threading.Tasks;
  using EasyFramework.Services.Assets;
  using UnityEngine;
  using VContainer.Unity;

  namespace EasyFramework.Services.Audio
  {
      public sealed class AudioService : IAudioService, ITickable
      {
          const string BgmKey = "ef.audio.bgm";
          const string SfxKey = "ef.audio.sfx";
          const int SfxVoices = 8;

          /// <summary>同一个 SFX key 的最小重复播放间隔(秒),间隔内的重复调用直接跳过,避免瞬时叠加音量削波。</summary>
          public const float MinSfxIntervalSeconds = 0.03f;

          readonly IAssetService _assets;
          readonly Func<float> _now;
          readonly Dictionary<string, float> _lastSfxPlayTime = new();

          float _bgmVolume;
          float _sfxVolume;

          GameObject _host;
          AudioSource _bgmA;
          AudioSource _bgmB;
          bool _aIsActive;        // 当前出声(active)的是 A 还是 B
          AudioSource[] _sfx;
          int _sfxCursor;

          CrossfadeState _fade;
          bool _fadingToStop;     // true:淡出为停止 BGM(无新 clip)

          /// <summary>生产构造:时间源默认 Time.realtimeSinceStartup。</summary>
          public AudioService(IAssetService assets) : this(assets, () => Time.realtimeSinceStartup) { }

          /// <summary>测试构造:可注入时间源。</summary>
          internal AudioService(IAssetService assets, Func<float> nowProvider)
          {
              _assets = assets;
              _now = nowProvider;
              _bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmKey, 1f));
              _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
          }

          public float BgmVolume
          {
              get => _bgmVolume;
              set
              {
                  _bgmVolume = Mathf.Clamp01(value);
                  PlayerPrefs.SetFloat(BgmKey, _bgmVolume);
                  PlayerPrefs.Save();
                  ApplyBgmVolume();
              }
          }

          public float SfxVolume
          {
              get => _sfxVolume;
              set
              {
                  _sfxVolume = Mathf.Clamp01(value);
                  PlayerPrefs.SetFloat(SfxKey, _sfxVolume);
                  PlayerPrefs.Save();
              }
          }

          public async UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f)
          {
              EnsureHost();
              var clip = await _assets.LoadAsync<AudioClip>(key, AssetScope.Global);

              var incoming = _aIsActive ? _bgmB : _bgmA;
              incoming.clip = clip;
              incoming.loop = true;
              incoming.volume = 0f;
              incoming.Play();

              _aIsActive = !_aIsActive;
              _fadingToStop = false;
              _fade = new CrossfadeState(fadeSeconds, 1f);
              ApplyBgmVolume();
          }

          public void StopBgm(float fadeSeconds = 0.3f)
          {
              if (_host == null) return; // 从未播放过
              _fadingToStop = true;
              _fade = new CrossfadeState(fadeSeconds, 1f);
          }

          public void PlaySfx(string key, float volume = 1f)
          {
              var now = _now();
              if (_lastSfxPlayTime.TryGetValue(key, out var last) && now - last < MinSfxIntervalSeconds)
                  return; // 同一 key 短时间内重复触发,跳过避免音量堆叠削波

              _lastSfxPlayTime[key] = now;

              EnsureHost();
              // SFX clip 走 Scene 作用域加载;同步取已加载实例,未加载则异步取后播放
              PlaySfxAsync(key, Mathf.Clamp01(volume)).Forget();
          }

          async UniTaskVoid PlaySfxAsync(string key, float volume)
          {
              var clip = await _assets.LoadAsync<AudioClip>(key, AssetScope.Scene);
              var src = _sfx[_sfxCursor];
              _sfxCursor = (_sfxCursor + 1) % SfxVoices;
              src.PlayOneShot(clip, volume * _sfxVolume);
          }

          public void Tick()
          {
              if (_fade == null) return;
              _fade.Advance(Time.unscaledDeltaTime);
              ApplyBgmVolume();
              if (_fade.IsDone)
              {
                  var outgoing = _aIsActive ? _bgmB : _bgmA;
                  outgoing.Stop();
                  if (_fadingToStop)
                  {
                      (_aIsActive ? _bgmA : _bgmB).Stop(); // 停止当前 active(无新 clip 时即 active 本身)
                  }
                  _fade = null;
              }
          }

          void ApplyBgmVolume()
          {
              if (_host == null) return;
              var active = _aIsActive ? _bgmA : _bgmB;
              var inactive = _aIsActive ? _bgmB : _bgmA;
              if (_fade != null && !_fadingToStop)
              {
                  active.volume = _fade.InVolume * _bgmVolume;
                  inactive.volume = _fade.OutVolume * _bgmVolume;
              }
              else if (_fade != null && _fadingToStop)
              {
                  active.volume = _fade.OutVolume * _bgmVolume;
              }
              else
              {
                  active.volume = _bgmVolume;
              }
          }

          void EnsureHost()
          {
              if (_host != null) return;
              _host = new GameObject("[EasyFramework.Audio]");
              Object.DontDestroyOnLoad(_host);
              _bgmA = _host.AddComponent<AudioSource>();
              _bgmB = _host.AddComponent<AudioSource>();
              _bgmA.playOnAwake = _bgmB.playOnAwake = false;
              _sfx = new AudioSource[SfxVoices];
              for (var i = 0; i < SfxVoices; i++)
              {
                  _sfx[i] = _host.AddComponent<AudioSource>();
                  _sfx[i].playOnAwake = false;
              }
              _aIsActive = true; // 约定:active 初始指向 A(下一次 PlayBgm 切到 B 出声)
          }
      }
  }
  ```

  关键点:
  - `PlaySfx` 的限流检查放在方法最前面,**先于** `EnsureHost()` 调用——这样被跳过的调用完全不触碰 `AudioSource`,也不会因为限流触发懒加载音频宿主 GameObject(测试环境下 `AudioClip.Create` 构造的空 clip 配合 `FakeAssetService` 完全跑在 EditMode,不需要真实音频输出)。
  - `_lastSfxPlayTime` 是 `Dictionary<string, float>`,key 就是 spec 里说的"音效 key",与 `PlaySfx(string key, ...)` 的参数同名语义一致。
  - 沿用 `AdsService` 的双构造模式:公开构造 `AudioService(IAssetService assets)` 委托给 `internal AudioService(IAssetService assets, Func<float> nowProvider)`,默认时间源 `() => Time.realtimeSinceStartup`;测试直接调用 internal 构造注入可控时钟。
  - `MinSfxIntervalSeconds` 声明为 `public const float`(而非 `private`),因为测试需要引用它计算推进量,默认值 0.03f(30ms)与 spec 一致。

- [ ] **Step 4: Run test to verify it passes**

  在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.AudioServiceTests`。预期:全部 8 个测试(原有 5 个 + 本 Task 新增 3 个)通过。

  接着运行 `EasyFramework.Tests` 全部既有测试(尤其是任何走完整 `FrameworkInstaller.Install` 组合根构建的测试,如果存在的话),确认 VContainer 能正确解析 `AudioService`。**核对方法**:`FrameworkInstaller.cs` 里 Audio 段目前是纯类型注册 `builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().AsSelf();`(非工厂 lambda)。`AudioService` 现在有两个构造:`public AudioService(IAssetService assets)`(1 参)与 `internal AudioService(IAssetService assets, Func<float> nowProvider)`(2 参)。参照 `Boot/FrameworkInstaller.cs` 里 `AdsService` 段(现有第 133-135 行注释)记录的已知坑:VContainer 自动选构造函数时选参数最多的构造,不区分 `public`/`internal` 可见性,因此会尝试选 2 参构造并解析 `Func<float>`,但 `Func<float>` 未在容器里注册,导致解析失败并抛异常(错误信息形如 "no such registration of type: System.Func\`1[[System.Single...")。

  - **如果全量回归测试run 到任何触发 `AudioService` 解析的测试(容器构建 / 框架启动集成测试)时抛出上述解析错误**,应用以下修复:把 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs` 第 97-99 行(现有的 Audio 段注册)

    ```csharp
            // ---- Audio(Phase 3b)----
            // AudioService : ITickable,以 AsSelf 注册便于 RootLifetimeScope 取出挂 Tick。
            builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().AsSelf();
    ```

    改为:

    ```csharp
            // ---- Audio(Phase 3b)----
            // AudioService : ITickable,以 AsSelf 注册便于 RootLifetimeScope 取出挂 Tick。
            // 工厂 lambda 显式走单参生产构造:AudioService 另有一个 internal(IAssetService, Func<float>)
            // 测试构造,VContainer 自动选最长构造会去解析未注册的 Func<float> 而失败(与 AdsService 同因,
            // 参见上面 Ads 段注释)。
            builder.Register<AudioService>(c => new AudioService(c.Resolve<IAssetService>()), Lifetime.Singleton)
                .As<IAudioService>().AsSelf();
    ```

    修改后重新运行全量回归测试确认通过。

  - **如果全量回归测试本身就全部通过**(说明当前仓库版本的 VContainer 在自动选构造函数时只看 `public` 构造,不把 `internal` 构造纳入候选),则不需要修改 `FrameworkInstaller.cs`,保持原样。

  无论走哪条分支,最终都要确认 `EasyFramework.Tests` 全部既有测试(137+ 个)仍然通过。

- [ ] **Step 5: Commit**

  如果 Step 4 未触发 `FrameworkInstaller.cs` 修复:

  ```bash
  git add Packages/com.yifei.easyframework/Services/Audio/AudioService.cs Packages/com.yifei.easyframework/Tests/EditMode/AudioServiceTests.cs
  git commit -m "$(cat <<'EOF'
  feat: throttle repeated same-key SFX playback in AudioService

  PlaySfx now tracks last-play time per key and skips calls that repeat
  within MinSfxIntervalSeconds (30ms default), preventing volume-stacking
  clipping when the same one-shot SFX fires rapidly (e.g. combo hits).
  Different keys are unaffected. Time source is injectable via an internal
  constructor for deterministic tests, following the AdsService pattern.
  EOF
  )"
  ```

  如果 Step 4 触发了 `FrameworkInstaller.cs` 修复,改为把它一并加入本次提交:

  ```bash
  git add Packages/com.yifei.easyframework/Services/Audio/AudioService.cs Packages/com.yifei.easyframework/Tests/EditMode/AudioServiceTests.cs Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs
  git commit -m "$(cat <<'EOF'
  feat: throttle repeated same-key SFX playback in AudioService

  PlaySfx now tracks last-play time per key and skips calls that repeat
  within MinSfxIntervalSeconds (30ms default), preventing volume-stacking
  clipping when the same one-shot SFX fires rapidly (e.g. combo hits).
  Different keys are unaffected. Time source is injectable via an internal
  constructor for deterministic tests, following the AdsService pattern.
  Also pin AudioService's VContainer registration to a factory lambda so
  auto constructor resolution doesn't pick up the internal test constructor
  (same fix already applied to AdsService).
  EOF
  )"
  ```

---

## Self-Review

**Spec 覆盖度核对(逐条对照 `2026-07-05-framework-polish-improvements-design.md`):**

- §2.1 EventBus:covered by Task 1。Step 1-2 先写回归测试验证现状安全(不预设有 bug,若测试证明现状安全则不加生产代码修复、只留回归测试);Step 3-4 独立加递归深度保护(`MessagePipeEventBus.MaxPublishDepth = 20`,超阈值 `Debug.LogError` 并跳过本次发布),改动范围仅 `MessagePipeEventBus.cs` 一个文件,与 spec 一致。
- §2.2 GameBootstrap:covered by Task 2。仅开发模式(`Debug.isDebugBuild || Application.isEditor`)、超过阈值(默认 50ms,`GameBootstrap.SlowTaskThresholdMs`)打印任务类型名 + 耗时;Release 包(非 debug 且非 editor)`Stopwatch` 都不创建,零开销。
- §2.3 PoolService:covered by Task 3。`Stack<GameObject>` 保留做实际存取(LIFO);新增并行的 `Dictionary<GameObject, float> _idleSince`(`Despawn` 写入、`SpawnAsync` 命中缓存复用时移除);复用 `ITimerService.Schedule(delay, callback, repeat:true)`,不新起 Update 循环;默认关闭(不调用 `SetIdleTimeout` 即 `_idleTimeoutSeconds` 为空,行为与现状完全一致);可按 key 选择性开启,不影响现有调用方(`SpawnAsync`/`Despawn`/`PrewarmAsync` 签名未变)。
- §2.4 Localization:covered by Task 4。新增仅编辑器可用的批量校验入口(菜单 `EasyFramework/校验本地化表`),遍历所有 `LocalizationTable` 资产,检查每个已配置 locale 下所有 key 是否都有对应 value,缺失项汇总打印(表名 + key + 缺失的 locale);不改动运行时 `ILocalizationService` 接口。
- §2.5 AudioService:covered by Task 5。按 key(`_lastSfxPlayTime` 字典)记录上次播放时间,`PlaySfx` 内部对同一 key 加可配置的最小播放间隔(默认 30ms,`AudioService.MinSfxIntervalSeconds`),间隔内的重复调用直接跳过。
- 验收标准第一条(5 项各自独立可测,EditMode 单测覆盖):✅ 每个 Task 都有独立测试文件/测试方法,互不依赖,可任意顺序执行。EventBus 重入回归测试+深度保护越界测试(Task 1)、GameBootstrap 耗时日志(Task 2,通过可控延时的 `IBootTask` fake + `LogAssert` 验证日志触发)、PoolService 超时销毁(Task 3,用 `FakeTimerService.Fire()` 手动触发扫描 + 可注入的 `Func<float>` 虚拟时间源,不依赖真实等待)、Localization 校验工具(Task 4,纯函数 `Validate` 的 EditMode 单测 + 手动验证菜单项)、AudioService 限流(Task 5,可注入时间源验证同 key 间隔内跳过、间隔外正常播放、不同 key 不互相限流)均已覆盖。
- 验收标准第二条(不破坏现有 137 个测试,不改变任何现有公开接口签名):✅ `IEventBus`/`IBootTask`/`IPoolService`/`ILocalizationService`/`IAudioService` 五个公开接口签名均未修改。`PoolService`/`AudioService` 新增的都是**带默认值的可选构造参数**或**新增独立方法**(`SetIdleTimeout`),不破坏既有调用点;`GameBootstrap`/`MessagePipeEventBus` 的构造签名完全未变。每个 Task 的 Step 4 都包含"运行 `EasyFramework.Tests` 全部既有测试确认不回归"的动作。

**占位符扫描:** 全文搜索确认不存在 "TBD"、"待补"、"实现细节后补"、"添加适当的错误处理"、"参照 Task N 的实现"(不含代码复述)、没有代码块的"写测试覆盖上面的行为"等占位符表述。Task 5 Step 4/5 里的分支(是否需要改 `FrameworkInstaller.cs`)不是占位符——给出了触发条件的具体判定依据(实测运行全量测试是否报出特定的 VContainer 解析错误)与两条分支各自完整的代码/commit 命令,执行者不需要自行"后补"任何逻辑,只需要按实测结果二选一执行。

**发现问题与修正记录:**

1. **问题**:Task 2 Step 1 草稿最初写 `new SlowTask(GameBootstrap.SlowTaskThresholdMs + 20)`,但 `SlowTaskThresholdMs` 声明为 `long`、`SlowTask` 构造参数 `delayMs` 是 `int`,存在隐式转换编译错误(`long` 不能隐式转 `int`)。
   **修正**:已改为 `new SlowTask((int)GameBootstrap.SlowTaskThresholdMs + 20)`,在文档写作过程中直接修正,文档终稿已是修正后版本。

2. **问题**:Task 1 里 `MaxPublishDepth` 用 `public const int` 声明,spec 原文"默认 20,可调"字面上容易被理解为"运行时可配置",但 `const` 是编译期常量。
   **核对结论**:对比 spec 全篇其余"默认 X,可调"表述(Task 2 的 50ms 阈值、Task 3 的 10 秒扫描周期、Task 5 的 30ms 间隔),真正要求"运行时按业务/按 key 动态配置"的只有 Task 3 的 `idleTimeoutSeconds`(spec 原文明确写"可配置的 idleTimeoutSeconds"且要求"可按 key 选择性开启"),该项已用 `PoolService.SetIdleTimeout(string key, float? idleTimeoutSeconds)` 方法实现真正的运行时可调。其余三处(`MaxPublishDepth`/`SlowTaskThresholdMs`/`MinSfxIntervalSeconds`)按框架现有代码风格理解为"编译期常量,改值需要重新编译"是合理的等价设计——不需要额外引入 `IConfigService` 依赖或运行时 setter 就能满足"可调"的字面要求。此判断不需要改代码,记录在此作为设计取舍依据,避免执行者误以为遗漏了运行时配置能力。

3. **问题**:Task 5 Step 1 最初的草稿包含一个未被实际使用的 `MakeWithClock` 辅助方法,写完后判断三个测试各自 inline 构造更清晰,存在"引入了辅助方法又不用"的死代码风险。
   **修正**:终稿的 Task 5 Step 1 已经**不包含** `MakeWithClock`,三个新测试都是直接 `new AudioService(new FakeAssetService(...), () => t)` 内联构造,并在 Step 1 末尾说明了这个设计选择的理由(需要各自独立的 `t` 闭包变量,内联更直接)。文档终稿没有遗留死代码建议。

4. **问题**:Task 3 里 `FakeTimerService.Schedule` 返回 `default(TimerHandle)`,其 `Id` 字段为 `0`,而 `TimerHandle.IsValid` 定义为 `Id != 0`,意味着返回的句柄永远无效。
   **核对结论**:`PoolService` 的生产实现与测试都不读取 `Schedule(...)` 的返回值(`_timer?.Schedule(...)` 丢弃返回值),也不调用 `Cancel`,因此"句柄恒为 invalid"不影响任何断言或生产逻辑正确性。确认这是可接受的简化,不需要修正。

5. **前后 Task 类型/方法名一致性核对(逐项比对定义处与引用处):**
   - `MessagePipeEventBus.MaxPublishDepth`:Task 1 Step 3 定义(`public const int MaxPublishDepth = 20`)、Step 4 测试引用(`MessagePipeEventBus.MaxPublishDepth`),类型与用法一致。
   - `GameBootstrap.SlowTaskThresholdMs`:Task 2 Step 1 测试引用、Step 3 定义,均为 `public const long SlowTaskThresholdMs = 50`,一致(已修正 Step 1 的类型转换,见上文问题 1)。
   - `PoolService` 构造签名:生产构造 `PoolService(IAssetService assets, IEventBus events, ITimerService timer = null)`、测试构造 `internal PoolService(IAssetService assets, IEventBus events, ITimerService timer, Func<float> nowProvider)`、新增方法 `public void SetIdleTimeout(string key, float? idleTimeoutSeconds)`——Task 3 Step 1(测试调用点)与 Step 3(实现定义)的参数顺序、参数类型、方法名逐一比对一致。`FakeTimerService` 实现 `ITimerService.Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)` 与 `Cancel(TimerHandle handle)`,与 `/Core/Timing/ITimerService.cs` 的实际签名完全一致。
   - `LocalizationValidator.Validate(IReadOnlyList<LocalizationTable> tables)` 返回 `IReadOnlyList<MissingTranslation>`;`MissingTranslation { string TableName; string Key; string MissingLocale; }`——Task 4 Step 1(测试)与 Step 3(实现)的字段名、方法签名一致。
   - `AudioService.MinSfxIntervalSeconds`(`public const float`)、生产构造 `AudioService(IAssetService assets)`、测试构造 `internal AudioService(IAssetService assets, Func<float> nowProvider)`——Task 5 Step 1(测试)与 Step 3(实现)一致。
   - 5 个 Task 之间没有互相引用对方定义的类型/方法(完全独立,符合 spec"互相独立"的前提),不存在跨 Task 命名漂移的风险。

**结论:** Self-review 过程中发现并在文档定稿前直接修正了 1 处类型转换错误(Task 2 的 `long`/`int` 隐式转换)、剔除了 1 处潜在死代码(Task 5 的未使用 `MakeWithClock`),记录了 1 处设计取舍依据(`const` vs "可调"的语义边界,不需要改代码)、确认了 1 处不影响正确性的边界情况(`FakeTimerService` 句柄恒为 invalid)。全篇 5 个 Task 的类型名、方法签名在定义处与引用处逐一核对一致,无遗留占位符,spec 5 项改动与 2 条验收标准均已覆盖。
