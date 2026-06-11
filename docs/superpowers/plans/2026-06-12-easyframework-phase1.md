# EasyFramework Phase 1(骨架)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建 EasyFramework 骨架:第三方包、asmdef 分层、Core 五件套(StateMachine / ObjectPool / Timer / EventBus / Boot 管线)、G 门面与 RootLifetimeScope,全部带 EditMode 单测。

**Architecture:** VContainer DI 内核 + 静态门面 `G`。Core 层为纯 C#(可引第三方库,不依赖场景),Boot 层为组合根(引用所有层,含 `RootLifetimeScope`、`G`、`FrameworkInstaller`)。注册逻辑抽到静态 `FrameworkInstaller` 以便脱离 MonoBehaviour 单测。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / Unity Test Framework 1.6

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包 `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`)。
- **并行实现代理只允许用 Write 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(状态 Enter 等均同步完成),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。

---

## 文件结构总览

```
Packages/manifest.json                                  (修改:新增 4 个包)
Assets/EasyFramework/
├── Core/
│   ├── EasyFramework.Core.asmdef
│   ├── Events/IEventBus.cs, MessagePipeEventBus.cs
│   ├── Fsm/State.cs, StateMachine.cs
│   ├── Pooling/IPoolable.cs, ObjectPool.cs
│   ├── Timing/ITimerService.cs, TimerHandle.cs, TimerService.cs
│   └── Boot/IBootTask.cs, GameBootstrap.cs, BootCompletedEvent.cs
├── Services/EasyFramework.Services.asmdef              (Phase 1 仅占位 AssemblyInfo)
├── Monetization/EasyFramework.Monetization.asmdef      (同上)
├── DevTools/EasyFramework.DevTools.asmdef              (同上)
├── Boot/
│   ├── EasyFramework.Boot.asmdef
│   ├── G.cs, FrameworkInstaller.cs
│   ├── RootLifetimeScope.cs, GameLifetimeScope.cs
│   └── AssemblyInfo.cs                                 (InternalsVisibleTo 测试程序集)
└── Tests/EditMode/
    ├── EasyFramework.Tests.EditMode.asmdef
    ├── StateMachineTests.cs, ObjectPoolTests.cs, TimerServiceTests.cs,
    ├── EventBusTests.cs, GameBootstrapTests.cs, FrameworkInstallerTests.cs
Assets/Game/Game.asmdef                                 (空业务程序集占位)
Assets/Scenes/Boot.unity                                (启动场景:RootLifetimeScope 节点)
```

依赖方向:`Tests → Boot → {Core, Services, Monetization}`;`Services/Monetization → Core`;`Game → {Boot, Core, Services}`。

---

### Task 1: 安装第三方包(串行,使用 UnityMCP)

**Files:** Modify: `Packages/manifest.json`(由 Unity Package Manager 写入)

- [ ] **Step 1: 依次安装 4 个包**(用 `manage_packages` 的 `add_package`,逐个执行并等待完成;git 包若带 tag 失败,去掉 `#tag` 重试取默认分支)

```
com.unity.nuget.newtonsoft-json@3.2.1
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.10
https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer#1.16.9
https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe#1.8.1
https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.VContainer#1.8.1
```

- [ ] **Step 2: 验证编译干净**

轮询 `mcpforunity://editor/state` 至 `is_compiling == false`,然后 `read_console(types=["error"])`。Expected: 0 errors。

- [ ] **Step 3: Commit**(编排者执行)

```bash
git add Packages/manifest.json Packages/packages-lock.json
git commit -m "feat: add VContainer/UniTask/MessagePipe/Newtonsoft packages"
```

---

### Task 2: asmdef 分层骨架(串行)

**Files:** Create 上方文件结构中全部 `.asmdef` 与占位 `AssemblyInfo.cs`

- [ ] **Step 1: 写 Core asmdef** — `Assets/EasyFramework/Core/EasyFramework.Core.asmdef`

```json
{
  "name": "EasyFramework.Core",
  "rootNamespace": "EasyFramework.Core",
  "references": ["UniTask", "VContainer", "MessagePipe", "MessagePipe.VContainer"],
  "noEngineReferences": false
}
```

- [ ] **Step 2: 写 Services / Monetization / DevTools asmdef**(三个同构,仅名字不同)

```json
{
  "name": "EasyFramework.Services",
  "rootNamespace": "EasyFramework.Services",
  "references": ["EasyFramework.Core", "UniTask", "VContainer", "MessagePipe"]
}
```

每个目录放一个占位 `AssemblyInfo.cs`(空程序集没有 .cs 文件时 Unity 会警告):

```csharp
// Placeholder: populated in later phases.
```

(注:文件内容就是这一行注释,合法的空编译单元。)

- [ ] **Step 3: 写 Boot asmdef** — `Assets/EasyFramework/Boot/EasyFramework.Boot.asmdef`

```json
{
  "name": "EasyFramework.Boot",
  "rootNamespace": "EasyFramework",
  "references": ["EasyFramework.Core", "EasyFramework.Services", "EasyFramework.Monetization", "UniTask", "VContainer", "MessagePipe", "MessagePipe.VContainer"]
}
```

`Assets/EasyFramework/Boot/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EasyFramework.Tests.EditMode")]
```

- [ ] **Step 4: 写 Tests asmdef** — `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

```json
{
  "name": "EasyFramework.Tests.EditMode",
  "rootNamespace": "EasyFramework.Tests",
  "references": ["EasyFramework.Core", "EasyFramework.Boot", "UniTask", "VContainer", "MessagePipe", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "overrideReferences": true
}
```

- [ ] **Step 5: 写 Game asmdef** — `Assets/Game/Game.asmdef`

```json
{
  "name": "Game",
  "rootNamespace": "Game",
  "references": ["EasyFramework.Core", "EasyFramework.Services", "EasyFramework.Boot", "UniTask", "VContainer"]
}
```

加占位 `Assets/Game/AssemblyInfo.cs`(同 Step 2 格式)。

- [ ] **Step 6: 刷新并验证编译**(`refresh_unity` → 轮询 editor/state → `read_console`)。Expected: 0 errors。

- [ ] **Step 7: Commit**(编排者)`git add Assets/EasyFramework Assets/Game && git commit -m "feat: add asmdef layer skeleton"`

---

### Task 3: StateMachine(可并行)

**Files:**
- Create: `Assets/EasyFramework/Core/Fsm/State.cs`, `Assets/EasyFramework/Core/Fsm/StateMachine.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/StateMachineTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Fsm;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class StateMachineTests
    {
        class Ctx { public List<string> Log = new(); }

        class StateA : State<Ctx>
        {
            public override UniTask Enter() { Context.Log.Add("A.Enter"); return UniTask.CompletedTask; }
            public override void Update(float dt) => Context.Log.Add($"A.Update:{dt}");
            public override void Exit() => Context.Log.Add("A.Exit");
        }

        class StateB : State<Ctx>
        {
            public override UniTask Enter() { Context.Log.Add("B.Enter"); return UniTask.CompletedTask; }
        }

        [Test]
        public void ChangeState_RunsExitThenEnter()
        {
            var ctx = new Ctx();
            var fsm = new StateMachine<Ctx>(ctx);
            fsm.AddState(new StateA());
            fsm.AddState(new StateB());

            fsm.ChangeState<StateA>().GetAwaiter().GetResult();
            fsm.Update(0.5f);
            fsm.ChangeState<StateB>().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[] { "A.Enter", "A.Update:0.5", "A.Exit", "B.Enter" }, ctx.Log);
            Assert.IsInstanceOf<StateB>(fsm.Current);
        }

        [Test]
        public void Update_WithoutState_DoesNothing()
        {
            var fsm = new StateMachine<Ctx>(new Ctx());
            Assert.DoesNotThrow(() => fsm.Update(0.1f));
        }

        [Test]
        public void ChangeState_ToUnregistered_Throws()
        {
            var fsm = new StateMachine<Ctx>(new Ctx());
            Assert.Throws<KeyNotFoundException>(() =>
                fsm.ChangeState<StateA>().GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: 实现 State 基类** — `State.cs`

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Fsm
{
    public abstract class State<TContext>
    {
        protected TContext Context { get; private set; }
        protected StateMachine<TContext> Machine { get; private set; }

        internal void Attach(StateMachine<TContext> machine, TContext context)
        {
            Machine = machine;
            Context = context;
        }

        public virtual UniTask Enter() => UniTask.CompletedTask;
        public virtual void Update(float deltaTime) { }
        public virtual void Exit() { }
    }
}
```

- [ ] **Step 3: 实现 StateMachine** — `StateMachine.cs`

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Fsm
{
    public sealed class StateMachine<TContext>
    {
        readonly Dictionary<Type, State<TContext>> _states = new();
        readonly TContext _context;
        int _version;

        public State<TContext> Current { get; private set; }

        public StateMachine(TContext context) => _context = context;

        public void AddState(State<TContext> state)
        {
            state.Attach(this, _context);
            _states[state.GetType()] = state;
        }

        public async UniTask ChangeState<TState>() where TState : State<TContext>
        {
            if (!_states.TryGetValue(typeof(TState), out var next))
                throw new KeyNotFoundException($"State {typeof(TState).Name} not registered.");

            // 版本守卫:Enter await 期间若有更晚的 ChangeState,本次后续不再生效
            var version = ++_version;
            Current?.Exit();
            Current = next;
            await next.Enter();
            if (version != _version) return;
        }

        public void Update(float deltaTime) => Current?.Update(deltaTime);
    }
}
```

- [ ] **Step 4: 验证测试通过**(验证代理:`run_tests(mode="EditMode", test_names=["EasyFramework.Tests.StateMachineTests"])` 风格按 fixture 过滤)Expected: 全 PASS。

- [ ] **Step 5: Commit**(编排者)`git commit -m "feat(core): add async state machine"`

---

### Task 4: ObjectPool(可并行)

**Files:**
- Create: `Assets/EasyFramework/Core/Pooling/IPoolable.cs`, `Assets/EasyFramework/Core/Pooling/ObjectPool.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/ObjectPoolTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System;
using EasyFramework.Core.Pooling;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ObjectPoolTests
    {
        class Bullet : IPoolable
        {
            public int SpawnCount, DespawnCount;
            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        [Test]
        public void Spawn_ReusesDespawnedInstance()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet());
            var a = pool.Spawn();
            pool.Despawn(a);
            var b = pool.Spawn();
            Assert.AreSame(a, b);
            Assert.AreEqual(2, a.SpawnCount);
            Assert.AreEqual(1, a.DespawnCount);
        }

        [Test]
        public void Despawn_Twice_Throws()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet());
            var a = pool.Spawn();
            pool.Despawn(a);
            Assert.Throws<InvalidOperationException>(() => pool.Despawn(a));
        }

        [Test]
        public void Prewarm_FillsInactiveCount()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet(), initialCapacity: 5);
            Assert.AreEqual(5, pool.CountInactive);
        }

        [Test]
        public void Clear_InvokesDestroyCallback()
        {
            var destroyed = 0;
            var pool = new ObjectPool<Bullet>(() => new Bullet(), initialCapacity: 3);
            pool.Clear(_ => destroyed++);
            Assert.AreEqual(3, destroyed);
            Assert.AreEqual(0, pool.CountInactive);
        }
    }
}
```

- [ ] **Step 2: 实现** — `IPoolable.cs`

```csharp
namespace EasyFramework.Core.Pooling
{
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
```

`ObjectPool.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace EasyFramework.Core.Pooling
{
    public sealed class ObjectPool<T> where T : class
    {
        readonly Func<T> _factory;
        readonly Stack<T> _inactive = new();
        readonly HashSet<T> _inactiveSet = new();

        public int CountInactive => _inactive.Count;

        public ObjectPool(Func<T> factory, int initialCapacity = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            for (var i = 0; i < initialCapacity; i++)
            {
                var item = _factory();
                _inactive.Push(item);
                _inactiveSet.Add(item);
            }
        }

        public T Spawn()
        {
            var item = _inactive.Count > 0 ? _inactive.Pop() : _factory();
            _inactiveSet.Remove(item);
            (item as IPoolable)?.OnSpawn();
            return item;
        }

        public void Despawn(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!_inactiveSet.Add(item))
                throw new InvalidOperationException("Item already despawned.");
            (item as IPoolable)?.OnDespawn();
            _inactive.Push(item);
        }

        public void Clear(Action<T> onDestroy = null)
        {
            while (_inactive.Count > 0)
                onDestroy?.Invoke(_inactive.Pop());
            _inactiveSet.Clear();
        }
    }
}
```

- [ ] **Step 3: 验证测试通过**(验证代理统一执行)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(core): add generic object pool"`

---

### Task 5: TimerService(可并行)

**Files:**
- Create: `Assets/EasyFramework/Core/Timing/ITimerService.cs`, `TimerHandle.cs`, `TimerService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/TimerServiceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using EasyFramework.Core.Timing;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class TimerServiceTests
    {
        [Test]
        public void Schedule_FiresAfterDelay()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++);
            svc.Advance(0.5f, 0.5f);
            Assert.AreEqual(0, fired);
            svc.Advance(0.6f, 0.6f);
            Assert.AreEqual(1, fired);
            svc.Advance(5f, 5f);
            Assert.AreEqual(1, fired, "一次性定时器只触发一次");
        }

        [Test]
        public void Schedule_Repeat_FiresEveryInterval()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++, repeat: true);
            svc.Advance(3.05f, 3.05f);
            Assert.AreEqual(3, fired);
        }

        [Test]
        public void Cancel_PreventsFiring()
        {
            var svc = new TimerService();
            var fired = 0;
            var handle = svc.Schedule(1.0f, () => fired++);
            svc.Cancel(handle);
            svc.Advance(2f, 2f);
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void UnscaledTimer_UsesUnscaledDelta()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(1.0f, () => fired++, useUnscaledTime: true);
            svc.Advance(scaledDelta: 0f, unscaledDelta: 1.5f); // timeScale=0 模拟暂停
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Callback_CanScheduleAnotherTimer()
        {
            var svc = new TimerService();
            var fired = 0;
            svc.Schedule(0.5f, () => svc.Schedule(0.5f, () => fired++));
            svc.Advance(0.6f, 0.6f);
            svc.Advance(0.6f, 0.6f);
            Assert.AreEqual(1, fired);
        }
    }
}
```

- [ ] **Step 2: 实现** — `TimerHandle.cs`

```csharp
namespace EasyFramework.Core.Timing
{
    public readonly struct TimerHandle
    {
        internal readonly long Id;
        internal TimerHandle(long id) => Id = id;
        public bool IsValid => Id != 0;
    }
}
```

`ITimerService.cs`:

```csharp
using System;

namespace EasyFramework.Core.Timing
{
    public interface ITimerService
    {
        TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false);
        void Cancel(TimerHandle handle);
    }
}
```

`TimerService.cs`(实现 `VContainer.Unity.ITickable`,真实驱动用 `Time.deltaTime`;`Advance` 抽出便于单测):

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace EasyFramework.Core.Timing
{
    public sealed class TimerService : ITimerService, ITickable
    {
        sealed class Entry
        {
            public long Id;
            public float Remaining;
            public float Interval;
            public bool Repeat;
            public bool Unscaled;
            public Action Callback;
            public bool Cancelled;
        }

        readonly List<Entry> _entries = new();
        readonly List<Entry> _pendingAdd = new();
        long _nextId = 1;
        bool _iterating;

        public TimerHandle Schedule(float delay, Action callback, bool repeat = false, bool useUnscaledTime = false)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var entry = new Entry
            {
                Id = _nextId++, Remaining = delay, Interval = delay,
                Repeat = repeat, Unscaled = useUnscaledTime, Callback = callback
            };
            if (_iterating) _pendingAdd.Add(entry); else _entries.Add(entry);
            return new TimerHandle(entry.Id);
        }

        public void Cancel(TimerHandle handle)
        {
            foreach (var e in _entries)
                if (e.Id == handle.Id) { e.Cancelled = true; return; }
            foreach (var e in _pendingAdd)
                if (e.Id == handle.Id) { e.Cancelled = true; return; }
        }

        public void Tick() => Advance(Time.deltaTime, Time.unscaledDeltaTime);

        // internal for tests via direct call(本类在 Core,方法设 public 供测试与高级用法)
        public void Advance(float scaledDelta, float unscaledDelta)
        {
            _iterating = true;
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Cancelled) continue;
                e.Remaining -= e.Unscaled ? unscaledDelta : scaledDelta;
                while (e.Remaining <= 0f && !e.Cancelled)
                {
                    e.Callback();
                    if (e.Repeat) e.Remaining += e.Interval;
                    else { e.Cancelled = true; break; }
                }
            }
            _iterating = false;
            _entries.RemoveAll(e => e.Cancelled);
            if (_pendingAdd.Count > 0) { _entries.AddRange(_pendingAdd); _pendingAdd.Clear(); }
        }
    }
}
```

- [ ] **Step 3: 验证测试通过**(验证代理统一执行)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(core): add timer service"`

---

### Task 6: EventBus(可并行)

**Files:**
- Create: `Assets/EasyFramework/Core/Events/IEventBus.cs`, `MessagePipeEventBus.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/EventBusTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using EasyFramework.Core.Events;
using MessagePipe;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class EventBusTests
    {
        readonly struct ScoreEvent
        {
            public readonly int Amount;
            public ScoreEvent(int amount) => Amount = amount;
        }

        IObjectResolver BuildContainer()
        {
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            return builder.Build();
        }

        [Test]
        public void PublishedEvent_ReachesSubscriber()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var total = 0;
            bus.Subscribe<ScoreEvent>(e => total += e.Amount);
            bus.Publish(new ScoreEvent(10));
            bus.Publish(new ScoreEvent(5));
            Assert.AreEqual(15, total);
        }

        [Test]
        public void DisposedSubscription_StopsReceiving()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            var total = 0;
            var sub = bus.Subscribe<ScoreEvent>(e => total += e.Amount);
            bus.Publish(new ScoreEvent(10));
            sub.Dispose();
            bus.Publish(new ScoreEvent(99));
            Assert.AreEqual(10, total);
        }

        [Test]
        public void MultipleSubscribers_AllReceive()
        {
            var bus = BuildContainer().Resolve<IEventBus>();
            int a = 0, b = 0;
            bus.Subscribe<ScoreEvent>(_ => a++);
            bus.Subscribe<ScoreEvent>(_ => b++);
            bus.Publish(new ScoreEvent(1));
            Assert.AreEqual(1, a);
            Assert.AreEqual(1, b);
        }
    }
}
```

- [ ] **Step 2: 实现** — `IEventBus.cs`

```csharp
using System;

namespace EasyFramework.Core.Events
{
    public interface IEventBus
    {
        void Publish<T>(T evt);
        IDisposable Subscribe<T>(Action<T> handler);
    }
}
```

`MessagePipeEventBus.cs`(懒解析 + 缓存 broker,避免每次 Resolve):

```csharp
using System;
using System.Collections.Generic;
using MessagePipe;
using VContainer;

namespace EasyFramework.Core.Events
{
    public sealed class MessagePipeEventBus : IEventBus
    {
        readonly IObjectResolver _resolver;
        readonly Dictionary<Type, object> _publishers = new();
        readonly Dictionary<Type, object> _subscribers = new();

        public MessagePipeEventBus(IObjectResolver resolver) => _resolver = resolver;

        public void Publish<T>(T evt)
        {
            if (!_publishers.TryGetValue(typeof(T), out var pub))
            {
                pub = _resolver.Resolve<IPublisher<T>>();
                _publishers[typeof(T)] = pub;
            }
            ((IPublisher<T>)pub).Publish(evt);
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

**风险与回退:** 此实现依赖 MessagePipe.VContainer 的 `RegisterMessagePipe()` 以开放泛型注册 `IPublisher<>`/`ISubscriber<>`(无需逐类型注册)。若测试报 VContainer 解析异常,先 WebFetch MessagePipe README 的 VContainer 章节核对 API;若该版本确不支持开放泛型,则改为自实现:`Dictionary<Type, List<Delegate>>` + 锁,接口不变、测试不变(去掉 `RegisterMessagePipe()` 一行)。

- [ ] **Step 3: 验证测试通过**(验证代理统一执行)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(core): add typed event bus over MessagePipe"`

---

### Task 7: Boot 管线(可并行)

**Files:**
- Create: `Assets/EasyFramework/Core/Boot/IBootTask.cs`, `GameBootstrap.cs`, `BootCompletedEvent.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/GameBootstrapTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class GameBootstrapTests
    {
        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        sealed class FakeTask : IBootTask
        {
            readonly Action _onRun;
            public FakeTask(int priority, bool critical, Action onRun = null, bool fail = false)
            { Priority = priority; IsCritical = critical; _onRun = onRun; _fail = fail; }
            readonly bool _fail;
            public int Priority { get; }
            public bool IsCritical { get; }
            public UniTask InitializeAsync(CancellationToken ct)
            {
                _onRun?.Invoke();
                return _fail ? UniTask.FromException(new Exception("boom")) : UniTask.CompletedTask;
            }
        }

        [Test]
        public void Tasks_RunInPriorityOrder()
        {
            var order = new List<int>();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(20, true, () => order.Add(20)),
                new FakeTask(0, true, () => order.Add(0)),
                new FakeTask(10, true, () => order.Add(10)),
            }, new FakeBus());
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 0, 10, 20 }, order);
        }

        [Test]
        public void NonCriticalFailure_DoesNotAbortBoot()
        {
            var ranAfter = false;
            var bus = new FakeBus();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(0, critical: false, fail: true),
                new FakeTask(10, true, () => ranAfter = true),
            }, bus);
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(ranAfter);
            Assert.AreEqual(1, bus.Published.Count, "完成事件仍应发布");
        }

        [Test]
        public void CriticalFailure_AbortsBoot()
        {
            var ranAfter = false;
            var bus = new FakeBus();
            var boot = new GameBootstrap(new IBootTask[]
            {
                new FakeTask(0, critical: true, fail: true),
                new FakeTask(10, true, () => ranAfter = true),
            }, bus);
            Assert.Throws<BootFailedException>(() =>
                boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsFalse(ranAfter);
            Assert.AreEqual(0, bus.Published.Count);
        }

        [Test]
        public void Completion_PublishesBootCompletedEvent()
        {
            var bus = new FakeBus();
            var boot = new GameBootstrap(Array.Empty<IBootTask>(), bus);
            boot.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsInstanceOf<BootCompletedEvent>(bus.Published[0]);
        }
    }
}
```

- [ ] **Step 2: 实现** — `IBootTask.cs`

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Boot
{
    public interface IBootTask
    {
        /// <summary>越小越先执行;相同优先级的任务并行执行。</summary>
        int Priority { get; }
        /// <summary>true:失败中止启动并抛 BootFailedException;false:记录日志后继续。</summary>
        bool IsCritical { get; }
        UniTask InitializeAsync(CancellationToken ct);
    }
}
```

`BootCompletedEvent.cs`:

```csharp
namespace EasyFramework.Core.Boot
{
    public readonly struct BootCompletedEvent { }
}
```

`GameBootstrap.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using UnityEngine;
using VContainer.Unity;

namespace EasyFramework.Core.Boot
{
    public sealed class BootFailedException : Exception
    {
        public BootFailedException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class GameBootstrap : IAsyncStartable
    {
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
        }
    }
}
```

- [ ] **Step 3: 验证测试通过**(验证代理统一执行)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(core): add boot pipeline with priority groups"`

---

### Task 8: G 门面 + FrameworkInstaller + LifetimeScope(可并行;接口契约见 Task 5/6/7)

**Files:**
- Create: `Assets/EasyFramework/Boot/G.cs`, `FrameworkInstaller.cs`, `RootLifetimeScope.cs`, `GameLifetimeScope.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/FrameworkInstallerTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        [TearDown]
        public void TearDown() => EasyFramework.G.Reset();

        IObjectResolver Build()
        {
            var builder = new ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder);
            return builder.Build();
        }

        [Test]
        public void Install_ResolvesCoreServices()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IEventBus>());
            Assert.NotNull(c.Resolve<ITimerService>());
        }

        [Test]
        public void GFacade_BindsAfterInitialize()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IEventBus>(), EasyFramework.G.Events);
            Assert.AreSame(c.Resolve<ITimerService>(), EasyFramework.G.Timer);
        }

        [Test]
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Events);
        }
    }
}
```

- [ ] **Step 2: 实现** — `G.cs`

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using VContainer;

namespace EasyFramework
{
    /// <summary>业务层快速访问门面。框架内部禁止使用,模块间一律构造注入。</summary>
    public static class G
    {
        public static IEventBus Events { get; private set; }
        public static ITimerService Timer { get; private set; }
        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            IsInitialized = true;
        }

        internal static void Reset()
        {
            Events = null;
            Timer = null;
            IsInitialized = false;
        }
    }
}
```

`FrameworkInstaller.cs`:

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using MessagePipe;
using VContainer;

namespace EasyFramework
{
    /// <summary>框架服务注册(纯逻辑,便于脱离 MonoBehaviour 测试)。入口点注册在 RootLifetimeScope。</summary>
    public static class FrameworkInstaller
    {
        public static void Install(IContainerBuilder builder)
        {
            builder.RegisterMessagePipe();
            builder.Register<IEventBus, MessagePipeEventBus>(Lifetime.Singleton);
            builder.Register<TimerService>(Lifetime.Singleton).As<ITimerService>().AsSelf();
        }
    }
}
```

`RootLifetimeScope.cs`:

```csharp
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架组合根。挂在 Boot 场景的常驻 GameObject 上。</summary>
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            FrameworkInstaller.Install(builder);

            builder.RegisterEntryPoint<GameBootstrap>();
            // TimerService 已在 Installer 注册,这里把它挂进 Tick 调度
            builder.RegisterBuildCallback(r =>
            {
                G.Initialize(r);
            });
            builder.UseEntryPoints(ep => ep.Add<TimerTicker>());
        }
    }

    /// <summary>把 TimerService.Advance 桥接到 VContainer 的 Tick 循环。</summary>
    sealed class TimerTicker : ITickable
    {
        readonly TimerService _timer;
        public TimerTicker(TimerService timer) => _timer = timer;
        public void Tick() => _timer.Tick();
    }
}
```

(实现代理注意:若 `UseEntryPoints` 链式 API 与所装 VContainer 版本不符,用 `unity_reflect`/WebFetch VContainer 文档核对后改用等效写法,目标只有一个——`TimerTicker` 与 `GameBootstrap` 作为入口点被调度。)

`GameLifetimeScope.cs`:

```csharp
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>每个小游戏继承此类注册自己的服务;作为 RootLifetimeScope 的子作用域。</summary>
    public abstract class GameLifetimeScope : LifetimeScope
    {
        protected sealed override void Configure(IContainerBuilder builder) => ConfigureGame(builder);
        protected abstract void ConfigureGame(IContainerBuilder builder);
    }
}
```

- [ ] **Step 3: 验证测试通过**(验证代理统一执行)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(boot): add G facade, installer and lifetime scopes"`

---

### Task 9: Boot 场景(串行,使用 UnityMCP)

**Files:** Create: `Assets/Scenes/Boot.unity`

- [ ] **Step 1:** `manage_scene(action="create", name="Boot", path="Assets/Scenes")` 创建空场景并加载。
- [ ] **Step 2:** 创建节点 `[EasyFramework]`,挂 `RootLifetimeScope` 组件(`manage_gameobject` create + `components_to_add=["RootLifetimeScope"]`)。
- [ ] **Step 3:** 保存场景;进入 Play 模式 3 秒(`manage_editor(action="play")` → `read_console` → `stop`)。Expected: 无错误,无 BootFailedException。
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat: add Boot scene with framework root scope"`

---

### Task 10: 全量验证(串行)

- [ ] **Step 1:** `run_tests(mode="EditMode")` 全量跑;`get_test_job` 轮询。Expected: 全部 PASS(约 18 个用例)。
- [ ] **Step 2:** `read_console(types=["error","warning"])`。Expected: 无框架相关警告。
- [ ] **Step 3:** 更新 `docs/superpowers/specs/2026-06-12-easyframework-design.md` 不需要;若实现与计划有偏差(如 VContainer API 调整),在本计划文档末尾追加 "Deviations" 小节记录。
- [ ] **Step 4: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- Spec 覆盖:Phase 1 范围 = 设计文档 §10 Phase 1 条目(asmdef、VContainer、Bootstrap、G、EventBus、StateMachine、Timer)+ ObjectPool(设计 §3.4,纯 C# 部分;GameObject 池依赖 Phase 2 Asset 服务,延后)。✓
- 类型一致性:`IBootTask.Priority/IsCritical/InitializeAsync`、`IEventBus.Publish/Subscribe`、`ITimerService.Schedule/Cancel`、`G.Events/Timer/IsInitialized/Initialize/Reset` 在测试与实现间已核对。✓
- 占位符:无 TBD;两处"版本/API 可能漂移"均给出具体回退动作(去 tag 重装、查文档改注册写法、EventBus 自实现回退)。✓

---

## Deviations

验证代理(Task 9/10)记录的实际偏差:

1. **Tests asmdef 增加 `MessagePipe.VContainer` 引用**(`Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`)。
   - 计划 Task 2 Step 4 的 Tests asmdef references 列表缺少 `MessagePipe.VContainer`,但 `EventBusTests.cs`(Task 6 Step 1)直接调用 `builder.RegisterMessagePipe()`——该扩展方法定义在 `MessagePipe.VContainer` 程序集中,导致编译错误 CS1061("'ContainerBuilder' does not contain a definition for 'RegisterMessagePipe'")。
   - 修复:在 Tests asmdef references 中追加 `"MessagePipe.VContainer"`(与 Core/Boot asmdef 一致)。
   - 性质:仅构建依赖图修正,未改动任何接口契约、测试代码或实现代码。修复后编译 0 error,全量 22 用例全 PASS。

其余无偏差:EventBus 的 `RegisterMessagePipe()` 开放泛型注册按计划主路径工作,未触发 Task 6 / Task 8 的回退方案;VContainer `UseEntryPoints` 链式 API 按计划写法编译通过;Boot 场景 Play 模式无报错、无 BootFailedException。
