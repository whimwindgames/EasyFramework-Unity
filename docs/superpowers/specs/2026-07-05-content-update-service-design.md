# EasyFramework 资源热更新服务设计文档

- 日期:2026-07-05
- 状态:待所有者确认
- 前置:MyFramework 架构对比研究;与所有者确认的 Apple App Store 合规评估结论——代码热更新(HybridCLR 类)因违反 Guideline 2.5.2、且存在账号级封禁风险,明确不做;资源/内容更新是苹果认可的标准做法,值得做

## 1. 目标与背景

给 EasyFramework 加一个轻量的资源热更新能力,让游戏在不经应用商店审核的前提下更新美术、关卡数据、配置这类"内容"。基于 Unity Addressables 官方自带的 Remote Content Update 机制包一层服务,不做 MyFramework 那种从零手写的 AssetBundle 版本比对 + 断点续传下载系统——那一整套是 Addressables 出现之前的产物,现在直接用官方机制即可。

## 2. 设计

### 2.1 服务接口

```csharp
namespace EasyFramework.Services.ContentUpdate
{
    public readonly struct ContentUpdateInfo
    {
        public readonly bool IsAvailable;
        public readonly long EstimatedDownloadSizeBytes; // 无法预估时为 -1
    }

    public readonly struct ContentAvailableEvent { public readonly ContentUpdateInfo Info; }
    public readonly struct ContentUpdateAppliedEvent { }
    public readonly struct ContentUpdateFailedEvent { public readonly string Reason; }

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

### 2.2 Addressables 网关(测试替身用)

`Addressables.CheckForCatalogUpdates()` / `Addressables.UpdateCatalogs()` 是静态 API,EditMode 测试没法真的跑一个远程 catalog,加一层薄接口把它们包起来:

```csharp
public interface IAddressablesCatalogGateway
{
    UniTask<List<string>> CheckForCatalogUpdatesAsync();
    UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress);
}
```

真实实现 `AddressablesCatalogGateway` 直接转发到 Addressables 静态 API;测试用 `FakeAddressablesCatalogGateway` 模拟"有更新/无更新/下载失败"几种场景,让 `ContentUpdateService` 的业务逻辑(有更新才发事件、下载失败发失败事件、避免重复检查)完全脱离真实 Addressables 单测。

### 2.3 启动集成

`ContentUpdateBootTask : IBootTask`(`IsCritical = false`,`Priority` 排在 Asset/Config 之后)在启动时静默调用一次 `CheckAsync()`。**不自动下载**——检测到更新只发布 `ContentAvailableEvent`,是否立即下载、仅 WiFi 下载、提示用户,这类策略决定权在具体游戏的业务层(订阅事件后自己决定何时调用 `DownloadAndApplyAsync`)。这一点和 Config/Ads 等"检测到就自动处理"的现有服务不同,是刻意的产品决策留白——框架不替业务做主。

### 2.4 挂载点

`IContentUpdateService` 挂 `G.ContentUpdate`——所有游戏默认都有检查能力,哪怕业务层选择完全不处理这个事件也不影响其他功能。

## 3. 边界

- 只做 Addressables 目录/资源层面的内容更新,不涉及任何代码逻辑热更。
- 只提供"检查/下载/应用"三个动作和三个事件,不做 MyFramework 式的"大版本/小版本"强制策略判断——是否需要区分"必须强制更新的破坏性内容变更"由业务层自己判断(可以配合 `IConfigService` 加一个"最低内容版本号"字段,这个字段的具体设计留到有真实需求时再做,本设计不预设)。
- 不做下载进度 UI——`IProgress<float>` 回调交给业务层自己接进度条。

## 4. 验收标准

- EditMode 测试(全部通过 `FakeAddressablesCatalogGateway` 完成,不发起真实网络请求):
  - 有更新可下载 → `CheckAsync` 返回 `IsAvailable=true` 且发布 `ContentAvailableEvent`
  - 下载成功 → 发布 `ContentUpdateAppliedEvent`
  - 下载失败 → 发布 `ContentUpdateFailedEvent`,`DownloadAndApplyAsync` 返回 `false` 且不抛异常(非关键路径,不应导致游戏崩溃)
- Template 或 TapRush Sample 补一个最小接入示例:订阅 `ContentAvailableEvent` 后调用 `DownloadAndApplyAsync`,用于在真实 Addressables Remote Group 场景下端到端手动验证(需要实际配置远程 Profile,属于实现阶段的手动验证项,不在自动化测试范围内)。

## 5. YAGNI 裁剪

- 不做资源版本号比对系统(MyFramework 的 AssetVersionSystem)——Addressables 自带的 catalog hash 比对已经够用。
- 不做断点续传/下载失败重试队列——Addressables 内容更新通常是整批目录级操作,失败了整体重试即可;重试策略(几次、间隔多久)留给业务层根据 `DownloadAndApplyAsync` 的返回值自己决定,不在服务内部硬编码。
- 不做"大版本/小版本"分级——这是产品决策,不是通用框架能力,真的需要时业务层基于 `IConfigService` 自己实现,不预先在框架里加这个概念。
