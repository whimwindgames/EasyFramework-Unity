# EasyFramework Phase 4(商业化层:Ads / IAP / Analytics)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase 1 骨架 + Phase 2 资源数据层 + Phase 3 表现层之上,实现商业化层三大模块——广告(奖励/插屏/横幅 + 插屏频控 + 全链路打点)、内购(商品目录 SO + 已购持久化 + 掉单补发 + 恢复购买 + 打点)、数据统计(多后端广播 + 异常隔离 + 框架自动标准事件),并接线进 `FrameworkInstaller` / `G` 门面 / `FrameworkOptions` / `RootLifetimeScope`。全部业务逻辑(频控、冷却、掉单队列、已购集合、广播隔离、自动事件)均以纯逻辑/Fake 隔离 Unity 与 SDK 依赖,带 EditMode 单测。

**Architecture:** 沿用「VContainer DI 内核 + 静态门面 `G`」。所有新服务落在 `EasyFramework.Monetization` 程序集,命名空间 `EasyFramework.Monetization.{Ads|IAP|Analytics}`,目录与命名空间一一对应。分层规则:**Monetization → Services → Core**(本层允许依赖 Services,以便注入 `IConfigService` 取频控参数、订阅 `BootCompletedEvent`/`SceneLoadedEvent` 打自动事件)。服务一律先接口后实现:`IAdsService`/`IIAPService`/`IAnalyticsService` 为业务层入口(经 `G`),`IAdsProvider`/`IIAPProvider`/`IAnalyticsBackend` 为 SDK 适配点。所有 SDK/平台/Unity 静态依赖(广告 SDK、Unity IAP、PlayerPrefs、`Time.realtimeSinceStartup`)抽到边界:业务逻辑(频控、冷却、掉单队列、已购集合、多后端广播)在纯 `ContainerBuilder`(无 MonoBehaviour、无场景、无真实 SDK)下可 Build、可 EditMode 单测,时间源抽 `Func<float>` 便于单测。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / **Unity IAP(com.unity.purchasing,最新稳定版)** / Unity Test Framework 1.6

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包 `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`、API 核对 `unity_reflect`)。
- **并行实现代理只允许用 Write/Edit 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(Fake Provider 的所有 `UniTask` 均同步返回;`FakeAdsProvider` 默认延迟在测试里设为 0 以同步完成),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。
- **PlayerPrefs 测试污染:** IAP 的已购/掉单持久化测试会写 `PlayerPrefs`(`ef.iap.owned`、`ef.iap.pending`)。**所有触碰 PlayerPrefs 的 fixture 必须 `[SetUp]` 删除这两个键、`[TearDown]` 再删一次并 `PlayerPrefs.Save()`**,避免跨用例 / 跨机污染。具体见 IAP Task 的测试代码。

**⚠️ Task 6(接线)前置要求(必读):** Task 6 要修改 Phase 2 / Phase 3b 落地的三个文件 `Boot/G.cs`、`Boot/FrameworkInstaller.cs`、`Boot/RootLifetimeScope.cs`,以及 `Boot/EasyFramework.Boot.asmdef`(需新增对 `EasyFramework.Monetization` 的引用)。**Phase 2 / Phase 3 正由其它组代理实现中。执行 Task 6 前,实现代理必须先 `Read` 这三个文件的实际落地内容核对**(`G` 已有的属性集、`FrameworkInstaller.Install(builder, options)` 的实际签名与已注册服务、`FrameworkOptions` 的已有字段、`RootLifetimeScope.Configure` 的 build callback / entrypoint 写法)。本计划给出的是「在实际基线上增量合并所需的新增片段 + 合并后完整文件」,但若实际文件与 Phase 2/3 计划有偏差,**以实际文件为基线做等效增量合并,不要机械覆盖**(只新增 Ads/IAP/Analytics 三组属性、三组注册、`ProductCatalog` 字段,保留其余既有内容)。

---

## ⚠️ 偏差声明区(执行前必读)

### 偏差 1:真实广告 / Firebase SDK 本阶段不接入,只交付接口 + 业务层 + Fake + 接入槽

**设计规格 §5.1 / §5.3** 要求广告聚合 SDK(LevelPlay 或 AdMob)与 Firebase Analytics 后端。本阶段**主动偏离**真实 SDK 接入:

- **理由:** AdMob / LevelPlay 与 Firebase 的运行时依赖**开发者账号、App ID、平台密钥、原生插件导入与编辑器侧配置**,这些在**无人值守 agent-team 模式**下无法获取也无法可靠配置;强行导入会引入无法编译/无法测试的原生依赖,风险显著高于收益。
- **本阶段决定:** 交付**全部接口 + 完整业务逻辑层(频控、冷却、掉单补发、多后端打点广播、自动标准事件)+ Fake Provider(编辑器/测试用)**,业务逻辑全部 EditMode 单测覆盖。真实广告 SDK 与 Firebase 后端**只留接入槽**(`AdMobAdsProvider`/`FirebaseAnalyticsBackend` 各一个 Stub 文件),用 `#if EF_ADMOB` / `#if EF_FIREBASE` 编译符号包住空实现,并在文件头注明**这不是占位符,而是设计好的接入槽**——接入时定义对应 define、填充已写好签名的方法体即可,业务代码、`IAdsService`/`IAnalyticsService` 接口、Fake 与测试零改动。
- **扩展点(留作后续接入):** `IAdsProvider` / `IAnalyticsBackend` 接口签名锁定不变(见下方「锁定契约」)。接 AdMob:定义 `EF_ADMOB`,在 `AdMobAdsProvider` 填充 SDK 调用,`GameLifetimeScope` 覆盖注册 `IAdsProvider → AdMobAdsProvider` 即可;接 Firebase:定义 `EF_FIREBASE`,在 `FirebaseAnalyticsBackend` 填充 `FirebaseAnalytics.LogEvent`,在 `GameLifetimeScope` 追加注册一个 `IAnalyticsBackend`(多注册,见 Analytics Task)。本偏差不锁死规格目标,只推迟其原生 SDK 依赖。

### 偏差 2:Unity IAP 走官方包,真实现仅真机路径;编辑器 / 测试走 Fake

`com.unity.purchasing`(Unity IAP)是**官方包,可在无人值守下安装**,故本阶段**真实安装并写 `UnityIAPProvider` 真实现**。但:

- `UnityIAPProvider` 内部依赖 `UnityPurchasing.Initialize` 与 `IStoreController` 回调,这套链路**只在真机(配置好 IAP catalog 与商店密钥)上可跑通**,EditMode 无法稳定驱动其异步回调。
- **本阶段决定:** `UnityIAPProvider` 按真实 Unity IAP API 书写(验证代理用 `unity_reflect` 核对 `com.unity.purchasing` 实际类型成员),标注**仅真机路径,不写 EditMode 单测**;编辑器与全部 EditMode 测试走 `FakeIAPProvider`。`FrameworkInstaller` 在 `#if UNITY_EDITOR` 下注册 `FakeIAPProvider`,真机分支注册 `UnityIAPProvider`(接入槽位置带注释)。`UnityIAPProvider` 的 Unity IAP API 名/签名以安装到工程的版本为准,若有出入按实际签名等效调整(`IIAPProvider` 接口不变)。这不是占位符,而是有真实现的真机路径。

### 偏差 3:Provider 真实现(Ads / IAP 真机路径)不写 EditMode 单测

`FakeAdsProvider`(编辑器模拟)走 EditMode 全覆盖;`AdMobAdsProvider`(接入槽,空实现)与 `UnityIAPProvider`(真机路径)因依赖 SDK/原生回调,**按 spec/任务书明确许可不写 EditMode 单测**(可测的频控/冷却/掉单/广播逻辑已全部抽到 `AdsService`/`IAPService`/`AnalyticsService` 业务层单独测)。这不是占位符,是被许可的延后,真机冒烟推迟到 Phase 5 示例游戏。

---

## 锁定契约(签名不可改,跨 Task 一致)

```csharp
namespace EasyFramework.Monetization.Ads
{
    public enum AdResult { Completed, Skipped, NotReady, Failed }
    public enum BannerPosition { Top, Bottom }
    public interface IAdsProvider   // SDK 适配点
    {
        Cysharp.Threading.Tasks.UniTask InitializeAsync(System.Threading.CancellationToken ct);
        bool IsRewardedReady { get; }
        Cysharp.Threading.Tasks.UniTask<AdResult> ShowRewardedAsync(string placement);
        Cysharp.Threading.Tasks.UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }
    public interface IAdsService    // 业务层入口(G.Ads)
    {
        bool IsRewardedReady { get; }
        Cysharp.Threading.Tasks.UniTask<AdResult> ShowRewardedAsync(string placement);
        Cysharp.Threading.Tasks.UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }
}
namespace EasyFramework.Monetization.IAP
{
    public enum ProductType { Consumable, NonConsumable }
    public sealed class PurchaseResult
    {
        public bool Success;
        public string ProductId;
        public string FailureReason;   // 取消="cancelled", 未初始化="not_initialized" 等
    }
    public interface IIAPProvider   // SDK 适配点(Unity IAP / Fake)
    {
        Cysharp.Threading.Tasks.UniTask InitializeAsync(System.Collections.Generic.IReadOnlyList<string> productIds, System.Threading.CancellationToken ct);
        bool IsInitialized { get; }
        Cysharp.Threading.Tasks.UniTask<PurchaseResult> PurchaseAsync(string productId);
        Cysharp.Threading.Tasks.UniTask RestoreAsync();
    }
    public interface IIAPService    // 业务层入口(G.IAP)
    {
        Cysharp.Threading.Tasks.UniTask<PurchaseResult> PurchaseAsync(string productId);
        Cysharp.Threading.Tasks.UniTask RestoreAsync();
        bool IsOwned(string productId);
    }
}
namespace EasyFramework.Monetization.Analytics
{
    public interface IAnalyticsBackend
    {
        void Track(string eventName, System.Collections.Generic.IReadOnlyDictionary<string, object> parameters);
        void SetUserProperty(string key, string value);
    }
    public interface IAnalyticsService   // G.Analytics
    {
        void Track(string eventName);
        void Track(string eventName, params (string key, object value)[] parameters);
        void SetUserProperty(string key, string value);
    }
}
```

---

## 文件结构总览

```
Packages/manifest.json                                  (修改:新增 com.unity.purchasing)
Packages/packages-lock.json                             (由 PM 写入)
Assets/EasyFramework/
├── Monetization/
│   ├── EasyFramework.Monetization.asmdef               (修改:references 升级 Core/Services/UniTask/VContainer/Unity IAP)
│   ├── AssemblyInfo.cs                                 (新增/替换占位:InternalsVisibleTo Tests + Boot)
│   ├── Ads/
│   │   ├── IAdsService.cs                              (IAdsProvider + IAdsService + AdResult + BannerPosition)
│   │   ├── AdsService.cs                               (业务层:频控 + 冷却 + 打点,纯逻辑可测)
│   │   ├── FakeAdsProvider.cs                          (编辑器/测试 provider)
│   │   ├── AdMobAdsProvider.cs                         (#if EF_ADMOB 接入槽 Stub)
│   │   └── AdsBootTask.cs                              (Priority=30, IsCritical=false)
│   ├── IAP/
│   │   ├── IIAPService.cs                              (IIAPProvider + IIAPService + ProductType + PurchaseResult)
│   │   ├── ProductCatalog.cs                           (ScriptableObject:条目 id/type/奖励描述)
│   │   ├── IAPService.cs                               (业务层:已购持久化 + 掉单补发 + 打点)
│   │   ├── FakeIAPProvider.cs                          (可配成功/取消/失败)
│   │   ├── UnityIAPProvider.cs                         (真实现,仅真机路径,不单测)
│   │   └── IAPBootTask.cs                              (Priority=31, IsCritical=false,重放掉单)
│   └── Analytics/
│       ├── IAnalyticsService.cs                        (IAnalyticsBackend + IAnalyticsService)
│       ├── AnalyticsService.cs                         (多后端广播 + 异常隔离,纯逻辑可测)
│       ├── DebugAnalyticsBackend.cs                    (编辑器 Debug.Log)
│       ├── AnalyticsAutoTracker.cs                     (IStartable + IDisposable,订阅自动事件)
│       └── FirebaseAnalyticsBackend.cs                 (#if EF_FIREBASE 接入槽 Stub)
├── Boot/
│   ├── EasyFramework.Boot.asmdef                       (修改:references 追加 EasyFramework.Monetization)
│   ├── G.cs                                            (修改:新增 Ads/IAP/Analytics 3 属性)
│   ├── FrameworkInstaller.cs                           (修改:FrameworkOptions 加 ProductCatalog + 注册 Monetization)
│   └── RootLifetimeScope.cs                            (修改:options 传 ProductCatalog;Inspector 暴露)
└── Tests/EditMode/
    ├── EasyFramework.Tests.EditMode.asmdef             (修改:追加 EasyFramework.Monetization + Unity.Purchasing)
    ├── AdsServiceTests.cs
    ├── IAPServiceTests.cs
    ├── AnalyticsServiceTests.cs
    └── FrameworkInstallerTests.cs                      (修改:扩展断言 3 个新服务 + G 绑定)
```

依赖方向:`Tests → Boot → {Core, Services, Monetization}`;`Monetization → {Services, Core}`;`Services → Core`。Phase 4 只在 `Monetization` 内新增三个目录,并修改 `Boot` 的三个文件与两个 asmdef。

---

### Task 1: 包安装 + asmdef / AssemblyInfo 升级(串行,使用 UnityMCP)

> 本 Task 是后续全部 Task 的前置(`UnityIAPProvider`/`FakeIAPProvider` 测试要引用 `UnityEngine.Purchasing`;Monetization 要引用 Services)。先落这一步。

**Files:**
- Modify: `Packages/manifest.json`(由 Unity Package Manager 写入)
- Modify: `Assets/EasyFramework/Monetization/EasyFramework.Monetization.asmdef`
- Create/Replace: `Assets/EasyFramework/Monetization/AssemblyInfo.cs`
- Modify: `Assets/EasyFramework/Boot/EasyFramework.Boot.asmdef`
- Modify: `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

- [ ] **Step 1: 查最新稳定版并安装 Unity IAP**

用 `manage_packages` 查询 `com.unity.purchasing` 在当前 Unity 6000.3.15f1 下的最新稳定版(若工具支持 `search`/`list_versions`,先查到具体 `x.y.z` 再装;否则 `add_package` 不带版本号解析为 manifest 兼容推荐版)。Unity IAP 首次导入会生成配置资产并可能要求在 `Services > In-App Purchasing` 启用(无人值守下可跳过启用,仅需程序集可用)。

```
com.unity.purchasing   (最新稳定版,由 manage_packages 解析)
```

- [ ] **Step 2: 验证编译干净**

轮询 `mcpforunity://editor/state` 至 `is_compiling == false`,然后 `read_console(types=["error"])`。Expected: 0 errors。

- [ ] **Step 3: 核对 Unity IAP 程序集名(验证代理)**

Unity IAP 公开程序集名随版本不同(常见 `Unity.Purchasing`,旧版含 `UnityEngine.Purchasing`/`Stores` 等)。验证代理用 `unity_reflect` 或查 `Packages/com.unity.purchasing/**/​*.asmdef` 确认**实际程序集名**,Step 4/5/6 的 asmdef references 中的 Unity IAP 条目以核对结果为准(本计划按 `Unity.Purchasing` 书写,若不符按实际替换)。

- [ ] **Step 4: 升级 Monetization asmdef** — `Assets/EasyFramework/Monetization/EasyFramework.Monetization.asmdef`

Phase 1 中此 asmdef 为占位(references 可能仅 Core/UniTask/VContainer/MessagePipe)。完整替换为(`EasyFramework.Services` 用于注入 `IConfigService`、订阅 `SceneLoadedEvent`;`Unity.Purchasing` 用于 `UnityIAPProvider`,以 Step 3 核对名为准):

```json
{
  "name": "EasyFramework.Monetization",
  "rootNamespace": "EasyFramework.Monetization",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "UniTask",
    "VContainer",
    "MessagePipe",
    "Unity.Purchasing"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true,
  "defineConstraints": [],
  "noEngineReferences": false
}
```

> 说明:`UnityIAPProvider` 用 `#if` 不包裹(Unity IAP 是已安装的官方包,程序集恒在),但若 Step 3 发现该工程未启用 IAP 导致 `Unity.Purchasing` 不可用,验证代理可临时给 `UnityIAPProvider.cs` 加 `#if UNITY_PURCHASING` 包裹(Unity IAP 启用时自动定义该符号),并从 asmdef references 去掉 `Unity.Purchasing` 以保证其余文件编译——此为环境兜底,不改 `IIAPProvider` 契约。

- [ ] **Step 5: Monetization AssemblyInfo** — `Assets/EasyFramework/Monetization/AssemblyInfo.cs`

替换 Phase 1 占位注释为(`FakeAdsProvider`/`FakeIAPProvider`/`AdsService` 等内部成员对测试可见;`UnityIAPProvider` 等 internal 注册类对 Boot 可见):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EasyFramework.Tests.EditMode")]
[assembly: InternalsVisibleTo("EasyFramework.Boot")]
```

- [ ] **Step 6: 升级 Boot asmdef** — `Assets/EasyFramework/Boot/EasyFramework.Boot.asmdef`

**先 Read 实际文件**。Phase 1 计划中 Boot 已 references `EasyFramework.Monetization`(见 Phase 1 Task 2 Step 3),若实际已含则**无需改动**,仅核对;若实际缺失则增量追加 `"EasyFramework.Monetization"`。合并后 references 应至少含:`EasyFramework.Core`、`EasyFramework.Services`、`EasyFramework.Monetization`、`UniTask`、`VContainer`、`MessagePipe`、`MessagePipe.VContainer`。

- [ ] **Step 7: 升级 Tests asmdef** — `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

**先 Read 实际文件(Phase 2/3 已扩展)**,在其上增量追加 `EasyFramework.Monetization` 与 `Unity.Purchasing`(测试要 new `FakeAdsProvider`/`FakeIAPProvider`、引用 `EasyFramework.Monetization.*` 类型;`Unity.Purchasing` 仅在测试需要其类型时才需,本计划测试只用 Fake 故 `Unity.Purchasing` 可选——但加上无害,便于将来真机测试)。合并后 references(以实际基线为准增量,示例):

```json
{
  "name": "EasyFramework.Tests.EditMode",
  "rootNamespace": "EasyFramework.Tests",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "EasyFramework.Monetization",
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

> 注:上方 references 含 Phase 2/3 已加的条目(Addressables/ResourceManager/Newtonsoft 等)仅为示例完整性;实现代理**以实际文件为基线只增量加 `EasyFramework.Monetization`**(`Unity.Purchasing` 本阶段测试不直接引用,可不加,避免未启用 IAP 时编译失败)。

- [ ] **Step 8: 验证编译干净**(验证代理:`refresh_unity` → 轮询 editor/state → `read_console(types=["error"])`)。Expected: 0 errors(此时 Monetization 内还无业务代码,只验证 asmdef 引用图正确)。

- [ ] **Step 9: Commit**(编排者执行)

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/EasyFramework/Monetization Assets/EasyFramework/Boot/EasyFramework.Boot.asmdef Assets/EasyFramework/Tests
git commit -m "feat: add Unity IAP package and monetization asmdef references"
```

---

### Task 2: Ads 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Monetization/Ads/IAdsService.cs`, `AdsService.cs`, `FakeAdsProvider.cs`, `AdMobAdsProvider.cs`, `AdsBootTask.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/AdsServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `AdsServiceTests.cs`

用 `FakeAdsProvider`(可配 `NextResult`/`IsRewardedReady`,测试里把延迟设为 0 同步完成)、`FakeAnalytics`(收集打点序列)、Fake `IConfigService`(返回固定频控参数)、可注入的时间源 `Func<float>`。覆盖:插屏冷却拦截(第二次在冷却内返回 `NotReady` 且不打 `ad_show`)、`Completed` 才刷新冷却、Provider 未就绪→`NotReady` 且不计冷却、打点序列(`ad_request`→`ad_show`→`ad_complete` 带 placement/type)、奖励视频结果透传、关卡间隔门禁。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Configs;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class AdsServiceTests
    {
        // ---- Fakes ----
        sealed class FakeConfig : IConfigService
        {
            readonly Dictionary<string, object> _values = new();
            public void Set(string key, object value) => _values[key] = value;
            public T Get<T>(string key, T defaultValue)
                => _values.TryGetValue(key, out var v) ? (T)v : defaultValue;
            public bool Has(string key) => _values.ContainsKey(key);
        }

        sealed class FakeAnalytics : IAnalyticsService
        {
            public readonly List<(string name, Dictionary<string, object> p)> Events = new();
            public void Track(string eventName) => Events.Add((eventName, new Dictionary<string, object>()));
            public void Track(string eventName, params (string key, object value)[] parameters)
            {
                var d = new Dictionary<string, object>();
                foreach (var (k, val) in parameters) d[k] = val;
                Events.Add((eventName, d));
            }
            public void SetUserProperty(string key, string value) { }
            public List<string> Names()
            {
                var n = new List<string>();
                foreach (var e in Events) n.Add(e.name);
                return n;
            }
        }

        static (AdsService svc, FakeAdsProvider prov, FakeAnalytics an, FakeConfig cfg, float[] clock)
            Build(float cooldown = 30f, int minGap = 1)
        {
            var prov = new FakeAdsProvider { Delay = 0f, IsRewardedReady = true, NextResult = AdResult.Completed };
            var an = new FakeAnalytics();
            var cfg = new FakeConfig();
            cfg.Set("ads.interstitial_cooldown", cooldown);
            cfg.Set("ads.interstitial_min_gap", minGap);
            var clock = new[] { 0f };
            var svc = new AdsService(prov, cfg, an, () => clock[0]);
            return (svc, prov, an, cfg, clock);
        }

        [Test]
        public void Interstitial_FirstShow_RunsAndTracksFullChain()
        {
            var (svc, _, an, _, _) = Build();
            var r = svc.ShowInterstitialAsync("level_end").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r);
            CollectionAssert.AreEqual(new[] { "ad_request", "ad_show", "ad_complete" }, an.Names());
            Assert.AreEqual("level_end", an.Events[0].p["placement"]);
            Assert.AreEqual("interstitial", an.Events[0].p["type"]);
        }

        [Test]
        public void Interstitial_WithinCooldown_ReturnsNotReadyAndSkipsShow()
        {
            var (svc, _, an, _, clock) = Build(cooldown: 30f, minGap: 0);
            svc.ShowInterstitialAsync("a").GetAwaiter().GetResult(); // t=0, completes, cooldown set
            clock[0] = 10f; // still within 30s cooldown
            an.Events.Clear();
            var r = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r);
            CollectionAssert.DoesNotContain(an.Names(), "ad_show", "冷却内不应展示");
        }

        [Test]
        public void Interstitial_AfterCooldown_ShowsAgain()
        {
            var (svc, _, _, _, clock) = Build(cooldown: 30f, minGap: 0);
            svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            clock[0] = 31f; // past cooldown
            var r = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r);
        }

        [Test]
        public void Interstitial_NotReady_DoesNotConsumeCooldown()
        {
            var (svc, prov, an, _, clock) = Build(cooldown: 30f, minGap: 0);
            prov.NextResult = AdResult.NotReady;
            var r1 = svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r1);
            // cooldown 未刷新,下次(同一时刻)Completed 应能展示
            prov.NextResult = AdResult.Completed;
            var r2 = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r2, "NotReady 不计冷却,后续可立即展示");
            // NotReady 一路只打 ad_request(无 ad_show/ad_complete)
            Assert.AreEqual("ad_request", an.Events[0].name);
        }

        [Test]
        public void Interstitial_MinGap_BlocksBeforeEnoughCalls()
        {
            // min_gap=2:每 2 次 ShowInterstitial 才允许一次真实展示
            var (svc, _, _, _, clock) = Build(cooldown: 0f, minGap: 2);
            var r1 = svc.ShowInterstitialAsync("a").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r1, "首次满足间隔(计数从允许态起)");
            var r2 = svc.ShowInterstitialAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r2, "间隔未到");
            var r3 = svc.ShowInterstitialAsync("c").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Completed, r3, "达到间隔");
        }

        [Test]
        public void Rewarded_ResultPassesThrough_AndTracks()
        {
            var (svc, prov, an, _, _) = Build();
            prov.NextResult = AdResult.Skipped;
            var r = svc.ShowRewardedAsync("double_coins").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.Skipped, r, "奖励视频结果原样透传");
            CollectionAssert.AreEqual(new[] { "ad_request", "ad_show", "ad_complete" }, an.Names());
            Assert.AreEqual("rewarded", an.Events[0].p["type"]);
        }

        [Test]
        public void Rewarded_NotReady_ReturnsNotReady()
        {
            var (svc, prov, an, _, _) = Build();
            prov.IsRewardedReady = false;
            Assert.IsFalse(svc.IsRewardedReady);
            var r = svc.ShowRewardedAsync("p").GetAwaiter().GetResult();
            Assert.AreEqual(AdResult.NotReady, r);
            Assert.AreEqual("ad_request", an.Events[0].name);
            CollectionAssert.DoesNotContain(an.Names(), "ad_show");
        }
    }
}
```

- [ ] **Step 2: 实现接口 + 枚举** — `IAdsService.cs`

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.Ads
{
    public enum AdResult { Completed, Skipped, NotReady, Failed }

    public enum BannerPosition { Top, Bottom }

    /// <summary>SDK 适配点。Fake / AdMob / LevelPlay 各实现一个;在 LifetimeScope 按平台/环境切换。</summary>
    public interface IAdsProvider
    {
        UniTask InitializeAsync(CancellationToken ct);
        bool IsRewardedReady { get; }
        UniTask<AdResult> ShowRewardedAsync(string placement);
        UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }

    /// <summary>业务层入口(G.Ads)。频控 / 冷却 / 打点在此层,SDK 细节藏在 IAdsProvider。</summary>
    public interface IAdsService
    {
        bool IsRewardedReady { get; }
        UniTask<AdResult> ShowRewardedAsync(string placement);
        UniTask<AdResult> ShowInterstitialAsync(string placement);
        void ShowBanner(BannerPosition position);
        void HideBanner();
    }
}
```

- [ ] **Step 3: 实现 AdsService** — `AdsService.cs`

构造注入 `IAdsProvider`、`IConfigService`、`IAnalyticsService`、时间源 `Func<float>`(默认 `Time.realtimeSinceStartup`,经 `internal` 构造参数注入便于单测)。插屏频控:冷却秒数 `IConfigService.Get("ads.interstitial_cooldown", 30f)`、关卡间隔 `Get("ads.interstitial_min_gap", 1)`。行为:Provider 未就绪 / 频控拦截→`NotReady` 且不计冷却、不打 `ad_show`;`Completed` 才刷新冷却与间隔计数;全链路打点(`ad_request`/`ad_show`/`ad_complete`,参数 `placement`/`type`)。

```csharp
using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Configs;
using UnityEngine;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdsService : IAdsService
    {
        const string CooldownKey = "ads.interstitial_cooldown";
        const string MinGapKey = "ads.interstitial_min_gap";
        const float DefaultCooldown = 30f;
        const int DefaultMinGap = 1;

        readonly IAdsProvider _provider;
        readonly IConfigService _config;
        readonly IAnalyticsService _analytics;
        readonly Func<float> _now;

        float _lastInterstitialTime = float.NegativeInfinity;
        int _interstitialCallsSinceShown;

        /// <summary>生产构造:时间源默认 Time.realtimeSinceStartup。</summary>
        public AdsService(IAdsProvider provider, IConfigService config, IAnalyticsService analytics)
            : this(provider, config, analytics, () => Time.realtimeSinceStartup) { }

        /// <summary>测试构造:可注入时间源。</summary>
        internal AdsService(IAdsProvider provider, IConfigService config,
            IAnalyticsService analytics, Func<float> nowProvider)
        {
            _provider = provider;
            _config = config;
            _analytics = analytics;
            _now = nowProvider;
            // 首次允许展示:间隔计数从达到态起。
            _interstitialCallsSinceShown = Mathf.Max(0, _config.Get(MinGapKey, DefaultMinGap) - 1);
        }

        public bool IsRewardedReady => _provider.IsRewardedReady;

        public async UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            _analytics.Track("ad_request", ("placement", placement), ("type", "rewarded"));
            if (!_provider.IsRewardedReady)
                return AdResult.NotReady;

            _analytics.Track("ad_show", ("placement", placement), ("type", "rewarded"));
            var result = await _provider.ShowRewardedAsync(placement);
            _analytics.Track("ad_complete", ("placement", placement), ("type", "rewarded"),
                ("result", result.ToString()));
            return result;
        }

        public async UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            _analytics.Track("ad_request", ("placement", placement), ("type", "interstitial"));

            if (!PassesFrequencyGate())
                return AdResult.NotReady;

            _analytics.Track("ad_show", ("placement", placement), ("type", "interstitial"));
            var result = await _provider.ShowInterstitialAsync(placement);
            _analytics.Track("ad_complete", ("placement", placement), ("type", "interstitial"),
                ("result", result.ToString()));

            if (result == AdResult.Completed)
            {
                _lastInterstitialTime = _now();
                _interstitialCallsSinceShown = 0;
            }
            return result;
        }

        bool PassesFrequencyGate()
        {
            var cooldown = _config.Get(CooldownKey, DefaultCooldown);
            var minGap = Mathf.Max(1, _config.Get(MinGapKey, DefaultMinGap));

            // 关卡间隔:每 minGap 次调用允许一次展示。
            _interstitialCallsSinceShown++;
            if (_interstitialCallsSinceShown < minGap)
                return false;

            // 冷却:距上次成功展示需超过 cooldown 秒。
            if (_now() - _lastInterstitialTime < cooldown)
                return false;

            return true;
        }

        public void ShowBanner(BannerPosition position) => _provider.ShowBanner(position);

        public void HideBanner() => _provider.HideBanner();
    }
}
```

> 频控语义说明(与测试对齐):`_interstitialCallsSinceShown` 在构造时初始化为 `minGap-1`,使**首次**调用即满足间隔门;`PassesFrequencyGate` 每次调用自增该计数,`< minGap` 直接拒(此时不打 `ad_show`、不消耗冷却);满足间隔后再判冷却。只有真实 `Completed` 才把计数清零并刷新 `_lastInterstitialTime`——`NotReady`/`Failed`/`Skipped` 都不刷新,符合「Completed 才刷新冷却」与「NotReady 不计冷却」。注意 `PassesFrequencyGate` 自增后被冷却拒绝时计数已加(下一次更接近间隔门),这是有意:间隔统计的是「调用次数」,冷却统计的是「时间」,两者独立叠加。

- [ ] **Step 4: 实现 FakeAdsProvider** — `FakeAdsProvider.cs`

可配 `NextResult` 与 `IsRewardedReady`,默认 0.1s 延迟后 `Completed`,编辑器 `Debug.Log` 模拟展示。测试把 `Delay` 设为 0 以同步完成。

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.Ads
{
    /// <summary>编辑器 / 测试用广告 provider。模拟弹窗:可配结果与延迟。</summary>
    public sealed class FakeAdsProvider : IAdsProvider
    {
        public AdResult NextResult = AdResult.Completed;
        public bool IsRewardedReady { get; set; } = true;
        /// <summary>模拟展示耗时(秒);测试设 0 以同步完成。</summary>
        public float Delay = 0.1f;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            Debug.Log("[EasyFramework] FakeAdsProvider initialized.");
            return UniTask.CompletedTask;
        }

        public async UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            Debug.Log($"[EasyFramework] Fake rewarded ad @ '{placement}' -> {NextResult}");
            await DelayIfNeeded();
            return NextResult;
        }

        public async UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            Debug.Log($"[EasyFramework] Fake interstitial @ '{placement}' -> {NextResult}");
            await DelayIfNeeded();
            return NextResult;
        }

        UniTask DelayIfNeeded()
            => Delay > 0f
                ? UniTask.Delay(TimeSpan.FromSeconds(Delay), DelayType.Realtime)
                : UniTask.CompletedTask;

        public void ShowBanner(BannerPosition position)
            => Debug.Log($"[EasyFramework] Fake banner @ {position}");

        public void HideBanner() => Debug.Log("[EasyFramework] Fake banner hidden.");
    }
}
```

- [ ] **Step 5: 实现 AdMobAdsProvider 接入槽** — `AdMobAdsProvider.cs`

`#if EF_ADMOB` 包住的空实现。**文件头注释明确这不是占位符,是设计好的接入槽。**

```csharp
// ===========================================================================
// AdMobAdsProvider —— 真实广告 SDK 接入槽(NOT a placeholder)。
//
// 这是设计好的「接入位置」:本阶段(无人值守)不接入真实 AdMob/LevelPlay SDK,
// 因其需要开发者账号、App ID、原生插件与编辑器侧配置。接入步骤:
//   1. 导入 Google Mobile Ads (AdMob) Unity 插件;
//   2. 在 Player Settings > Scripting Define Symbols 定义 EF_ADMOB;
//   3. 填充下方各方法体(签名已与 IAdsProvider 锁定,业务层零改动);
//   4. 在 GameLifetimeScope 覆盖注册 IAdsProvider -> AdMobAdsProvider。
// IAdsService(AdsService 业务层)、FakeAdsProvider、AdsServiceTests 全部不变。
// ===========================================================================
#if EF_ADMOB
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdMobAdsProvider : IAdsProvider
    {
        // TODO(接入时): 注入并初始化 GoogleMobileAds,缓存 RewardedAd / InterstitialAd / BannerView。

        public UniTask InitializeAsync(CancellationToken ct)
        {
            // MobileAds.Initialize(...); 预加载首个奖励/插屏。
            return UniTask.CompletedTask;
        }

        public bool IsRewardedReady => false; // 接入时: 返回缓存的 RewardedAd != null && CanShow

        public UniTask<AdResult> ShowRewardedAsync(string placement)
        {
            // 接入时: rewardedAd.Show(reward => ...); 把 OnUserEarnedReward/OnAdClosed 映射为 AdResult。
            return UniTask.FromResult(AdResult.NotReady);
        }

        public UniTask<AdResult> ShowInterstitialAsync(string placement)
        {
            // 接入时: interstitialAd.Show(); 映射关闭/失败为 AdResult。
            return UniTask.FromResult(AdResult.NotReady);
        }

        public void ShowBanner(BannerPosition position) { /* 接入时: 创建/显示 BannerView。 */ }

        public void HideBanner() { /* 接入时: bannerView.Hide()。 */ }
    }
}
#endif
```

- [ ] **Step 6: 实现 AdsBootTask** — `AdsBootTask.cs`

`Priority=30, IsCritical=false`,调 `provider.InitializeAsync`(SDK 初始化失败不阻塞启动,符合 spec §3.1 / §8)。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Monetization.Ads
{
    public sealed class AdsBootTask : IBootTask
    {
        readonly IAdsProvider _provider;
        public AdsBootTask(IAdsProvider provider) => _provider = provider;

        public int Priority => 30;
        public bool IsCritical => false;   // SDK 初始化失败降级该服务,不阻塞启动。

        public async UniTask InitializeAsync(CancellationToken ct)
            => await _provider.InitializeAsync(ct);
    }
}
```

- [ ] **Step 7: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.AdsServiceTests` 过滤)Expected: 全 PASS(7 用例)。
- [ ] **Step 8: Commit**(编排者)`git commit -m "feat(monetization): add ads service with frequency cap and analytics"`

---

### Task 3: IAP 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Monetization/IAP/IIAPService.cs`, `ProductCatalog.cs`, `IAPService.cs`, `FakeIAPProvider.cs`, `UnityIAPProvider.cs`, `IAPBootTask.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/IAPServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `IAPServiceTests.cs`

用 `FakeIAPProvider`(可配成功/取消/失败)、`ProductCatalog`(`ScriptableObject.CreateInstance` + internal 测试钩子加条目)、`FakeAnalytics`。**PlayerPrefs 清理:`[SetUp]`/`[TearDown]` 删 `ef.iap.owned`/`ef.iap.pending` 并 `Save()`。** 覆盖:购买成功发奖励 + 打点、取消不发奖励 + `success`/`fail` 打点、NonConsumable `IsOwned` 持久化(重建 service 仍 owned)、掉单补发(奖励回调抛异常→入 pending→重建 service 启动重放)、未初始化返回失败结果不抛异常、RewardHandler 可注册替换。

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class IAPServiceTests
    {
        const string OwnedKey = "ef.iap.owned";
        const string PendingKey = "ef.iap.pending";

        sealed class FakeAnalytics : IAnalyticsService
        {
            public readonly List<string> Names = new();
            public void Track(string eventName) => Names.Add(eventName);
            public void Track(string eventName, params (string key, object value)[] parameters) => Names.Add(eventName);
            public void SetUserProperty(string key, string value) { }
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
        }

        static ProductCatalog MakeCatalog()
        {
            var c = ScriptableObject.CreateInstance<ProductCatalog>();
            c.AddEntryForTest("coins_100", ProductType.Consumable, new List<string> { "+100 coins" });
            c.AddEntryForTest("remove_ads", ProductType.NonConsumable, new List<string> { "no more ads" });
            return c;
        }

        [Test]
        public void Purchase_Success_InvokesRewardHandlerAndTracks()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var an = new FakeAnalytics();
            var rewarded = new List<string>();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(rewarded.Add);

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsTrue(r.Success);
            Assert.AreEqual("coins_100", r.ProductId);
            CollectionAssert.AreEqual(new[] { "coins_100" }, rewarded);
            CollectionAssert.AreEqual(new[] { "iap_purchase_start", "iap_purchase_success" }, an.Names);
        }

        [Test]
        public void Purchase_Cancelled_DoesNotReward_AndTracksFail()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Cancelled };
            var an = new FakeAnalytics();
            var rewarded = new List<string>();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(rewarded.Add);

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsFalse(r.Success);
            Assert.AreEqual("cancelled", r.FailureReason);
            CollectionAssert.IsEmpty(rewarded);
            CollectionAssert.AreEqual(new[] { "iap_purchase_start", "iap_purchase_fail" }, an.Names);
        }

        [Test]
        public void NonConsumable_Owned_PersistsAcrossInstances()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => { });
            svc.PurchaseAsync("remove_ads").GetAwaiter().GetResult();
            Assert.IsTrue(svc.IsOwned("remove_ads"));

            // 重建 service:从 PlayerPrefs 恢复已购集合。
            var svc2 = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            Assert.IsTrue(svc2.IsOwned("remove_ads"));
        }

        [Test]
        public void Consumable_NotMarkedOwned()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => { });
            svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsFalse(svc.IsOwned("coins_100"), "消耗型不记入已购");
        }

        [Test]
        public void RewardThrows_EnqueuesPending_AndReplaysOnNextStart()
        {
            var prov = new FakeIAPProvider { Initialized = true, NextOutcome = FakeIAPProvider.Outcome.Success };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc.SetRewardHandler(_ => throw new Exception("reward boom")); // 发奖失败

            var r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult();
            Assert.IsTrue(r.Success, "购买本身成功(发奖失败不回滚购买)");
            // 掉单入 pending 队列(持久化)。
            CollectionAssert.Contains(svc.PendingForTest(), "coins_100");

            // 重建 service + 正常 RewardHandler,重放 pending。
            var replayed = new List<string>();
            var svc2 = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            svc2.SetRewardHandler(replayed.Add);
            svc2.ReplayPending();
            CollectionAssert.AreEqual(new[] { "coins_100" }, replayed);
            CollectionAssert.IsEmpty(svc2.PendingForTest(), "重放成功后清空 pending");
        }

        [Test]
        public void Purchase_NotInitialized_ReturnsFailureResult_NoThrow()
        {
            var prov = new FakeIAPProvider { Initialized = false };
            var an = new FakeAnalytics();
            var svc = new IAPService(prov, MakeCatalog(), an);
            svc.SetRewardHandler(_ => { });

            PurchaseResult r = null;
            Assert.DoesNotThrow(() => r = svc.PurchaseAsync("coins_100").GetAwaiter().GetResult());
            Assert.IsFalse(r.Success);
            Assert.AreEqual("not_initialized", r.FailureReason);
            CollectionAssert.Contains(an.Names, "iap_purchase_fail");
        }

        [Test]
        public void Restore_DoesNotThrow()
        {
            var prov = new FakeIAPProvider { Initialized = true };
            var svc = new IAPService(prov, MakeCatalog(), new FakeAnalytics());
            Assert.DoesNotThrow(() => svc.RestoreAsync().GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: 实现接口 + 枚举 + 结果对象** — `IIAPService.cs`

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Monetization.IAP
{
    public enum ProductType { Consumable, NonConsumable }

    public sealed class PurchaseResult
    {
        public bool Success;
        public string ProductId;
        public string FailureReason;   // 取消="cancelled", 未初始化="not_initialized" 等
    }

    /// <summary>SDK 适配点(Unity IAP / Fake)。</summary>
    public interface IIAPProvider
    {
        UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct);
        bool IsInitialized { get; }
        UniTask<PurchaseResult> PurchaseAsync(string productId);
        UniTask RestoreAsync();
    }

    /// <summary>业务层入口(G.IAP)。已购持久化 / 掉单补发 / 打点在此层。</summary>
    public interface IIAPService
    {
        UniTask<PurchaseResult> PurchaseAsync(string productId);
        UniTask RestoreAsync();
        bool IsOwned(string productId);
    }
}
```

- [ ] **Step 3: 实现 ProductCatalog** — `ProductCatalog.cs`(ScriptableObject)

条目:`id` / `type` / 奖励描述字符串列表。

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    [CreateAssetMenu(fileName = "ProductCatalog", menuName = "EasyFramework/Product Catalog")]
    public sealed class ProductCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string Id;
            public ProductType Type;
            public List<string> RewardDescriptions;
        }

        [SerializeField] List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        public IReadOnlyList<string> ProductIds()
        {
            var ids = new List<string>(_entries.Count);
            foreach (var e in _entries) ids.Add(e.Id);
            return ids;
        }

        public bool TryGet(string id, out Entry entry)
        {
            foreach (var e in _entries)
                if (e.Id == id) { entry = e; return true; }
            entry = default;
            return false;
        }

        /// <summary>测试钩子:运行时追加条目。</summary>
        internal void AddEntryForTest(string id, ProductType type, List<string> rewards)
            => _entries.Add(new Entry { Id = id, Type = type, RewardDescriptions = rewards });
    }
}
```

- [ ] **Step 4: 实现 IAPService** — `IAPService.cs`

构造注入 `IIAPProvider`、`ProductCatalog`、`IAnalyticsService`。NonConsumable 已购记录 `PlayerPrefs "ef.iap.owned"`(JSON id 列表);掉单补发:`PurchaseAsync` 成功但奖励回调抛异常时入 pending 队列(`PlayerPrefs "ef.iap.pending"`),`ReplayPending` 重放(由 `IAPBootTask` 启动时调);奖励发放经可注册的 `Action<string productId> RewardHandler`(`GameLifetimeScope` 注册,框架默认 `Debug.Log`);打点 `iap_purchase_start`/`iap_purchase_success`/`iap_purchase_fail`。**未初始化返回失败结果(`not_initialized`)不抛异常(spec §8)。**

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Monetization.Analytics;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    public sealed class IAPService : IIAPService
    {
        const string OwnedKey = "ef.iap.owned";
        const string PendingKey = "ef.iap.pending";

        readonly IIAPProvider _provider;
        readonly ProductCatalog _catalog;
        readonly IAnalyticsService _analytics;

        readonly HashSet<string> _owned;
        readonly List<string> _pending;
        Action<string> _rewardHandler;

        public IAPService(IIAPProvider provider, ProductCatalog catalog, IAnalyticsService analytics)
        {
            _provider = provider;
            _catalog = catalog;
            _analytics = analytics;
            _owned = new HashSet<string>(LoadList(OwnedKey));
            _pending = new List<string>(LoadList(PendingKey));
            _rewardHandler = id => Debug.Log($"[EasyFramework] Default reward grant for '{id}' (override via SetRewardHandler).");
        }

        /// <summary>由 GameLifetimeScope 注册游戏侧发奖逻辑。</summary>
        public void SetRewardHandler(Action<string> handler)
            => _rewardHandler = handler ?? throw new ArgumentNullException(nameof(handler));

        public bool IsOwned(string productId) => _owned.Contains(productId);

        public async UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            _analytics.Track("iap_purchase_start", ("product", productId));

            if (!_provider.IsInitialized)
            {
                _analytics.Track("iap_purchase_fail", ("product", productId), ("reason", "not_initialized"));
                return new PurchaseResult { Success = false, ProductId = productId, FailureReason = "not_initialized" };
            }

            var result = await _provider.PurchaseAsync(productId);
            if (!result.Success)
            {
                _analytics.Track("iap_purchase_fail", ("product", productId),
                    ("reason", result.FailureReason ?? "unknown"));
                return result;
            }

            // 购买成功:标记非消耗型已购,发奖(发奖失败入 pending 补发)。
            if (_catalog.TryGet(productId, out var entry) && entry.Type == ProductType.NonConsumable)
                MarkOwned(productId);

            _analytics.Track("iap_purchase_success", ("product", productId));
            TryGrantReward(productId);
            return result;
        }

        public UniTask RestoreAsync() => _provider.RestoreAsync();

        /// <summary>重放 pending 掉单(IAPBootTask 启动时调)。逐个发奖,成功者移出队列。</summary>
        public void ReplayPending()
        {
            if (_pending.Count == 0) return;
            var still = new List<string>();
            foreach (var id in _pending)
            {
                try { _rewardHandler(id); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Pending reward replay failed for '{id}': {e.Message}");
                    still.Add(id);
                }
            }
            _pending.Clear();
            _pending.AddRange(still);
            SaveList(PendingKey, _pending);
        }

        void TryGrantReward(string productId)
        {
            try
            {
                _rewardHandler(productId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Reward grant threw for '{productId}'; enqueuing for replay: {e.Message}");
                if (!_pending.Contains(productId))
                {
                    _pending.Add(productId);
                    SaveList(PendingKey, _pending);
                }
            }
        }

        void MarkOwned(string productId)
        {
            if (_owned.Add(productId))
                SaveList(OwnedKey, new List<string>(_owned));
        }

        // ---- PlayerPrefs JSON id 列表持久化 ----
        [Serializable] sealed class IdList { public List<string> Ids = new(); }

        static List<string> LoadList(string key)
        {
            var json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return JsonUtility.FromJson<IdList>(json)?.Ids ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        static void SaveList(string key, List<string> ids)
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(new IdList { Ids = ids }));
            PlayerPrefs.Save();
        }

        // ---- 测试钩子 ----
        internal IReadOnlyList<string> PendingForTest() => _pending;
    }
}
```

> 设计说明:`JsonUtility` 用于 id 列表序列化(无需 Newtonsoft,且 PlayerPrefs 值是字符串)。`SetRewardHandler` 返回 void、可重复设置(`GameLifetimeScope` 注册一次,测试可重设)。`ReplayPending` 与 `TryGrantReward` 共用 `_rewardHandler`:正常购买路径发奖失败→入 pending;`IAPBootTask` 启动时 `ReplayPending` 重放。`PurchaseAsync` 全程 try/await 不向业务抛异常——provider 自身异常由 provider 内部捕获返回失败结果(Fake/Unity 实现均如此),发奖异常被 `TryGrantReward` 吞掉转 pending。

- [ ] **Step 5: 实现 FakeIAPProvider** — `FakeIAPProvider.cs`

可配成功/取消/失败。

```csharp
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Monetization.IAP
{
    /// <summary>编辑器 / 测试用 IAP provider。可配购买结果。</summary>
    public sealed class FakeIAPProvider : IIAPProvider
    {
        public enum Outcome { Success, Cancelled, Failed }

        public bool Initialized = true;
        public Outcome NextOutcome = Outcome.Success;

        public bool IsInitialized => Initialized;

        public UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct)
        {
            Initialized = true;
            Debug.Log($"[EasyFramework] FakeIAPProvider initialized with {productIds?.Count ?? 0} products.");
            return UniTask.CompletedTask;
        }

        public UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            var result = NextOutcome switch
            {
                Outcome.Success => new PurchaseResult { Success = true, ProductId = productId },
                Outcome.Cancelled => new PurchaseResult { Success = false, ProductId = productId, FailureReason = "cancelled" },
                _ => new PurchaseResult { Success = false, ProductId = productId, FailureReason = "failed" },
            };
            Debug.Log($"[EasyFramework] Fake purchase '{productId}' -> {NextOutcome}");
            return UniTask.FromResult(result);
        }

        public UniTask RestoreAsync()
        {
            Debug.Log("[EasyFramework] Fake restore (no-op).");
            return UniTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 6: 实现 UnityIAPProvider** — `UnityIAPProvider.cs`(真实现,仅真机路径,不单测)

按真实 Unity IAP API 书写(`IDetailedStoreListener`/`IStoreController`/`UnityPurchasing.Initialize`)。**验证代理用 `unity_reflect` 核对 `com.unity.purchasing` 安装版本的实际类型成员**,若 API 名/签名有出入按实际等效调整(`IIAPProvider` 契约不变)。SDK 异步回调用 `UniTaskCompletionSource` 桥接为 `UniTask`,回调统一切主线程(Unity IAP 回调本就在主线程)。**文件头注释标明这是有真实现的真机路径,编辑器走 Fake。**

```csharp
// ===========================================================================
// UnityIAPProvider —— Unity IAP(com.unity.purchasing)真实现,仅真机路径。
//
// 官方包,真实现非占位符。但其异步回调链路只在真机(配置好 IAP catalog 与
// 商店密钥)上可跑通,EditMode 无法稳定驱动,故不写 EditMode 单测;编辑器与
// 全部 EditMode 测试走 FakeIAPProvider(FrameworkInstaller 在 #if UNITY_EDITOR
// 下注册 Fake,真机注册本类)。Unity IAP API 名/签名以工程安装版本为准——
// 验证代理用 unity_reflect 核对后等效调整,IIAPProvider 契约不变。
// ===========================================================================
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace EasyFramework.Monetization.IAP
{
    public sealed class UnityIAPProvider : IIAPProvider, IDetailedStoreListener
    {
        IStoreController _controller;
        IExtensionProvider _extensions;
        UniTaskCompletionSource _initTcs;
        UniTaskCompletionSource<PurchaseResult> _purchaseTcs;

        public bool IsInitialized => _controller != null;

        public UniTask InitializeAsync(IReadOnlyList<string> productIds, CancellationToken ct)
        {
            if (IsInitialized) return UniTask.CompletedTask;
            _initTcs = new UniTaskCompletionSource();

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            // 类型未知时按 Consumable 注册兜底;实际类型由业务侧 catalog 决定,真机接入时映射。
            foreach (var id in productIds)
                builder.AddProduct(id, UnityEngine.Purchasing.ProductType.Consumable);

            UnityPurchasing.Initialize(this, builder);
            return _initTcs.Task;
        }

        public UniTask<PurchaseResult> PurchaseAsync(string productId)
        {
            if (!IsInitialized)
                return UniTask.FromResult(new PurchaseResult
                { Success = false, ProductId = productId, FailureReason = "not_initialized" });

            _purchaseTcs = new UniTaskCompletionSource<PurchaseResult>();
            _controller.InitiatePurchase(productId);
            return _purchaseTcs.Task;
        }

        public UniTask RestoreAsync()
        {
            // iOS: AppleExtensions.RestoreTransactions;其它平台一般无需手动恢复。
            var apple = _extensions?.GetExtension<IAppleExtensions>();
            apple?.RestoreTransactions((_, __) => { });
            return UniTask.CompletedTask;
        }

        // ---- IDetailedStoreListener 回调(主线程)----
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;
            _initTcs?.TrySetResult();
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => _initTcs?.TrySetResult(); // 初始化失败不阻塞:IsInitialized 仍 false,后续购买返回 not_initialized。

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => _initTcs?.TrySetResult();

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = true, ProductId = args.purchasedProduct.definition.id });
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            var reason = failureDescription.reason == PurchaseFailureReason.UserCancelled
                ? "cancelled" : failureDescription.reason.ToString();
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = false, ProductId = product.definition.id, FailureReason = reason });
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            var reason = failureReason == PurchaseFailureReason.UserCancelled
                ? "cancelled" : failureReason.ToString();
            _purchaseTcs?.TrySetResult(new PurchaseResult
            { Success = false, ProductId = product.definition.id, FailureReason = reason });
        }
    }
}
```

> 接入提示(验证代理):新版 Unity IAP 推荐 `IDetailedStoreListener`(含 `OnPurchaseFailed(Product, PurchaseFailureDescription)`);旧版用 `IStoreListener`。`UnityEngine.Purchasing.ProductType` 与本框架 `EasyFramework.Monetization.IAP.ProductType` 同名不同命名空间,代码中已用全限定名区分。若 `unity_reflect` 核对发现该版本接口成员不同,按实际等效调整(目标:初始化成功置 `_controller`,购买成功/失败/取消正确映射 `PurchaseResult`)。

- [ ] **Step 7: 实现 IAPBootTask** — `IAPBootTask.cs`

`Priority=31, IsCritical=false`。初始化 provider(传 catalog 的 product ids),成功后重放 pending 掉单。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Monetization.IAP
{
    public sealed class IAPBootTask : IBootTask
    {
        readonly IIAPProvider _provider;
        readonly ProductCatalog _catalog;
        readonly IAPService _service;

        public IAPBootTask(IIAPProvider provider, ProductCatalog catalog, IAPService service)
        {
            _provider = provider;
            _catalog = catalog;
            _service = service;
        }

        public int Priority => 31;
        public bool IsCritical => false;   // IAP 初始化失败不阻塞启动。

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            var ids = _catalog != null ? _catalog.ProductIds() : new string[0];
            await _provider.InitializeAsync(ids, ct);
            _service.ReplayPending();   // 重放上次会话遗留的掉单。
        }
    }
}
```

> 注:`IAPBootTask` 依赖具体类 `IAPService`(调 `ReplayPending`,不在 `IIAPService` 上)。`FrameworkInstaller` 以 `IAPService` 注册并 `.As<IIAPService>().AsSelf()`,使两者均可解析(见 Task 5)。`ProductCatalog` 可空——`FrameworkOptions.ProductCatalog` 为 null 时框架注册一个空 catalog(见 Task 5),`ids` 为空数组,provider 初始化空目录、`ReplayPending` 无操作。

- [ ] **Step 8: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.IAPServiceTests` 过滤)Expected: 全 PASS(7 用例)。
- [ ] **Step 9: Commit**(编排者)`git commit -m "feat(monetization): add iap service with ownership, pending replay and unity iap provider"`

---

### Task 4: Analytics 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Monetization/Analytics/IAnalyticsService.cs`, `AnalyticsService.cs`, `DebugAnalyticsBackend.cs`, `AnalyticsAutoTracker.cs`, `FirebaseAnalyticsBackend.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/AnalyticsServiceTests.cs`

- [ ] **Step 1: 写失败测试** — `AnalyticsServiceTests.cs`

用记录型 `RecordingBackend`、抛异常的 `ThrowingBackend`、Fake `IEventBus`。覆盖:多 backend 广播(同事件到所有后端)、异常隔离(一个 backend 抛异常只 `LogWarning`、不影响其它 backend、不传染到调用方,§8)、params 元组参数转 `IReadOnlyDictionary`、无参 `Track` 传空字典、`SetUserProperty` 广播、自动事件(`AnalyticsAutoTracker` 订阅 `BootCompletedEvent`→`boot_completed`、`SceneLoadedEvent`→`scene_loaded` 带 scene 参数)。

```csharp
using System;
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Services.Scenes;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class AnalyticsServiceTests
    {
        sealed class RecordingBackend : IAnalyticsBackend
        {
            public readonly List<(string name, IReadOnlyDictionary<string, object> p)> Events = new();
            public readonly List<(string key, string value)> Props = new();
            public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
                => Events.Add((eventName, parameters));
            public void SetUserProperty(string key, string value) => Props.Add((key, value));
        }

        sealed class ThrowingBackend : IAnalyticsBackend
        {
            public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
                => throw new Exception("backend boom");
            public void SetUserProperty(string key, string value)
                => throw new Exception("prop boom");
        }

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
                readonly Action _d; public Sub(Action d) => _d = d; public void Dispose() => _d();
            }
        }

        [Test]
        public void Track_BroadcastsToAllBackends()
        {
            var a = new RecordingBackend();
            var b = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a, b });
            svc.Track("level_start", ("level", 3));
            Assert.AreEqual(1, a.Events.Count);
            Assert.AreEqual(1, b.Events.Count);
            Assert.AreEqual("level_start", a.Events[0].name);
            Assert.AreEqual(3, a.Events[0].p["level"]);
        }

        [Test]
        public void Track_BackendThrows_IsolatedFromOthersAndCaller()
        {
            var good = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { new ThrowingBackend(), good });
            Assert.DoesNotThrow(() => svc.Track("e"), "异常不传染到调用方");
            Assert.AreEqual(1, good.Events.Count, "抛异常的 backend 不影响其它 backend");
        }

        [Test]
        public void Track_NoParams_PassesEmptyDictionary()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            svc.Track("ping");
            Assert.AreEqual(0, a.Events[0].p.Count);
        }

        [Test]
        public void Track_TupleParams_ConvertedToDictionary()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            svc.Track("buy", ("item", "sword"), ("price", 9.99f));
            Assert.AreEqual("sword", a.Events[0].p["item"]);
            Assert.AreEqual(9.99f, a.Events[0].p["price"]);
        }

        [Test]
        public void SetUserProperty_BroadcastsAndIsolatesExceptions()
        {
            var good = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { new ThrowingBackend(), good });
            Assert.DoesNotThrow(() => svc.SetUserProperty("tier", "gold"));
            Assert.AreEqual(("tier", "gold"), good.Props[0]);
        }

        [Test]
        public void AutoTracker_BootCompleted_TracksEvent()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            var bus = new FakeBus();
            var tracker = new AnalyticsAutoTracker(svc, bus);
            tracker.Start();

            bus.Publish(new BootCompletedEvent());
            Assert.AreEqual("boot_completed", a.Events[0].name);

            tracker.Dispose();
            bus.Publish(new BootCompletedEvent());
            Assert.AreEqual(1, a.Events.Count, "Dispose 后退订,不再收事件");
        }

        [Test]
        public void AutoTracker_SceneLoaded_TracksWithSceneParam()
        {
            var a = new RecordingBackend();
            var svc = new AnalyticsService(new IAnalyticsBackend[] { a });
            var bus = new FakeBus();
            var tracker = new AnalyticsAutoTracker(svc, bus);
            tracker.Start();

            bus.Publish(new SceneLoadedEvent("Level1"));
            Assert.AreEqual("scene_loaded", a.Events[0].name);
            Assert.AreEqual("Level1", a.Events[0].p["scene"]);
            tracker.Dispose();
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IAnalyticsService.cs`

```csharp
using System.Collections.Generic;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>统计后端适配点。Debug / Firebase / 其它后端各实现一个,多注册广播。</summary>
    public interface IAnalyticsBackend
    {
        void Track(string eventName, IReadOnlyDictionary<string, object> parameters);
        void SetUserProperty(string key, string value);
    }

    /// <summary>业务层入口(G.Analytics)。广播到所有 IAnalyticsBackend。</summary>
    public interface IAnalyticsService
    {
        void Track(string eventName);
        void Track(string eventName, params (string key, object value)[] parameters);
        void SetUserProperty(string key, string value);
    }
}
```

- [ ] **Step 3: 实现 AnalyticsService** — `AnalyticsService.cs`

广播到 `IReadOnlyList<IAnalyticsBackend>`(VContainer 多注册);任何 backend 抛异常只 `LogWarning` 不传染(§8)。

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Monetization.Analytics
{
    public sealed class AnalyticsService : IAnalyticsService
    {
        static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

        readonly IReadOnlyList<IAnalyticsBackend> _backends;

        public AnalyticsService(IReadOnlyList<IAnalyticsBackend> backends)
            => _backends = backends ?? Array.Empty<IAnalyticsBackend>();

        public void Track(string eventName) => Dispatch(eventName, Empty);

        public void Track(string eventName, params (string key, object value)[] parameters)
        {
            var dict = new Dictionary<string, object>(parameters?.Length ?? 0);
            if (parameters != null)
                foreach (var (k, v) in parameters) dict[k] = v;
            Dispatch(eventName, dict);
        }

        public void SetUserProperty(string key, string value)
        {
            for (var i = 0; i < _backends.Count; i++)
            {
                try { _backends[i].SetUserProperty(key, value); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Analytics backend {_backends[i].GetType().Name} " +
                                     $"threw on SetUserProperty('{key}'): {e.Message}");
                }
            }
        }

        void Dispatch(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            for (var i = 0; i < _backends.Count; i++)
            {
                try { _backends[i].Track(eventName, parameters); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Analytics backend {_backends[i].GetType().Name} " +
                                     $"threw on Track('{eventName}'): {e.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 4: 实现 DebugAnalyticsBackend** — `DebugAnalyticsBackend.cs`

编辑器 `Debug.Log`,格式化参数。

```csharp
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>编辑器 / 真机调试用后端:把事件打到 Console。真机 DevTools 可看事件流。</summary>
    public sealed class DebugAnalyticsBackend : IAnalyticsBackend
    {
        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                Debug.Log($"[Analytics] {eventName}");
                return;
            }
            var sb = new StringBuilder();
            sb.Append("[Analytics] ").Append(eventName).Append(" { ");
            var first = true;
            foreach (var kv in parameters)
            {
                if (!first) sb.Append(", ");
                sb.Append(kv.Key).Append('=').Append(kv.Value);
                first = false;
            }
            sb.Append(" }");
            Debug.Log(sb.ToString());
        }

        public void SetUserProperty(string key, string value)
            => Debug.Log($"[Analytics] user.{key} = {value}");
    }
}
```

- [ ] **Step 5: 实现 AnalyticsAutoTracker** — `AnalyticsAutoTracker.cs`

`VContainer.Unity.IStartable + IDisposable`。`Start` 订阅 `BootCompletedEvent`→`boot_completed`、`SceneLoadedEvent`→`scene_loaded`(`scene` 参数);`Dispose` 退订。

```csharp
using System;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Services.Scenes;
using VContainer.Unity;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>框架自动标准事件:启动完成 / 场景加载。订阅在 Start,退订在 Dispose。</summary>
    public sealed class AnalyticsAutoTracker : IStartable, IDisposable
    {
        readonly IAnalyticsService _analytics;
        readonly IEventBus _events;
        IDisposable _bootSub;
        IDisposable _sceneSub;

        public AnalyticsAutoTracker(IAnalyticsService analytics, IEventBus events)
        {
            _analytics = analytics;
            _events = events;
        }

        public void Start()
        {
            _bootSub = _events.Subscribe<BootCompletedEvent>(_ => _analytics.Track("boot_completed"));
            _sceneSub = _events.Subscribe<SceneLoadedEvent>(e =>
                _analytics.Track("scene_loaded", ("scene", e.SceneName)));
        }

        public void Dispose()
        {
            _bootSub?.Dispose();
            _sceneSub?.Dispose();
        }
    }
}
```

> 注:`AnalyticsAutoTracker` 依赖 `EasyFramework.Services.Scenes.SceneLoadedEvent`(Phase 2 定义,字段 `SceneName`)与 `EasyFramework.Core.Boot.BootCompletedEvent`(Phase 1)。Monetization 已 references Services/Core,可见。**`SceneLoadedEvent` 的实际字段名以 Phase 2 落地为准**(Phase 2 计划为 `public readonly string SceneName`),若实际不同,验证代理在 `AnalyticsAutoTracker` 与测试里按实际字段名等效调整。

- [ ] **Step 6: 实现 FirebaseAnalyticsBackend 接入槽** — `FirebaseAnalyticsBackend.cs`

`#if EF_FIREBASE` 包住的空文件。**文件头注释说明接法,标明非占位符。**

```csharp
// ===========================================================================
// FirebaseAnalyticsBackend —— 真实统计后端接入槽(NOT a placeholder)。
//
// 本阶段(无人值守)不接入 Firebase:其需要 google-services.json / GoogleService-Info.plist
// 与 Firebase Unity SDK 原生插件。接入步骤:
//   1. 导入 Firebase Analytics Unity SDK;
//   2. 在 Player Settings > Scripting Define Symbols 定义 EF_FIREBASE;
//   3. 填充下方方法体(把 parameters 映射为 Firebase.Analytics.Parameter[]);
//   4. 在 GameLifetimeScope 追加注册一个 IAnalyticsBackend -> FirebaseAnalyticsBackend
//      (多注册:AnalyticsService 会广播到全部后端,DebugAnalyticsBackend 与本类并存)。
// IAnalyticsBackend / IAnalyticsService 接口、AnalyticsService、打点代码全部不变。
// ===========================================================================
#if EF_FIREBASE
using System.Collections.Generic;

namespace EasyFramework.Monetization.Analytics
{
    public sealed class FirebaseAnalyticsBackend : IAnalyticsBackend
    {
        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            // 接入时:把 parameters 转 Firebase.Analytics.Parameter[](按值类型 string/long/double 分支),
            // 调 FirebaseAnalytics.LogEvent(eventName, parms)。
        }

        public void SetUserProperty(string key, string value)
        {
            // 接入时:FirebaseAnalytics.SetUserProperty(key, value);
        }
    }
}
#endif
```

- [ ] **Step 7: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.AnalyticsServiceTests` 过滤)Expected: 全 PASS(7 用例)。
- [ ] **Step 8: Commit**(编排者)`git commit -m "feat(monetization): add analytics service with multi-backend broadcast and auto events"`

---

### Task 5: 接线 —— FrameworkInstaller / G / FrameworkOptions / RootLifetimeScope(串行,依赖 Task 2-4)

> **执行前必读本文件开头的「Task 6 前置要求」(本 Task 即接线 Task)。先 `Read` Phase 2/3 落地的 `G.cs`/`FrameworkInstaller.cs`/`RootLifetimeScope.cs` 核对实际签名,再在其上增量合并,只新增 Ads/IAP/Analytics 相关内容,保留其余既有属性/注册/字段。**

**Files:**
- Modify: `Assets/EasyFramework/Boot/FrameworkInstaller.cs`(`FrameworkOptions` 加 `ProductCatalog` + 注册 Monetization 三组)
- Modify: `Assets/EasyFramework/Boot/G.cs`(新增 Ads/IAP/Analytics 3 属性)
- Modify: `Assets/EasyFramework/Boot/RootLifetimeScope.cs`(Inspector 暴露 ProductCatalog 并传入 options)
- Modify: `Assets/EasyFramework/Tests/EditMode/FrameworkInstallerTests.cs`(扩展断言)

- [ ] **Step 1: 升级失败测试** — `FrameworkInstallerTests.cs`

**先 Read Phase 2/3 实际文件**,在其上增量修改:`MakeOptions` 保留既有字段(`SaveDirectory`/`ConfigTables`/...),`ProductCatalog` 用空 catalog;新增断言三个 Monetization 服务可解析、`G` 绑定三个新属性。下方为**需新增/修改的片段**(合并进实际文件,不要整体覆盖 Phase 2/3 的既有用例):

```csharp
// using 区追加:
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;
using UnityEngine; // ScriptableObject.CreateInstance<ProductCatalog>()

// MakeOptions() 内追加字段(保留 Phase 2/3 既有字段):
//   ProductCatalog = ScriptableObject.CreateInstance<ProductCatalog>(),

[Test]
public void Install_ResolvesMonetizationServices()
{
    var c = Build();
    Assert.NotNull(c.Resolve<IAdsService>());
    Assert.NotNull(c.Resolve<IIAPService>());
    Assert.NotNull(c.Resolve<IAnalyticsService>());
}

[Test]
public void GFacade_BindsMonetizationServices()
{
    var c = Build();
    EasyFramework.G.Initialize(c);
    Assert.AreSame(c.Resolve<IAdsService>(), EasyFramework.G.Ads);
    Assert.AreSame(c.Resolve<IIAPService>(), EasyFramework.G.IAP);
    Assert.AreSame(c.Resolve<IAnalyticsService>(), EasyFramework.G.Analytics);
}
```

> 注:`GFacade_ResetClearsBindings`(Phase 2 已有)可追加断言 `Assert.IsNull(EasyFramework.G.Ads)` 等,但非必须;Build() 仍用既有 `FrameworkInstaller.Install(builder, MakeOptions())` 签名(本阶段不改 `Install` 的方法签名,只在 `FrameworkOptions` 加字段、在方法体内多注册)。

- [ ] **Step 2: 升级 G 门面** — `Assets/EasyFramework/Boot/G.cs`

在 Phase 2/3 既有属性基础上**增量新增** Ads/IAP/Analytics 三属性,`Initialize`/`Reset` 同步更新。**先 Read 实际文件**,下方为需追加的片段(合并,不覆盖既有 Events/Timer/Asset/... 等):

```csharp
// using 区追加:
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;

// 属性区追加:
public static IAdsService Ads { get; private set; }
public static IIAPService IAP { get; private set; }
public static IAnalyticsService Analytics { get; private set; }

// Initialize(IObjectResolver resolver) 内追加:
Ads = resolver.Resolve<IAdsService>();
IAP = resolver.Resolve<IIAPService>();
Analytics = resolver.Resolve<IAnalyticsService>();

// Reset() 内追加:
Ads = null;
IAP = null;
Analytics = null;
```

- [ ] **Step 3: 升级 FrameworkInstaller** — `Assets/EasyFramework/Boot/FrameworkInstaller.cs`

**先 Read 实际文件。** `FrameworkOptions` 增 `ProductCatalog` 字段(可空);`Install` 方法体内**追加** Monetization 三组注册(保留 Phase 2/3 既有 Core/Services 注册)。下方为需新增的片段:

```csharp
// using 区追加:
using EasyFramework.Monetization.Ads;
using EasyFramework.Monetization.Analytics;
using EasyFramework.Monetization.IAP;

// FrameworkOptions 类内追加字段:
/// <summary>内购商品目录;可空(为 null 时框架用空 catalog,无商品)。</summary>
public ProductCatalog ProductCatalog;
```

`Install(IContainerBuilder builder, FrameworkOptions options)` 方法体末尾追加:

```csharp
            // ---- Analytics ----
            // 后端多注册:DebugAnalyticsBackend(编辑器/调试)。真机接 Firebase 时,
            // 在 GameLifetimeScope 追加注册 IAnalyticsBackend -> FirebaseAnalyticsBackend(#if EF_FIREBASE)。
            builder.Register<IAnalyticsBackend, DebugAnalyticsBackend>(Lifetime.Singleton);
            builder.Register<IAnalyticsService>(c =>
                new AnalyticsService(new List<IAnalyticsBackend>(c.Resolve<IReadOnlyList<IAnalyticsBackend>>())),
                Lifetime.Singleton);
            // 自动标准事件订阅(BootCompleted / SceneLoaded)。
            builder.RegisterEntryPoint<AnalyticsAutoTracker>();

            // ---- Ads ----
            // Fake provider 在编辑器与真机都注册(保证真机也能跑通);接 AdMob/LevelPlay 时,
            // 在 GameLifetimeScope 覆盖注册 IAdsProvider -> AdMobAdsProvider(#if EF_ADMOB)。
            builder.Register<IAdsProvider, FakeAdsProvider>(Lifetime.Singleton);
            builder.Register<AdsService>(Lifetime.Singleton).As<IAdsService>().AsSelf();
            builder.Register<AdsBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- IAP ----
            var catalog = options.ProductCatalog;
            if (catalog == null)
                catalog = ScriptableObject.CreateInstance<ProductCatalog>(); // 空目录兜底,可空契约。
            builder.RegisterInstance(catalog);
#if UNITY_EDITOR
            // 编辑器走 Fake,EditMode 测试与编辑器联调均不触真实 SDK。
            builder.Register<IIAPProvider, FakeIAPProvider>(Lifetime.Singleton);
#else
            // 真机:Unity IAP 真实现(官方包)。这是接入槽,换其它商店 SDK 适配器也在此替换。
            builder.Register<IIAPProvider, UnityIAPProvider>(Lifetime.Singleton);
#endif
            builder.Register<IAPService>(Lifetime.Singleton).As<IIAPService>().AsSelf();
            builder.Register<IAPBootTask>(Lifetime.Singleton).As<IBootTask>();
```

> 注册要点:
> - `using UnityEngine;` 与 `using System.Collections.Generic;` 须在文件 using 区(Phase 2 已有 `System.Collections.Generic`;`UnityEngine` 用于 `ScriptableObject.CreateInstance`)。
> - `IAnalyticsService` 用工厂 lambda 注册,经 `c.Resolve<IReadOnlyList<IAnalyticsBackend>>()` 收集所有多注册后端(VContainer 对同接口多次 `Register` 自动支持 `IReadOnlyList<T>`/`IEnumerable<T>` 解析)。包成 `new List<>(...)` 以匹配 `AnalyticsService` 构造的 `IReadOnlyList<IAnalyticsBackend>` 参数。
> - `AdsService`/`IAPService` 以具体类注册并 `.As<I...>().AsSelf()`,使 `AdsBootTask`(注 `IAdsProvider`)、`IAPBootTask`(注具体 `IAPService` 调 `ReplayPending`)与门面(注接口)均可解析。
> - 三个 BootTask 以 `.As<IBootTask>()` 注册,`GameBootstrap`(Phase 1)自动收集:`AdsBootTask`(Priority 30)、`IAPBootTask`(Priority 31)。它们都 `IsCritical=false`,SDK 初始化失败不阻塞启动(spec §3.1)。
> - `AnalyticsAutoTracker` 以 `RegisterEntryPoint`(`IStartable`)注册,容器 Build 后 `Start` 自动订阅事件。
> - 全部 `Lifetime.Singleton`,纯 `ContainerBuilder.Build()` 可成功(无 MonoBehaviour 依赖),FrameworkInstallerTests 可在纯容器下解析。

- [ ] **Step 4: 升级 RootLifetimeScope** — `Assets/EasyFramework/Boot/RootLifetimeScope.cs`

**先 Read 实际文件。** Inspector 暴露 `ProductCatalog` 字段并传入 `FrameworkOptions`。下方为需追加的片段(合并进实际 `Configure`,保留 Phase 2/3 既有 options 构造与 entrypoint / build callback):

```csharp
// using 区追加:
using EasyFramework.Monetization.IAP;

// 字段区追加:
[Header("内购商品目录(可空)")]
[SerializeField] ProductCatalog _productCatalog;

// Configure 内构造 FrameworkOptions 时追加字段(保留既有 SaveDirectory/ConfigTables/...):
//   ProductCatalog = _productCatalog,
```

- [ ] **Step 5: 验证测试通过**(验证代理:`FrameworkInstallerTests` 全过,含新增 Monetization 断言)
- [ ] **Step 6: Commit**(编排者)`git commit -m "feat(boot): wire monetization services into installer, facade and root scope"`

---

### Task 6: 全量验证(串行,使用 UnityMCP)

- [ ] **Step 1: 刷新编译** — `refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false`。
- [ ] **Step 2: 0 error** — `read_console(types=["error"])`。Expected: 0 errors。
- [ ] **Step 3: EditMode 全量测试** — `run_tests(mode="EditMode")` → `get_test_job` 轮询。Expected: **Phase 1 + Phase 2 + Phase 3 全部既有用例 + Phase 4 新增全绿**(Phase 4 新增:AdsService 7、IAPService 7、AnalyticsService 7、FrameworkInstaller 升级后 +2 = 共约 23 个新用例)。
- [ ] **Step 4: 无新警告** — `read_console(types=["warning"])`。Expected: 无框架相关新警告(注意:Analytics 异常隔离测试、IAP 发奖异常入 pending 测试会在**测试运行时**主动 `Debug.LogWarning`,这是被测路径的预期输出,不计作框架缺陷;若验证代理用 `LogAssert.Expect` 收口更佳,否则在报告中注明这些是测试触发的预期警告)。
- [ ] **Step 5: Provider 真机路径编译核对** — 验证代理用 `unity_reflect` 核对 `UnityIAPProvider` 的 Unity IAP API(`IDetailedStoreListener`/`IStoreController`/`ConfigurationBuilder`/`PurchaseFailureReason` 等)与安装版本一致;`#if EF_ADMOB`/`#if EF_FIREBASE` 包住的接入槽默认未定义符号,**不参与编译**(只验证未定义时整体编译 0 error)。
- [ ] **Step 6: 偏差记录** — 若实现与计划有偏差(如 Unity IAP API 名调整、VContainer 多后端解析写法调整、`SceneLoadedEvent` 字段名差异),在本计划文档末尾追加 "Deviations" 小节记录。
- [ ] **Step 7: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- **Spec 覆盖:** Phase 4 范围 = 设计文档 §10 Phase 4 条目(Ads / IAP / Analytics)。逐项核对:
  - §5.1 Ads「业务 await 结果、Completed 才发奖、加载/重试/线程切换藏适配器、Fake 编辑器模拟、插屏频控(冷却+关卡间隔,远程配置可覆盖)」→ `AdsService`(频控 `IConfigService.Get("ads.interstitial_cooldown",30f)`/`("ads.interstitial_min_gap",1)`,Completed 才刷新冷却)+ `FakeAdsProvider`(模拟弹窗)+ `IAdsProvider` 适配点 + 全链路打点。✓
  - §5.2 IAP「商品目录 SO(id/消耗非消耗/奖励)、await 结果对象、发货可配置或手写、恢复购买、掉单补发(pending 队列持久化)、Fake 成功/取消/失败」→ `ProductCatalog` + `IAPService`(`RewardHandler` 可注册 / 默认 Debug.Log、`RestoreAsync`、pending 队列持久化 `ef.iap.pending` + `ReplayPending`、NonConsumable 已购 `ef.iap.owned`)+ `FakeIAPProvider` + `UnityIAPProvider`(真机真实现)。票据校验 `IReceiptValidator` 按 spec §9 为 YAGNI 不做,符合范围。✓
  - §5.3 Analytics「Track→IAnalyticsBackend 列表广播、新增后端不改打点、框架自动标准事件(启动/场景等)、编辑器 Backend 控制台打印」→ `AnalyticsService`(多后端广播 + §8 异常隔离)+ `DebugAnalyticsBackend` + `AnalyticsAutoTracker`(`boot_completed`/`scene_loaded`)+ `FirebaseAnalyticsBackend` 接入槽。✓
  - §3.1 / §8「商业化 SDK 初始化失败不阻塞启动、框架不向业务抛异常返回结果对象、第三方 SDK 异常只降级」→ 三个 BootTask `IsCritical=false`;`PurchaseAsync` 未初始化返回 `PurchaseResult{not_initialized}` 不抛;Analytics backend 异常只 `LogWarning`。✓
- **类型一致性:** 跨 Task 核对了锁定契约签名与测试/实现/Installer 三处一致:`AdResult`/`BannerPosition`/`IAdsProvider`/`IAdsService`、`ProductType`/`PurchaseResult`/`IIAPProvider`/`IIAPService`、`IAnalyticsBackend`/`IAnalyticsService`、`G.Ads/IAP/Analytics`、`FrameworkOptions.ProductCatalog`、三个 BootTask 的 `Priority`(30/31)/`IsCritical`(false)。衔接类型:`IConfigService.Get<T>(key, default)`/`Has`(Phase 2)、`SceneLoadedEvent.SceneName`(Phase 2)、`BootCompletedEvent`/`IBootTask.Priority/IsCritical/InitializeAsync`(Phase 1)、`VContainer.Unity.IStartable`、`FrameworkInstaller.Install(builder, options)`/`G.Initialize/Reset`(Phase 2/3 基线,本阶段只增量加字段/属性不改签名)。✓
  - 失败原因约定一致:取消=`"cancelled"`(`FakeIAPProvider.Cancelled` + `UnityIAPProvider` 的 `UserCancelled` 映射 + 测试断言)、未初始化=`"not_initialized"`(`IAPService` 与 `UnityIAPProvider` 双处一致 + 测试断言)。✓
- **占位符:** 无 TBD。三处「不写 EditMode 单测 / 空实现」均为偏差声明区明确许可的延后或设计接入槽,非占位符:(a) `AdMobAdsProvider`(`#if EF_ADMOB`)、`FirebaseAnalyticsBackend`(`#if EF_FIREBASE`)是设计好的接入槽,文件头注明非占位符 + 接入步骤;(b) `UnityIAPProvider` 是官方包真实现,仅真机路径不单测(可测的频控/掉单/广播逻辑已全抽业务层单测)。`FakeAdsProvider`/`FakeIAPProvider`/`DebugAnalyticsBackend` 是 spec 显式要求的 Fake/默认实现,非占位符。可能漂移处均给出具体回退动作:Unity IAP API 名 → `unity_reflect` 核对等效调整;VContainer 多后端解析 → 工厂 lambda 收集 `IReadOnlyList<IAnalyticsBackend>`;`SceneLoadedEvent` 字段名差异 → 按 Phase 2 实际等效调整;Unity IAP 未启用 → `#if UNITY_PURCHASING` 兜底。✓

---

## 自查发现并修正的问题(起草阶段)

1. **`AdsService` 时间源依赖 `Time.realtimeSinceStartup` 会破坏纯逻辑单测** —— 修正:抽 `internal Func<float> NowProvider` 构造参数(public 生产构造默认 `() => Time.realtimeSinceStartup`,internal 测试构造可注入假时钟数组),冷却/间隔逻辑全程不直接触 Unity 静态,EditMode 可控时间推进。
2. **「Completed 才刷新冷却」与「NotReady 不计冷却」语义需在频控门里精确分离** —— 修正:`PassesFrequencyGate` 拒绝(间隔未到 / 冷却内)直接返回 `NotReady` 且不打 `ad_show`、不动 `_lastInterstitialTime`;仅在 `ShowInterstitialAsync` 拿到 `result == AdResult.Completed` 时才刷新 `_lastInterstitialTime` 与清零间隔计数;Provider 返回 `NotReady`(已通过门但 provider 没货)同样不刷新冷却。测试 `Interstitial_NotReady_DoesNotConsumeCooldown` 锁定该行为。
3. **关卡间隔计数首次应放行,否则第一帧插屏永远被拦** —— 修正:构造时 `_interstitialCallsSinceShown = max(0, minGap-1)`,使首次调用即达到间隔门;之后每次成功展示清零。测试 `Interstitial_MinGap_BlocksBeforeEnoughCalls` 锁定。
4. **`IAPBootTask` 需调 `IAPService.ReplayPending`,但该方法不在 `IIAPService` 上** —— 修正:`FrameworkInstaller` 以具体类 `IAPService` 注册并 `.As<IIAPService>().AsSelf()`,BootTask 注入具体类;同理 `AdsBootTask` 注入 `IAdsProvider`(初始化适配点,非业务层)。
5. **发奖回调抛异常不应回滚购买,但奖励不能丢** —— 修正:`PurchaseAsync` 成功后 `TryGrantReward` try/catch,异常时把 `productId` 入 `ef.iap.pending`(去重 + 持久化),购买结果仍返回 `Success=true`;`ReplayPending`(`IAPBootTask` 启动调)逐个重放,成功移出、失败留队。测试 `RewardThrows_EnqueuesPending_AndReplaysOnNextStart` 锁定掉单补发闭环。
6. **未初始化购买必须返回失败结果而非抛异常(§8)** —— 修正:`IAPService.PurchaseAsync` 与 `UnityIAPProvider.PurchaseAsync` 双处在 `!IsInitialized` 时返回 `PurchaseResult{Success=false, FailureReason="not_initialized"}` 并打 `iap_purchase_fail`,不抛。测试 `Purchase_NotInitialized_ReturnsFailureResult_NoThrow` 锁定。
7. **Analytics 一个 backend 抛异常不能传染其它 backend 与调用方(§8)** —— 修正:`AnalyticsService.Dispatch`/`SetUserProperty` 对每个 backend 单独 try/catch,异常只 `Debug.LogWarning`,循环继续,调用方无感。测试 `Track_BackendThrows_IsolatedFromOthersAndCaller` 锁定。
8. **真实 SDK 接入槽易被误当占位符删掉** —— 修正:`AdMobAdsProvider`/`FirebaseAnalyticsBackend` 用 `#if EF_ADMOB`/`#if EF_FIREBASE` 包住,文件头大段注释明确「NOT a placeholder」+ 四步接入流程 + 锁定签名不变,默认 define 未定义故不参与编译、不污染 0-error 验证。
9. **`UnityIAPProvider` 与框架 `ProductType` 同名冲突** —— 修正:`UnityEngine.Purchasing.ProductType` 与 `EasyFramework.Monetization.IAP.ProductType` 同名不同命名空间,`UnityIAPProvider` 内用全限定名 `UnityEngine.Purchasing.ProductType.Consumable` 区分,避免 `using` 二义。
10. **多后端注册需让 `IAnalyticsService` 拿到全部 backend** —— 修正:`FrameworkInstaller` 以工厂 lambda 经 `c.Resolve<IReadOnlyList<IAnalyticsBackend>>()` 收集所有 `Register<IAnalyticsBackend, ...>` 多注册项;接 Firebase 时 `GameLifetimeScope` 再 `Register` 一个 `IAnalyticsBackend` 即并入广播,业务零改动。验证代理若发现该 VContainer 版本多注册解析 API 不同,按实际等效调整(目标:`AnalyticsService` 构造拿到含 Debug + 后续追加后端的列表)。
