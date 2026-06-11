# EasyFramework Phase 2(资源与数据层)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase 1 骨架之上实现资源与数据层:Addressables 封装的 Asset 服务、Scene 切换服务、JSON 存档服务(HMAC + 原子写 + 版本迁移)、Config 服务(本地默认 + 远程覆盖)、GameObject 池服务,并接线进 `FrameworkInstaller` / `G` 门面 / `RootLifetimeScope`。全部带 EditMode 单测。

**Architecture:** 沿用 Phase 1 的「VContainer DI 内核 + 静态门面 `G`」。所有新服务落在 `EasyFramework.Services` 程序集,命名空间 `EasyFramework.Services.*`,目录与命名空间一一对应。服务一律先接口后实现,Unity 静态依赖(`Application.persistentDataPath`、`SceneManager`)抽到边界接口/构造参数,核心逻辑在纯 `ContainerBuilder`(无 MonoBehaviour)下可 Build、可单测。`FrameworkInstaller.Install` 签名升级为 `Install(IContainerBuilder, FrameworkOptions)`,把 Unity 静态值从 `RootLifetimeScope` 注入,测试传 Fake/临时目录。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / Newtonsoft Json / Addressables / Unity Test Framework 1.6

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包 `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`)。
- **并行实现代理只允许用 Write/Edit 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(Fake 服务的所有 `UniTask` 均同步返回),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。

**⚠️ Task 7 前置要求(必读):** Task 7 要修改 Phase 1 落地的三个文件 `Boot/G.cs`、`Boot/FrameworkInstaller.cs`、`Boot/RootLifetimeScope.cs`,以及 `Boot/EasyFramework.Boot.asmdef`(新增 Services 已有引用,Boot 已引用 Services 故无需改 asmdef references,但需确认)。**Phase 1 正由另一组代理实现中。执行 Task 7 前,实现代理必须先 `Read` 这三个文件的实际落地内容核对**(签名、命名空间、`G.Initialize`/`Reset` 的具体写法、`RootLifetimeScope.Configure` 的 build callback 写法)。**若文件尚不存在,以 Phase 1 计划文档中的代码为准。**本计划给出的是「修改后的完整文件内容」,但若实际文件与 Phase 1 计划有偏差(例如 VContainer API 调整),以实际文件为基线做等效合并,不要机械覆盖。

---

## 文件结构总览

```
Packages/manifest.json                                  (修改:新增 com.unity.addressables)
Assets/EasyFramework/
├── Services/
│   ├── EasyFramework.Services.asmdef                   (修改:新增 Addressables/ResourceManager/Newtonsoft 引用)
│   ├── AssemblyInfo.cs                                 (新增/替换占位:InternalsVisibleTo 测试程序集)
│   ├── Assets/
│   │   ├── IAssetService.cs                            (IAssetService + AssetScope)
│   │   ├── AddressablesAssetService.cs
│   │   └── FakeAssetService.cs
│   ├── Scenes/
│   │   ├── ISceneService.cs                            (ISceneService + 事件 + ISceneTransition + NoopSceneTransition)
│   │   ├── ISceneLoader.cs                             (internal 边界接口)
│   │   ├── UnitySceneLoader.cs                         (internal,薄实现,不单测)
│   │   └── SceneService.cs
│   ├── Saves/
│   │   ├── ISaveService.cs                             (ISaveService + SaveData + ISaveMigration + SaveProfile)
│   │   ├── JsonSaveService.cs
│   │   ├── SaveBootTask.cs
│   │   ├── SaveOnPauseListener.cs                      (MonoBehaviour)
│   │   └── DefaultSaveData.cs
│   ├── Configs/
│   │   ├── IConfigService.cs                           (IConfigService + IRemoteConfigProvider + NoopRemoteConfigProvider)
│   │   ├── ConfigTable.cs                              (ScriptableObject)
│   │   ├── ConfigService.cs
│   │   └── ConfigBootTask.cs
│   └── Pooling/
│       ├── IPoolService.cs
│       ├── PooledMarker.cs                             (internal MonoBehaviour)
│       └── PoolService.cs
├── Boot/
│   ├── G.cs                                            (修改:新增 5 个属性)
│   ├── FrameworkInstaller.cs                           (修改:Install(builder, options) + 注册新服务 + FrameworkOptions)
│   └── RootLifetimeScope.cs                            (修改:构造 FrameworkOptions、挂 SaveOnPauseListener、注册 Pools 根)
└── Tests/EditMode/
    ├── EasyFramework.Tests.EditMode.asmdef             (修改:新增 EasyFramework.Services 等引用)
    ├── AssetServiceTests.cs
    ├── SceneServiceTests.cs
    ├── SaveServiceTests.cs
    ├── ConfigServiceTests.cs
    ├── PoolServiceTests.cs
    └── FrameworkInstallerTests.cs                      (修改:扩展断言新服务可解析、G 绑定)
```

依赖方向不变:`Tests → Boot → {Core, Services, Monetization}`;`Services → Core`。Phase 2 只在 Services 内部新增目录,并修改 Boot 的三个文件。

---

### Task 1: 安装 Addressables 包(串行,使用 UnityMCP)

**Files:** Modify: `Packages/manifest.json`(由 Unity Package Manager 写入)

- [ ] **Step 1: 查最新稳定版并安装**

用 `manage_packages` 列出/查询 `com.unity.addressables` 在当前 Unity 6000.3.15f1 下的最新稳定版(`add_package` 不带版本号通常解析为 manifest 兼容的推荐版;若工具支持 `search`/`list_versions`,先查到具体 `x.y.z` 再装)。Addressables 会自动带入依赖 `com.unity.resourcemanager` 与 `com.unity.scriptablebuildpipeline`,无需手动加。

```
com.unity.addressables   (最新稳定版,由 manage_packages 解析)
```

- [ ] **Step 2: 验证编译干净**

轮询 `mcpforunity://editor/state` 至 `is_compiling == false`,然后 `read_console(types=["error"])`。Expected: 0 errors。Addressables 首次导入会生成 `Assets/AddressableAssetsData/`(settings 资产),属正常产物。

- [ ] **Step 3: Commit**(编排者执行)

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/AddressableAssetsData
git commit -m "feat: add Unity Addressables package"
```

---

### Task 2: asmdef / AssemblyInfo 升级 + Asset 服务(串行写文件,可与 Task 3-6 并行实现)

> Asset 服务是 Scene/Pool 的依赖底座,asmdef 升级也是后续全部 Task 的前置。先落这一步。

**Files:**
- Modify: `Assets/EasyFramework/Services/EasyFramework.Services.asmdef`
- Create/Replace: `Assets/EasyFramework/Services/AssemblyInfo.cs`
- Modify: `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`
- Create: `Assets/EasyFramework/Services/Assets/IAssetService.cs`, `AddressablesAssetService.cs`, `FakeAssetService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/AssetServiceTests.cs`

- [ ] **Step 1: 升级 Services asmdef** — `Assets/EasyFramework/Services/EasyFramework.Services.asmdef`

Phase 1 中此 asmdef 为占位(references 仅 Core/UniTask/VContainer/MessagePipe)。完整替换为:

```json
{
  "name": "EasyFramework.Services",
  "rootNamespace": "EasyFramework.Services",
  "references": [
    "EasyFramework.Core",
    "UniTask",
    "VContainer",
    "MessagePipe",
    "Unity.Addressables",
    "Unity.ResourceManager"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["Newtonsoft.Json.dll"],
  "autoReferenced": true,
  "defineConstraints": [],
  "noEngineReferences": false
}
```

说明:`Newtonsoft.Json.dll` 来自 `com.unity.nuget.newtonsoft-json`(Phase 1 已装),通过 `precompiledReferences` + `overrideReferences:true` 引入(Save 服务用 `Newtonsoft.Json` 与 `Newtonsoft.Json.Linq`)。`Unity.Addressables` 提供 `UnityEngine.AddressableAssets`,`Unity.ResourceManager` 提供 `UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`。

- [ ] **Step 2: Services AssemblyInfo** — `Assets/EasyFramework/Services/AssemblyInfo.cs`

替换 Phase 1 的占位注释为:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EasyFramework.Tests.EditMode")]
```

- [ ] **Step 3: 升级 Tests asmdef** — `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

在 Phase 1 基础上新增 `EasyFramework.Services` 与 Addressables 引用(测试要 new `FakeAssetService`、引用 `EasyFramework.Services.*` 类型;`Newtonsoft.Json` 经 Services 间接可用,Save 测试直接构造 `JObject` 也需引用,加 precompiled):

```json
{
  "name": "EasyFramework.Tests.EditMode",
  "rootNamespace": "EasyFramework.Tests",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "EasyFramework.Boot",
    "UniTask",
    "VContainer",
    "MessagePipe",
    "Unity.Addressables",
    "Unity.ResourceManager",
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

- [ ] **Step 4: 写失败测试** — `AssetServiceTests.cs`

仅测 `FakeAssetService`(Addressables 真实加载的冒烟测试推迟到 Phase 5 示例游戏,见 Self-Review,不算占位符)。验证:加载命中字典返回对象;同 key 重复加载返回同实例;`ReleaseScope` 是接口语义占位(Fake 下 release 不抛错且后续仍可加载);未命中 key 抛 `KeyNotFoundException`。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class AssetServiceTests
    {
        static FakeAssetService MakeService(out Material mat, string key = "mat")
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            var dict = new Dictionary<string, Object> { { key, mat } };
            return new FakeAssetService(dict);
        }

        [Test]
        public void LoadAsync_ReturnsMappedAsset()
        {
            var svc = MakeService(out var mat);
            var loaded = svc.LoadAsync<Material>("mat").GetAwaiter().GetResult();
            Assert.AreSame(mat, loaded);
        }

        [Test]
        public void LoadAsync_SameKey_ReturnsSameInstance()
        {
            var svc = MakeService(out var mat);
            var a = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            var b = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.AreSame(a, b);
            Assert.AreSame(mat, a);
        }

        [Test]
        public void LoadAsync_MissingKey_Throws()
        {
            var svc = new FakeAssetService(new Dictionary<string, Object>());
            Assert.Throws<KeyNotFoundException>(() =>
                svc.LoadAsync<Material>("nope").GetAwaiter().GetResult());
        }

        [Test]
        public void ReleaseScope_AfterRelease_CanLoadAgain()
        {
            var svc = MakeService(out var mat);
            svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Scene));
            var again = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.AreSame(mat, again);
        }

        [Test]
        public void ReleaseScope_OnlyAffectsTargetScope()
        {
            var svc = MakeService(out var mat);
            svc.LoadAsync<Material>("mat", AssetScope.Global).GetAwaiter().GetResult();
            // 释放 Scene 不应影响 Global 记录的 release 计数(Fake 仅校验不抛错)
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Scene));
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Global));
        }
    }
}
```

- [ ] **Step 5: 实现接口** — `IAssetService.cs`

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Assets
{
    public enum AssetScope { Global, Scene }

    public interface IAssetService
    {
        UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene) where T : UnityEngine.Object;
        void ReleaseScope(AssetScope scope);
    }
}
```

- [ ] **Step 6: 实现 AddressablesAssetService** — `AddressablesAssetService.cs`

按 scope 分组记录句柄;同 key 复用句柄并计数;收到 `SceneWillUnloadEvent` 自动 `ReleaseScope(Scene)`。构造注入 `IEventBus`,订阅句柄随服务 `IDisposable` 释放。

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace EasyFramework.Services.Assets
{
    public sealed class AddressablesAssetService : IAssetService, IDisposable
    {
        sealed class Entry
        {
            public AsyncOperationHandle Handle;
            public int RefCount;
        }

        readonly Dictionary<AssetScope, Dictionary<string, Entry>> _byScope = new()
        {
            { AssetScope.Global, new Dictionary<string, Entry>() },
            { AssetScope.Scene, new Dictionary<string, Entry>() },
        };
        readonly IDisposable _sceneUnloadSub;

        public AddressablesAssetService(IEventBus events)
        {
            _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ReleaseScope(AssetScope.Scene));
        }

        public async UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene)
            where T : UnityEngine.Object
        {
            var group = _byScope[scope];
            if (group.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                await existing.Handle.ToUniTask();
                return (T)existing.Handle.Result;
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            var entry = new Entry { Handle = handle, RefCount = 1 };
            group[key] = entry;
            var result = await handle.ToUniTask();
            return result;
        }

        public void ReleaseScope(AssetScope scope)
        {
            var group = _byScope[scope];
            foreach (var entry in group.Values)
            {
                if (entry.Handle.IsValid())
                    Addressables.Release(entry.Handle);
            }
            group.Clear();
        }

        public void Dispose()
        {
            _sceneUnloadSub?.Dispose();
            ReleaseScope(AssetScope.Scene);
            ReleaseScope(AssetScope.Global);
        }
    }
}
```

> 说明:`AsyncOperationHandle.ToUniTask()` 由 UniTask 的 Addressables 集成提供(UniTask 检测到 Addressables 程序集时自动启用 `Cysharp.Threading.Tasks` 扩展)。若该扩展未自动启用,实现代理用 `await handle.Task`(`System.Threading.Tasks.Task`)等效替换;接口签名与行为不变。

- [ ] **Step 7: 实现 FakeAssetService** — `FakeAssetService.cs`

从注入字典取对象;按 scope 记录加载/释放计数,行为同接口语义(供 EditMode 测 scope 释放可测部分,也供 Pool 测试做 prefab 替身)。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Assets
{
    public sealed class FakeAssetService : IAssetService
    {
        readonly IReadOnlyDictionary<string, UnityEngine.Object> _assets;
        readonly Dictionary<AssetScope, Dictionary<string, int>> _refs = new()
        {
            { AssetScope.Global, new Dictionary<string, int>() },
            { AssetScope.Scene, new Dictionary<string, int>() },
        };

        /// <summary>测试可读:某 scope 当前活跃的 key 数(ReleaseScope 后归零)。</summary>
        public int ActiveCount(AssetScope scope) => _refs[scope].Count;

        public FakeAssetService(IReadOnlyDictionary<string, UnityEngine.Object> assets)
        {
            _assets = assets;
        }

        public UniTask<T> LoadAsync<T>(string key, AssetScope scope = AssetScope.Scene)
            where T : UnityEngine.Object
        {
            if (!_assets.TryGetValue(key, out var obj))
                throw new KeyNotFoundException($"FakeAssetService has no asset for key '{key}'.");

            var group = _refs[scope];
            group[key] = group.TryGetValue(key, out var c) ? c + 1 : 1;
            return UniTask.FromResult((T)obj);
        }

        public void ReleaseScope(AssetScope scope) => _refs[scope].Clear();
    }
}
```

> 依赖说明:`AddressablesAssetService` 引用了 `EasyFramework.Services.Scenes.SceneWillUnloadEvent`(Task 3 定义)。Task 2 与 Task 3 若并行实现,Task 3 必须先把 `ISceneService.cs`(含事件结构体)落地,Asset 才能编译。建议串行:先 Task 3 的事件文件,或将 Task 3 Step 5(事件+接口定义)提前。本计划在验证阶段统一编译,不影响最终结果。

- [ ] **Step 8: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.AssetServiceTests` 过滤)Expected: 全 PASS。
- [ ] **Step 9: Commit**(编排者)`git commit -m "feat(services): add asset service over Addressables with scope release"`

---

### Task 3: Scene 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Scenes/ISceneService.cs`, `ISceneLoader.cs`, `UnitySceneLoader.cs`, `SceneService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/SceneServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `SceneServiceTests.cs`

用 `FakeSceneLoader`(实现 internal `ISceneLoader`,InternalsVisibleTo 已对测试程序集开放)与记录型 `ISceneTransition`、Fake `IEventBus` 验证:过渡/事件顺序为 `PlayOut → SceneWillUnloadEvent → (loader 加载,报进度) → SceneLoadedEvent → PlayIn`;进度转发;`CurrentScene` 更新。

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class SceneServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        sealed class RecordingTransition : ISceneTransition
        {
            readonly List<string> _log;
            public RecordingTransition(List<string> log) => _log = log;
            public UniTask PlayOut() { _log.Add("PlayOut"); return UniTask.CompletedTask; }
            public UniTask PlayIn() { _log.Add("PlayIn"); return UniTask.CompletedTask; }
        }

        sealed class FakeSceneLoader : ISceneLoader
        {
            readonly List<string> _log;
            readonly float[] _progressSteps;
            public FakeSceneLoader(List<string> log, float[] progressSteps)
            { _log = log; _progressSteps = progressSteps; }

            public UniTask LoadAsync(string sceneName, IProgress<float> progress)
            {
                _log.Add($"Load:{sceneName}");
                if (progress != null)
                    foreach (var p in _progressSteps) progress.Report(p);
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void LoadAsync_RunsTransitionsAndEventsInOrder()
        {
            var log = new List<string>();
            var bus = new FakeBus();
            var svc = new SceneService(new FakeSceneLoader(log, new[] { 1f }),
                new RecordingTransition(log), bus, "Boot");

            // 事件发布也记录到 log 以校验交错顺序
            // 通过 bus.Published 的顺序与 log 顺序联合断言
            svc.LoadAsync("Level1").GetAwaiter().GetResult();

            CollectionAssert.AreEqual(
                new[] { "PlayOut", "Load:Level1", "PlayIn" }, log);
            Assert.IsInstanceOf<SceneWillUnloadEvent>(bus.Published[0]);
            Assert.IsInstanceOf<SceneLoadedEvent>(bus.Published[1]);
            Assert.AreEqual("Boot", ((SceneWillUnloadEvent)bus.Published[0]).SceneName);
            Assert.AreEqual("Level1", ((SceneLoadedEvent)bus.Published[1]).SceneName);
            Assert.AreEqual("Level1", svc.CurrentScene);
        }

        [Test]
        public void LoadAsync_ForwardsProgress()
        {
            var reported = new List<float>();
            var svc = new SceneService(
                new FakeSceneLoader(new List<string>(), new[] { 0.25f, 0.5f, 1f }),
                new NoopSceneTransition(), new FakeBus(), "Boot");

            svc.LoadAsync("Level1", new ProgressCollector(reported)).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f, 1f }, reported);
        }

        [Test]
        public void NoopTransition_CompletesImmediately()
        {
            var t = new NoopSceneTransition();
            Assert.IsTrue(t.PlayOut().Status.IsCompleted());
            Assert.IsTrue(t.PlayIn().Status.IsCompleted());
        }

        sealed class ProgressCollector : IProgress<float>
        {
            readonly List<float> _sink;
            public ProgressCollector(List<float> sink) => _sink = sink;
            public void Report(float value) => _sink.Add(value);
        }
    }
}
```

- [ ] **Step 2: 实现接口 + 事件 + 过渡** — `ISceneService.cs`

```csharp
using System;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Scenes
{
    public readonly struct SceneWillUnloadEvent
    {
        public readonly string SceneName;
        public SceneWillUnloadEvent(string n) => SceneName = n;
    }

    public readonly struct SceneLoadedEvent
    {
        public readonly string SceneName;
        public SceneLoadedEvent(string n) => SceneName = n;
    }

    /// <summary>转场视觉接口;Phase 3 提供淡入淡出实现,Phase 2 用 Noop。</summary>
    public interface ISceneTransition
    {
        UniTask PlayOut();
        UniTask PlayIn();
    }

    public sealed class NoopSceneTransition : ISceneTransition
    {
        public UniTask PlayOut() => UniTask.CompletedTask;
        public UniTask PlayIn() => UniTask.CompletedTask;
    }

    public interface ISceneService
    {
        string CurrentScene { get; }
        UniTask LoadAsync(string sceneName, IProgress<float> progress = null);
    }
}
```

- [ ] **Step 3: 实现边界接口** — `ISceneLoader.cs`(internal,供 Fake 替换)

```csharp
using System;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Scenes
{
    /// <summary>对 Unity SceneManager 的薄抽象,便于单测替换。</summary>
    internal interface ISceneLoader
    {
        UniTask LoadAsync(string sceneName, IProgress<float> progress);
    }
}
```

- [ ] **Step 4: 实现真实 loader** — `UnitySceneLoader.cs`(internal,薄到不单测)

```csharp
using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace EasyFramework.Services.Scenes
{
    internal sealed class UnitySceneLoader : ISceneLoader
    {
        public async UniTask LoadAsync(string sceneName, IProgress<float> progress)
        {
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = true;
            while (!op.isDone)
            {
                // LoadSceneAsync 进度上限 0.9,归一化到 0..1
                progress?.Report(op.progress >= 0.9f ? 1f : op.progress / 0.9f);
                await UniTask.Yield();
            }
            progress?.Report(1f);
        }
    }
}
```

- [ ] **Step 5: 实现 SceneService** — `SceneService.cs`

构造注入 `ISceneLoader`(测试传 Fake,生产传 `UnitySceneLoader`)、`ISceneTransition`、`IEventBus`、初始场景名。

```csharp
using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.Scenes
{
    public sealed class SceneService : ISceneService
    {
        readonly ISceneLoader _loader;
        readonly ISceneTransition _transition;
        readonly IEventBus _events;

        public string CurrentScene { get; private set; }

        internal SceneService(ISceneLoader loader, ISceneTransition transition,
            IEventBus events, string initialScene)
        {
            _loader = loader;
            _transition = transition;
            _events = events;
            CurrentScene = initialScene;
        }

        public async UniTask LoadAsync(string sceneName, IProgress<float> progress = null)
        {
            await _transition.PlayOut();
            _events.Publish(new SceneWillUnloadEvent(CurrentScene));
            await _loader.LoadAsync(sceneName, progress);
            CurrentScene = sceneName;
            _events.Publish(new SceneLoadedEvent(sceneName));
            await _transition.PlayIn();
        }
    }
}
```

> 构造函数为 `internal`(签名含 internal 类型 `ISceneLoader`)。`FrameworkInstaller`(`EasyFramework.Boot` 程序集)需要注册 `SceneService`——Boot 不在 Services 的 InternalsVisibleTo 列表里。**解决方案:** 在 `Services/AssemblyInfo.cs` 追加一行 `[assembly: InternalsVisibleTo("EasyFramework.Boot")]`(见 Task 7 Step 1,合并到该文件)。这样 Installer 可直接 `new SceneService(new UnitySceneLoader(), transition, events, initialScene)` 并以 `ISceneService` 注册。

- [ ] **Step 6: 验证测试通过**(验证代理)
- [ ] **Step 7: Commit**(编排者)`git commit -m "feat(services): add scene service with transition and progress"`

---

### Task 4: Save 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Saves/ISaveService.cs`, `JsonSaveService.cs`, `SaveBootTask.cs`, `SaveOnPauseListener.cs`, `DefaultSaveData.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/SaveServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `SaveServiceTests.cs`

用临时目录(`Path.Combine(Path.GetTempPath(), Guid)`),`[SetUp]`/`[TearDown]` 建删。覆盖:新档创建、保存后重载一致、HMAC 篡改检测(改文件 payload 后重载触发损坏处理 → `.corrupt` 备份 + 重建新档)、两级迁移链(v1→v2→v3)、原子写(目标文件已存在时 `Save` 成功覆盖)。

```csharp
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class SaveServiceTests
    {
        string _dir;

        sealed class TestSave : SaveData
        {
            public int Coins;
            public string PlayerName = "";
        }

        // v1: 无 Coins;v2: 加 Coins=0;v3: 加 PlayerName="anon"
        sealed class MigrateV1ToV2 : ISaveMigration
        {
            public int FromVersion => 1;
            public void Migrate(JObject raw) { raw["Coins"] = 0; raw["Version"] = 2; }
        }
        sealed class MigrateV2ToV3 : ISaveMigration
        {
            public int FromVersion => 2;
            public void Migrate(JObject raw) { raw["PlayerName"] = "anon"; raw["Version"] = 3; }
        }

        SaveProfile MakeProfile(int currentVersion = 3, IReadOnlyList<ISaveMigration> migrations = null)
            => new SaveProfile
            {
                DataType = typeof(TestSave),
                CurrentVersion = currentVersion,
                CreateNew = () => new TestSave { Version = currentVersion, Coins = 0, PlayerName = "" },
                Migrations = migrations ?? new ISaveMigration[] { new MigrateV1ToV2(), new MigrateV2ToV3() },
                FileName = "save.json",
                HmacSalt = "test-salt",
            };

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ef_save_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void LoadAsync_NoFile_CreatesNew()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, svc.Data<TestSave>().Version);
            Assert.AreEqual(0, svc.Data<TestSave>().Coins);
        }

        [Test]
        public void SaveThenReload_RoundTrips()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 42;
            svc.Data<TestSave>().PlayerName = "hero";
            svc.Save();

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(42, svc2.Data<TestSave>().Coins);
            Assert.AreEqual("hero", svc2.Data<TestSave>().PlayerName);
        }

        [Test]
        public void TamperedFile_DetectedAndRebuilt()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 99;
            svc.Save();

            // 篡改 payload(不更新 HMAC)
            var path = Path.Combine(_dir, "save.json");
            var json = File.ReadAllText(path);
            File.WriteAllText(path, json.Replace("99", "999999"));

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            // 损坏 → 备份 + 重建新档
            Assert.IsTrue(File.Exists(path + ".corrupt"), "应备份损坏文件");
            Assert.AreEqual(0, svc2.Data<TestSave>().Coins, "应回退为新档");
        }

        [Test]
        public void MigrationChain_V1ToV3()
        {
            // 手写一个 v1 档(只有 Version=1),用框架写盘格式(payload + hmac)
            var profile = MakeProfile();
            var raw = new JObject { ["Version"] = 1 };
            var svcForWrite = new JsonSaveService(profile, _dir);
            // 暴露的 internal 测试钩子:把任意 JObject 以合法签名写入存档文件
            svcForWrite.WriteRawForTest(raw);

            var svc = new JsonSaveService(profile, _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            var data = svc.Data<TestSave>();
            Assert.AreEqual(3, data.Version);
            Assert.AreEqual(0, data.Coins);       // v1→v2 注入
            Assert.AreEqual("anon", data.PlayerName); // v2→v3 注入
        }

        [Test]
        public void Save_OverwritesExistingFileAtomically()
        {
            var svc = new JsonSaveService(MakeProfile(), _dir);
            svc.LoadAsync().GetAwaiter().GetResult();
            svc.Data<TestSave>().Coins = 1;
            svc.Save();
            svc.Data<TestSave>().Coins = 2;
            Assert.DoesNotThrow(() => svc.Save()); // File.Replace 目标已存在仍成功

            var svc2 = new JsonSaveService(MakeProfile(), _dir);
            svc2.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, svc2.Data<TestSave>().Coins);
        }
    }
}
```

- [ ] **Step 2: 实现接口与契约** — `ISaveService.cs`

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Saves
{
    public abstract class SaveData { public int Version; }

    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Migrate(Newtonsoft.Json.Linq.JObject raw);
    }

    public sealed class SaveProfile
    {
        public Type DataType;
        public int CurrentVersion;
        public Func<SaveData> CreateNew;
        public IReadOnlyList<ISaveMigration> Migrations;
        public string FileName = "save.json";
        public string HmacSalt = "easyframework";
    }

    public interface ISaveService
    {
        UniTask LoadAsync();
        void Save();
        T Data<T>() where T : SaveData;
    }
}
```

- [ ] **Step 3: 实现 DefaultSaveData** — `DefaultSaveData.cs`(框架默认占位,游戏可覆盖)

```csharp
namespace EasyFramework.Services.Saves
{
    /// <summary>框架默认存档载荷。游戏在 GameLifetimeScope 覆盖注册自定义 SaveProfile 即可替换。</summary>
    public sealed class DefaultSaveData : SaveData
    {
    }
}
```

- [ ] **Step 4: 实现 JsonSaveService** — `JsonSaveService.cs`

文件格式:`{ "payload": <serialized data json string>, "hmac": <hex> }`,HMAC = HMACSHA256(key=salt, message=payload)。读取流程:无文件→CreateNew;读到→验 HMAC,失败→备份 `.corrupt` + CreateNew;成功→解析 payload 为 JObject,`Version < CurrentVersion` 时按迁移链升级,再反序列化为 `DataType`。写入:序列化→算 HMAC→组装外层 json→写临时文件→`File.Replace`(目标不存在则 `File.Move`)。

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EasyFramework.Services.Saves
{
    public sealed class JsonSaveService : ISaveService
    {
        readonly SaveProfile _profile;
        readonly string _dir;
        readonly string _path;
        SaveData _data;

        public JsonSaveService(SaveProfile profile, string saveDirectory)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _dir = saveDirectory ?? throw new ArgumentNullException(nameof(saveDirectory));
            _path = Path.Combine(_dir, _profile.FileName);
        }

        public UniTask LoadAsync()
        {
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);

            if (!File.Exists(_path))
            {
                _data = _profile.CreateNew();
                return UniTask.CompletedTask;
            }

            string payload;
            try
            {
                var envelope = JObject.Parse(File.ReadAllText(_path));
                payload = (string)envelope["payload"];
                var storedHmac = (string)envelope["hmac"];
                if (payload == null || storedHmac == null || !ConstantTimeEquals(storedHmac, ComputeHmac(payload)))
                    throw new InvalidDataException("HMAC mismatch.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Save corrupt ({e.Message}); backing up and recreating.");
                BackupCorrupt();
                _data = _profile.CreateNew();
                return UniTask.CompletedTask;
            }

            var raw = JObject.Parse(payload);
            ApplyMigrations(raw);
            _data = (SaveData)raw.ToObject(_profile.DataType);
            return UniTask.CompletedTask;
        }

        void ApplyMigrations(JObject raw)
        {
            var version = raw["Version"]?.Value<int>() ?? 1;
            if (_profile.Migrations == null) return;
            while (version < _profile.CurrentVersion)
            {
                var found = false;
                foreach (var m in _profile.Migrations)
                {
                    if (m.FromVersion == version)
                    {
                        m.Migrate(raw);
                        version = raw["Version"]?.Value<int>() ?? version + 1;
                        found = true;
                        break;
                    }
                }
                if (!found) break; // 无对应迁移,停止避免死循环
            }
        }

        public void Save()
        {
            if (_data == null) throw new InvalidOperationException("Call LoadAsync before Save.");
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);

            _data.Version = _profile.CurrentVersion;
            var payload = JsonConvert.SerializeObject(_data, Formatting.None);
            var envelope = new JObject
            {
                ["payload"] = payload,
                ["hmac"] = ComputeHmac(payload),
            };

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, envelope.ToString(Formatting.None));
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }

        public T Data<T>() where T : SaveData => (T)_data;

        void BackupCorrupt()
        {
            try
            {
                var corrupt = _path + ".corrupt";
                if (File.Exists(corrupt)) File.Delete(corrupt);
                File.Move(_path, corrupt);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Failed to back up corrupt save: {e.Message}");
            }
        }

        string ComputeHmac(string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_profile.HmacSalt));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>测试钩子:把任意 JObject 以合法签名写入存档文件(用于构造旧版本档)。</summary>
        internal void WriteRawForTest(JObject raw)
        {
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
            var payload = raw.ToString(Formatting.None);
            var envelope = new JObject
            {
                ["payload"] = payload,
                ["hmac"] = ComputeHmac(payload),
            };
            File.WriteAllText(_path, envelope.ToString(Formatting.None));
        }
    }
}
```

- [ ] **Step 5: 实现 SaveBootTask** — `SaveBootTask.cs`

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Services.Saves
{
    public sealed class SaveBootTask : IBootTask
    {
        readonly ISaveService _save;
        public SaveBootTask(ISaveService save) => _save = save;

        public int Priority => 0;     // 存档最先
        public bool IsCritical => true;

        public async UniTask InitializeAsync(CancellationToken ct) => await _save.LoadAsync();
    }
}
```

- [ ] **Step 6: 实现 SaveOnPauseListener** — `SaveOnPauseListener.cs`

挂在 RootLifetimeScope 所在 GameObject 上(Task 7 在 `RootLifetimeScope.Configure` 里用 `gameObject.AddComponent<SaveOnPauseListener>()` 创建并注入,选定此方案,见 Task 7 说明)。

```csharp
using UnityEngine;

namespace EasyFramework.Services.Saves
{
    /// <summary>移动端切后台(OnApplicationPause(true))自动落盘。由 RootLifetimeScope 持有。</summary>
    public sealed class SaveOnPauseListener : MonoBehaviour
    {
        ISaveService _save;

        public void Bind(ISaveService save) => _save = save;

        void OnApplicationPause(bool paused)
        {
            if (paused) _save?.Save();
        }
    }
}
```

- [ ] **Step 7: 验证测试通过**(验证代理)
- [ ] **Step 8: Commit**(编排者)`git commit -m "feat(services): add json save service with hmac, atomic write, migrations"`

---

### Task 5: Config 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Configs/IConfigService.cs`, `ConfigTable.cs`, `ConfigService.cs`, `ConfigBootTask.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/ConfigServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `ConfigServiceTests.cs`

`ConfigTable` 在测试里用 `ScriptableObject.CreateInstance<ConfigTable>()` 构造,经 internal 测试钩子写条目;`IRemoteConfigProvider` 用 Fake 返回固定字典。覆盖:本地默认、远程覆盖本地、`Get<T>` int/float/bool/string、类型解析失败回退 default、`Has` 语义。

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Configs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class ConfigServiceTests
    {
        sealed class FakeRemote : IRemoteConfigProvider
        {
            readonly IReadOnlyDictionary<string, string> _data;
            public FakeRemote(IReadOnlyDictionary<string, string> data) => _data = data;
            public UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct)
                => UniTask.FromResult(_data);
        }

        static ConfigTable MakeTable(params (string, string)[] entries)
        {
            var t = ScriptableObject.CreateInstance<ConfigTable>();
            foreach (var (k, v) in entries) t.AddEntryForTest(k, v);
            return t;
        }

        static ConfigService MakeService(ConfigTable table, IReadOnlyDictionary<string, string> remote = null)
            => new ConfigService(new[] { table }, new NoopRemoteConfigProvider());

        [Test]
        public void Get_ReturnsLocalValue()
        {
            var svc = MakeService(MakeTable(("cooldown", "30"), ("enabled", "true")));
            Assert.AreEqual(30, svc.Get("cooldown", 0));
            Assert.IsTrue(svc.Get("enabled", false));
        }

        [Test]
        public void Get_ParsesAllSupportedTypes()
        {
            var svc = MakeService(MakeTable(
                ("i", "7"), ("f", "1.5"), ("b", "true"), ("s", "hello")));
            Assert.AreEqual(7, svc.Get("i", 0));
            Assert.AreEqual(1.5f, svc.Get("f", 0f));
            Assert.IsTrue(svc.Get("b", false));
            Assert.AreEqual("hello", svc.Get("s", ""));
        }

        [Test]
        public void Get_MissingKey_ReturnsDefault()
        {
            var svc = MakeService(MakeTable(("x", "1")));
            Assert.AreEqual(99, svc.Get("missing", 99));
        }

        [Test]
        public void Get_ParseFailure_ReturnsDefault()
        {
            var svc = MakeService(MakeTable(("n", "not_a_number")));
            Assert.AreEqual(42, svc.Get("n", 42)); // 解析失败回退,内部 Debug.LogWarning
        }

        [Test]
        public void Has_ReflectsPresence()
        {
            var svc = MakeService(MakeTable(("present", "1")));
            Assert.IsTrue(svc.Has("present"));
            Assert.IsFalse(svc.Has("absent"));
        }

        [Test]
        public void RemoteValue_OverridesLocal()
        {
            var table = MakeTable(("cooldown", "30"));
            var svc = new ConfigService(new[] { table },
                new FakeRemote(new Dictionary<string, string> { { "cooldown", "5" } }));
            svc.RefreshRemoteAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(5, svc.Get("cooldown", 0));
            Assert.IsTrue(svc.Has("cooldown"));
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IConfigService.cs`

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Configs
{
    public interface IRemoteConfigProvider
    {
        UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct);
    }

    public sealed class NoopRemoteConfigProvider : IRemoteConfigProvider
    {
        static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
        public UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct)
            => UniTask.FromResult(Empty);
    }

    public interface IConfigService
    {
        T Get<T>(string key, T defaultValue);
        bool Has(string key);
    }
}
```

- [ ] **Step 3: 实现 ConfigTable** — `ConfigTable.cs`(ScriptableObject)

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    [CreateAssetMenu(fileName = "ConfigTable", menuName = "EasyFramework/Config Table")]
    public sealed class ConfigTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Key;
            public string Value;
        }

        [SerializeField] List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>测试钩子:运行时向表内追加条目(编辑器/单测用)。</summary>
        internal void AddEntryForTest(string key, string value)
            => _entries.Add(new Entry { Key = key, Value = value });
    }
}
```

- [ ] **Step 4: 实现 ConfigService** — `ConfigService.cs`

构造注入 `IReadOnlyList<ConfigTable>` 与 `IRemoteConfigProvider`;远程值覆盖本地;`Get<T>` 支持 int/float/bool/string,解析失败 `Debug.LogWarning` 并回退 default。

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    public sealed class ConfigService : IConfigService
    {
        readonly Dictionary<string, string> _local = new();
        readonly Dictionary<string, string> _remote = new();
        readonly IRemoteConfigProvider _provider;

        public ConfigService(IReadOnlyList<ConfigTable> tables, IRemoteConfigProvider provider)
        {
            _provider = provider;
            if (tables != null)
                foreach (var table in tables)
                    if (table != null)
                        foreach (var e in table.Entries)
                            _local[e.Key] = e.Value;
        }

        public async UniTask RefreshRemoteAsync(CancellationToken ct)
        {
            var fetched = await _provider.FetchAsync(ct);
            if (fetched == null) return;
            foreach (var kv in fetched) _remote[kv.Key] = kv.Value;
        }

        public bool Has(string key) => _remote.ContainsKey(key) || _local.ContainsKey(key);

        public T Get<T>(string key, T defaultValue)
        {
            if (!_remote.TryGetValue(key, out var raw) && !_local.TryGetValue(key, out raw))
                return defaultValue;

            try
            {
                var t = typeof(T);
                object parsed;
                if (t == typeof(int)) parsed = int.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) parsed = float.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(bool)) parsed = bool.Parse(raw);
                else if (t == typeof(string)) parsed = raw;
                else
                {
                    Debug.LogWarning($"[EasyFramework] Config '{key}': unsupported type {t.Name}, using default.");
                    return defaultValue;
                }
                return (T)parsed;
            }
            catch (Exception)
            {
                Debug.LogWarning($"[EasyFramework] Config '{key}': failed to parse '{raw}' as {typeof(T).Name}, using default.");
                return defaultValue;
            }
        }
    }
}
```

- [ ] **Step 5: 实现 ConfigBootTask** — `ConfigBootTask.cs`

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    public sealed class ConfigBootTask : IBootTask
    {
        readonly ConfigService _config;
        public ConfigBootTask(ConfigService config) => _config = config;

        public int Priority => 10;
        public bool IsCritical => false;   // 远程拉取失败不阻塞启动

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await _config.RefreshRemoteAsync(ct);
        }
    }
}
```

> 注:`ConfigBootTask` 依赖具体类 `ConfigService`(非接口)以调用 `RefreshRemoteAsync`(该方法不在 `IConfigService` 上)。`FrameworkInstaller` 以 `ConfigService` 注册并 `.As<IConfigService>().AsSelf()`,使两者均可解析(见 Task 7)。

- [ ] **Step 6: 验证测试通过**(验证代理)
- [ ] **Step 7: Commit**(编排者)`git commit -m "feat(services): add config service with remote override"`

---

### Task 6: GameObject 池服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Pooling/IPoolService.cs`, `PooledMarker.cs`, `PoolService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/PoolServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `PoolServiceTests.cs`

用 `FakeAssetService` + `new GameObject` 的 prefab 替身。验证:复用(spawn→despawn→spawn 拿回同实例)、`IPoolable.OnSpawn/OnDespawn` 回调计数、重复回收抛 `InvalidOperationException`、场景卸载(直接 `Publish(SceneWillUnloadEvent)`)清池并 Destroy。EditMode 下 `Object.Destroy` 换 `Object.DestroyImmediate`——`PoolService` 暴露 `internal static Action<GameObject> DestroyHandler`(默认 `Object.Destroy`),测试在 `[SetUp]` 替换为 `Object.DestroyImmediate`、`[TearDown]` 还原。

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class PoolServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<Type, List<Delegate>> _handlers = new();
            public void Publish<T>(T evt)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((Action<T>)d)(evt);
            }
            public IDisposable Subscribe<T>(Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }
            sealed class Sub : IDisposable
            {
                readonly Action _dispose;
                public Sub(Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }
        }

        sealed class Resettable : MonoBehaviour, IPoolable
        {
            public int SpawnCount, DespawnCount;
            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        GameObject _prefab;
        FakeBus _bus;
        FakeAssetService _assets;
        PoolService _pool;
        Action<GameObject> _originalDestroy;

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
            _pool = new PoolService(_assets, _bus);
        }

        [TearDown]
        public void TearDown()
        {
            PoolService.DestroyHandler = _originalDestroy;
            if (_prefab != null) UnityEngine.Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Spawn_ReusesDespawnedInstance()
        {
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(a);
            var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreSame(a, b);
        }

        [Test]
        public void Spawn_Despawn_InvokePoolableCallbacks()
        {
            var go = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            var r = go.GetComponent<Resettable>();
            Assert.AreEqual(1, r.SpawnCount);
            _pool.Despawn(go);
            Assert.AreEqual(1, r.DespawnCount);
            Assert.IsFalse(go.activeSelf);
        }

        [Test]
        public void Despawn_Twice_Throws()
        {
            var go = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(go);
            Assert.Throws<InvalidOperationException>(() => _pool.Despawn(go));
        }

        [Test]
        public void Despawn_ForeignInstance_Throws()
        {
            var foreign = new GameObject("foreign");
            try { Assert.Throws<InvalidOperationException>(() => _pool.Despawn(foreign)); }
            finally { UnityEngine.Object.DestroyImmediate(foreign); }
        }

        [Test]
        public void Prewarm_FillsPool()
        {
            _pool.PrewarmAsync("bullet", 3).GetAwaiter().GetResult();
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.IsNotNull(a); // 取自预热实例,不再实例化新的(行为以复用为准)
        }

        [Test]
        public void SceneWillUnload_ClearsAllPools()
        {
            var a = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            _pool.Despawn(a);
            _bus.Publish(new SceneWillUnloadEvent("Level1"));
            // 清池后再 spawn 应得到新实例(旧的已 Destroy)
            var b = _pool.SpawnAsync("bullet").GetAwaiter().GetResult();
            Assert.AreNotSame(a, b);
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IPoolService.cs`

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    public interface IPoolService
    {
        UniTask PrewarmAsync(string key, int count);
        UniTask<GameObject> SpawnAsync(string key, Vector3 position = default, Quaternion rotation = default, Transform parent = null);
        void Despawn(GameObject instance);
    }
}
```

- [ ] **Step 3: 实现 PooledMarker** — `PooledMarker.cs`(internal)

```csharp
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    /// <summary>池内实例标记,记录所属 key,用于 Despawn 校验。</summary>
    internal sealed class PooledMarker : MonoBehaviour
    {
        public string Key;
    }
}
```

- [ ] **Step 4: 实现 PoolService** — `PoolService.cs`

经 `IAssetService` 加载 prefab(`AssetScope.Scene`);每 key 一个 `Stack<GameObject>`;实例挂 `PooledMarker`;Despawn 校验 marker + 去重(重复回收抛 `InvalidOperationException`);Spawn/Despawn 时对实例上所有 `EasyFramework.Core.Pooling.IPoolable` 组件调 `OnSpawn/OnDespawn`;`SetActive` 切换;收到 `SceneWillUnloadEvent` 清空全部池并 Destroy 实例。池根节点统一挂在 `[Pools]` GameObject 下。`DestroyHandler` 可注入便于 EditMode 测试。

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Core.Pooling;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Scenes;
using UnityEngine;

namespace EasyFramework.Services.Pooling
{
    public sealed class PoolService : IPoolService, IDisposable
    {
        /// <summary>EditMode 测试可替换为 Object.DestroyImmediate;运行时为 Object.Destroy。</summary>
        internal static Action<GameObject> DestroyHandler = UnityEngine.Object.Destroy;

        readonly IAssetService _assets;
        readonly IDisposable _sceneUnloadSub;
        readonly Dictionary<string, Stack<GameObject>> _idle = new();
        readonly HashSet<GameObject> _active = new();
        readonly Dictionary<GameObject, IPoolable[]> _poolablesCache = new();

        GameObject _root;
        GameObject Root => _root != null ? _root : (_root = new GameObject("[Pools]"));

        public PoolService(IAssetService assets, IEventBus events)
        {
            _assets = assets;
            _sceneUnloadSub = events.Subscribe<SceneWillUnloadEvent>(_ => ClearAll());
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
            instance.SetActive(false);
            instance.transform.SetParent(Root.transform, false);
            GetStack(marker.Key).Push(instance);
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

        void ClearAll()
        {
            foreach (var stack in _idle.Values)
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go != null) DestroyHandler(go);
                }
            _idle.Clear();
            foreach (var go in _active)
                if (go != null) DestroyHandler(go);
            _active.Clear();
            _poolablesCache.Clear();
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

> 注:`GetComponentsInChildren<IPoolable>(true)` 在接口上工作(Unity 支持按接口取组件)。测试里 `Resettable : MonoBehaviour, IPoolable` 因此会被命中。`_poolablesCache` 以实例为键缓存,避免每次 spawn 反射开销;`ClearAll` 清缓存。

- [ ] **Step 5: 验证测试通过**(验证代理)
- [ ] **Step 6: Commit**(编排者)`git commit -m "feat(services): add gameobject pool service"`

---

### Task 7: 接线 —— FrameworkInstaller / G / RootLifetimeScope(串行,依赖 Task 2-6)

> **执行前必读本文件开头的「Task 7 前置要求」。先 `Read` Phase 1 落地的 `G.cs`/`FrameworkInstaller.cs`/`RootLifetimeScope.cs` 核对实际签名,再按下方完整内容等效合并。**

**Files:**
- Modify: `Assets/EasyFramework/Services/AssemblyInfo.cs`(追加 InternalsVisibleTo Boot)
- Modify: `Assets/EasyFramework/Boot/FrameworkInstaller.cs`(签名升级 + 注册新服务 + 新增 FrameworkOptions)
- Modify: `Assets/EasyFramework/Boot/G.cs`(新增 5 属性)
- Modify: `Assets/EasyFramework/Boot/RootLifetimeScope.cs`(构造 options、挂 SaveOnPauseListener)
- Modify: `Assets/EasyFramework/Tests/EditMode/FrameworkInstallerTests.cs`(扩展断言)

- [ ] **Step 1: Services AssemblyInfo 追加 Boot 可见性** — `Assets/EasyFramework/Services/AssemblyInfo.cs`

`SceneService` 构造函数与 `ISceneLoader`/`UnitySceneLoader`/`PoolService.DestroyHandler` 为 internal,`FrameworkInstaller`(Boot 程序集)需要 new `SceneService`、`UnitySceneLoader`。完整内容:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EasyFramework.Tests.EditMode")]
[assembly: InternalsVisibleTo("EasyFramework.Boot")]
```

- [ ] **Step 2: 升级失败测试** — `FrameworkInstallerTests.cs`

在 Phase 1 基础上扩展:`Install` 现需 `FrameworkOptions`;断言新服务可解析、`G` 绑定五个新属性。**先 Read Phase 1 实际文件**,在其上增量修改;下方为合并后的完整文件。

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        [TearDown]
        public void TearDown() => EasyFramework.G.Reset();

        static FrameworkOptions MakeOptions()
            => new FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_installer_" + System.Guid.NewGuid().ToString("N")),
                ConfigTables = new List<ConfigTable>(),
                SaveProfile = null, // 用默认 DefaultSaveData profile
            };

        IObjectResolver Build()
        {
            var builder = new ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeOptions());
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
        public void Install_ResolvesPhase2Services()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IAssetService>());
            Assert.NotNull(c.Resolve<ISceneService>());
            Assert.NotNull(c.Resolve<ISaveService>());
            Assert.NotNull(c.Resolve<IConfigService>());
            Assert.NotNull(c.Resolve<IPoolService>());
        }

        [Test]
        public void GFacade_BindsPhase2Services()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IEventBus>(), EasyFramework.G.Events);
            Assert.AreSame(c.Resolve<ITimerService>(), EasyFramework.G.Timer);
            Assert.AreSame(c.Resolve<IAssetService>(), EasyFramework.G.Asset);
            Assert.AreSame(c.Resolve<ISceneService>(), EasyFramework.G.Scene);
            Assert.AreSame(c.Resolve<ISaveService>(), EasyFramework.G.Save);
            Assert.AreSame(c.Resolve<IConfigService>(), EasyFramework.G.Config);
            Assert.AreSame(c.Resolve<IPoolService>(), EasyFramework.G.Pool);
        }

        [Test]
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Events);
            Assert.IsNull(EasyFramework.G.Asset);
            Assert.IsNull(EasyFramework.G.Save);
        }
    }
}
```

- [ ] **Step 3: 升级 G 门面** — `Assets/EasyFramework/Boot/G.cs`

在 Phase 1 的 Events/Timer 基础上新增 Asset/Scene/Save/Config/Pool 五属性,`Initialize`/`Reset` 同步更新。合并后完整内容:

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
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
            IsInitialized = false;
        }
    }
}
```

- [ ] **Step 4: 升级 FrameworkInstaller** — `Assets/EasyFramework/Boot/FrameworkInstaller.cs`

签名升级为 `Install(IContainerBuilder, FrameworkOptions)`;新增 `FrameworkOptions`;注册全部 Phase 2 服务 + 两个 BootTask。**关键约束:必须在纯 `ContainerBuilder` 下可 Build(不依赖 MonoBehaviour)。** Unity 静态值(persistentDataPath)由 `RootLifetimeScope` 经 options 传入。合并后完整内容:

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
using MessagePipe;
using VContainer;

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
            builder.Register<ISceneTransition, NoopSceneTransition>(Lifetime.Singleton);
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
> - `SceneService`/`ConfigService` 用工厂 lambda 注册(构造含 internal 类型 / `IReadOnlyList<ConfigTable>`),Boot 已被 Services 加入 InternalsVisibleTo(Step 1)故可见 `ISceneLoader`/`UnitySceneLoader`/`SceneService` 的 internal 构造。
> - 两个 BootTask 以 `.As<IBootTask>()` 注册,`GameBootstrap`(Phase 1,构造接收 `IEnumerable<IBootTask>`)会自动收集到 `SaveBootTask`(Priority 0)与 `ConfigBootTask`(Priority 10)。
> - 全部用 `Lifetime.Singleton`,纯 `ContainerBuilder.Build()` 可成功(无 `RegisterComponentInHierarchy` 等 MonoBehaviour 依赖)。`SaveOnPauseListener` 不在此注册,由 `RootLifetimeScope` 在 Configure 里 AddComponent(下步)。

- [ ] **Step 5: 升级 RootLifetimeScope** — `Assets/EasyFramework/Boot/RootLifetimeScope.cs`

构造 `FrameworkOptions`(传 `Application.persistentDataPath` 与 Inspector 暴露的 ConfigTables);用新签名调 `Install`;在 Configure 里 `gameObject.AddComponent<SaveOnPauseListener>()` 并在 build callback 里 `Bind` 它(选定「Configure 里 AddComponent」方案,不用 `RegisterComponentInHierarchy`,因为 listener 不需被注入,只需持有 ISaveService 引用)。**先 Read Phase 1 实际文件**,保留其 TimerTicker / GameBootstrap entrypoint / `G.Initialize` build callback,合并后完整内容:

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Saves;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>框架组合根。挂在 Boot 场景的常驻 GameObject 上。</summary>
    public class RootLifetimeScope : LifetimeScope
    {
        [Header("本地配置表(可空)")]
        [SerializeField] List<ConfigTable> _configTables = new();

        [Header("初始场景名")]
        [SerializeField] string _initialScene = "Boot";

        protected override void Configure(IContainerBuilder builder)
        {
            var options = new FrameworkOptions
            {
                SaveDirectory = Application.persistentDataPath,
                ConfigTables = _configTables,
                SaveProfile = null,
                InitialScene = _initialScene,
            };

            FrameworkInstaller.Install(builder, options);

            builder.RegisterEntryPoint<GameBootstrap>();
            builder.UseEntryPoints(ep => ep.Add<TimerTicker>());

            // SaveOnPauseListener 挂到本组合根 GameObject,build 后绑定 ISaveService。
            var pauseListener = gameObject.AddComponent<SaveOnPauseListener>();

            builder.RegisterBuildCallback(r =>
            {
                pauseListener.Bind(r.Resolve<ISaveService>());
                G.Initialize(r);
            });
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

> 若 Phase 1 实际的 `RootLifetimeScope` 用了不同的 build callback / entrypoint 写法(例如未用 `UseEntryPoints` 链式 API),实现代理以实际文件为基线,只增量加入三件事:(a) 构造 `FrameworkOptions` 并改 `Install` 调用为双参版;(b) `AddComponent<SaveOnPauseListener>()` + build callback 里 `Bind`;(c) 保持 `G.Initialize(r)` 调用。`_configTables`/`_initialScene` 的 `[SerializeField]` 字段为新增,不影响已有逻辑。

- [ ] **Step 6: 验证测试通过**(验证代理:`FrameworkInstallerTests` 全过)
- [ ] **Step 7: Commit**(编排者)`git commit -m "feat(boot): wire phase2 services into installer, facade and root scope"`

---

### Task 8: 全量验证(串行,使用 UnityMCP)

- [ ] **Step 1: 刷新编译** — `refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false`。
- [ ] **Step 2: 0 error** — `read_console(types=["error"])`。Expected: 0 errors。
- [ ] **Step 3: EditMode 全量测试** — `run_tests(mode="EditMode")` → `get_test_job` 轮询。Expected: Phase 1 全部用例 + Phase 2 新增(AssetService 5、SceneService 3、SaveService 6、ConfigService 6、PoolService 6、FrameworkInstaller 升级后 4)全 PASS。
- [ ] **Step 4: 无新警告** — `read_console(types=["warning"])`。Expected: 无框架相关新警告(注意:Save 损坏检测测试、Config 解析失败测试会在**测试运行时**主动 `Debug.LogWarning`,这是被测路径的预期输出,不计作框架缺陷;若验证代理用 `LogAssert.Expect` 收口更佳,否则在报告中注明这些是测试触发的预期警告)。
- [ ] **Step 5: 偏差记录** — 若实现与计划有偏差(如 UniTask Addressables 扩展 API、VContainer 工厂注册写法调整),在本计划文档末尾追加 "Deviations" 小节记录。
- [ ] **Step 6: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- **Spec 覆盖:** Phase 2 范围 = 设计文档 §10 Phase 2 条目(Asset / Scene / Save / Config / ObjectPool)。逐项核对:
  - §4.1 Asset「按场景分组、场景卸载自动 Release 整组」→ `AddressablesAssetService` 订阅 `SceneWillUnloadEvent` + `ReleaseScope(Scene)`。✓
  - §4.2 Scene「异步加载、过渡遮罩、Loading 进度、加载前后钩子」→ `SceneService` PlayOut/PlayIn + 进度转发 + Will/Loaded 事件;过渡视觉留 `NoopSceneTransition`(Phase 3 实现)。✓
  - §4.3 Save「单文件 JSON / persistentDataPath、临时文件+原子替换、版本迁移链、HMAC、OnApplicationPause 落盘」→ `JsonSaveService`(File.Replace + HMACSHA256 + 迁移链)+ `SaveOnPauseListener` + `SaveBootTask`。云存档 `ISaveBackend` 按 spec §9 为 YAGNI,Phase 2 不做,符合范围。✓
  - §4.4 Config「ScriptableObject 本地默认 + 远程覆盖、远程失败不阻塞」→ `ConfigService`(远程覆盖本地)+ `ConfigBootTask`(IsCritical=false)+ `NoopRemoteConfigProvider`。✓
  - §3.4 ObjectPool 的 GameObject 池部分「经 AssetService 加载 prefab、IPoolable 钩子、场景卸载清池」→ `PoolService`。✓
- **类型一致性:** 跨 Task 核对了锁定的公共契约签名与测试/实现/Installer 三处的一致性:`IAssetService.LoadAsync<T>/ReleaseScope`、`AssetScope`、`ISceneService.CurrentScene/LoadAsync`、`SceneWillUnloadEvent/SceneLoadedEvent/ISceneTransition/NoopSceneTransition`、`SaveData/ISaveMigration/SaveProfile/ISaveService`、`IRemoteConfigProvider/NoopRemoteConfigProvider/IConfigService`、`IPoolService.PrewarmAsync/SpawnAsync/Despawn`、`G.Asset/Scene/Save/Config/Pool`、`FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)`、`FrameworkOptions{SaveDirectory,ConfigTables,SaveProfile}`。Phase 1 衔接类型(`IEventBus`、`IBootTask.Priority/IsCritical/InitializeAsync`、`Core.Pooling.IPoolable`、`ITimerService`、`G.Initialize/Reset`)均与 Phase 1 计划一致。✓
- **占位符:** 无 TBD。两处「真实 Unity 路径不单测」均为 spec/任务书明确许可的延后,非占位符:(a) Addressables 真实加载冒烟测试推迟到 Phase 5 示例游戏;(b) `UnitySceneLoader` 薄实现不单测(EditMode 用 `FakeSceneLoader`)。`NoopSceneTransition`/`NoopRemoteConfigProvider`/`DefaultSaveData` 是 spec 显式要求的默认实现与扩展点,非占位符。可能漂移处均给出具体回退动作:UniTask Addressables 扩展未启用 → 用 `handle.Task`;VContainer 工厂注册 API 不符 → 以实际文件为基线等效合并(Task 7 已声明 Read-then-merge 流程)。✓

---

## 自查发现并修正的问题(起草阶段)

1. **SceneService 构造为 internal,Boot 无法注册** —— 修正:在 `Services/AssemblyInfo.cs` 追加 `InternalsVisibleTo("EasyFramework.Boot")`(Task 7 Step 1),使 Installer 可见 internal 的 `ISceneLoader`/`UnitySceneLoader`/`SceneService` 构造。
2. **ConfigBootTask 需调 `RefreshRemoteAsync`,但该方法不在 `IConfigService` 上** —— 修正:`FrameworkInstaller` 以具体类 `ConfigService` 注册并 `.As<IConfigService>().AsSelf()`,BootTask 注入具体类。
3. **EditMode 下 Object.Destroy 不立即生效导致清池断言不稳** —— 修正:`PoolService.DestroyHandler` 设为 `internal static Action<GameObject>`,测试替换为 `DestroyImmediate` 并在 TearDown 还原。
4. **Save 迁移链测试需要构造合法签名的旧版本档** —— 修正:`JsonSaveService` 暴露 `internal WriteRawForTest(JObject)` 钩子,以正确 HMAC 写入任意 JObject,避免测试硬编码签名格式。
5. **Install 依赖 Unity 静态(persistentDataPath)会破坏纯容器单测** —— 修正:引入 `FrameworkOptions` 承载 SaveDirectory/ConfigTables/SaveProfile,`RootLifetimeScope` 构造、测试传临时目录,`Install` 全程不触 Unity 静态。
6. **AddressablesAssetService 依赖 Scenes 命名空间事件,Task 2/3 并行会有编译序问题** —— 修正:在 Task 2 Step 7 注明 Scene 事件文件须先于 Asset 落地(或两者由统一编译验证收口),已在依赖说明中标注。

---

## Deviations(Task 8 验证代理记录)

验证时发现并修复了两处由 asmdef `overrideReferences: true` 引起的"扩展方法所在程序集未被引用"编译错误。两处均为第三方 API 接线层的 asmdef 引用修正,**未触碰任何公共接口契约 / 实现逻辑 / 测试代码**。

1. **`AddressablesAssetService.cs(45,17): CS0815 Cannot assign void to an implicitly-typed variable`**
   - 根因:`Services` asmdef 设了 `overrideReferences: true` 却只引用了 `UniTask`,未引用 UniTask 的 Addressables 集成程序集 `UniTask.Addressables`(位于 `com.cysharp.unitask/.../External/Addressables/`,`autoReferenced: true`)。`overrideReferences: true` 会关闭自动引用,导致 `AsyncOperationHandle<T>.ToUniTask<T>()` 泛型重载不可见,编译器退化匹配到非泛型 `ToUniTask()`(返回无结果的 `UniTask`),`var result = await ...` 因此拿到 void。
   - 修复:`Assets/EasyFramework/Services/EasyFramework.Services.asmdef` 的 `references` 增加 `"UniTask.Addressables"`。`AddressablesAssetService.cs` 源码**未改**,计划 Task 2 Step 6 预留的 `await handle.Task` 回退方案因此无需采用(`ToUniTask()` 写法保留)。
   - 该 asmdef 的 `versionDefines` 在检测到 `com.unity.addressables` 时定义 `UNITASK_ADDRESSABLE_SUPPORT`,Addressables 已装,扩展正常启用。

2. **`EventBusTests.cs(19,21): CS1061 'ContainerBuilder' does not contain 'RegisterMessagePipe'`**
   - 根因:Task 2 Step 3 给 `Tests.EditMode` asmdef 加了 `overrideReferences: true`,但 `references` 只含 `MessagePipe`,缺 `MessagePipe.VContainer`(`RegisterMessagePipe` 扩展所在程序集)。Phase 1 时该 asmdef 无 `overrideReferences`,靠自动引用拿到 `MessagePipe.VContainer`,故 Phase 1 通过;Phase 2 开启 override 后该传递引用丢失,Phase 1 的 `EventBusTests` 反被打断编译。Core / Boot asmdef 本就显式引用了 `MessagePipe.VContainer`,故未受影响。
   - 修复:`Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef` 的 `references` 增加 `"MessagePipe.VContainer"`(置于 `MessagePipe` 之后)。测试源码未改。

**验证结果(2026-06-12,Unity 6000.3.15f1,UnityMCP):**
- 编译:0 error(`read_console types=["error"]` 仅余 MCP 桥自身的 `Cannot access a disposed object`,属 `com.coplaydev.unity-mcp` 包瞬时错误,与本框架代码无关)。
- EditMode 测试:`EasyFramework.Tests.EditMode` 程序集 48/48 全 PASS(Phase 1 的 22 + Phase 2 新增 26:AssetService 5、SceneService 3、SaveService 6、ConfigService 6、PoolService 6)。全量 EditMode 运行 49/49(含 Addressables 包自带的 `AddressableAssets.DocExampleCode.TestStub.RequiredTest` 占位用例 1 个,非本项目代码)。
- 警告(Task 8 Step 4):框架相关 warning 仅 3 类,均为**被测错误路径的预期 `Debug.LogWarning`**,出现在对应测试的 `output` 字段、未导致任何失败:① `ConfigService` 解析失败回退(`ConfigServiceTests.Get_ParseFailure_ReturnsDefault`)、② `JsonSaveService` HMAC 损坏检测+备份重建(`SaveServiceTests.TamperedFile_DetectedAndRebuilt`)、③ `GameBootstrap` 非关键 BootTask 失败(Phase 1 `GameBootstrapTests.NonCriticalFailure_DoesNotAbortBoot`)。未用 `LogAssert.Expect` 收口(测试源码保持计划原样,公共测试行为不变),按计划 Step 4 许可在此注明:这些是测试主动触发的预期输出,不计作框架缺陷,无新增异常警告。
