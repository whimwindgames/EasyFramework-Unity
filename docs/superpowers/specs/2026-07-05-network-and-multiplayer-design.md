# EasyFramework 网络基础层与联机适配层设计文档

- 日期:2026-07-05
- 状态:待所有者确认
- 前置:MyFramework 架构对比研究;与所有者确认的方向——网络层只做"接口 + 适配器"基础设施,不照抄 MyFramework 自建协议栈;联机分同步/异步两种模式,均由具体游戏接第三方方案实现,框架只定义契约,且只有部分游戏需要(不进核心门面)

## 1. 目标与背景

覆盖三类网络需求:

- **A. 热更新/配置的请求-响应**——已被资源热更新服务(见同批次另一份设计文档)和现有 `IConfigService` 覆盖大部分,这里补的是通用 HTTP 基础设施。
- **B. 排行榜/云存档/社交/内购服务器校验回调等后端功能**——本设计**只提供通用 HTTP 基础设施**,不预先设计具体的 `ILeaderboardService` 等接口。现在没有具体后端选型和具体游戏需求,预先设计的接口形状大概率猜不对;等某个具体游戏真的要接某个后端时,再用同样的接口 + 适配器模式单独加。
- **C. 少数游戏需要的实时/异步联机**——框架只定义连接生命周期契约和 Editor Fake,不内置任何传输实现。

## 2. 网络基础层:IHttpService

### 2.1 接口

```csharp
namespace EasyFramework.Services.Network
{
    public enum HttpMethod { Get, Post } // Put/Delete 目前没有具体需求驱动,真正用到时再加

    public sealed class HttpRequestOptions
    {
        public IReadOnlyDictionary<string, string> Headers;
        public int TimeoutSeconds = 10;
        public int RetryCount = 2;
    }

    public sealed class HttpException : Exception
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

JSON 序列化走已有的 Newtonsoft 依赖,和 `IConfigService`/`ISaveService` 保持一致的技术选型,不引入第二套序列化库。

### 2.2 可测试性

`UnityWebRequest` 不能在 EditMode 测试里真正发起网络请求。加一层薄 transport 接口:

```csharp
public interface IHttpTransport
{
    UniTask<(long statusCode, string body)> SendAsync(HttpMethod method, string url, string jsonBody, HttpRequestOptions options);
}
```

真实实现 `UnityWebRequestTransport` 走真实网络;`HttpService` 的重试/超时/反序列化逻辑通过注入 `FakeHttpTransport`(可编程返回值/模拟超时/模拟 4xx-5xx)完全脱离真实网络单测。

### 2.3 挂载点

`IHttpService` 挂 `G.Http`——通用网络基础设施,所有游戏都可能用到(哪怕只是业务层自己拼一个 API 调用),不需要为每个具体后端功能单独加服务才能用上网络能力。

## 3. 联机适配层:IMultiplayerService

### 3.1 设计原则

同步(实时)和异步(回合制)两种模式在**契约层面是同一套**——差异完全在于具体游戏注册的 `IMultiplayerProvider` 内部怎么实现(异步 Provider 内部可能就是走 §2 的 `IHttpService` 轮询;实时 Provider 内部接 Photon/Mirror 等第三方 SDK)。框架不对"这是同步还是异步"做任何区分处理。

### 3.2 接口

```csharp
namespace EasyFramework.Services.Multiplayer
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }

    public readonly struct ConnectionStateChangedEvent { public readonly ConnectionState State; }
    public readonly struct MultiplayerMessageReceivedEvent { public readonly string Channel; public readonly byte[] Payload; }

    /// <summary>SDK 适配点。具体游戏在自己的 GameLifetimeScope 里注册真实实现;默认 FakeMultiplayerProvider(本地回环)。</summary>
    public interface IMultiplayerProvider
    {
        ConnectionState State { get; }
        UniTask ConnectAsync(string sessionId, CancellationToken ct);
        UniTask DisconnectAsync();
        UniTask SendAsync(string channel, byte[] payload);
    }

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

`MultiplayerService`(`IMultiplayerService` 的默认实现)在 `IMultiplayerProvider` 之上加统一的事件发布,业务层订阅 `IEventBus` 即可,不用关心底层是 Photon 回调还是 HTTP 轮询在触发。

### 3.3 关键设计决定:不进 FrameworkInstaller、不挂 G

这是 EasyFramework 里第一个"可选模块",和其他 14 个"每个游戏都有"的服务不同:

- **不在 `FrameworkInstaller.Install` 里注册** `IMultiplayerProvider` / `IMultiplayerService`。
- **不出现在 `G` 门面里**。`G` 的契约是"`BootCompletedEvent` 后一定可用",给不需要联机的游戏也挂一个可能是 null 的 `G.Multiplayer`,会破坏这个契约、引入没必要的判空心智负担。
- 需要联机的具体游戏,在自己的 `GameLifetimeScope` 里注册 `IMultiplayerProvider` 的真实实现(比如 `PhotonMultiplayerProvider`)和 `MultiplayerService`,业务代码走**构造函数注入** `IMultiplayerService`,不经过 `G`。
- 框架包内提供 `FakeMultiplayerProvider`(本地回环——`SendAsync` 直接原地触发一个 `MultiplayerMessageReceivedEvent`,模拟"自己发的消息自己收到")供接入联机的游戏在编辑器/单测下开发调试用,不需要连真实后端。

这个"可选模块,由具体游戏自己注册,不进核心门面"的模式是框架里第一次出现,之后如果还要加其他非通用能力,可以照此复用。

## 4. 边界

- 不实现任何具体的实时同步协议(状态同步、客户端预测、回滚)——这是 Photon/Mirror 这类专门方案的领域,框架只提供接入的"插槽"。
- 不实现任何具体的异步回合制协议——如果某个游戏走异步模式,`IMultiplayerProvider` 的具体实现直接用 §2 的 `IHttpService` 对接自选后端(Firebase/PlayFab/自建 API 都可以),框架不预设服务端形态。
- 不提供 `ILeaderboardService`/`ICloudSaveService`/`ISocialService` 等具体后端功能接口——等真实游戏需求出现时按同样模式单独加。

## 5. 验收标准

- `IHttpService`:EditMode 测试覆盖成功响应反序列化、超时重试、4xx/5xx 抛 `HttpException`、`RetryCount` 用尽后的最终失败路径,全部通过 `FakeHttpTransport` 完成,不发起真实网络请求。
- `IMultiplayerService`:EditMode 测试覆盖连接状态流转事件、`FakeMultiplayerProvider` 回环收发;并验证"没有注册具体 Provider 的游戏,`FrameworkInstaller`/`G` 均不受影响,现有 137 个测试不受影响"。
- Template Sample 补一份文档说明(不需要代码):如果某个新游戏需要联机,在自己的 `GameLifetimeScope` 里怎么接一个真实 `IMultiplayerProvider`,给后续实际做联机游戏时的团队成员参考。

## 6. YAGNI 裁剪

- `IHttpService` 只暴露 `GetAsync`/`PostAsync`,不预先加 `PutAsync`/`DeleteAsync`——目前没有具体需求用到,真正需要时再连同 `HttpMethod` 枚举一起扩展。
- 不自建 TCP/UDP/WebSocket 协议栈或配套服务器(MyFramework 路线)——之前的架构对比已详细论证过,这条路径对小团队多小游戏的定位不划算。
- 不做消息二进制协议/位级序列化——`byte[] Payload` 留给具体游戏自己决定序列化格式(JSON/MessagePack/Protobuf 都可以),由接入的 Provider 决定,框架不强制。
- 不做断线重连的具体退避策略——`ConnectionState.Reconnecting` 这个状态本身由框架定义,但重连时机/退避算法由具体 Provider 实现决定,不同后端(Photon vs 自建 HTTP 轮询)重连策略差异很大,框架层不该假设。
