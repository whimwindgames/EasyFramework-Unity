# EasyFramework 资源热更新服务(ContentUpdate)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 EasyFramework 加一个 `IContentUpdateService`,基于 Unity Addressables 官方 Remote Content Update 机制(`Addressables.CheckForCatalogUpdates` / `Addressables.UpdateCatalogs`)检测并下载资源/内容更新,启动时静默检查一次但不自动下载,是否下载的决策权留给业务层。

**Architecture:** 沿用框架既有的 Provider/Service/BootTask 三件套(参照 `Services/Configs` 与 `Monetization/Ads`)。新增 `EasyFramework.Services.ContentUpdate` 命名空间,挂在既有 `EasyFramework.Services.asmdef` 下(不新建 asmdef)。`IAddressablesCatalogGateway` 把 Addressables 静态 API 包一层,真实实现 `AddressablesCatalogGateway` 是唯一调用 `Addressables.CheckForCatalogUpdates`/`Addressables.UpdateCatalogs` 的地方;`ContentUpdateService` 的全部业务逻辑(是否发事件、失败处理、避免重复检查)只依赖 `IAddressablesCatalogGateway` 接口,单测用 `FakeAddressablesCatalogGateway` 完全脱离真实 Addressables。`ContentUpdateBootTask`(`IsCritical=false`,`Priority=20`,排在 `SaveBootTask`(0)、`ConfigBootTask`(10)之后,`AdsBootTask`(30)之前)在启动时调用一次 `CheckAsync()`,检测到更新只发布 `ContentAvailableEvent`,不自动下载。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe(`IEventBus`)/ Unity Addressables / Unity Test Framework 1.6(NUnit EditMode)

---

## 文件结构总览

```
Packages/com.yifei.easyframework/Services/ContentUpdate/
├── IContentUpdateService.cs          (Create — ContentUpdateInfo/事件结构体/接口)
├── IAddressablesCatalogGateway.cs    (Create — 网关接口)
├── AddressablesCatalogGateway.cs     (Create — 真实实现,唯一调用 Addressables 静态 API 处)
├── ContentUpdateService.cs           (Create — 业务逻辑实现)
└── ContentUpdateBootTask.cs          (Create — 启动时静默 CheckAsync 一次)

Packages/com.yifei.easyframework/Boot/
├── G.cs                              (Modify — 加 G.ContentUpdate)
└── FrameworkInstaller.cs             (Modify — 注册 Gateway/Service/BootTask)

Packages/com.yifei.easyframework/Tests/EditMode/
└── ContentUpdateServiceTests.cs      (Create — FakeAddressablesCatalogGateway 内嵌于此)

Packages/com.yifei.easyframework/Samples~/Template/
└── TemplateGameFlow.cs               (Modify — GameplayState.Enter 补订阅示例注释,手动验证用)
```

依赖方向:`ContentUpdateService` 依赖 `IAddressablesCatalogGateway` + `IEventBus`;`ContentUpdateBootTask` 依赖 `ContentUpdateService`;`FrameworkInstaller` 注册三者;`G.ContentUpdate` 从容器解析 `IContentUpdateService`。

---

### Task 1: 接口与事件结构体

**Files:**
- Create: `Packages/com.yifei.easyframework/Services/ContentUpdate/IContentUpdateService.cs`
- Create: `Packages/com.yifei.easyframework/Services/ContentUpdate/IAddressablesCatalogGateway.cs`
- Test: `Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs`(本 Task 只写编译期契约测试,后续 Task 追加行为测试到同一文件)

- [ ] **Step 1: Write the failing test**

先写一个只验证类型契约能编译、`ContentUpdateInfo` 字段可读写的最小测试,确认接口先于实现存在时测试能表达意图(此时 `ContentUpdateService` 还不存在,测试整体不能编译,这就是失败态)。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.ContentUpdate;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ContentUpdateServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<System.Type, List<System.Delegate>> _handlers = new();
            public readonly List<object> Published = new();

            public void Publish<T>(T evt)
            {
                Published.Add(evt);
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((System.Action<T>)d)(evt);
            }

            public System.IDisposable Subscribe<T>(System.Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<System.Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }

            sealed class Sub : System.IDisposable
            {
                readonly System.Action _dispose;
                public Sub(System.Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public bool UpdateSucceeds = true;

            public UniTask<List<string>> CheckForCatalogUpdatesAsync()
                => UniTask.FromResult(CatalogsWithUpdates);

            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
                => UniTask.FromResult(UpdateSucceeds);
        }

        [Test]
        public void ContentUpdateInfo_FieldsAreReadable()
        {
            var info = new ContentUpdateInfo(true, 1024L);
            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(1024L, info.EstimatedDownloadSizeBytes);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateServiceTests`。
预期:编译失败(`CS0246: The type or namespace name 'IAddressablesCatalogGateway' could not be found` 等),因为 `EasyFramework.Services.ContentUpdate` 命名空间下还没有任何类型。Test Runner 会在 EditMode 标签页显示编译错误而非红叉用例。

- [ ] **Step 3: Write minimal implementation**

`Packages/com.yifei.easyframework/Services/ContentUpdate/IContentUpdateService.cs`:

```csharp
using System;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.ContentUpdate
{
    public readonly struct ContentUpdateInfo
    {
        public readonly bool IsAvailable;
        /// <summary>无法预估时为 -1。</summary>
        public readonly long EstimatedDownloadSizeBytes;

        public ContentUpdateInfo(bool isAvailable, long estimatedDownloadSizeBytes)
        {
            IsAvailable = isAvailable;
            EstimatedDownloadSizeBytes = estimatedDownloadSizeBytes;
        }
    }

    public readonly struct ContentAvailableEvent
    {
        public readonly ContentUpdateInfo Info;
        public ContentAvailableEvent(ContentUpdateInfo info) => Info = info;
    }

    public readonly struct ContentUpdateAppliedEvent
    {
    }

    public readonly struct ContentUpdateFailedEvent
    {
        public readonly string Reason;
        public ContentUpdateFailedEvent(string reason) => Reason = reason;
    }

    public interface IContentUpdateService
    {
        bool HasChecked { get; }

        /// <summary>启动时已自动检查一次;业务层可在设置页提供"检查更新"按钮再次调用。</summary>
        UniTask<ContentUpdateInfo> CheckAsync();

        /// <summary>下载并应用检测到的更新。未先调用过 CheckAsync 时会内部先检查一次。</summary>
        UniTask<bool> DownloadAndApplyAsync(IProgress<float> progress = null);
    }
}
```

`Packages/com.yifei.easyframework/Services/ContentUpdate/IAddressablesCatalogGateway.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.ContentUpdate
{
    /// <summary>
    /// Addressables.CheckForCatalogUpdates() / Addressables.UpdateCatalogs() 的薄封装。
    /// 真实实现见 AddressablesCatalogGateway;EditMode 测试用 FakeAddressablesCatalogGateway 替身。
    /// </summary>
    public interface IAddressablesCatalogGateway
    {
        /// <summary>返回有更新的 catalog key 列表;无更新时返回空列表。</summary>
        UniTask<List<string>> CheckForCatalogUpdatesAsync();

        /// <summary>下载并应用指定 catalog 的更新;成功返回 true。</summary>
        UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateServiceTests`。
预期:编译通过,`ContentUpdateInfo_FieldsAreReadable` 用例 PASS(1 个用例绿)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/ContentUpdate/IContentUpdateService.cs Packages/com.yifei.easyframework/Services/ContentUpdate/IAddressablesCatalogGateway.cs Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs
git commit -m "feat(content-update): add IContentUpdateService and IAddressablesCatalogGateway contracts"
```

---

### Task 2: ContentUpdateService 业务逻辑(核心行为,TDD 主体)

**Files:**
- Create: `Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateService.cs`
- Modify: `Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs`(追加行为测试,`FakeAddressablesCatalogGateway`/`FakeBus` 已在 Task 1 定义,直接复用)

- [ ] **Step 1: Write the failing test**

把 `ContentUpdateServiceTests.cs` 整体替换为下面版本(在 Task 1 基础上新增 `Build` 帮助方法与 5 个行为用例,`FakeBus`/`FakeAddressablesCatalogGateway`/`ContentUpdateInfo_FieldsAreReadable` 保持不变)：

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.ContentUpdate;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ContentUpdateServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<System.Type, List<System.Delegate>> _handlers = new();
            public readonly List<object> Published = new();

            public void Publish<T>(T evt)
            {
                Published.Add(evt);
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((System.Action<T>)d)(evt);
            }

            public System.IDisposable Subscribe<T>(System.Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<System.Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }

            sealed class Sub : System.IDisposable
            {
                readonly System.Action _dispose;
                public Sub(System.Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }

            public List<T> PublishedOf<T>()
            {
                var result = new List<T>();
                foreach (var p in Published)
                    if (p is T t) result.Add(t);
                return result;
            }
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public bool UpdateSucceeds = true;
            public int CheckCallCount;
            public int UpdateCallCount;

            public UniTask<List<string>> CheckForCatalogUpdatesAsync()
            {
                CheckCallCount++;
                return UniTask.FromResult(CatalogsWithUpdates);
            }

            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
            {
                UpdateCallCount++;
                return UniTask.FromResult(UpdateSucceeds);
            }
        }

        [Test]
        public void ContentUpdateInfo_FieldsAreReadable()
        {
            var info = new ContentUpdateInfo(true, 1024L);
            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(1024L, info.EstimatedDownloadSizeBytes);
        }

        [Test]
        public void CheckAsync_UpdateAvailable_ReturnsAvailableAndPublishesEvent()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string> { "catalog_001" } };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            var info = svc.CheckAsync().GetAwaiter().GetResult();

            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(-1L, info.EstimatedDownloadSizeBytes);
            Assert.IsTrue(svc.HasChecked);
            var published = bus.PublishedOf<ContentAvailableEvent>();
            Assert.AreEqual(1, published.Count);
            Assert.IsTrue(published[0].Info.IsAvailable);
        }

        [Test]
        public void CheckAsync_NoUpdate_ReturnsNotAvailableAndPublishesNoEvent()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string>() };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            var info = svc.CheckAsync().GetAwaiter().GetResult();

            Assert.IsFalse(info.IsAvailable);
            Assert.IsTrue(svc.HasChecked);
            Assert.AreEqual(0, bus.PublishedOf<ContentAvailableEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_Success_PublishesAppliedEventAndReturnsTrue()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = true,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result);
            Assert.AreEqual(1, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_Failure_PublishesFailedEventReturnsFalseNoThrow()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = false,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            bool result = false;
            Assert.DoesNotThrow(() => result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult());

            Assert.IsFalse(result);
            Assert.AreEqual(1, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_WithoutPriorCheck_ChecksInternallyFirst()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = true,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            Assert.IsFalse(svc.HasChecked);
            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result);
            Assert.IsTrue(svc.HasChecked);
            Assert.AreEqual(1, gateway.CheckCallCount);
            Assert.AreEqual(1, gateway.UpdateCallCount);
        }

        [Test]
        public void DownloadAndApplyAsync_NoUpdateAvailable_ReturnsFalseWithoutCallingUpdate()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string>() };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result);
            Assert.AreEqual(0, gateway.UpdateCallCount);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateServiceTests`。
预期:编译失败(`CS0246: The type or namespace name 'ContentUpdateService' could not be found`),因为 `ContentUpdateService` 类还不存在。

- [ ] **Step 3: Write minimal implementation**

`Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateService.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.ContentUpdate
{
    public sealed class ContentUpdateService : IContentUpdateService
    {
        readonly IAddressablesCatalogGateway _gateway;
        readonly IEventBus _events;

        List<string> _pendingCatalogKeys = new();

        public bool HasChecked { get; private set; }

        public ContentUpdateService(IAddressablesCatalogGateway gateway, IEventBus events)
        {
            _gateway = gateway;
            _events = events;
        }

        public async UniTask<ContentUpdateInfo> CheckAsync()
        {
            _pendingCatalogKeys = await _gateway.CheckForCatalogUpdatesAsync() ?? new List<string>();
            HasChecked = true;

            var isAvailable = _pendingCatalogKeys.Count > 0;
            var info = new ContentUpdateInfo(isAvailable, -1L);

            if (isAvailable)
                _events.Publish(new ContentAvailableEvent(info));

            return info;
        }

        public async UniTask<bool> DownloadAndApplyAsync(IProgress<float> progress = null)
        {
            if (!HasChecked)
                await CheckAsync();

            if (_pendingCatalogKeys.Count == 0)
                return false;

            var success = await _gateway.UpdateCatalogsAsync(_pendingCatalogKeys, progress);
            if (success)
            {
                _events.Publish(new ContentUpdateAppliedEvent());
            }
            else
            {
                _events.Publish(new ContentUpdateFailedEvent("Addressables.UpdateCatalogs returned failure."));
            }

            return success;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateServiceTests`。
预期:编译通过,7 个用例全部 PASS(`ContentUpdateInfo_FieldsAreReadable`、`CheckAsync_UpdateAvailable_ReturnsAvailableAndPublishesEvent`、`CheckAsync_NoUpdate_ReturnsNotAvailableAndPublishesNoEvent`、`DownloadAndApplyAsync_Success_PublishesAppliedEventAndReturnsTrue`、`DownloadAndApplyAsync_Failure_PublishesFailedEventReturnsFalseNoThrow`、`DownloadAndApplyAsync_WithoutPriorCheck_ChecksInternallyFirst`、`DownloadAndApplyAsync_NoUpdateAvailable_ReturnsFalseWithoutCallingUpdate`)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateService.cs Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs
git commit -m "feat(content-update): implement ContentUpdateService business logic with event publishing"
```

---

### Task 3: AddressablesCatalogGateway 真实实现

**Files:**
- Create: `Packages/com.yifei.easyframework/Services/ContentUpdate/AddressablesCatalogGateway.cs`
- Test: 无新增自动化测试(spec 明确真实 Addressables 静态 API 不参与 EditMode 单测;本 Task 只做编译验证,行为由 Task 6 的手动接入示例覆盖)

- [ ] **Step 1: Write the failing test**

本组件是对 `Addressables.CheckForCatalogUpdates`/`Addressables.UpdateCatalogs` 静态 API 的直接转发,EditMode 测试环境没有真实远程 catalog 可用,spec §4 明确排除在自动化测试范围外。用编译期契约测试代替行为测试:确认 `AddressablesCatalogGateway` 实现了 `IAddressablesCatalogGateway` 且可被实例化。追加到 `ContentUpdateServiceTests.cs` 末尾(`ContentUpdateService` 相关测试类之外，同文件追加一个新的测试类)：

```csharp
using EasyFramework.Services.ContentUpdate;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class AddressablesCatalogGatewayTests
    {
        [Test]
        public void ImplementsIAddressablesCatalogGateway()
        {
            IAddressablesCatalogGateway gateway = new AddressablesCatalogGateway();
            Assert.IsNotNull(gateway);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.AddressablesCatalogGatewayTests`。
预期:编译失败(`CS0246: The type or namespace name 'AddressablesCatalogGateway' could not be found`),因为该类还不存在。

- [ ] **Step 3: Write minimal implementation**

`Packages/com.yifei.easyframework/Services/ContentUpdate/AddressablesCatalogGateway.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyFramework.Services.ContentUpdate
{
    /// <summary>
    /// IAddressablesCatalogGateway 的真实实现,直接转发到 Addressables 静态 API。
    /// 本类是唯一调用 Addressables.CheckForCatalogUpdates / Addressables.UpdateCatalogs 的地方;
    /// ContentUpdateService 的全部业务逻辑测试改走 FakeAddressablesCatalogGateway,不依赖本类。
    /// </summary>
    public sealed class AddressablesCatalogGateway : IAddressablesCatalogGateway
    {
        public async UniTask<List<string>> CheckForCatalogUpdatesAsync()
        {
            var handle = Addressables.CheckForCatalogUpdates(false);
            var result = await handle.ToUniTask();
            Addressables.Release(handle);
            return result ?? new List<string>();
        }

        public async UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
        {
            var handle = Addressables.UpdateCatalogs(catalogKeys, false);
            await handle.ToUniTask(progress: progress);
            progress?.Report(1f);

            var succeeded = handle.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(handle);
            return succeeded;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.AddressablesCatalogGatewayTests`。
预期:编译通过,`ImplementsIAddressablesCatalogGateway` PASS(1 个用例绿)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/ContentUpdate/AddressablesCatalogGateway.cs Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs
git commit -m "feat(content-update): add AddressablesCatalogGateway forwarding to Addressables static API"
```

---

### Task 4: ContentUpdateBootTask

**Files:**
- Create: `Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateBootTask.cs`
- Modify: `Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs`(追加 `ContentUpdateBootTaskTests` 测试类)

- [ ] **Step 1: Write the failing test**

追加到 `ContentUpdateServiceTests.cs` 末尾：

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.ContentUpdate;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ContentUpdateBootTaskTests
    {
        sealed class FakeBus : EasyFramework.Core.Events.IEventBus
        {
            public void Publish<T>(T evt) { }
            public System.IDisposable Subscribe<T>(System.Action<T> handler) => new NoopSub();
            sealed class NoopSub : System.IDisposable { public void Dispose() { } }
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public UniTask<List<string>> CheckForCatalogUpdatesAsync() => UniTask.FromResult(CatalogsWithUpdates);
            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
                => UniTask.FromResult(true);
        }

        [Test]
        public void Priority_Is20()
        {
            var svc = new ContentUpdateService(new FakeAddressablesCatalogGateway(), new FakeBus());
            var task = new ContentUpdateBootTask(svc);
            Assert.AreEqual(20, task.Priority);
        }

        [Test]
        public void IsCritical_IsFalse()
        {
            var svc = new ContentUpdateService(new FakeAddressablesCatalogGateway(), new FakeBus());
            var task = new ContentUpdateBootTask(svc);
            Assert.IsFalse(task.IsCritical);
        }

        [Test]
        public void InitializeAsync_CallsCheckAsyncOnce()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string> { "c" } };
            var svc = new ContentUpdateService(gateway, new FakeBus());
            var task = new ContentUpdateBootTask(svc);

            Assert.IsFalse(svc.HasChecked);
            task.InitializeAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(svc.HasChecked);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateBootTaskTests`。
预期:编译失败(`CS0246: The type or namespace name 'ContentUpdateBootTask' could not be found`),因为该类还不存在。

- [ ] **Step 3: Write minimal implementation**

`Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateBootTask.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Services.ContentUpdate
{
    public sealed class ContentUpdateBootTask : IBootTask
    {
        readonly ContentUpdateService _contentUpdate;
        public ContentUpdateBootTask(ContentUpdateService contentUpdate) => _contentUpdate = contentUpdate;

        public int Priority => 20;         // 晚于 Save(0)/Config(10),早于 Ads(30)/IAP(31)
        public bool IsCritical => false;   // 检查失败不阻塞启动;不自动下载

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await _contentUpdate.CheckAsync();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.ContentUpdateBootTaskTests`。
预期:编译通过,3 个用例全部 PASS(`Priority_Is20`、`IsCritical_IsFalse`、`InitializeAsync_CallsCheckAsyncOnce`)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/ContentUpdate/ContentUpdateBootTask.cs Packages/com.yifei.easyframework/Tests/EditMode/ContentUpdateServiceTests.cs
git commit -m "feat(content-update): add ContentUpdateBootTask for silent startup check"
```

---

### Task 5: 接线 —— FrameworkInstaller / G

**Files:**
- Modify: `Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs:154-155`(在 IAP 注册块之后追加 ContentUpdate 注册块)
- Modify: `Packages/com.yifei.easyframework/Boot/G.cs:30`(加 `ContentUpdate` 属性)、`:58`(Initialize 里加解析)、`:88`(Reset 里加清空)
- Test: `Packages/com.yifei.easyframework/Tests/EditMode/FrameworkInstallerTests.cs`

- [ ] **Step 1: Write the failing test**

在 `FrameworkInstallerTests.cs` 里追加 using 与三个测试方法。先展示需要新增的 using（放在既有 using 块的合适位置，按字母序插入 `EasyFramework.Services.ContentUpdate`）：

```csharp
using EasyFramework.Services.ContentUpdate;
```

在类内追加测试方法（放在 `GFacade_BindsMonetizationServices` 之后）：

```csharp
        [Test]
        public void Install_ResolvesContentUpdateService()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IContentUpdateService>());
        }

        [Test]
        public void GFacade_BindsContentUpdateService()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.AreSame(c.Resolve<IContentUpdateService>(), EasyFramework.G.ContentUpdate);
        }
```

同时把既有 `GFacade_ResetClearsBindings` 测试方法体里的断言列表追加一行（在 `Assert.IsNull(EasyFramework.G.Analytics);` 之后）：

```csharp
            Assert.IsNull(EasyFramework.G.Analytics);
            Assert.IsNull(EasyFramework.G.ContentUpdate);
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.FrameworkInstallerTests`。
预期:`Install_ResolvesContentUpdateService` 与 `GFacade_BindsContentUpdateService` 编译失败(`CS0246: The type or namespace name 'IContentUpdateService' could not be found` 若命名空间已从 Task 1 存在则改为解析失败 —— 实际上 `IContentUpdateService` 已在 Task 1 定义，故此处是运行时失败：`VContainer.VContainerException: EasyFramework.Services.ContentUpdate.IContentUpdateService is not registered`);`G.ContentUpdate` 编译失败(`CS0117: 'G' does not contain a definition for 'ContentUpdate'`)。

- [ ] **Step 3: Write minimal implementation**

`Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs` 第 154-155 行（`IAPBootTask` 注册之后、`}` 收尾之前）追加：

```csharp
            builder.Register<IAPBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- ContentUpdate(资源热更新)----
            // 真实实现直接转发 Addressables 静态 API;单测全部用 FakeAddressablesCatalogGateway 替身。
            builder.Register<IAddressablesCatalogGateway, AddressablesCatalogGateway>(Lifetime.Singleton);
            builder.Register<ContentUpdateService>(Lifetime.Singleton)
                .As<IContentUpdateService>().AsSelf();
            builder.Register<ContentUpdateBootTask>(Lifetime.Singleton).As<IBootTask>();
        }
```

（即：删除原来收尾的单独 `}`，把新块插入其前，再补回 `}`。）同时在文件顶部 using 块按字母序追加：

```csharp
using EasyFramework.Services.ContentUpdate;
```

`Packages/com.yifei.easyframework/Boot/G.cs` 第 30 行（`IConfigService Config` 属性之后）追加属性声明：

```csharp
        public static IConfigService Config { get; private set; }
        public static IContentUpdateService ContentUpdate { get; private set; }
        public static IPoolService Pool { get; private set; }
```

第 58 行 `Initialize` 方法内（`Config = resolver.Resolve<IConfigService>();` 之后）追加：

```csharp
            Config = resolver.Resolve<IConfigService>();
            ContentUpdate = resolver.Resolve<IContentUpdateService>();
            Pool = resolver.Resolve<IPoolService>();
```

第 88 行 `Reset` 方法内（`Config = null;` 之后）追加：

```csharp
            Config = null;
            ContentUpdate = null;
            Pool = null;
```

文件顶部 using 块按字母序追加（`EasyFramework.Services.Configs` 与 `EasyFramework.Services.Haptics` 之间）：

```csharp
using EasyFramework.Services.ContentUpdate;
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.FrameworkInstallerTests`。
预期:编译通过，全部用例 PASS，包括新增的 `Install_ResolvesContentUpdateService`、`GFacade_BindsContentUpdateService`，以及更新后的 `GFacade_ResetClearsBindings`。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs Packages/com.yifei.easyframework/Boot/G.cs Packages/com.yifei.easyframework/Tests/EditMode/FrameworkInstallerTests.cs
git commit -m "feat(content-update): wire ContentUpdate service into FrameworkInstaller and G facade"
```

---

### Task 6: Template Sample 最小接入示例(手动验证用,非自动化测试)

**Files:**
- Modify: `Packages/com.yifei.easyframework/Samples~/Template/TemplateGameFlow.cs:44-56`(`GameplayState` 内追加订阅示例)

- [ ] **Step 1: Write the failing test**

本 Task 不新增自动化测试——spec §4 明确"Template 或 TapRush Sample 补一个最小接入示例……属于实现阶段的手动验证项，不在自动化测试范围内"。`Samples~/` 目录本身不参与本工程编译（详见 `docs/superpowers/plans/2026-06-13-easyframework-upm-packaging.md` 的贯穿约束 2），因此这里没有可运行的失败测试。跳过 Step 1/Step 2 的"失败测试"环节，直接进入实现；验证方式改为"手动检查文件内容"。

- [ ] **Step 2: Run test to verify it fails**

不适用（本 Task 不涉及自动化测试）。人工检查:在修改前打开 `TemplateGameFlow.cs`，确认 `GameplayState.Enter()` 内当前只有占位注释，没有 ContentUpdate 相关代码。

- [ ] **Step 3: Write minimal implementation**

把 `Packages/com.yifei.easyframework/Samples~/Template/TemplateGameFlow.cs` 第 44-56 行的 `GameplayState` 类替换为：

```csharp
        public sealed class GameplayState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:加载关卡场景、G.UI.ShowHudAsync<GameHud>()、开始计时/生成对象。

                // ContentUpdate 最小接入示例(手动验证用,需要真实 Addressables Remote Group 配置):
                // 订阅 ContentAvailableEvent 后自行决定何时下载——框架不预设"立即下载/仅 WiFi/提示用户"策略。
                // G.Events.Subscribe<EasyFramework.Services.ContentUpdate.ContentAvailableEvent>(async _ =>
                // {
                //     var applied = await G.ContentUpdate.DownloadAndApplyAsync();
                //     if (applied) { /* 提示玩家重启关卡以应用新内容 */ }
                // });
                return UniTask.CompletedTask;
            }

            public override void Update(float deltaTime)
            {
                // 扩展点:玩法每帧逻辑(倒计时、刷怪)。结束时调 Context.ToResult()。
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

不适用（无自动化测试）。人工检查:重新打开 `TemplateGameFlow.cs`，确认新增的注释块语法正确（是合法 C# 注释，不会被误当作代码编译——因为 `Samples~/` 不参与编译，即使写成真代码也不会在本工程触发编译，但保持注释形式是为了明确"这是接入示例，不是默认行为"）。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Samples~/Template/TemplateGameFlow.cs
git commit -m "docs(content-update): add ContentUpdate subscription example to Template sample"
```

---

### Task 7: 全量回归验证

**Files:**
- 无新增文件;仅运行既有 + 新增全部 EditMode 测试确认无回归。

- [ ] **Step 1: Write the failing test**

不适用——本 Task 是纯验证性质，不新增测试用例。

- [ ] **Step 2: Run test to verify it fails**

不适用。

- [ ] **Step 3: Write minimal implementation**

不适用。

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行全部测试（不加过滤，或用 `-runTests -testPlatform EditMode -projectPath <项目路径> -testResults <输出路径>.xml` 命令行方式）。
预期:全部 PASS，测试总数从基线 137 增至 137 + 1(`ContentUpdateInfo_FieldsAreReadable`) + 6(`ContentUpdateService` 行为用例) + 1(`AddressablesCatalogGatewayTests`) + 3(`ContentUpdateBootTaskTests`) + 2(`FrameworkInstallerTests` 新增用例) = **150**，且既有 `FrameworkInstallerTests.GFacade_ResetClearsBindings` 用例（已在 Task 5 追加断言）依旧 PASS，无回归。

- [ ] **Step 5: Commit**

本 Task 无代码变更，不产生 commit。若发现回归，回到对应 Task 修复后重新验证。

---

## Self-Review

**1. Spec 覆盖度检查**（逐条对照 `docs/superpowers/specs/2026-07-05-content-update-service-design.md`）：

- §2.1 服务接口 `ContentUpdateInfo`/`ContentAvailableEvent`/`ContentUpdateAppliedEvent`/`ContentUpdateFailedEvent`/`IContentUpdateService`（`HasChecked`/`CheckAsync`/`DownloadAndApplyAsync`）—— Task 1 全部定义，字段/方法签名与 spec 一致。
- §2.2 `IAddressablesCatalogGateway` + 真实实现 `AddressablesCatalogGateway` + `FakeAddressablesCatalogGateway` —— Task 1(接口)、Task 3(真实实现)、Task 2/3/4(Fake 内嵌在测试文件)全部覆盖。
- §2.3 `ContentUpdateBootTask`：`IsCritical=false`、`Priority` 排在 Asset/Config 之后、启动时静默 `CheckAsync()`、不自动下载 —— Task 4 覆盖，`Priority=20` 晚于 `ConfigBootTask`(10)，早于 `AdsBootTask`(30)；`InitializeAsync` 只调 `CheckAsync()`，未调用 `DownloadAndApplyAsync`，符合"不自动下载"。
- §2.4 挂载点 `G.ContentUpdate` —— Task 5 覆盖（`G.cs` 属性 + Initialize + Reset）。
- §3 边界：只做目录/资源层面更新（未涉及代码热更，全计划无相关代码）；只提供三动作三事件（未新增"大版本/小版本"概念）；不做下载进度 UI（`IProgress<float>` 只是透传参数，无 UI 代码）—— 均满足，无越界实现。
- §4 验收标准：
  - "有更新可下载 → CheckAsync 返回 IsAvailable=true 且发布 ContentAvailableEvent" —— Task 2 `CheckAsync_UpdateAvailable_ReturnsAvailableAndPublishesEvent` 覆盖。
  - "下载成功 → 发布 ContentUpdateAppliedEvent" —— Task 2 `DownloadAndApplyAsync_Success_PublishesAppliedEventAndReturnsTrue` 覆盖。
  - "下载失败 → 发布 ContentUpdateFailedEvent，DownloadAndApplyAsync 返回 false 且不抛异常" —— Task 2 `DownloadAndApplyAsync_Failure_PublishesFailedEventReturnsFalseNoThrow` 用 `Assert.DoesNotThrow` 显式覆盖。
  - "Template 或 TapRush Sample 补一个最小接入示例" —— Task 6 覆盖（选择 Template，注释形式给出订阅示例，说明是手动验证项）。
- §5 YAGNI 裁剪：未实现版本号比对系统、未实现重试队列、未实现大小版本分级 —— 全计划确认无相关代码，符合裁剪范围。

未发现遗漏的 spec 需求。

**2. 占位符扫描**：全文搜索 "TBD"、"TODO"、"后补"、"适当的错误处理"、"参照 Task"、无代码块的"写测试覆盖"等模式 —— 均未出现。Task 6 的 Step 1/2 因客观原因（Samples~ 不参与编译，spec 明确排除自动化测试）标注"不适用"并给出替代的人工检查动作，不是占位符规避，是如实反映约束。所有 Step 3 实现代码均为完整代码，无省略。

**3. 前后 Task 类型/方法名一致性检查**：

- `ContentUpdateInfo`：Task 1 定义 `(bool IsAvailable, long EstimatedDownloadSizeBytes)` + 构造函数 `ContentUpdateInfo(bool, long)`；Task 2 测试中 `new ContentUpdateInfo(true, 1024L)` 与 `info.IsAvailable`/`info.EstimatedDownloadSizeBytes` 读取，签名一致。
- `IContentUpdateService.CheckAsync()` / `DownloadAndApplyAsync(IProgress<float> progress = null)`：Task 1 声明，Task 2 `ContentUpdateService` 实现、Task 4 `ContentUpdateBootTask.InitializeAsync` 调用 `_contentUpdate.CheckAsync()`，方法名与参数一致，无 `CheckForUpdateAsync` 之类的漂移。
- `IAddressablesCatalogGateway.CheckForCatalogUpdatesAsync()` / `UpdateCatalogsAsync(List<string>, IProgress<float>)`：Task 1 声明，Task 2 `FakeAddressablesCatalogGateway` 实现、Task 3 `AddressablesCatalogGateway` 实现，Task 2 `ContentUpdateService` 内部调用，四处签名一致。
- `ContentUpdateService` 构造函数 `(IAddressablesCatalogGateway gateway, IEventBus events)`：Task 2 定义，Task 4 `ContentUpdateBootTask` 构造 `(ContentUpdateService contentUpdate)` 持有的是已构造实例（不重复构造），Task 5 `FrameworkInstaller` 里 `builder.Register<ContentUpdateService>(Lifetime.Singleton).As<IContentUpdateService>().AsSelf()` 让 VContainer 自动按最长构造解析 `IAddressablesCatalogGateway`/`IEventBus`（两者均已注册，无 `Ads`/`Scene` 那种未注册 `Func<float>` 参数的坑，故不需要工厂 lambda），一致。
- `ContentUpdateBootTask(ContentUpdateService contentUpdate)`：Task 4 定义并在 `FrameworkInstaller`（Task 5）里 `builder.Register<ContentUpdateBootTask>(Lifetime.Singleton).As<IBootTask>()` 注册，构造参数类型 `ContentUpdateService`（具体类而非接口）与 `ConfigBootTask(ConfigService config)` 的既有写法一致（`ConfigBootTask` 依赖具体类 `ConfigService` 而非 `IConfigService`，因为它要调用 `RefreshRemoteAsync` 这类不在接口上的方法；这里 `ContentUpdateBootTask` 同理依赖具体类，虽然本计划中 `CheckAsync` 恰好也在接口上，但保持与 `ConfigBootTask` 一致的具体类依赖写法，避免后续给 `ContentUpdateService` 加内部方法时又要改签名）。
- `Priority => 20`：Task 4 定义，Task 5 的接线不涉及 Priority 断言冲突；Task 4 注释与 Task 5 实际注册顺序（Save=0 < Config=10 < ContentUpdate=20 < Ads=30 < IAP=31）一致。
- `G.ContentUpdate`：Task 5 在 `G.cs` 三处（属性声明、Initialize、Reset）同步添加，类型 `IContentUpdateService`，与 `FrameworkInstallerTests` 新增断言 `Assert.AreSame(c.Resolve<IContentUpdateService>(), EasyFramework.G.ContentUpdate)` 一致。

未发现命名/签名漂移问题，无需修改。
