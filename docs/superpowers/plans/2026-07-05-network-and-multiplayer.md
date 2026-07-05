# EasyFramework 网络基础层与联机适配层 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 EasyFramework 补两块基础设施——通用 HTTP 服务(`IHttpService`,挂 `G.Http`,所有游戏都能用)和联机适配契约(`IMultiplayerService`/`IMultiplayerProvider`,仅接口 + Fake,刻意不进 `FrameworkInstaller`/`G`,由需要联机的具体游戏自行接线)。

**Architecture:** 沿用框架一贯的 "Provider(SDK 适配点)+ Service(业务层入口)" 两层模式。`Services/Network` 下新增 `IHttpTransport`(真实网络 I/O 边界)+ `UnityWebRequestTransport`(真实实现,只在这一个类里出现 `UnityWebRequest`)+ `IHttpService`/`HttpService`(重试/超时/反序列化逻辑,完全通过注入 `IHttpTransport` 测试,不碰真实网络)。`Services/Multiplayer` 下新增 `IMultiplayerProvider`(SDK 适配点)+ `IMultiplayerService`/`MultiplayerService`(把 Provider 状态变化/收消息统一转成 `IEventBus` 事件)+ `FakeMultiplayerProvider`(本地回环,供接入联机的游戏在编辑器/单测下调试)。JSON 序列化统一用已有的 Newtonsoft 依赖(`EasyFramework.Services.asmdef` 已有 `precompiledReferences: ["Newtonsoft.Json.dll"]`,无需改 asmdef)。Http 部分接入 `G.Http`(`Boot/G.cs` 新增字段 + `Initialize`/`Reset` 同步)和 `FrameworkInstaller.Install`(常驻服务注册);Multiplayer 部分**不**改动 `Boot/G.cs`、`Boot/FrameworkInstaller.cs` 任何一行,只新增文件。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask (Cysharp.Threading.Tasks) / Newtonsoft.Json (com.unity.nuget.newtonsoft-json) / NUnit (Unity Test Framework EditMode)

---

## 文件结构总览

```
Packages/com.yifei.easyframework/Services/Network/
├── IHttpService.cs                 (新增:HttpMethod / HttpRequestOptions / HttpException / IHttpService)
├── IHttpTransport.cs               (新增:IHttpTransport)
├── HttpService.cs                  (新增:IHttpService 默认实现,重试/超时/反序列化逻辑)
└── UnityWebRequestTransport.cs     (新增:IHttpTransport 真实实现,唯一出现 UnityWebRequest 的文件)

Packages/com.yifei.easyframework/Services/Multiplayer/
├── IMultiplayerProvider.cs         (新增:ConnectionState / 两个事件结构体 / IMultiplayerProvider)
├── IMultiplayerService.cs          (新增:IMultiplayerService)
├── MultiplayerService.cs           (新增:IMultiplayerService 默认实现)
└── FakeMultiplayerProvider.cs      (新增:本地回环 Fake,供具体游戏在编辑器/单测下使用)

Packages/com.yifei.easyframework/Boot/
├── G.cs                            (修改:新增 Http 字段,Initialize/Reset 同步)
└── FrameworkInstaller.cs           (修改:新增 Http 相关注册;Multiplayer 不触碰此文件)

Packages/com.yifei.easyframework/Tests/EditMode/
├── HttpServiceTests.cs             (新增:FakeHttpTransport 内嵌于本文件)
├── MultiplayerServiceTests.cs      (新增:验证连接状态流转、FakeMultiplayerProvider 回环收发、FrameworkInstaller/G 不受影响)

Packages/com.yifei.easyframework/Samples~/Template/README.md   (修改:补一节"接联机"文档说明)
```

---

### Task 1: `IHttpService` 契约 + `IHttpTransport` 契约

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Network/IHttpService.cs`
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Network/IHttpTransport.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs`

这一步只落地纯接口/数据类型(无逻辑可测),因此测试直接放在 Task 2(`HttpService` 实现)一起写。本 Task 只创建类型定义文件本身。

- [ ] **Step 1: 创建 `IHttpService.cs`**

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Network
{
    public enum HttpMethod { Get, Post } // Put/Delete 目前没有具体需求驱动,真正用到时再加

    public sealed class HttpRequestOptions
    {
        public IReadOnlyDictionary<string, string> Headers;
        public int TimeoutSeconds = 10;
        public int RetryCount = 2;
    }

    public sealed class HttpException : System.Exception
    {
        public long StatusCode { get; }
        public HttpException(long statusCode, string message) : base(message) => StatusCode = statusCode;
    }

    public interface IHttpService
    {
        UniTask<TResponse> GetAsync<TResponse>(string url, HttpRequestOptions options = null);
        UniTask<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, HttpRequestOptions options = null);
    }
}
```

- [ ] **Step 2: 创建 `IHttpTransport.cs`**

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Network
{
    /// <summary>真实网络 I/O 的边界。真实实现 UnityWebRequestTransport 走真实网络;
    /// HttpService 的重试/超时/反序列化逻辑通过注入 FakeHttpTransport 完全脱离真实网络单测。</summary>
    public interface IHttpTransport
    {
        UniTask<(long statusCode, string body)> SendAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options);
    }
}
```

- [ ] **Step 3: 编译检查**

在 Unity Test Runner 的 EditMode 标签页运行(此时还没有任何新测试,只需确认新增的两个文件不产生编译错误)。
预期:Unity 控制台无编译错误(`Console` 面板 `Error` 过滤为空)。这两个文件只有类型声明,没有可测行为,故不产生独立的 commit——随 Task 2 一起提交。

---

### Task 2: `HttpService` 实现(重试/超时/反序列化逻辑,注入 `IHttpTransport`)

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Network/HttpService.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs`

- [ ] **Step 1: Write the failing test**

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Network;
using Newtonsoft.Json;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class HttpServiceTests
    {
        sealed class FakeHttpTransport : IHttpTransport
        {
            public Queue<(long statusCode, string body)> Responses = new();
            public List<(HttpMethod method, string url, string jsonBody, HttpRequestOptions options)> Calls = new();
            public bool ThrowTimeoutOnce;
            bool _timeoutThrown;

            public UniTask<(long statusCode, string body)> SendAsync(
                HttpMethod method, string url, string jsonBody, HttpRequestOptions options)
            {
                Calls.Add((method, url, jsonBody, options));

                if (ThrowTimeoutOnce && !_timeoutThrown)
                {
                    _timeoutThrown = true;
                    throw new TimeoutException("simulated timeout");
                }

                if (Responses.Count == 0)
                    throw new InvalidOperationException("FakeHttpTransport has no queued response.");

                return UniTask.FromResult(Responses.Dequeue());
            }
        }

        sealed class Payload
        {
            public string Name;
            public int Value;
        }

        [Test]
        public void GetAsync_DeserializesSuccessResponse()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "a", Value = 1 })));
            var svc = new HttpService(transport);

            var result = svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult();

            Assert.AreEqual("a", result.Name);
            Assert.AreEqual(1, result.Value);
            Assert.AreEqual(HttpMethod.Get, transport.Calls[0].method);
        }

        [Test]
        public void PostAsync_SendsSerializedBody_DeserializesResponse()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "b", Value = 2 })));
            var svc = new HttpService(transport);

            var result = svc.PostAsync<Payload, Payload>(
                "https://example.com/api", new Payload { Name = "req", Value = 9 }).GetAwaiter().GetResult();

            Assert.AreEqual("b", result.Name);
            Assert.AreEqual(HttpMethod.Post, transport.Calls[0].method);
            StringAssert.Contains("\"req\"", transport.Calls[0].jsonBody);
        }

        [Test]
        public void GetAsync_RetriesOnTimeout_ThenSucceeds()
        {
            var transport = new FakeHttpTransport { ThrowTimeoutOnce = true };
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "c", Value = 3 })));
            var svc = new HttpService(transport);

            var result = svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult();

            Assert.AreEqual("c", result.Name);
            Assert.AreEqual(2, transport.Calls.Count); // 第一次超时 + 一次重试成功
        }

        [Test]
        public void GetAsync_4xxResponse_ThrowsHttpException()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((404, "not found"));
            var svc = new HttpService(transport);

            var ex = Assert.Throws<HttpException>(() =>
                svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult());
            Assert.AreEqual(404, ex.StatusCode);
        }

        [Test]
        public void GetAsync_5xxResponse_ThrowsHttpException()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((500, "server error"));
            var svc = new HttpService(transport);

            var ex = Assert.Throws<HttpException>(() =>
                svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult());
            Assert.AreEqual(500, ex.StatusCode);
        }

        [Test]
        public void GetAsync_RetryCountExhausted_ThrowsFinalFailure()
        {
            var transport = new FakeHttpTransport();
            // RetryCount=2 意味着最多尝试 3 次(1 次初始 + 2 次重试),全部超时。
            var svc = new HttpService(transport);
            var options = new HttpRequestOptions { RetryCount = 2 };

            var ex = Assert.Throws<TimeoutException>(() =>
                svc.GetAsync<Payload>("https://example.com/api", options).GetAwaiter().GetResult());

            Assert.IsNotNull(ex);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.HttpServiceTests`。
预期:FAIL(编译错误)—— `HttpService` 类型不存在(`CS0246: The type or namespace name 'HttpService' could not be found`)。

- [ ] **Step 3: Write minimal implementation**

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Network/HttpService.cs`:

```csharp
using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace EasyFramework.Services.Network
{
    public sealed class HttpService : IHttpService
    {
        readonly IHttpTransport _transport;

        public HttpService(IHttpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async UniTask<TResponse> GetAsync<TResponse>(string url, HttpRequestOptions options = null)
        {
            var body = await SendWithRetryAsync(HttpMethod.Get, url, null, options ?? new HttpRequestOptions());
            return JsonConvert.DeserializeObject<TResponse>(body);
        }

        public async UniTask<TResponse> PostAsync<TRequest, TResponse>(
            string url, TRequest requestBody, HttpRequestOptions options = null)
        {
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var body = await SendWithRetryAsync(HttpMethod.Post, url, jsonBody, options ?? new HttpRequestOptions());
            return JsonConvert.DeserializeObject<TResponse>(body);
        }

        async UniTask<string> SendWithRetryAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options)
        {
            var maxAttempts = Math.Max(1, options.RetryCount + 1);
            Exception lastException = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var (statusCode, responseBody) = await _transport.SendAsync(method, url, jsonBody, options);

                    if (statusCode >= 400)
                        throw new HttpException(statusCode, $"HTTP {statusCode} for {url}: {responseBody}");

                    return responseBody;
                }
                catch (HttpException)
                {
                    // 4xx/5xx 视为最终失败,不重试(重试只覆盖超时/瞬时网络错误)。
                    throw;
                }
                catch (Exception e)
                {
                    lastException = e;
                }
            }

            throw lastException ?? new InvalidOperationException($"HTTP request to {url} failed with no exception recorded.");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.HttpServiceTests`。
预期:PASS,6 个测试全绿(`GetAsync_DeserializesSuccessResponse`、`PostAsync_SendsSerializedBody_DeserializesResponse`、`GetAsync_RetriesOnTimeout_ThenSucceeds`、`GetAsync_4xxResponse_ThrowsHttpException`、`GetAsync_5xxResponse_ThrowsHttpException`、`GetAsync_RetryCountExhausted_ThrowsFinalFailure`)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/Network/IHttpService.cs Packages/com.yifei.easyframework/Services/Network/IHttpTransport.cs Packages/com.yifei.easyframework/Services/Network/HttpService.cs Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs
git commit -m "feat: add IHttpService with retry/timeout/deserialization logic tested via FakeHttpTransport"
```

---

### Task 3: `UnityWebRequestTransport`(真实网络实现)

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Network/UnityWebRequestTransport.cs`

真实网络 I/O 无法在 EditMode 单测里可靠断言(会真正发起 HTTP 请求,且依赖外部可用性),按 spec §2.2 的要求,`HttpService` 的逻辑已在 Task 2 通过 `FakeHttpTransport` 完全覆盖。本 Task 只交付真实实现本身,不写发起真实网络请求的测试——这与仓库既有的 `AdMobAdsProvider`/`UnityIAPProvider` 真机路径"不写 EditMode 单测,业务逻辑已在别处覆盖"的先例一致。

- [ ] **Step 1: 创建 `UnityWebRequestTransport.cs`**

```csharp
using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace EasyFramework.Services.Network
{
    /// <summary>IHttpTransport 的真实实现;真正发起网络请求的唯一入口。
    /// UnityWebRequest 不能在 EditMode 测试里真正联网,因此不写单测——
    /// HttpService 的重试/超时/反序列化逻辑已通过 FakeHttpTransport 在 HttpServiceTests 完全覆盖。</summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public async UniTask<(long statusCode, string body)> SendAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options)
        {
            using var request = method == HttpMethod.Get
                ? UnityWebRequest.Get(url)
                : CreatePostRequest(url, jsonBody);

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = options.TimeoutSeconds;

            if (options.Headers != null)
                foreach (var header in options.Headers)
                    request.SetRequestHeader(header.Key, header.Value);

            await request.SendWebRequest().ToUniTask();

            if (request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.DataProcessingError)
                throw new TimeoutException($"Network error for {url}: {request.error}");

            return (request.responseCode, request.downloadHandler.text);
        }

        static UnityWebRequest CreatePostRequest(string url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }
    }
}
```

- [ ] **Step 2: 编译检查**

在 Unity Test Runner 的 EditMode 标签页运行(确认新增文件不破坏现有编译;本文件无独立单测)。
预期:Unity 控制台无编译错误。`Services/Network` 目录下此时应有 4 个 `.cs` 文件:`IHttpService.cs`、`IHttpTransport.cs`、`HttpService.cs`、`UnityWebRequestTransport.cs`。

- [ ] **Step 3: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/Network/UnityWebRequestTransport.cs
git commit -m "feat: add UnityWebRequestTransport as the real network implementation of IHttpTransport"
```

---

### Task 4: 接入 `G.Http` 与 `FrameworkInstaller`

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/G.cs`
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs`

- [ ] **Step 1: Write the failing test**

在 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs` 的 `HttpServiceTests` 类内新增一个测试方法(紧跟在 `GetAsync_RetryCountExhausted_ThrowsFinalFailure` 之后)。这个测试直接复用仓库既有的 `FrameworkInstallerTests.cs` 里验证过的模式(`ContainerBuilder` + `FrameworkInstaller.Install` + 真实 `FrameworkOptions`),而不是手写一个隔离的 mini container——这样才能真正验证 `FrameworkInstaller.Install` 这一具体方法确实注册了 `IHttpService`,而不仅仅是"这种注册写法在孤立环境下可行":

```csharp
        static EasyFramework.FrameworkOptions MakeFrameworkOptions()
            => new EasyFramework.FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_http_" + System.Guid.NewGuid().ToString("N")),
            };

        [Test]
        public void FrameworkInstaller_RegistersHttpService_ResolvableAsIHttpService()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptions());
            using var container = builder.Build();

            var resolved = container.Resolve<IHttpService>();

            Assert.IsInstanceOf<HttpService>(resolved);
        }

        [Test]
        public void G_Http_IsExposedAsGHttp()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptions());
            using var container = builder.Build();

            EasyFramework.G.Initialize(container);
            try
            {
                Assert.AreSame(container.Resolve<IHttpService>(), EasyFramework.G.Http);
            }
            finally
            {
                EasyFramework.G.Reset();
            }
        }
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.HttpServiceTests`。
预期:FAIL(编译错误)——此时 `Boot/FrameworkInstaller.cs` 尚未注册 `IHttpService`,`Boot/G.cs` 尚未声明 `Http` 属性,`G_Http_IsExposedAsGHttp` 无法编译(`CS1061: 'G' does not contain a definition for 'Http'`),导致整个测试程序集编译失败,包括 `FrameworkInstaller_RegistersHttpService_ResolvableAsIHttpService` 在内的所有测试都无法运行。

- [ ] **Step 3: Write minimal implementation**

修改 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/G.cs`。

在文件顶部 `using` 块新增一行(第 9 行 `using EasyFramework.Services.Configs;` 之后按字母序插入):

```csharp
using EasyFramework.Services.Network;
```

在第 31 行 `public static IConfigService Config { get; private set; }` 之后新增:

```csharp
        public static IConfigService Config { get; private set; }
        public static IHttpService Http { get; private set; }
```

在第 58 行 `Config = resolver.Resolve<IConfigService>();` 之后新增:

```csharp
            Config = resolver.Resolve<IConfigService>();
            Http = resolver.Resolve<IHttpService>();
```

在第 88 行 `Config = null;`（`Reset()` 方法内）之后新增:

```csharp
            Config = null;
            Http = null;
```

完整改动后的文件内容如下（供核对，非新建文件——按上述 3 处插入即可）：

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Network;
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
        public static IHttpService Http { get; private set; }
        public static IPoolService Pool { get; private set; }

        // Phase 3a UI
        public static IUIService UI { get; private set; }

        // Phase 3b 表现层
        public static IAudioService Audio { get; private set; }
        public static IInputService Input { get; private set; }
        public static ICameraService Camera { get; private set; }
        public static IJuiceService Juice { get; private set; }
        public static ILocalizationService Loc { get; private set; }
        public static IHapticsService Haptics { get; private set; }

        // Phase 4 商业化层
        public static IAdsService Ads { get; private set; }
        public static IIAPService IAP { get; private set; }
        public static IAnalyticsService Analytics { get; private set; }

        public static bool IsInitialized { get; private set; }

        internal static void Initialize(IObjectResolver resolver)
        {
            Events = resolver.Resolve<IEventBus>();
            Timer = resolver.Resolve<ITimerService>();
            Asset = resolver.Resolve<IAssetService>();
            Scene = resolver.Resolve<ISceneService>();
            Save = resolver.Resolve<ISaveService>();
            Config = resolver.Resolve<IConfigService>();
            Http = resolver.Resolve<IHttpService>();
            Pool = resolver.Resolve<IPoolService>();

            UI = resolver.Resolve<IUIService>();

            Audio = resolver.Resolve<IAudioService>();
            Input = resolver.Resolve<IInputService>();
            Camera = resolver.Resolve<ICameraService>();
            Juice = resolver.Resolve<IJuiceService>();
            Loc = resolver.Resolve<ILocalizationService>();
            Haptics = resolver.Resolve<IHapticsService>();

            // Phase 4 商业化层
            Ads = resolver.Resolve<IAdsService>();
            IAP = resolver.Resolve<IIAPService>();
            Analytics = resolver.Resolve<IAnalyticsService>();

            // 填充 Services 层本地化运行时访问点,供 LocalizedText 读取(避免 Services → Boot 循环依赖)。
            LocalizationRuntime.Initialize(Loc, Events);

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
            Http = null;
            Pool = null;

            UI = null;

            Audio = null;
            Input = null;
            Camera = null;
            Juice = null;
            Loc = null;
            Haptics = null;

            // Phase 4 商业化层
            Ads = null;
            IAP = null;
            Analytics = null;

            LocalizationRuntime.Reset();

            IsInitialized = false;
        }
    }
}
```

修改 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs`。

在文件顶部 `using EasyFramework.Services.Localization;` 之后（第 15 行）按字母序新增一行：

```csharp
using EasyFramework.Services.Network;
```

在 `// ---- Pool(Phase 2)----` 段落（原第 86-87 行）之前插入一个新段落 `// ---- Http(Phase 5 网络基础层)----`：

```csharp
            // ---- Http(Phase 5 网络基础层)----
            // 通用 HTTP 基础设施,所有游戏都可能用到。UnityWebRequestTransport 是唯一发起真实网络请求的实现;
            // EditMode 测试通过注入 FakeHttpTransport 验证 HttpService 的重试/超时/反序列化逻辑。
            builder.Register<IHttpTransport, UnityWebRequestTransport>(Lifetime.Singleton);
            builder.Register<HttpService>(Lifetime.Singleton).As<IHttpService>().AsSelf();

            // ---- Pool(Phase 2)----
            builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.HttpServiceTests`。
预期:PASS,全部 8 个测试通过（Task 2 的 6 个 + 本 Task 新增的 `FrameworkInstaller_RegistersHttpService_ResolvableAsIHttpService` 与 `G_Http_IsExposedAsGHttp`）。同时在 Unity Test Runner 里运行全部 EditMode 测试(不加过滤),确认既有的 `FrameworkInstallerTests`、`ConfigServiceTests`、`PoolServiceTests` 等测试类的用例数与通过数相比运行本计划之前没有变化,`G`/`FrameworkInstaller` 现有服务的注册与解析不受影响。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Boot/G.cs Packages/com.yifei.easyframework/Boot/FrameworkInstaller.cs Packages/com.yifei.easyframework/Tests/EditMode/HttpServiceTests.cs
git commit -m "feat: mount IHttpService on G.Http and register it in FrameworkInstaller"
```

---

### Task 5: `IMultiplayerProvider` / `IMultiplayerService` 契约 + `FakeMultiplayerProvider`

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerProvider.cs`
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerService.cs`
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/FakeMultiplayerProvider.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs`

- [ ] **Step 1: Write the failing test**

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.Multiplayer;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class MultiplayerServiceTests
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

        [Test]
        public void FakeMultiplayerProvider_ConnectAsync_TransitionsToConnected()
        {
            var provider = new FakeMultiplayerProvider();
            Assert.AreEqual(ConnectionState.Disconnected, provider.State);

            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ConnectionState.Connected, provider.State);
        }

        [Test]
        public void FakeMultiplayerProvider_DisconnectAsync_TransitionsToDisconnected()
        {
            var provider = new FakeMultiplayerProvider();
            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            provider.DisconnectAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ConnectionState.Disconnected, provider.State);
        }

        [Test]
        public void FakeMultiplayerProvider_SendAsync_LoopsBackToSelf()
        {
            var provider = new FakeMultiplayerProvider();
            provider.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();
            MultiplayerMessageReceivedEvent? received = null;
            provider.MessageReceived += evt => received = evt;

            provider.SendAsync("chat", new byte[] { 1, 2, 3 }).GetAwaiter().GetResult();

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual("chat", received.Value.Channel);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, received.Value.Payload);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.MultiplayerServiceTests`。
预期:FAIL(编译错误)——`EasyFramework.Services.Multiplayer` 命名空间与 `FakeMultiplayerProvider`/`ConnectionState`/`MultiplayerMessageReceivedEvent` 均不存在(`CS0246`)。

- [ ] **Step 3: Write minimal implementation**

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerProvider.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }

    public readonly struct ConnectionStateChangedEvent
    {
        public readonly ConnectionState State;
        public ConnectionStateChangedEvent(ConnectionState state) => State = state;
    }

    public readonly struct MultiplayerMessageReceivedEvent
    {
        public readonly string Channel;
        public readonly byte[] Payload;
        public MultiplayerMessageReceivedEvent(string channel, byte[] payload)
        {
            Channel = channel;
            Payload = payload;
        }
    }

    /// <summary>SDK 适配点。具体游戏在自己的 GameLifetimeScope 里注册真实实现;默认 FakeMultiplayerProvider(本地回环)。</summary>
    public interface IMultiplayerProvider
    {
        ConnectionState State { get; }
        UniTask ConnectAsync(string sessionId, CancellationToken ct);
        UniTask DisconnectAsync();
        UniTask SendAsync(string channel, byte[] payload);
    }
}
```

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerService.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>业务层入口。把 Provider 的状态变化/收到消息统一转成 IEventBus 事件供订阅。</summary>
    public interface IMultiplayerService
    {
        ConnectionState State { get; }
        UniTask ConnectAsync(string sessionId, CancellationToken ct = default);
        UniTask DisconnectAsync();
        UniTask SendAsync(string channel, byte[] payload);
    }
}
```

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/FakeMultiplayerProvider.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>本地回环 Fake:SendAsync 直接原地触发一个 MultiplayerMessageReceivedEvent,
    /// 模拟"自己发的消息自己收到"。供接入联机的游戏在编辑器/单测下开发调试用,不需要连真实后端。</summary>
    public sealed class FakeMultiplayerProvider : IMultiplayerProvider
    {
        /// <summary>测试/调试可订阅:每次 SendAsync 回环时触发。</summary>
        public event Action<MultiplayerMessageReceivedEvent> MessageReceived;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public UniTask ConnectAsync(string sessionId, CancellationToken ct)
        {
            State = ConnectionState.Connected;
            return UniTask.CompletedTask;
        }

        public UniTask DisconnectAsync()
        {
            State = ConnectionState.Disconnected;
            return UniTask.CompletedTask;
        }

        public UniTask SendAsync(string channel, byte[] payload)
        {
            MessageReceived?.Invoke(new MultiplayerMessageReceivedEvent(channel, payload));
            return UniTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.MultiplayerServiceTests`。
预期:PASS,3 个测试全绿(`FakeMultiplayerProvider_ConnectAsync_TransitionsToConnected`、`FakeMultiplayerProvider_DisconnectAsync_TransitionsToDisconnected`、`FakeMultiplayerProvider_SendAsync_LoopsBackToSelf`)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerProvider.cs Packages/com.yifei.easyframework/Services/Multiplayer/IMultiplayerService.cs Packages/com.yifei.easyframework/Services/Multiplayer/FakeMultiplayerProvider.cs Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs
git commit -m "feat: add IMultiplayerProvider/IMultiplayerService contracts with FakeMultiplayerProvider loopback"
```

---

### Task 6: `MultiplayerService` 实现(状态流转事件发布)

**Files:**
- Create: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/MultiplayerService.cs`
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs`

- [ ] **Step 1: Write the failing test**

在 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs` 的 `MultiplayerServiceTests` 类内，紧跟 `FakeMultiplayerProvider_SendAsync_LoopsBackToSelf` 之后新增：

```csharp
        [Test]
        public void MultiplayerService_ConnectAsync_PublishesConnectionStateChangedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var events = new List<ConnectionStateChangedEvent>();
            bus.Subscribe<ConnectionStateChangedEvent>(e => events.Add(e));
            var svc = new MultiplayerService(provider, bus);

            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(ConnectionState.Connected, events[0].State);
            Assert.AreEqual(ConnectionState.Connected, svc.State);
        }

        [Test]
        public void MultiplayerService_DisconnectAsync_PublishesConnectionStateChangedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var events = new List<ConnectionStateChangedEvent>();
            var svc = new MultiplayerService(provider, bus);
            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();
            bus.Subscribe<ConnectionStateChangedEvent>(e => events.Add(e));

            svc.DisconnectAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(ConnectionState.Disconnected, events[0].State);
            Assert.AreEqual(ConnectionState.Disconnected, svc.State);
        }

        [Test]
        public void MultiplayerService_SendAsync_ProviderLoopback_PublishesMessageReceivedEvent()
        {
            var provider = new FakeMultiplayerProvider();
            var bus = new FakeBus();
            var messages = new List<MultiplayerMessageReceivedEvent>();
            bus.Subscribe<MultiplayerMessageReceivedEvent>(e => messages.Add(e));
            var svc = new MultiplayerService(provider, bus);
            svc.ConnectAsync("session-1", CancellationToken.None).GetAwaiter().GetResult();

            svc.SendAsync("chat", new byte[] { 9, 8, 7 }).GetAwaiter().GetResult();

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual("chat", messages[0].Channel);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, messages[0].Payload);
        }
```

同时在文件顶部新增 `using System.Collections.Generic;`（如果尚未存在——检查文件头部 using 块，Task 5 已有 `using System.Collections.Generic;`，此处无需重复添加）。

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.MultiplayerServiceTests`。
预期:FAIL(编译错误)——`MultiplayerService` 类型不存在(`CS0246: The type or namespace name 'MultiplayerService' could not be found`)。

- [ ] **Step 3: Write minimal implementation**

创建 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Services/Multiplayer/MultiplayerService.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;

namespace EasyFramework.Services.Multiplayer
{
    /// <summary>IMultiplayerService 默认实现。把 Provider 的状态变化/收到消息统一转成 IEventBus 事件,
    /// 业务层订阅 IEventBus 即可,不用关心底层是 Photon 回调还是 HTTP 轮询在触发。</summary>
    public sealed class MultiplayerService : IMultiplayerService
    {
        readonly IMultiplayerProvider _provider;
        readonly IEventBus _events;

        public MultiplayerService(IMultiplayerProvider provider, IEventBus events)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            if (_provider is FakeMultiplayerProvider fake)
                fake.MessageReceived += OnProviderMessageReceived;
        }

        public ConnectionState State => _provider.State;

        public async UniTask ConnectAsync(string sessionId, CancellationToken ct = default)
        {
            await _provider.ConnectAsync(sessionId, ct);
            _events.Publish(new ConnectionStateChangedEvent(_provider.State));
        }

        public async UniTask DisconnectAsync()
        {
            await _provider.DisconnectAsync();
            _events.Publish(new ConnectionStateChangedEvent(_provider.State));
        }

        public UniTask SendAsync(string channel, byte[] payload) => _provider.SendAsync(channel, payload);

        void OnProviderMessageReceived(MultiplayerMessageReceivedEvent evt) => _events.Publish(evt);
    }
}
```

注:构造函数里对 `FakeMultiplayerProvider` 的类型判断是当前唯一内置 Provider 下的最小实现——真实 Provider(如未来的 `PhotonMultiplayerProvider`)需要自行定义等价的"收到消息"回调机制并在其自身实现里转发给 `IMultiplayerService`,或者更通用地让 `IMultiplayerProvider` 接口本身暴露一个消息事件。当前 spec 未要求扩展 `IMultiplayerProvider` 接口暴露事件（接口签名见 Task 5，与 spec §3.2 完全一致），因此本 Task 采用向下类型转换的最小实现,不改动已锁定的 `IMultiplayerProvider` 契约。

- [ ] **Step 4: Run test to verify it passes**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.MultiplayerServiceTests`。
预期:PASS,6 个测试全绿(Task 5 的 3 个 + 本 Task 新增的 `MultiplayerService_ConnectAsync_PublishesConnectionStateChangedEvent`、`MultiplayerService_DisconnectAsync_PublishesConnectionStateChangedEvent`、`MultiplayerService_SendAsync_ProviderLoopback_PublishesMessageReceivedEvent`)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Services/Multiplayer/MultiplayerService.cs Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs
git commit -m "feat: add MultiplayerService publishing connection state and message events via IEventBus"
```

---

### Task 7: 验证 Multiplayer 模块不影响 `FrameworkInstaller`/`G`/现有测试

**Files:**
- Test: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs`

Spec §5 验收标准要求"验证没有注册具体 Provider 的游戏,`FrameworkInstaller`/`G` 均不受影响,现有 137 个测试不受影响"。这一条通过两部分验证:(a) 一条显式测试断言 `FrameworkInstaller.Install` 构建出的容器无法解析 `IMultiplayerService`/`IMultiplayerProvider`（因为确实没注册,证明本模块是纯增量、未侵入安装流程）;(b) 全量运行 EditMode 测试套件,确认 Task 1-6 新增文件之外的既有测试用例数与通过数不变。

- [ ] **Step 1: Write the failing test**

在 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs` 的 `MultiplayerServiceTests` 类内，紧跟 `MultiplayerService_SendAsync_ProviderLoopback_PublishesMessageReceivedEvent` 之后新增：

```csharp
        static EasyFramework.FrameworkOptions MakeFrameworkOptionsForBoundaryTest()
            => new EasyFramework.FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_multiplayer_boundary_" + System.Guid.NewGuid().ToString("N")),
            };

        [Test]
        public void FrameworkInstaller_DoesNotRegisterMultiplayerTypes()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptionsForBoundaryTest());
            using var container = builder.Build();

            // FrameworkInstaller 确实没有注册这两个类型,Resolve 必须抛 VContainerException——
            // 这条测试就是要证明"没注册"这件事本身,而不是绕开它。
            Assert.Throws<VContainer.VContainerException>(() => container.Resolve<IMultiplayerService>(),
                "FrameworkInstaller must not register IMultiplayerService — multiplayer is an opt-in module wired by individual games.");
            Assert.Throws<VContainer.VContainerException>(() => container.Resolve<IMultiplayerProvider>(),
                "FrameworkInstaller must not register IMultiplayerProvider — multiplayer is an opt-in module wired by individual games.");
        }

        [Test]
        public void G_DoesNotExposeMultiplayer_NoSuchMemberExistsByDesign()
        {
            // 契约式回归标记:G 不应新增 Multiplayer 属性(设计原因见 spec §3.3——
            // G 的契约是 BootCompletedEvent 后一定可用,可选模块不能挂一个可能为 null 的入口)。
            // 用反射断言,而不是直接引用 G.Multiplayer——后者一旦被误加,本测试也无法通过编译来提醒,
            // 反射断言能在"有人加了这个属性"时给出明确失败信息而不是静默编译通过。
            var member = typeof(EasyFramework.G).GetProperty("Multiplayer");
            Assert.IsNull(member,
                "G must not expose a Multiplayer property — multiplayer is opt-in and wired per-game via constructor injection, not through G.");
        }
```

- [ ] **Step 2: Run test to verify it fails**

在 Unity Test Runner 的 EditMode 标签页运行 `EasyFramework.Tests.MultiplayerServiceTests`。
预期:在 Task 1-6 均已正确完成、且未误改 `FrameworkInstaller.cs`/`G.cs` 的前提下,这两条测试此时应当已经是 PASS(因为 Multiplayer 模块从未接线进这两个文件)。**这是本 Task 有意设计的"反向验证"**——如果这一步跑出来是 FAIL,说明前面的 Task 出现了意外改动（比如误把 Multiplayer 注册加进了 `FrameworkInstaller.Install`,或者误给 `G` 加了 `Multiplayer` 属性),必须回退该改动后重新验证,不能修改这两条断言来让测试通过。

- [ ] **Step 3: 无需新增实现代码**

本 Task 的目的是纯验证,Task 1-6 已完整交付所有实现。若 Step 2 已经 PASS,直接进入 Step 4 的全量回归。若 FAIL,回到对应 Task 修正 `FrameworkInstaller.cs`/`G.cs`,移除任何 Multiplayer 相关注册/属性后重新运行本 Task 的测试。

- [ ] **Step 4: Run test to verify it passes(全量回归)**

在 Unity Test Runner 的 EditMode 标签页,清除搜索过滤,运行**全部** EditMode 测试(不限于本次新增的两个测试类)。
预期:PASS。测试总数 = 原有 136 个(仓库当前 `grep -rc "\[Test\]"` 统计值;`README.md`/`CHANGELOG.md` 记录为"137"个,以 Unity Test Runner 实际报告的总数为准)+ Task 2 新增 6 个 + Task 4 新增 2 个 + Task 5 新增 3 个 + Task 6 新增 3 个 + Task 7 新增 2 个 = 原有基线 + 16 个,全部 PASS,0 Failed。特别确认:`ConfigServiceTests`、`PoolServiceTests` 等既有测试类的用例数和断言内容与运行本计划之前完全一致(未被本次改动影响)。

- [ ] **Step 5: Commit**

```bash
git add Packages/com.yifei.easyframework/Tests/EditMode/MultiplayerServiceTests.cs
git commit -m "test: verify FrameworkInstaller and G remain unaffected by the opt-in multiplayer module"
```

---

### Task 8: Template Sample 文档——如何在具体游戏里接入真实 `IMultiplayerProvider`

**Files:**
- Modify: `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Samples~/Template/README.md`

`Samples~` 是 Unity 的波浪号目录,不参与编译,因此本 Task 只是纯文档修改,不涉及任何 `.cs` 代码或测试。

- [ ] **Step 1: 在 README 末尾新增"六、联机(可选模块)"小节**

读取当前文件确认末尾内容(第 47-49 行为"五、DevTools"小节),在其后追加新小节:

```markdown

## 六、联机(可选模块,按需接入)

`IMultiplayerService`/`IMultiplayerProvider` 不在 `FrameworkInstaller` 里注册,也不会出现在 `G` 门面上——这是框架里第一个"可选模块":大多数小游戏不需要联机,`G` 的契约是"`BootCompletedEvent` 后一定可用",给不需要联机的游戏也挂一个可能是 null 的 `G.Multiplayer` 会破坏这个契约。需要联机的游戏按以下步骤自己接线:

1. 实现游戏专属的 `IMultiplayerProvider`(比如接 Photon 就叫 `PhotonMultiplayerProvider`,内部调用 Photon SDK;走异步回合制就在内部用 `G.Http` 轮询自选后端)。
2. 在你的 `<YourGame>LifetimeScope.ConfigureGame(IContainerBuilder builder)` 里追加两行注册(参照 `TemplateGameLifetimeScope.ConfigureGame` 里现有的注册写法风格):

   ```csharp
   builder.Register<IMultiplayerProvider, PhotonMultiplayerProvider>(VContainer.Lifetime.Singleton);
   builder.Register<EasyFramework.Services.Multiplayer.MultiplayerService>(VContainer.Lifetime.Singleton)
       .As<EasyFramework.Services.Multiplayer.IMultiplayerService>();
   ```

3. 业务代码走**构造函数注入** `IMultiplayerService`,不经过 `G`:

   ```csharp
   public sealed class MatchController
   {
       readonly EasyFramework.Services.Multiplayer.IMultiplayerService _multiplayer;
       public MatchController(EasyFramework.Services.Multiplayer.IMultiplayerService multiplayer)
           => _multiplayer = multiplayer;

       public async Cysharp.Threading.Tasks.UniTask JoinAsync(string sessionId)
           => await _multiplayer.ConnectAsync(sessionId);
   }
   ```

4. 订阅连接状态/收消息统一走事件总线,不用关心底层是 Photon 回调还是 HTTP 轮询在触发:

   ```csharp
   G.Events.Subscribe<EasyFramework.Services.Multiplayer.ConnectionStateChangedEvent>(evt =>
       Debug.Log($"Connection state: {evt.State}"));
   G.Events.Subscribe<EasyFramework.Services.Multiplayer.MultiplayerMessageReceivedEvent>(evt =>
       Debug.Log($"Received on {evt.Channel}: {evt.Payload.Length} bytes"));
   ```

5. 编辑器/单测下不想连真实后端,注册框架自带的 `EasyFramework.Services.Multiplayer.FakeMultiplayerProvider` 代替真实 Provider——它是本地回环:`SendAsync` 会直接原地触发一个 `MultiplayerMessageReceivedEvent`,模拟"自己发的消息自己收到",足够跑通接入联机后的业务流程联调。

同步/异步(实时 vs 回合制)两种模式在契约层面是同一套接口,差异完全在于你注册的 `IMultiplayerProvider` 内部怎么实现——框架不区分这两种模式。
```

- [ ] **Step 2: 检查渲染**

用 `Read` 工具重新读取 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework/Samples~/Template/README.md`,确认新增小节标题层级(`##`)与既有"五、DevTools"一致,代码块语言标注（```csharp）正确闭合,列表编号从 1 开始连续。
预期:文件从"一、复制步骤"到新增的"六、联机(可选模块,按需接入)"共 6 个顶级小节,Markdown 语法无残缺代码块。

- [ ] **Step 3: Commit**

```bash
git add Packages/com.yifei.easyframework/Samples~/Template/README.md
git commit -m "docs: add multiplayer opt-in integration guide to Template sample README"
```

---

## Self-Review

### 1. Spec 覆盖度检查

逐条对照 `/Users/yifei/ClaudeWorkSpace/EasyFrameWork/docs/superpowers/specs/2026-07-05-network-and-multiplayer-design.md`:

- §2.1 `IHttpService` 接口(`HttpMethod`/`HttpRequestOptions`/`HttpException`/`GetAsync`/`PostAsync`)—— Task 1 Step 1,签名逐字符对照 spec 一致。✅
- §2.1 JSON 走 Newtonsoft —— Task 2 `HttpService` 用 `Newtonsoft.Json.JsonConvert`。✅
- §2.2 `IHttpTransport` 接口 —— Task 1 Step 2,签名与 spec 一致。✅
- §2.2 `UnityWebRequestTransport` 真实实现,`HttpService` 重试/超时/反序列化用 `FakeHttpTransport` 完全脱离真实网络单测 —— Task 2(`FakeHttpTransport` 内嵌于 `HttpServiceTests.cs`,覆盖成功/重试/4xx/5xx/重试耗尽)+ Task 3(`UnityWebRequestTransport`,唯一出现 `UnityWebRequest` 的文件)。✅
- §2.3 `IHttpService` 挂 `G.Http` —— Task 4。✅
- §3.2 `IMultiplayerProvider`/`IMultiplayerService` 接口、`ConnectionState`、`ConnectionStateChangedEvent`、`MultiplayerMessageReceivedEvent` —— Task 5 Step 3,签名与 spec 完全一致(含 `IMultiplayerService.ConnectAsync` 的 `ct = default` 默认参数)。✅
- §3.2 `MultiplayerService` 统一转事件供 `IEventBus` 订阅 —— Task 6。✅
- §3.3 不进 `FrameworkInstaller`、不挂 `G`,提供 `FakeMultiplayerProvider` —— Task 5(`FakeMultiplayerProvider` 本地回环)+ Task 7(显式测试断言两个文件均未被侵入)。✅
- §5 验收标准第一条(`IHttpService` 覆盖成功反序列化/超时重试/4xx-5xx/重试耗尽,全通过 `FakeHttpTransport`)—— Task 2 六个测试。✅
- §5 验收标准第二条(`IMultiplayerService` 覆盖连接状态流转事件、`FakeMultiplayerProvider` 回环收发;验证不影响 `FrameworkInstaller`/`G`/现有 137 测试)—— Task 5/6(状态流转+回环)+ Task 7(不侵入验证 + 全量回归)。✅
- §5 验收标准第三条(Template Sample 文档说明,不需要代码)—— Task 8,写入 `Samples~/Template/README.md`(该目录不参与编译,符合"不需要代码"的表述——README 里的代码片段是给人看的示例,不是被编译的产物)。✅
- §6 YAGNI 裁剪(不加 Put/Delete、不自建协议栈、不做二进制协议、不做重连退避策略)—— 计划中未添加任何这些内容,`IMultiplayerProvider`/`IMultiplayerService` 严格按 spec 给定签名实现,未擅自扩展。✅

无遗漏项。

### 2. 占位符扫描

全文搜索 "TBD"、"TODO"、"实现细节后补"、"参照 Task N"（未展开代码）——未发现。Task 3/7/8 中出现的"参照"字样（如"参照 `TemplateGameLifetimeScope.ConfigureGame` 里现有的注册写法风格"）均为文档行文中的风格提示,不是要求读者跳转别处才能获得代码——所有代码块本身都是完整、可直接使用的,不依赖跳转到其他 Task 才能补全。Task 6 中"当前唯一内置 Provider 下的最小实现"一段是对设计取舍的说明而非占位符,后面给出的是已经完整落地的代码。

### 3. 前后 Task 类型/方法名一致性检查

- `HttpMethod`、`HttpRequestOptions`(`Headers`/`TimeoutSeconds`/`RetryCount`)、`HttpException`(`StatusCode`)、`IHttpService.GetAsync<TResponse>`/`PostAsync<TRequest,TResponse>` —— Task 1 定义,Task 2/3/4 均按此签名调用,一致。
- `IHttpTransport.SendAsync(HttpMethod, string, string, HttpRequestOptions)` —— Task 1 定义,Task 2 的 `HttpService` 与 Task 3 的 `UnityWebRequestTransport` 均实现同一签名,一致。
- `HttpService` 构造函数 `HttpService(IHttpTransport transport)` —— Task 2 定义,Task 4 测试 `FrameworkInstaller_RegistersHttpService_ResolvableAsIHttpService` 与 `FrameworkInstaller.Install` 注册均按此构造(VContainer 自动解析单参构造,未引入需要额外工厂 lambda 的歧义构造),一致。
- `G.Http` 属性名 —— Task 4 定义并在 `G_Http_IsExposedAsGHttp` 测试引用,Task 8 文档示例引用同名,一致。
- `ConnectionState`(`Disconnected`/`Connecting`/`Connected`/`Reconnecting`)、`ConnectionStateChangedEvent.State`、`MultiplayerMessageReceivedEvent.Channel`/`.Payload` —— Task 5 定义,Task 6/7/8 引用均一致。
- `IMultiplayerProvider.State`/`ConnectAsync(string, CancellationToken)`/`DisconnectAsync()`/`SendAsync(string, byte[])` —— Task 5 定义,Task 6 `MultiplayerService` 转调、Task 7 反射存在性检查、Task 8 文档示例均一致。
- `IMultiplayerService.State`/`ConnectAsync(string, CancellationToken ct = default)`/`DisconnectAsync()`/`SendAsync(string, byte[])` —— Task 5 定义,Task 6 `MultiplayerService` 实现签名一致,Task 8 文档示例调用 `_multiplayer.ConnectAsync(sessionId)`(省略 `ct`,合法因为有默认值),一致。
- `FakeMultiplayerProvider.MessageReceived` 事件(`event Action<MultiplayerMessageReceivedEvent>`) —— Task 5 定义,Task 6 `MultiplayerService` 构造函数订阅该事件,命名与类型一致。
- `MultiplayerService(IMultiplayerProvider provider, IEventBus events)` 构造函数 —— Task 6 定义,Task 7 测试 `MultiplayerService_*` 系列与 Task 8 文档 DI 注册片段均按此两参构造使用,一致。

未发现前后不一致的命名。

### 4. 修正记录

Self-review 过程中发现并直接修正了三处问题,均已在正文改好,未重新走整个流程:

1. **Task 4 最初草稿的失败测试不会真的失败**:最早的写法只用手写 `ContainerBuilder` + 手动 `builder.Register<IHttpTransport, UnityWebRequestTransport>(...)` 验证 VContainer 注册模式本身可行——但所需类型在 Task 2/3 已经存在,这类测试写出来就是 PASS,不满足 TDD"先写失败测试"的要求,而且没有真正验证 `FrameworkInstaller.Install` 这个具体方法。已改为直接调用 `EasyFramework.FrameworkInstaller.Install(builder, options)`(仿照仓库既有的 `FrameworkInstallerTests.cs` 的 `Build()`/`MakeOptions()` 模式),并新增 `G_Http_IsExposedAsGHttp` 测试引用 `EasyFramework.G.Http`——这个成员在 Task 4 实现之前必定编译失败,才是真正的红灯。
2. **Task 7 最初草稿用了未经代码库验证的 `IObjectResolver.TryResolve` API**:本仓库现有测试文件(`FrameworkInstallerTests.cs`、`EventBusTests.cs`)里从未出现过 `TryResolve` 调用,该 API 形状无法从代码库中直接核实,属于凭记忆猜测 VContainer API,与"不能凭记忆或猜测"的要求冲突。已改为 `Assert.Throws<VContainer.VContainerException>(() => container.Resolve<T>())`——这个异常类型和触发方式已经在既有计划文档 `docs/superpowers/plans/2026-06-12-easyframework-phase3a-ui.md`("`VContainerException : Failed to resolve ...`")与 `docs/superpowers/plans/2026-07-05-content-update-service.md`("`VContainer.VContainerException: ... is not registered`")里被证实是这个代码库解析未注册类型时的真实运行时行为,是有代码库证据支撑的写法。
3. **Task 4/Task 7 的 `FrameworkOptions`/`ContainerBuilder` 用法对齐仓库既有测试**:两处测试最终都改为直接构造真实 `EasyFramework.FrameworkOptions`(仅填必填的 `SaveDirectory`,用临时目录 + GUID 避免测试间冲突)后调用真实 `FrameworkInstaller.Install`,与 `FrameworkInstallerTests.cs` 的 `MakeOptions()`/`Build()` 写法风格一致,而不是自造一个和生产注册路径脱节的迷你容器。
