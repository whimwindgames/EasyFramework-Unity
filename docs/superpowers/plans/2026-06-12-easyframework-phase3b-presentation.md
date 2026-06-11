# EasyFramework Phase 3b(表现层:Audio / Input / Camera / Juice / Localization / Haptics)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase 1 骨架 + Phase 2 资源数据层之上,实现表现层六个模块——音频(BGM 交叉淡变 + SFX 池)、输入(手势识别 + 虚拟摇杆 + 输入合流)、相机(Cinemachine 3.x 薄封装)、手感套件(punch/闪白/顿帧)、本地化(SO 表默认实现)、振动(平台分支),并接线进 `FrameworkInstaller` / `G` 门面 / `RootLifetimeScope`。可单测的纯逻辑(交叉淡变曲线、手势状态机、顿帧重入)全部带 EditMode 单测;薄视觉层(Cinemachine / PrimeTween 调用)以接口隔离、由验证代理用 `unity_reflect` 核对真实 API。

**Architecture:** 沿用「VContainer DI 内核 + 静态门面 `G`」。所有新服务落在 `EasyFramework.Services` 程序集,命名空间 `EasyFramework.Services.{Audio|Inputs|Cameras|Juice|Localization|Haptics}`,目录与命名空间一一对应。服务一律先接口后实现;Unity 静态依赖(`AudioSource`/`GameObject`/`Input System`/`Cinemachine`/`PrimeTween`/平台振动)抽到边界:纯逻辑(`CrossfadeState`、`GestureDetector`、`PinchDetector`、`HitStopState`、`TableLocalizationService`)在纯 `ContainerBuilder`(无 MonoBehaviour、无场景)下可 Build、可 EditMode 单测;视觉/平台调用经 internal 钩子或薄类隔离。GameObject 宿主(AudioSource 宿主、`[Pools]`-风格的相机根)一律**懒构建并 `DontDestroyOnLoad`,绝不在构造函数中触碰 `GameObject`**——构造函数只做字段赋值与事件订阅,保证纯容器单测与冷启动安全。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / **PrimeTween(OpenUPM)** / **Cinemachine 3.x** / **Unity Input System 1.19.0** / **TextMeshPro(随 com.unity.ugui 2.0.0)** / Unity Test Framework 1.6

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包/加 registry `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`、API 核对 `unity_reflect`)。
- **并行实现代理只允许用 Write/Edit 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(`CrossfadeState`/`GestureDetector`/`HitStopState`/`TableLocalizationService` 的同步路径,Fake 的所有 `UniTask` 均同步返回),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。
- **PlayerPrefs 测试污染:** Audio / Localization / Haptics 的持久化测试会写 `PlayerPrefs`(`ef.audio.bgm`、`ef.audio.sfx`、`ef.locale`、`ef.haptics`)。**所有触碰 PlayerPrefs 的 fixture 必须 `[SetUp]` 删除这些键、`[TearDown]` 再删一次并 `PlayerPrefs.Save()`**,避免跨用例 / 跨机污染。具体见各 Task 的测试代码。

---

## ⚠️ 偏差声明区(执行前必读)

### 偏差 1:Localization 不封装 Unity Localization 包,改用 ScriptableObject 表默认实现

**设计规格 §4.10** 要求「封装 Unity Localization:文本表 + 资源表、运行时切语言广播、UI 自动刷新」。本阶段**主动偏离**该实现路径:

- **理由:** Unity Localization 的运行时依赖一套编辑器侧资产配置(`LocalizationSettings`、`StringTableCollection`、`Locale` 资产、Addressables 组),这些必须在 Unity 编辑器里手工或脚本化创建。当前为**无人值守 agent-team 模式**,跑编辑器资产配置(创建 Locale、生成 String Table、绑定 Addressables)的失败率与不可重现性高,且难以用 EditMode 单测覆盖,风险显著高于收益。
- **本阶段决定:** 实现一个**自包含的 SO 表方案**——`LocalizationTable : ScriptableObject`(条目 `key` + 每 locale 一列字符串),`ILocalizationService` 由 `TableLocalizationService`(纯类,构造注入 `IReadOnlyList<LocalizationTable>` + `IEventBus`)实现,完全可 EditMode 单测;UI 侧 `LocalizedText` 组件订阅 `LocaleChangedEvent` 自动刷新。`FrameworkOptions` 新增 `LocalizationTables` 字段与 `DefaultLocale`(默认 `"zh-CN"`)。
- **扩展点(留作后续接入):** `ILocalizationService` 接口签名与 `LocaleChangedEvent` 不变。将来要接 Unity Localization,只需新增一个 `UnityLocalizationService : ILocalizationService` 适配器(包装 `LocalizationSettings.StringDatabase`),在 `GameLifetimeScope` 覆盖注册即可替换 `TableLocalizationService`,业务代码与 `LocalizedText` 组件零改动。本偏差不锁死规格目标,只推迟其编辑器资产依赖。

### 偏差 2:Camera / Juice 的视觉层 API 名以运行库实际为准

`Cinemachine 3.x`(类型 `CinemachineCamera` / `CinemachineImpulseSource` / `CinemachineConfiner2D`)与 `PrimeTween`(`Tween.PunchScale` / `Tween.Custom`)的**精确 API 名/签名**以安装到工程的版本为准。本计划按 Cinemachine 3.x 与 PrimeTween 最新稳定版书写;**验证代理在编译前用 `mcp__UnityMCP__unity_reflect` 核对** `Unity.Cinemachine.CinemachineCamera`、`Unity.Cinemachine.CinemachineImpulseSource`、`Unity.Cinemachine.CinemachineConfiner2D`、`PrimeTween.Tween` 的真实成员,若有出入按实际签名等效调整(接口签名 `ICameraService` / `IJuiceService` 不变)。这两层为**薄视觉/手感封装,不是占位符**,故按 spec 范围标注「不写 EditMode 单测」(可测的顿帧重入逻辑已抽 `HitStopState` 单独测)。

---

## 文件结构总览

```
Packages/manifest.json                                  (修改:加 OpenUPM scoped registry + primetween + cinemachine)
Packages/packages-lock.json                             (由 PM 写入)
Assets/EasyFramework/
├── Services/
│   ├── EasyFramework.Services.asmdef                   (修改:追加 PrimeTween / Unity.Cinemachine / Unity.InputSystem / Unity.TextMeshPro)
│   ├── Audio/
│   │   ├── IAudioService.cs                            (IAudioService)
│   │   ├── CrossfadeState.cs                           (纯类,EditMode 可测)
│   │   └── AudioService.cs                             (实现 ITickable,懒构建宿主)
│   ├── Inputs/
│   │   ├── IInputService.cs                            (IInputService + 手势事件结构体 + 枚举)
│   │   ├── IGestureEmitter.cs                          (干净发射接口)
│   │   ├── GestureDetector.cs                          (纯状态机,EditMode 可测)
│   │   ├── PinchDetector.cs                            (纯类,EditMode 可测)
│   │   ├── InputService.cs                             (实现 ITickable,读 Input System)
│   │   └── VirtualJoystick.cs                          (uGUI 组件 + 静态注册表)
│   ├── Cameras/
│   │   ├── ICameraService.cs                           (ICameraService)
│   │   └── CinemachineCameraService.cs                 (薄封装,不单测)
│   ├── Juice/
│   │   ├── IJuiceService.cs                            (IJuiceService)
│   │   ├── HitStopState.cs                             (纯类,重入逻辑,EditMode 可测)
│   │   └── JuiceService.cs                             (PrimeTween + ITimerService,薄层不单测)
│   ├── Localization/
│   │   ├── ILocalizationService.cs                     (ILocalizationService + LocaleChangedEvent)
│   │   ├── LocalizationTable.cs                        (ScriptableObject,CreateAssetMenu)
│   │   ├── TableLocalizationService.cs                 (纯类,EditMode 可测)
│   │   └── LocalizedText.cs                            (TMP_Text uGUI 组件)
│   └── Haptics/
│       ├── IHapticsService.cs                          (IHapticsService + HapticStrength)
│       └── HapticsService.cs                           (平台分支 + internal 振动钩子)
├── Boot/
│   ├── G.cs                                            (修改:新增 6 属性)
│   ├── FrameworkInstaller.cs                           (修改:FrameworkOptions 加 2 字段 + 注册 6 服务)
│   └── RootLifetimeScope.cs                            (修改:options 传 LocalizationTables/DefaultLocale + 挂 Audio/Input Tick)
└── Tests/EditMode/
    ├── EasyFramework.Tests.EditMode.asmdef             (修改:追加 PrimeTween / Cinemachine / InputSystem / TMP)
    ├── CrossfadeStateTests.cs
    ├── AudioServiceTests.cs                            (音量 clamp + PlayerPrefs 持久化往返)
    ├── GestureDetectorTests.cs
    ├── PinchDetectorTests.cs
    ├── HitStopStateTests.cs
    ├── TableLocalizationServiceTests.cs
    ├── HapticsServiceTests.cs                          (Enabled 持久化 + 开关拦截)
    └── FrameworkInstallerTests.cs                      (修改:扩展断言 6 个新服务 + G 绑定)
```

依赖方向不变:`Tests → Boot → {Core, Services, Monetization}`;`Services → Core`。Phase 3b 只在 `Services` 内新增六个目录,并修改 `Boot` 的三个文件与两个 asmdef。

> **与 Phase 3a(UI)的关系:** Phase 3a(UI 框架)依赖本 Phase 的 **Task 1 安装的 PrimeTween**(面板入出场动画)。Task 1 的包安装与 asmdef 升级是 Phase 3a 与 3b 的共同前置;若 Phase 3a 计划独立装 PrimeTween,二者择一执行,**registry + 包只装一次**(`manage_packages` 重复 add 幂等)。

---

### Task 1: 包 + asmdef 统一升级(串行,使用 UnityMCP)

**Files:**
- Modify: `Packages/manifest.json`(加 OpenUPM scoped registry + 由 PM 写入 primetween/cinemachine)
- Modify: `Assets/EasyFramework/Services/EasyFramework.Services.asmdef`
- Modify: `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

> 本 Task 是 Phase 3a(UI)与 3b 全部模块的前置。注:Phase 3a 计划依赖本 Task 的 PrimeTween。

- [ ] **Step 1: 添加 OpenUPM scoped registry 并安装 PrimeTween**

用 `manage_packages` 添加 scoped registry(若工具支持 `add_scoped_registry`/`add_registry`;否则直接编辑 `Packages/manifest.json` 顶层加 `scopedRegistries`),再装 `com.kyrylokuzyk.primetween`。registry 配置:

```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": ["com.kyrylokuzyk"]
  }
]
```

然后 `manage_packages` add：`com.kyrylokuzyk.primetween`(不带版本号,解析为 OpenUPM 当前最新稳定版;若工具支持 `list_versions` 先查到具体 `x.y.z` 再装)。

> 说明:`scopedRegistries` 是 `manifest.json` 与 `dependencies` 平级的顶层数组。PrimeTween 程序集名为 `PrimeTween`(asmdef 引用名),命名空间 `PrimeTween`。

- [ ] **Step 2: 安装 Cinemachine 3.x**

`manage_packages` 查 `com.unity.cinemachine` 在 Unity 6000.3.15f1 下的最新稳定版(`list_versions`/`search`;Unity 6 推荐 Cinemachine 3.x,程序集名 `Unity.Cinemachine`,命名空间 `Unity.Cinemachine`)。装最新 `3.x` 稳定版:

```
com.unity.cinemachine   (3.x 最新稳定版,由 manage_packages 解析)
```

> 注:`com.unity.inputsystem`(1.19.0)与 `com.unity.ugui`(2.0.0,提供 TextMeshPro / 程序集 `Unity.TextMeshPro`)已在 manifest 中,无需再装,只需在 asmdef 引用。

- [ ] **Step 3: 升级 Services asmdef** — `Assets/EasyFramework/Services/EasyFramework.Services.asmdef`

**执行前先 `Read` 实际落地文件**(Phase 2 已升级过 references,含 Addressables/ResourceManager/Newtonsoft precompiled)。以 Phase 2 升级后的版本为基线,**追加** `"PrimeTween"`、`"Unity.Cinemachine"`、`"Unity.InputSystem"`、`"Unity.TextMeshPro"` 到 `references` 数组(保留既有项,不要删 Addressables/ResourceManager/Newtonsoft 配置)。合并后完整内容:

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
    "Unity.ResourceManager",
    "PrimeTween",
    "Unity.Cinemachine",
    "Unity.InputSystem",
    "Unity.TextMeshPro"
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

> 若实际落地的 Phase 2 asmdef 与上方基线有差异(例如 references 顺序/是否含 ResourceManager),以**实际文件为基线增量追加**这四项,不要机械整体覆盖。`Unity.TextMeshPro` 供 `LocalizedText` 用(`TMPro.TMP_Text`);`Unity.InputSystem` 供 `InputService`(`Touchscreen`/`Mouse`/`Keyboard`);`Unity.Cinemachine` 供 Camera;`PrimeTween` 供 Juice。`Unity.InputSystem` 同时需要 uGUI 的 `EventSystem`——`EventSystem` 在 `UnityEngine.UI`(引擎模块,`noEngineReferences:false` 时自动可用),无需单列。

- [ ] **Step 4: 升级 Tests asmdef** — `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

**执行前先 `Read` 实际落地文件**(Phase 2 已升级,含 Services/Addressables/Newtonsoft)。以 Phase 2 升级后版本为基线,同步追加 `"PrimeTween"`、`"Unity.Cinemachine"`、`"Unity.InputSystem"`、`"Unity.TextMeshPro"`(测试要 new `JuiceService`/`InputService` 间接引用、构造 `VirtualJoystick`/`LocalizedText` 组件需要这些程序集可见)。合并后完整内容:

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
    "PrimeTween",
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

- [ ] **Step 5: 验证编译干净**

`refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false` → `read_console(types=["error"])`。Expected: 0 errors。Cinemachine 首次导入可能生成 `Assets/Settings`/采样资产,属正常产物。

- [ ] **Step 6: Commit**(编排者执行)

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/EasyFramework
git commit -m "feat: add PrimeTween (OpenUPM) + Cinemachine 3.x, upgrade asmdef references"
```

---

### Task 2: Audio 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Audio/IAudioService.cs`, `CrossfadeState.cs`, `AudioService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/CrossfadeStateTests.cs`, `AudioServiceTests.cs`

设计要点:BGM 双 `AudioSource` 交叉淡变(`AudioService : ITickable` 每帧手动 lerp,不依赖 PrimeTween——淡变逻辑抽成纯类 `CrossfadeState` 供 EditMode 测);SFX 用 8 个 `AudioSource` 轮转池;clip 经 `IAssetService.LoadAsync<AudioClip>` 加载;音量 setter 立即生效、`Mathf.Clamp01` + `PlayerPrefs` 持久化(键 `ef.audio.bgm` / `ef.audio.sfx`)。AudioSource 宿主 GameObject **懒构建**(首次播放时 `new GameObject` + `DontDestroyOnLoad`),**构造函数不碰 GameObject**。

- [ ] **Step 1: 写失败测试** — `CrossfadeStateTests.cs`

`CrossfadeState` 是纯结构/类:持有 `InVolume`/`OutVolume`(0~1)与剩余时间,`Advance(dt)` 推进、`IsDone` 标记完成。验证:线性曲线在中点各为 0.5 倍目标、到时钳到端点、`fadeSeconds<=0` 立即完成、目标音量(`targetVolume`)缩放。

```csharp
using EasyFramework.Services.Audio;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class CrossfadeStateTests
    {
        [Test]
        public void Advance_AtMidpoint_VolumesAreHalf()
        {
            // 从静默淡入到 targetVolume=1,用时 1s;出声道从 1 淡出
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(0.5f);
            Assert.AreEqual(0.5f, s.InVolume, 1e-4f);
            Assert.AreEqual(0.5f, s.OutVolume, 1e-4f);
            Assert.IsFalse(s.IsDone);
        }

        [Test]
        public void Advance_PastDuration_ClampsToEndpoints()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(2f);
            Assert.AreEqual(1f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
            Assert.IsTrue(s.IsDone);
        }

        [Test]
        public void TargetVolume_ScalesInChannel()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 0.6f);
            s.Advance(1f);
            Assert.AreEqual(0.6f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
        }

        [Test]
        public void ZeroFade_CompletesImmediately()
        {
            var s = new CrossfadeState(fadeSeconds: 0f, targetVolume: 1f);
            s.Advance(0f);
            Assert.IsTrue(s.IsDone);
            Assert.AreEqual(1f, s.InVolume, 1e-4f);
            Assert.AreEqual(0f, s.OutVolume, 1e-4f);
        }

        [Test]
        public void Advance_AccumulatesAcrossMultipleSteps()
        {
            var s = new CrossfadeState(fadeSeconds: 1f, targetVolume: 1f);
            s.Advance(0.25f);
            s.Advance(0.25f);
            Assert.AreEqual(0.5f, s.InVolume, 1e-4f);
            Assert.AreEqual(0.5f, s.OutVolume, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IAudioService.cs`

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Audio
{
    public interface IAudioService
    {
        UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f);
        void StopBgm(float fadeSeconds = 0.3f);
        void PlaySfx(string key, float volume = 1f);
        float BgmVolume { get; set; }   // 0~1, 持久化 PlayerPrefs "ef.audio.bgm"
        float SfxVolume { get; set; }   // 0~1, 持久化 PlayerPrefs "ef.audio.sfx"
    }
}
```

- [ ] **Step 3: 实现纯类** — `CrossfadeState.cs`

线性交叉淡变:进声道 `InVolume` 从 0→`targetVolume`,出声道 `OutVolume` 从 `targetVolume`→0,用时 `fadeSeconds`。`fadeSeconds<=0` 视为瞬时完成。

```csharp
using UnityEngine;

namespace EasyFramework.Services.Audio
{
    /// <summary>BGM 交叉淡变的纯逻辑(无 Unity 对象依赖),供 ITickable 手动驱动与 EditMode 单测。</summary>
    public sealed class CrossfadeState
    {
        readonly float _duration;
        readonly float _target;
        float _elapsed;

        public CrossfadeState(float fadeSeconds, float targetVolume)
        {
            _duration = Mathf.Max(0f, fadeSeconds);
            _target = Mathf.Clamp01(targetVolume);
            _elapsed = _duration <= 0f ? 1f : 0f; // 0 时长直接完成
        }

        /// <summary>淡入声道当前音量(0..target)。</summary>
        public float InVolume
        {
            get
            {
                var t = Progress;
                return _target * t;
            }
        }

        /// <summary>淡出声道当前音量(target..0)。</summary>
        public float OutVolume
        {
            get
            {
                var t = Progress;
                return _target * (1f - t);
            }
        }

        public bool IsDone => Progress >= 1f;

        float Progress => _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);

        public void Advance(float deltaTime)
        {
            if (_duration <= 0f) { _elapsed = 1f; return; }
            _elapsed += deltaTime;
        }
    }
}
```

- [ ] **Step 4: 写失败测试** — `AudioServiceTests.cs`(音量 clamp + PlayerPrefs 持久化往返)

`AudioService` 的播放路径依赖 `AudioSource`/`GameObject`(EditMode 难测、且任务书指明只测「音量 clamp 与持久化往返」)。验证:setter clamp01(传 1.5→1、-0.2→0)、setter 写 PlayerPrefs、新实例从 PlayerPrefs 读回初值。**构造 `AudioService` 不得触碰 GameObject**——只传 `IAssetService`,本测试不调播放方法,故宿主永不构建。

```csharp
using System.Collections.Generic;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class AudioServiceTests
    {
        const string BgmKey = "ef.audio.bgm";
        const string SfxKey = "ef.audio.sfx";

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.Save();
        }

        static AudioService Make()
            => new AudioService(new FakeAssetService(new Dictionary<string, Object>()));

        [Test]
        public void BgmVolume_Setter_Clamps01()
        {
            var svc = Make();
            svc.BgmVolume = 1.5f;
            Assert.AreEqual(1f, svc.BgmVolume, 1e-4f);
            svc.BgmVolume = -0.2f;
            Assert.AreEqual(0f, svc.BgmVolume, 1e-4f);
        }

        [Test]
        public void SfxVolume_Setter_Clamps01()
        {
            var svc = Make();
            svc.SfxVolume = 2f;
            Assert.AreEqual(1f, svc.SfxVolume, 1e-4f);
            svc.SfxVolume = -1f;
            Assert.AreEqual(0f, svc.SfxVolume, 1e-4f);
        }

        [Test]
        public void BgmVolume_Setter_PersistsToPlayerPrefs()
        {
            var svc = Make();
            svc.BgmVolume = 0.42f;
            Assert.AreEqual(0.42f, PlayerPrefs.GetFloat(BgmKey, -1f), 1e-4f);
        }

        [Test]
        public void Volumes_RoundTripThroughPlayerPrefs()
        {
            var svc = Make();
            svc.BgmVolume = 0.3f;
            svc.SfxVolume = 0.7f;

            var svc2 = Make(); // 新实例从 PlayerPrefs 读回
            Assert.AreEqual(0.3f, svc2.BgmVolume, 1e-4f);
            Assert.AreEqual(0.7f, svc2.SfxVolume, 1e-4f);
        }

        [Test]
        public void DefaultVolumes_AreOneWhenNoPrefs()
        {
            var svc = Make();
            Assert.AreEqual(1f, svc.BgmVolume, 1e-4f);
            Assert.AreEqual(1f, svc.SfxVolume, 1e-4f);
        }
    }
}
```

- [ ] **Step 5: 实现 AudioService** — `AudioService.cs`

`ITickable`(`VContainer.Unity`)每帧推进活跃 `CrossfadeState` 并把 `In/OutVolume * BgmVolume` 写到两个 BGM `AudioSource`;SFX 池 8 个轮转。构造函数只存 `IAssetService` 与从 PlayerPrefs 读音量,**不建 GameObject**;`EnsureHost()` 在首次播放时懒建宿主(`DontDestroyOnLoad`)。

```csharp
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

        readonly IAssetService _assets;

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

        public AudioService(IAssetService assets)
        {
            _assets = assets;
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

> 视觉/播放层不单测(spec 许可:Audio 播放走 `AudioSource`)。可测的 `CrossfadeState`(Step 1/3)与音量 clamp+持久化(Step 4)已覆盖任务书要求。`Tick` 用 `Time.unscaledDeltaTime` 使暂停时淡变仍进行(BGM 不随 timeScale 停)。`_aIsActive` 初值与首次 `PlayBgmAsync` 的切换约定:首次播放时 incoming = B(因 `_aIsActive==true` 取 `_bgmB`),播完切 `_aIsActive=false`,B 成为 active——实现代理验证此交替不影响外部行为即可,无外部可观察契约。

- [ ] **Step 6: 验证测试通过**(验证代理:`run_tests` 过滤 `CrossfadeStateTests`、`AudioServiceTests`)Expected: 全 PASS。
- [ ] **Step 7: Commit**(编排者)`git commit -m "feat(services): add audio service with crossfade bgm and sfx pool"`

---

### Task 3: Input 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Inputs/IInputService.cs`, `IGestureEmitter.cs`, `GestureDetector.cs`, `PinchDetector.cs`, `InputService.cs`, `VirtualJoystick.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/GestureDetectorTests.cs`, `PinchDetectorTests.cs`

设计要点:核心是纯类 `GestureDetector`(无 Unity 输入依赖),`OnPointerDown/Move/Up(Vector2 pos, float time)` 状态机判定 Tap / LongPress / Swipe / Drag;`PinchDetector` 处理双指距离变化→`DeltaScale`;手势经构造注入的 `IGestureEmitter`(干净发射接口)发出。`InputService : ITickable` 读 Input System(`Touchscreen.current`/`Mouse.current`)喂样本、查 `EventSystem.current.IsPointerOverGameObject` 缓存为 `IsPointerOverUI`、合并 `VirtualJoystick` 与键盘 WASD 为 `MoveAxis`。`VirtualJoystick` 为 uGUI 组件 + 静态注册表。

**判定阈值(锁定常量,测试与实现共用):**
- Tap:松手时 时长 `< 0.3s` 且 总位移 `< 20px`
- LongPress:按住 `>= 0.5s` 且 期间位移保持静止(`< 20px`)
- Swipe:松手时 位移 `>= 80px` 且 时长 `< 0.5s`,方向取主轴(|dx| vs |dy| 大者,正负定四向)
- Drag:位移一旦超过 `20px` 阈值,进入 Drag 模式,逐帧(每次 `OnPointerMove`)发 `DragEvent(Move)`;`OnPointerDown` 后超阈值首次发 `Start`,松手发 `End`

- [ ] **Step 1: 写失败测试** — `GestureDetectorTests.cs`

用记录型 `IGestureEmitter` 捕获发出的事件。覆盖典型 + 边界:Tap(快速小位移)、非 Tap(超时或超位移)、LongPress(久按静止)、LongPress 被位移打断、Swipe 四向、Swipe 太慢不算、Drag 起止与逐帧。

```csharp
using System.Collections.Generic;
using EasyFramework.Services.Inputs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class GestureDetectorTests
    {
        sealed class RecordingEmitter : IGestureEmitter
        {
            public readonly List<TapEvent> Taps = new();
            public readonly List<LongPressEvent> LongPresses = new();
            public readonly List<SwipeEvent> Swipes = new();
            public readonly List<DragEvent> Drags = new();
            public void Emit(TapEvent e) => Taps.Add(e);
            public void Emit(LongPressEvent e) => LongPresses.Add(e);
            public void Emit(SwipeEvent e) => Swipes.Add(e);
            public void Emit(DragEvent e) => Drags.Add(e);
        }

        static (GestureDetector d, RecordingEmitter e) Make()
        {
            var em = new RecordingEmitter();
            return (new GestureDetector(em), em);
        }

        [Test]
        public void QuickSmallMove_EmitsTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(105, 103), 0.1f); // <0.3s, 位移<20px
            Assert.AreEqual(1, e.Taps.Count);
            Assert.AreEqual(0, e.Swipes.Count);
        }

        [Test]
        public void SlowRelease_NotTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(102, 102), 0.4f); // >0.3s
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void LargeMove_NotTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(130, 100), 0.1f); // 位移 30 >=20,但 <80 不算 swipe,且 >20 不算 tap
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void HeldStill_EmitsLongPress()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(101, 101), 0.5f); // >=0.5s 静止
            Assert.AreEqual(1, e.LongPresses.Count);
        }

        [Test]
        public void LongPress_FiresOnce()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(100, 100), 0.6f);
            d.OnPointerMove(new Vector2(100, 100), 0.7f);
            Assert.AreEqual(1, e.LongPresses.Count, "长按只触发一次");
        }

        [Test]
        public void MoveBeyondThreshold_CancelsLongPress()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(130, 100), 0.2f); // 超 20px,转 Drag
            d.OnPointerMove(new Vector2(131, 100), 0.6f); // 即便过 0.5s 也不再 longpress
            Assert.AreEqual(0, e.LongPresses.Count);
            Assert.Greater(e.Drags.Count, 0);
        }

        [Test]
        public void FastLongMoveRight_EmitsSwipeRight()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(200, 110), 0.2f); // dx=100>=80, <0.5s, 主轴 X 正
            Assert.AreEqual(1, e.Swipes.Count);
            Assert.AreEqual(SwipeDirection.Right, e.Swipes[0].Direction);
        }

        [Test]
        public void FastLongMoveUp_EmitsSwipeUp()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(110, 200), 0.2f); // dy=100, 主轴 Y 正(屏幕坐标 Y 向上)
            Assert.AreEqual(SwipeDirection.Up, e.Swipes[0].Direction);
        }

        [Test]
        public void FastLongMoveLeftAndDown()
        {
            var (d1, e1) = Make();
            d1.OnPointerDown(new Vector2(200, 100), 0f);
            d1.OnPointerUp(new Vector2(100, 110), 0.2f);
            Assert.AreEqual(SwipeDirection.Left, e1.Swipes[0].Direction);

            var (d2, e2) = Make();
            d2.OnPointerDown(new Vector2(100, 200), 0f);
            d2.OnPointerUp(new Vector2(110, 100), 0.2f);
            Assert.AreEqual(SwipeDirection.Down, e2.Swipes[0].Direction);
        }

        [Test]
        public void SlowLongMove_NotSwipe_NoTap()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerUp(new Vector2(200, 100), 0.6f); // 位移够但 >=0.5s,非 swipe;且 drag 已起,松手发 End
            Assert.AreEqual(0, e.Swipes.Count);
            Assert.AreEqual(0, e.Taps.Count);
        }

        [Test]
        public void Drag_EmitsStartMoveEnd()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(125, 100), 0.05f); // 超阈值,Start
            d.OnPointerMove(new Vector2(150, 100), 0.1f);  // Move
            d.OnPointerUp(new Vector2(150, 100), 0.15f);   // End
            Assert.AreEqual(DragPhase.Start, e.Drags[0].Phase);
            Assert.AreEqual(DragPhase.Move, e.Drags[1].Phase);
            Assert.AreEqual(DragPhase.End, e.Drags[^1].Phase);
        }

        [Test]
        public void Drag_DeltaIsBetweenConsecutivePositions()
        {
            var (d, e) = Make();
            d.OnPointerDown(new Vector2(100, 100), 0f);
            d.OnPointerMove(new Vector2(130, 100), 0.05f); // Start, delta 从按下点算
            d.OnPointerMove(new Vector2(140, 100), 0.1f);  // Move, delta = (10,0)
            var move = e.Drags[1];
            Assert.AreEqual(new Vector2(10, 0), move.Delta);
        }
    }
}
```

- [ ] **Step 2: 实现接口 + 事件结构体 + 枚举 + 发射接口** — `IInputService.cs`

```csharp
namespace EasyFramework.Services.Inputs
{
    public enum SwipeDirection { Up, Down, Left, Right }

    public enum DragPhase { Start, Move, End }

    public readonly struct TapEvent
    {
        public readonly UnityEngine.Vector2 ScreenPosition;
        public TapEvent(UnityEngine.Vector2 pos) => ScreenPosition = pos;
    }

    public readonly struct LongPressEvent
    {
        public readonly UnityEngine.Vector2 ScreenPosition;
        public LongPressEvent(UnityEngine.Vector2 pos) => ScreenPosition = pos;
    }

    public readonly struct SwipeEvent
    {
        public readonly UnityEngine.Vector2 Start;
        public readonly UnityEngine.Vector2 End;
        public readonly SwipeDirection Direction;
        public SwipeEvent(UnityEngine.Vector2 start, UnityEngine.Vector2 end, SwipeDirection dir)
        {
            Start = start; End = end; Direction = dir;
        }
    }

    public readonly struct DragEvent
    {
        public readonly DragPhase Phase;
        public readonly UnityEngine.Vector2 Position;
        public readonly UnityEngine.Vector2 Delta;
        public DragEvent(DragPhase phase, UnityEngine.Vector2 position, UnityEngine.Vector2 delta)
        {
            Phase = phase; Position = position; Delta = delta;
        }
    }

    public readonly struct PinchEvent
    {
        public readonly float DeltaScale;
        public PinchEvent(float deltaScale) => DeltaScale = deltaScale;
    }

    public interface IInputService
    {
        UnityEngine.Vector2 MoveAxis { get; }
        bool IsPointerOverUI { get; }
    }
}
```

- [ ] **Step 3: 实现发射接口** — `IGestureEmitter.cs`

```csharp
namespace EasyFramework.Services.Inputs
{
    /// <summary>手势检测器的输出口;生产实现把事件转发到 IEventBus,测试用记录型替身。</summary>
    public interface IGestureEmitter
    {
        void Emit(TapEvent e);
        void Emit(LongPressEvent e);
        void Emit(SwipeEvent e);
        void Emit(DragEvent e);
    }
}
```

- [ ] **Step 4: 实现 GestureDetector** — `GestureDetector.cs`(纯状态机)

```csharp
using UnityEngine;

namespace EasyFramework.Services.Inputs
{
    /// <summary>单指手势状态机,纯逻辑无 Unity 输入依赖。坐标为屏幕像素,Y 向上。</summary>
    public sealed class GestureDetector
    {
        public const float TapMaxDuration = 0.3f;
        public const float MoveThreshold = 20f;      // 超过即非 Tap / 进 Drag
        public const float LongPressDuration = 0.5f;
        public const float SwipeMinDistance = 80f;
        public const float SwipeMaxDuration = 0.5f;

        readonly IGestureEmitter _emitter;

        bool _down;
        Vector2 _startPos;
        float _startTime;
        Vector2 _lastPos;
        bool _dragging;
        bool _longPressFired;

        public GestureDetector(IGestureEmitter emitter) => _emitter = emitter;

        public void OnPointerDown(Vector2 pos, float time)
        {
            _down = true;
            _startPos = _lastPos = pos;
            _startTime = time;
            _dragging = false;
            _longPressFired = false;
        }

        public void OnPointerMove(Vector2 pos, float time)
        {
            if (!_down) return;
            var totalDist = Vector2.Distance(pos, _startPos);

            if (!_dragging && totalDist >= MoveThreshold)
            {
                _dragging = true;
                _emitter.Emit(new DragEvent(DragPhase.Start, pos, pos - _startPos));
                _lastPos = pos;
                return;
            }

            if (_dragging)
            {
                _emitter.Emit(new DragEvent(DragPhase.Move, pos, pos - _lastPos));
                _lastPos = pos;
                return;
            }

            // 仍静止:检查长按
            if (!_longPressFired && totalDist < MoveThreshold
                && time - _startTime >= LongPressDuration)
            {
                _longPressFired = true;
                _emitter.Emit(new LongPressEvent(pos));
            }
            _lastPos = pos;
        }

        public void OnPointerUp(Vector2 pos, float time)
        {
            if (!_down) return;
            _down = false;
            var duration = time - _startTime;
            var delta = pos - _startPos;
            var dist = delta.magnitude;

            if (_dragging)
            {
                _emitter.Emit(new DragEvent(DragPhase.End, pos, pos - _lastPos));
                return;
            }

            if (_longPressFired) return; // 已长按,松手不再判其它

            if (dist >= SwipeMinDistance && duration < SwipeMaxDuration)
            {
                _emitter.Emit(new SwipeEvent(_startPos, pos, MainAxis(delta)));
                return;
            }

            if (dist < MoveThreshold && duration < TapMaxDuration)
            {
                _emitter.Emit(new TapEvent(pos));
            }
            // 其余(慢速大位移但未触发 drag 等)不发任何事件
        }

        static SwipeDirection MainAxis(Vector2 d)
        {
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
                return d.x >= 0 ? SwipeDirection.Right : SwipeDirection.Left;
            return d.y >= 0 ? SwipeDirection.Up : SwipeDirection.Down;
        }
    }
}
```

> 设计说明:`MoveThreshold`(20px)同时担当「非 Tap 阈值」与「进 Drag 阈值」。`SlowLongMove_NotSwipe_NoTap` 用例(位移 100、时长 0.6s)走 `OnPointerUp` 时 `_dragging` 为何?——该用例无 `OnPointerMove`,直接 down→up,故 `_dragging==false`,走 swipe 分支但 `duration>=0.5` 不满足,再走 tap 分支但 `dist>=20` 不满足,结果不发事件,与断言一致。`Drag_EmitsStartMoveEnd` 走 move 触发 drag。两条路径互不矛盾。

- [ ] **Step 5: 写失败测试** — `PinchDetectorTests.cs`

`PinchDetector`:`Update(Vector2 finger0, Vector2 finger1)` 计算当前双指距离,与上一帧距离比得 `DeltaScale`(当前/上一帧),首帧只记录基线不发;`Reset()` 清基线(松指)。验证:放大(距离增大→DeltaScale>1)、缩小(<1)、首帧不发、reset 后重新建立基线。

```csharp
using System.Collections.Generic;
using EasyFramework.Services.Inputs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class PinchDetectorTests
    {
        sealed class Sink
        {
            public readonly List<float> Scales = new();
            public void OnPinch(PinchEvent e) => Scales.Add(e.DeltaScale);
        }

        [Test]
        public void FirstFrame_OnlySetsBaseline_NoEmit()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0)); // 距离 10
            Assert.AreEqual(0, sink.Scales.Count);
        }

        [Test]
        public void Widening_EmitsScaleGreaterThanOne()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0));  // 基线 10
            p.Update(new Vector2(0, 0), new Vector2(20, 0));  // 20/10 = 2
            Assert.AreEqual(1, sink.Scales.Count);
            Assert.AreEqual(2f, sink.Scales[0], 1e-4f);
        }

        [Test]
        public void Narrowing_EmitsScaleLessThanOne()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(20, 0));  // 基线 20
            p.Update(new Vector2(0, 0), new Vector2(10, 0));  // 10/20 = 0.5
            Assert.AreEqual(0.5f, sink.Scales[0], 1e-4f);
        }

        [Test]
        public void Reset_RebuildsBaseline()
        {
            var sink = new Sink();
            var p = new PinchDetector(sink.OnPinch);
            p.Update(new Vector2(0, 0), new Vector2(10, 0));
            p.Reset();                                        // 松指
            p.Update(new Vector2(0, 0), new Vector2(30, 0));  // 新基线 30,不发
            Assert.AreEqual(0, sink.Scales.Count);
            p.Update(new Vector2(0, 0), new Vector2(60, 0));  // 60/30 = 2
            Assert.AreEqual(2f, sink.Scales[0], 1e-4f);
        }
    }
}
```

- [ ] **Step 6: 实现 PinchDetector** — `PinchDetector.cs`

```csharp
using System;
using UnityEngine;

namespace EasyFramework.Services.Inputs
{
    /// <summary>双指捏合检测,纯逻辑。DeltaScale = 当前帧双指距离 / 上一帧距离。</summary>
    public sealed class PinchDetector
    {
        readonly Action<PinchEvent> _onPinch;
        float _lastDistance;
        bool _hasBaseline;

        public PinchDetector(Action<PinchEvent> onPinch) => _onPinch = onPinch;

        public void Update(Vector2 finger0, Vector2 finger1)
        {
            var dist = Vector2.Distance(finger0, finger1);
            if (!_hasBaseline || _lastDistance <= Mathf.Epsilon)
            {
                _lastDistance = dist;
                _hasBaseline = true;
                return;
            }
            var scale = dist / _lastDistance;
            _lastDistance = dist;
            _onPinch(new PinchEvent(scale));
        }

        /// <summary>松指或单指,清除基线;下次 Update 重新建立。</summary>
        public void Reset()
        {
            _hasBaseline = false;
            _lastDistance = 0f;
        }
    }
}
```

- [ ] **Step 7: 实现 VirtualJoystick** — `VirtualJoystick.cs`(uGUI 组件 + 静态注册表)

输出归一化 `Vector2`(死区外 0..1);静态注册表 `Active` 供 `InputService` 读取(支持多摇杆取首个活跃)。

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.Services.Inputs
{
    /// <summary>uGUI 虚拟摇杆。输出归一化方向(死区外 magnitude 0..1)。静态注册表供 InputService 合流。</summary>
    public sealed class VirtualJoystick : MonoBehaviour,
        IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        static readonly List<VirtualJoystick> _registry = new();

        /// <summary>当前所有活跃(被按下)的摇杆中第一个的输出;无则 Vector2.zero。</summary>
        public static Vector2 CombinedAxis
        {
            get
            {
                foreach (var j in _registry)
                    if (j != null && j._pressed)
                        return j.Value;
                return Vector2.zero;
            }
        }

        [SerializeField] RectTransform _handle;
        [SerializeField] RectTransform _background;
        [SerializeField] float _radius = 80f;

        Vector2 _value;
        bool _pressed;

        public Vector2 Value => _value;

        void OnEnable() { if (!_registry.Contains(this)) _registry.Add(this); }
        void OnDisable() { _registry.Remove(this); ResetStick(); }

        public void OnPointerDown(PointerEventData e)
        {
            _pressed = true;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            var origin = _background != null
                ? (Vector2)_background.position
                : (Vector2)transform.position;
            var offset = e.position - origin;
            var clamped = Vector2.ClampMagnitude(offset, _radius);
            _value = _radius > 0f ? clamped / _radius : Vector2.zero;
            if (_handle != null) _handle.position = origin + clamped;
        }

        public void OnPointerUp(PointerEventData e) => ResetStick();

        void ResetStick()
        {
            _pressed = false;
            _value = Vector2.zero;
            if (_handle != null && _background != null)
                _handle.position = _background.position;
        }
    }
}
```

- [ ] **Step 8: 实现 InputService** — `InputService.cs`(`ITickable`,读 Input System)

`ITickable` 每帧:读 `Touchscreen.current`/`Mouse.current` 喂 `GestureDetector`/`PinchDetector`(把它们的 `IGestureEmitter` 接到 `IEventBus`);缓存 `EventSystem.current.IsPointerOverGameObject()` 为 `IsPointerOverUI`;合并 `VirtualJoystick.CombinedAxis` 与 `Keyboard.current` 的 WASD 为 `MoveAxis`。**薄输入读取层不单测**(纯逻辑 GestureDetector/PinchDetector 已测);构造函数只存依赖,不碰 Input System 静态(Input System 静态在无设备时返回 null,Tick 里防空)。

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Services.Inputs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer.Unity;

namespace EasyFramework.Services.Inputs
{
    /// <summary>事件总线发射器:把手势转发到 IEventBus。</summary>
    internal sealed class EventBusGestureEmitter : IGestureEmitter
    {
        readonly IEventBus _bus;
        public EventBusGestureEmitter(IEventBus bus) => _bus = bus;
        public void Emit(TapEvent e) => _bus.Publish(e);
        public void Emit(LongPressEvent e) => _bus.Publish(e);
        public void Emit(SwipeEvent e) => _bus.Publish(e);
        public void Emit(DragEvent e) => _bus.Publish(e);
    }

    public sealed class InputService : IInputService, ITickable
    {
        readonly IEventBus _bus;
        readonly GestureDetector _gestures;
        readonly PinchDetector _pinch;

        bool _pointerWasDown;

        public Vector2 MoveAxis { get; private set; }
        public bool IsPointerOverUI { get; private set; }

        public InputService(IEventBus bus)
        {
            _bus = bus;
            _gestures = new GestureDetector(new EventBusGestureEmitter(bus));
            _pinch = new PinchDetector(e => _bus.Publish(e));
        }

        public void Tick()
        {
            var time = Time.unscaledTime;
            IsPointerOverUI = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();

            FeedPointer(time);
            FeedPinch();
            MoveAxis = ResolveMoveAxis();
        }

        void FeedPointer(float time)
        {
            Vector2 pos;
            bool down;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                pos = touch.primaryTouch.position.ReadValue();
                down = true;
            }
            else if (touch != null && touch.primaryTouch.press.wasReleasedThisFrame)
            {
                pos = touch.primaryTouch.position.ReadValue();
                down = false;
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null) { HandleRelease(time); return; }
                pos = mouse.position.ReadValue();
                down = mouse.leftButton.isPressed;
            }

            if (down && !_pointerWasDown) { _gestures.OnPointerDown(pos, time); _pointerWasDown = true; }
            else if (down && _pointerWasDown) { _gestures.OnPointerMove(pos, time); }
            else if (!down && _pointerWasDown) { _gestures.OnPointerUp(pos, time); _pointerWasDown = false; }
        }

        void HandleRelease(float time)
        {
            if (_pointerWasDown) { _gestures.OnPointerUp(Vector2.zero, time); _pointerWasDown = false; }
        }

        void FeedPinch()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.touches.Count >= 2
                && touch.touches[0].press.isPressed && touch.touches[1].press.isPressed)
            {
                _pinch.Update(touch.touches[0].position.ReadValue(),
                              touch.touches[1].position.ReadValue());
            }
            else
            {
                _pinch.Reset();
            }
        }

        Vector2 ResolveMoveAxis()
        {
            var joystick = VirtualJoystick.CombinedAxis;
            if (joystick.sqrMagnitude > 0.0001f) return joystick;

            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            var x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            var y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }
}
```

> 验证代理用 `unity_reflect` 核对 Input System 1.19.0 的 `Touchscreen.primaryTouch.press.isPressed` / `touches` / `Mouse.current.leftButton` / `Keyboard.current.wKey` 真实成员名,若版本差异按实际调整(`IInputService` 契约不变)。`Tick` 路径不单测(任务书:GestureDetector/PinchDetector 已覆盖判定逻辑)。

- [ ] **Step 9: 验证测试通过**(验证代理:`GestureDetectorTests`、`PinchDetectorTests`)
- [ ] **Step 10: Commit**(编排者)`git commit -m "feat(services): add input gestures, pinch and virtual joystick"`

---

### Task 4: Camera 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Cameras/ICameraService.cs`, `CinemachineCameraService.cs`
- (无 EditMode 测试 —— 薄视觉层,见偏差 2)

设计要点:Cinemachine 3.x 薄封装。`Follow` 设 `CinemachineCamera.Follow`(场景中无相机时懒创建 `CinemachineCamera`);`SetBounds` 用 `CinemachineConfiner2D` + 程序化 `PolygonCollider2D`(由 `Bounds` 生成四角多边形);`Shake` 用 `CinemachineImpulseSource.GenerateImpulseWithForce`。**API 名以 Cinemachine 3.x 实际为准,验证代理用 `unity_reflect` 核对**(见偏差 2)。宿主 GameObject 懒创建 + `DontDestroyOnLoad`,构造函数不碰场景。

- [ ] **Step 1: 实现接口** — `ICameraService.cs`

```csharp
namespace EasyFramework.Services.Cameras
{
    public interface ICameraService
    {
        void Follow(UnityEngine.Transform target);
        void SetBounds(UnityEngine.Bounds bounds);
        void Shake(float intensity, float duration);
    }
}
```

- [ ] **Step 2: 实现 CinemachineCameraService** — `CinemachineCameraService.cs`

```csharp
using Unity.Cinemachine;
using UnityEngine;

namespace EasyFramework.Services.Cameras
{
    /// <summary>Cinemachine 3.x 薄封装。所有宿主对象懒创建并 DontDestroyOnLoad。
    /// 注:Cinemachine 3.x 的精确类型/成员名以工程安装版本为准(验证代理 unity_reflect 核对)。</summary>
    public sealed class CinemachineCameraService : ICameraService
    {
        CinemachineCamera _vcam;
        CinemachineConfiner2D _confiner;
        CinemachineImpulseSource _impulse;
        PolygonCollider2D _boundsShape;

        public void Follow(Transform target)
        {
            EnsureCamera();
            _vcam.Follow = target;
        }

        public void SetBounds(Bounds bounds)
        {
            EnsureCamera();
            if (_confiner == null)
                _confiner = _vcam.gameObject.AddComponent<CinemachineConfiner2D>();

            if (_boundsShape == null)
            {
                var holder = new GameObject("[EasyFramework.CameraBounds]");
                Object.DontDestroyOnLoad(holder);
                _boundsShape = holder.AddComponent<PolygonCollider2D>();
                _boundsShape.isTrigger = true;
            }

            var min = bounds.min;
            var max = bounds.max;
            _boundsShape.points = new[]
            {
                new Vector2(min.x, min.y),
                new Vector2(max.x, min.y),
                new Vector2(max.x, max.y),
                new Vector2(min.x, max.y),
            };
            _confiner.BoundingShape2D = _boundsShape;
            _confiner.InvalidateBoundingShapeCache();
        }

        public void Shake(float intensity, float duration)
        {
            EnsureCamera();
            if (_impulse == null)
                _impulse = _vcam.gameObject.AddComponent<CinemachineImpulseSource>();
            // 方向向量 * 强度;duration 由 ImpulseDefinition 配置承载(薄封装,以默认包络为主)
            _impulse.GenerateImpulseWithForce(intensity);
        }

        void EnsureCamera()
        {
            if (_vcam != null) return;
            var go = new GameObject("[EasyFramework.Camera]");
            Object.DontDestroyOnLoad(go);
            _vcam = go.AddComponent<CinemachineCamera>();
        }
    }
}
```

> **验证代理必读:** Cinemachine 3.x 的 `CinemachineCamera.Follow`(属性)、`CinemachineConfiner2D.BoundingShape2D` / `InvalidateBoundingShapeCache()`、`CinemachineImpulseSource.GenerateImpulseWithForce(...)` 的精确签名以工程安装的 Cinemachine 3.x 为准。编译前用 `mcp__UnityMCP__unity_reflect` 核对 `Unity.Cinemachine.CinemachineCamera` / `CinemachineConfiner2D` / `CinemachineImpulseSource` 成员,若名称或重载不符按实际等效调整(`ICameraService` 契约不变)。`Shake` 的 `duration` 若 3.x 的 impulse API 不直接吃时长,以 `ImpulseDefinition.ImpulseDuration` 或 default envelope 承载——薄层不追求逐参映射,达成「触发一次震屏」即可。

- [ ] **Step 3:** 无 EditMode 测试(薄视觉层,见偏差 2)。验证阶段编译干净即可。
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(services): add cinemachine camera service (follow/bounds/shake)"`

---

### Task 5: Juice 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Juice/IJuiceService.cs`, `HitStopState.cs`, `JuiceService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/HitStopStateTests.cs`

设计要点:`PunchScale`/`Flash` 用 PrimeTween(`Tween.PunchScale`、`Tween.Custom` 颜色闪烁——API 名验证代理核对);`HitStopAsync` 用 `Time.timeScale = 0` → 经 `ITimerService`(`useUnscaledTime: true`)恢复,**重入安全**(已在 hitstop 中再调只延长,不叠加恢复)。重入逻辑抽纯类 `HitStopState` 可 EditMode 测。

- [ ] **Step 1: 写失败测试** — `HitStopStateTests.cs`

`HitStopState`:`Request(duration, now)` 记录最晚恢复时刻(`max(已有, now+duration)`),`Begin()` 进入(返回是否「本次是首个进入」需要置 timeScale=0)、`ShouldRestore(now)` 判断到点恢复、`Restore()` 出。验证:首次进入应置 0;重入(在恢复前再 Request)延长 deadline 不叠加恢复;到 deadline 才恢复;恢复后再 Request 是新一轮。

```csharp
using EasyFramework.Services.Juice;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class HitStopStateTests
    {
        [Test]
        public void FirstRequest_IsEntering()
        {
            var s = new HitStopState();
            var entering = s.Request(0.05f, now: 0f);
            Assert.IsTrue(entering, "首次请求应触发进入(置 timeScale=0)");
            Assert.IsTrue(s.IsActive);
        }

        [Test]
        public void ReentrantRequest_DoesNotReEnter_ExtendsDeadline()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);               // deadline = 0.05
            var entering2 = s.Request(0.05f, now: 0.03f); // 恢复前重入,deadline -> 0.08
            Assert.IsFalse(entering2, "重入不应再次进入");
            Assert.IsFalse(s.ShouldRestore(0.06f), "原 deadline 已过但被延长,不应恢复");
            Assert.IsTrue(s.ShouldRestore(0.08f), "到延长后的 deadline 才恢复");
        }

        [Test]
        public void ShouldRestore_FalseBeforeDeadline_TrueAfter()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);
            Assert.IsFalse(s.ShouldRestore(0.04f));
            Assert.IsTrue(s.ShouldRestore(0.05f));
        }

        [Test]
        public void Restore_DeactivatesAndAllowsNewRound()
        {
            var s = new HitStopState();
            s.Request(0.05f, now: 0f);
            s.Restore();
            Assert.IsFalse(s.IsActive);
            var enteringAgain = s.Request(0.05f, now: 1f);
            Assert.IsTrue(enteringAgain, "恢复后再请求是新一轮进入");
        }

        [Test]
        public void ShorterReentrant_DoesNotShortenDeadline()
        {
            var s = new HitStopState();
            s.Request(0.1f, now: 0f);                 // deadline = 0.1
            s.Request(0.02f, now: 0.01f);             // now+0.02=0.03 < 0.1,不缩短
            Assert.IsFalse(s.ShouldRestore(0.05f));
            Assert.IsTrue(s.ShouldRestore(0.1f));
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IJuiceService.cs`

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Juice
{
    public interface IJuiceService
    {
        void PunchScale(UnityEngine.Transform target, float strength = 0.2f, float duration = 0.25f);
        void Flash(UnityEngine.SpriteRenderer renderer, UnityEngine.Color color, float duration = 0.1f);
        UniTask HitStopAsync(float duration = 0.05f);
    }
}
```

- [ ] **Step 3: 实现纯类** — `HitStopState.cs`

```csharp
using UnityEngine;

namespace EasyFramework.Services.Juice
{
    /// <summary>顿帧重入逻辑(纯,无 Unity 时间依赖,时间由调用方传入)。
    /// 多次 Request 只延长恢复 deadline,不叠加恢复动作。</summary>
    public sealed class HitStopState
    {
        float _restoreAt;

        public bool IsActive { get; private set; }

        /// <summary>请求顿帧;返回 true 表示「本次是进入」(调用方应置 timeScale=0)。</summary>
        public bool Request(float duration, float now)
        {
            var deadline = now + Mathf.Max(0f, duration);
            if (IsActive)
            {
                if (deadline > _restoreAt) _restoreAt = deadline; // 只延长,不缩短
                return false;
            }
            IsActive = true;
            _restoreAt = deadline;
            return true;
        }

        public bool ShouldRestore(float now) => IsActive && now >= _restoreAt;

        public void Restore() => IsActive = false;
    }
}
```

- [ ] **Step 4: 实现 JuiceService** — `JuiceService.cs`(PrimeTween + ITimerService,薄层不单测)

```csharp
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Timing;
using PrimeTween;
using UnityEngine;

namespace EasyFramework.Services.Juice
{
    /// <summary>手感套件:punch / 闪白 / 顿帧。视觉缓动用 PrimeTween;顿帧重入逻辑见 HitStopState。
    /// PrimeTween API 名(Tween.PunchScale / Tween.Custom)以安装版本为准(验证代理 unity_reflect 核对)。</summary>
    public sealed class JuiceService : IJuiceService
    {
        readonly ITimerService _timer;
        readonly HitStopState _hitStop = new();

        public JuiceService(ITimerService timer) => _timer = timer;

        public void PunchScale(Transform target, float strength = 0.2f, float duration = 0.25f)
        {
            if (target == null) return;
            Tween.PunchScale(target, Vector3.one * strength, duration);
        }

        public void Flash(SpriteRenderer renderer, Color color, float duration = 0.1f)
        {
            if (renderer == null) return;
            var original = renderer.color;
            // 闪到 color 再回到原色;用 Custom 驱动 color(PrimeTween 也有 Tween.Color,验证代理择优)
            Tween.Custom(color, original, duration, c => renderer.color = c);
        }

        public UniTask HitStopAsync(float duration = 0.05f)
        {
            var now = Time.unscaledTime;
            var entering = _hitStop.Request(duration, now);
            if (!entering)
                return UniTask.CompletedTask; // 重入:已有恢复定时器在跑,只延长了 deadline

            Time.timeScale = 0f;
            ScheduleRestore(duration);
            return UniTask.CompletedTask;
        }

        void ScheduleRestore(float delay)
        {
            _timer.Schedule(delay, () =>
            {
                var now = Time.unscaledTime;
                if (_hitStop.ShouldRestore(now))
                {
                    _hitStop.Restore();
                    Time.timeScale = 1f;
                }
                else
                {
                    // 被延长:重排到剩余时间后再查
                    ScheduleRestore(0.01f);
                }
            }, repeat: false, useUnscaledTime: true);
        }
    }
}
```

> **验证代理必读:** PrimeTween 的 `Tween.PunchScale(Transform, Vector3 strength, float duration, ...)` 与 `Tween.Custom(...)` / `Tween.Color(...)` 精确签名以安装版本为准,用 `unity_reflect` 核对 `PrimeTween.Tween` 成员后按实际调整。`Flash` 若 PrimeTween 有现成 `Tween.Color(SpriteRenderer, ...)` 重载,优先用之;否则用 `Tween.Custom` 驱动。`HitStopAsync` 当前为同步完成的 `UniTask`(置 0 即返回,恢复靠 timer 回调)——契约签名返回 `UniTask` 但不 await 恢复,符合「调用即生效」语义。`ScheduleRestore` 的延长重排保证重入只延后恢复、不叠加多次恢复(由 `HitStopState.ShouldRestore` 收口)。

- [ ] **Step 5: 验证测试通过**(验证代理:`HitStopStateTests`)
- [ ] **Step 6: Commit**(编排者)`git commit -m "feat(services): add juice service (punch/flash/reentrant hitstop)"`

---

### Task 6: Localization 服务(可并行)

> **见偏差 1:本阶段用 SO 表默认实现替代 Unity Localization 封装,Unity Localization 适配器留作后续接入点。**

**Files:**
- Create: `Assets/EasyFramework/Services/Localization/ILocalizationService.cs`, `LocalizationTable.cs`, `TableLocalizationService.cs`, `LocalizedText.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/TableLocalizationServiceTests.cs`

设计要点:`LocalizationTable : ScriptableObject`(条目 `key` + 每 locale 一列字符串;`CreateAssetMenu`);`TableLocalizationService`(构造注入 `IReadOnlyList<LocalizationTable>` + `IEventBus`)实现 `ILocalizationService`;`Get(key)` 未找到返回 key 本身并 `LogWarning`;`SetLocaleAsync` 持久化 `PlayerPrefs "ef.locale"` 并发 `LocaleChangedEvent`;`LocalizedText`(`TMP_Text` 自动刷新,订阅 `LocaleChangedEvent`,`OnDestroy` 退订)。多表同 key 后注册覆盖(后面的表覆盖前面)。

- [ ] **Step 1: 写失败测试** — `TableLocalizationServiceTests.cs`

`LocalizationTable` 用 `ScriptableObject.CreateInstance<LocalizationTable>()` 构造,经 internal 测试钩子写条目。覆盖:Get 命中、Get 未命中回退(返回 key 本身 + `LogAssert` 预期 warning)、SetLocale 触发事件 + 持久化、表合并(多表同 key 后注册覆盖)、默认 locale 初值。**PlayerPrefs `ef.locale` SetUp/TearDown 清理。**

```csharp
using System;
using System.Collections.Generic;
using EasyFramework.Core.Events;
using EasyFramework.Services.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyFramework.Tests
{
    public class TableLocalizationServiceTests
    {
        const string LocaleKey = "ef.locale";

        sealed class FakeBus : IEventBus
        {
            public readonly List<object> Published = new();
            public void Publish<T>(T evt) => Published.Add(evt);
            public IDisposable Subscribe<T>(Action<T> handler) => null;
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.Save();
        }

        static LocalizationTable MakeTable(params (string key, string zh, string en)[] rows)
        {
            var t = ScriptableObject.CreateInstance<LocalizationTable>();
            foreach (var (k, zh, en) in rows)
                t.AddEntryForTest(k, new Dictionary<string, string> { { "zh-CN", zh }, { "en", en } });
            return t;
        }

        static TableLocalizationService Make(FakeBus bus, params LocalizationTable[] tables)
            => new TableLocalizationService(tables, bus, defaultLocale: "zh-CN");

        [Test]
        public void Get_ReturnsCurrentLocaleValue()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("你好", svc.Get("hello"));
        }

        [Test]
        public void DefaultLocale_IsUsedInitially()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("zh-CN", svc.CurrentLocale);
        }

        [Test]
        public void Get_MissingKey_ReturnsKeyAndWarns()
        {
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*missing.*"));
            Assert.AreEqual("nope", svc.Get("nope"));
        }

        [Test]
        public void SetLocaleAsync_SwitchesValue_PublishesEvent_Persists()
        {
            var bus = new FakeBus();
            var svc = Make(bus, MakeTable(("hello", "你好", "Hello")));
            svc.SetLocaleAsync("en").GetAwaiter().GetResult();
            Assert.AreEqual("Hello", svc.Get("hello"));
            Assert.AreEqual("en", svc.CurrentLocale);
            Assert.AreEqual("en", PlayerPrefs.GetString(LocaleKey, ""));
            Assert.AreEqual(1, bus.Published.Count);
            Assert.IsInstanceOf<LocaleChangedEvent>(bus.Published[0]);
            Assert.AreEqual("en", ((LocaleChangedEvent)bus.Published[0]).LocaleCode);
        }

        [Test]
        public void LaterTable_OverridesEarlierTable_SameKey()
        {
            var t1 = MakeTable(("hello", "你好A", "HelloA"));
            var t2 = MakeTable(("hello", "你好B", "HelloB"));
            var svc = Make(new FakeBus(), t1, t2); // t2 后注册,覆盖
            Assert.AreEqual("你好B", svc.Get("hello"));
        }

        [Test]
        public void PersistedLocale_RestoredOnConstruction()
        {
            PlayerPrefs.SetString(LocaleKey, "en");
            PlayerPrefs.Save();
            var svc = Make(new FakeBus(), MakeTable(("hello", "你好", "Hello")));
            Assert.AreEqual("en", svc.CurrentLocale);
            Assert.AreEqual("Hello", svc.Get("hello"));
        }
    }
}
```

- [ ] **Step 2: 实现接口 + 事件** — `ILocalizationService.cs`

```csharp
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Localization
{
    public readonly struct LocaleChangedEvent
    {
        public readonly string LocaleCode;
        public LocaleChangedEvent(string localeCode) => LocaleCode = localeCode;
    }

    public interface ILocalizationService
    {
        string CurrentLocale { get; }
        string Get(string key);                       // 未找到返回 key 本身并 LogWarning
        UniTask SetLocaleAsync(string localeCode);    // 持久化 PlayerPrefs "ef.locale", 发 LocaleChangedEvent
    }
}
```

- [ ] **Step 3: 实现 LocalizationTable** — `LocalizationTable.cs`(ScriptableObject)

每行一个 key,每 locale 一列字符串。Inspector 用并行列表表达(key 列 + 每 locale 一组值);运行时聚合为 `key -> (locale -> value)`。

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "EasyFramework/Localization Table")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [System.Serializable]
        public struct LocaleValue
        {
            public string Locale;   // 如 "zh-CN" / "en"
            public string Value;
        }

        [System.Serializable]
        public struct Row
        {
            public string Key;
            public List<LocaleValue> Values;
        }

        [SerializeField] List<Row> _rows = new();

        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>测试钩子:运行时追加一行(key + 每 locale 值)。</summary>
        internal void AddEntryForTest(string key, IReadOnlyDictionary<string, string> localeValues)
        {
            var values = new List<LocaleValue>();
            foreach (var kv in localeValues)
                values.Add(new LocaleValue { Locale = kv.Key, Value = kv.Value });
            _rows.Add(new Row { Key = key, Values = values });
        }
    }
}
```

- [ ] **Step 4: 实现 TableLocalizationService** — `TableLocalizationService.cs`(纯类)

构造时聚合所有表为 `key -> (locale -> value)`(后注册表覆盖先注册表同 key);从 `PlayerPrefs "ef.locale"` 读初始 locale,无则用 `defaultLocale`。

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    public sealed class TableLocalizationService : ILocalizationService
    {
        const string LocaleKey = "ef.locale";

        readonly Dictionary<string, Dictionary<string, string>> _entries = new();
        readonly IEventBus _events;

        public string CurrentLocale { get; private set; }

        public TableLocalizationService(IReadOnlyList<LocalizationTable> tables,
            IEventBus events, string defaultLocale = "zh-CN")
        {
            _events = events;
            CurrentLocale = PlayerPrefs.GetString(LocaleKey, defaultLocale);

            if (tables != null)
            {
                foreach (var table in tables)
                {
                    if (table == null) continue;
                    foreach (var row in table.Rows)
                    {
                        if (string.IsNullOrEmpty(row.Key)) continue;
                        if (!_entries.TryGetValue(row.Key, out var map))
                            _entries[row.Key] = map = new Dictionary<string, string>();
                        if (row.Values != null)
                            foreach (var lv in row.Values)
                                map[lv.Locale] = lv.Value; // 后注册覆盖
                    }
                }
            }
        }

        public string Get(string key)
        {
            if (_entries.TryGetValue(key, out var map)
                && map.TryGetValue(CurrentLocale, out var value))
                return value;

            Debug.LogWarning($"[EasyFramework] Localization missing key '{key}' for locale '{CurrentLocale}'.");
            return key;
        }

        public UniTask SetLocaleAsync(string localeCode)
        {
            CurrentLocale = localeCode;
            PlayerPrefs.SetString(LocaleKey, localeCode);
            PlayerPrefs.Save();
            _events.Publish(new LocaleChangedEvent(localeCode));
            return UniTask.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: 实现 LocalizedText** — `LocalizedText.cs`(TMP_Text uGUI 组件)

挂在 `TMP_Text` 同物体上,序列化一个 `Key`;`Start` 时刷新一次并订阅 `LocaleChangedEvent`,`OnDestroy` 退订。通过 `G.Loc` 门面访问本地化服务(组件是 Game 层可见的表现层 UI,允许走门面)。

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Services.Localization;
using TMPro;
using UnityEngine;

namespace EasyFramework.Services.Localization
{
    /// <summary>把本地化文本绑定到 TMP_Text,语言切换时自动刷新。</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] string _key;

        TMP_Text _text;
        System.IDisposable _sub;

        void Awake() => _text = GetComponent<TMP_Text>();

        void OnEnable()
        {
            if (!G.IsInitialized) return;
            _sub = G.Events.Subscribe<LocaleChangedEvent>(_ => Refresh());
            Refresh();
        }

        void OnDisable()
        {
            _sub?.Dispose();
            _sub = null;
        }

        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        void Refresh()
        {
            if (_text == null || string.IsNullOrEmpty(_key) || !G.IsInitialized) return;
            _text.text = G.Loc.Get(_key);
        }
    }
}
```

> 说明:`LocalizedText` 走 `G.Loc` / `G.Events` 门面是有意为之——它是 Game 层使用的表现层 UI 组件(spec §2.2「门面服务 Game 层」),不属于框架内部模块间依赖。`OnEnable/OnDisable` 配对订阅/退订(比 `Start/OnDestroy` 更稳,反复启停不漏)。`G.Loc` 属性在 Task 7 加入门面。

- [ ] **Step 6: 验证测试通过**(验证代理:`TableLocalizationServiceTests`;注意 `LogAssert.Expect` 收口未命中 warning)
- [ ] **Step 7: Commit**(编排者)`git commit -m "feat(services): add SO-table localization with auto-refresh text"`

---

### Task 7: Haptics 服务(可并行)

**Files:**
- Create: `Assets/EasyFramework/Services/Haptics/IHapticsService.cs`, `HapticsService.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/HapticsServiceTests.cs`

设计要点:`#if UNITY_ANDROID` 用 `AndroidJavaObject` Vibrator(三档 50/100/200ms);`#elif UNITY_IOS` 用 `UnityEngine.Handheld.Vibrate()`(三档同振);`#else Debug.Log`。`Enabled=false` 时 `Vibrate` 直接 return;持久化 `PlayerPrefs "ef.haptics"`。EditMode 只测 Enabled 持久化与开关拦截——平台振动调用抽成 `internal static Action<HapticStrength>` 钩子,测试替换记录调用。

- [ ] **Step 1: 写失败测试** — `HapticsServiceTests.cs`

替换 `HapticsService.PlatformVibrate` 钩子为记录型;验证:`Enabled=true` 时 Vibrate 调钩子、`Enabled=false` 时不调、Enabled 持久化往返、默认 Enabled=true。**PlayerPrefs `ef.haptics` SetUp/TearDown 清理 + 钩子还原。**

```csharp
using System;
using System.Collections.Generic;
using EasyFramework.Services.Haptics;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class HapticsServiceTests
    {
        const string Key = "ef.haptics";
        Action<HapticStrength> _originalHook;
        List<HapticStrength> _calls;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            _originalHook = HapticsService.PlatformVibrate;
            _calls = new List<HapticStrength>();
            HapticsService.PlatformVibrate = s => _calls.Add(s);
        }

        [TearDown]
        public void TearDown()
        {
            HapticsService.PlatformVibrate = _originalHook;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [Test]
        public void Default_EnabledIsTrue()
        {
            var svc = new HapticsService();
            Assert.IsTrue(svc.Enabled);
        }

        [Test]
        public void Vibrate_WhenEnabled_CallsPlatformHook()
        {
            var svc = new HapticsService();
            svc.Vibrate(HapticStrength.Medium);
            Assert.AreEqual(1, _calls.Count);
            Assert.AreEqual(HapticStrength.Medium, _calls[0]);
        }

        [Test]
        public void Vibrate_WhenDisabled_DoesNothing()
        {
            var svc = new HapticsService { Enabled = false };
            svc.Vibrate(HapticStrength.Heavy);
            Assert.AreEqual(0, _calls.Count);
        }

        [Test]
        public void Enabled_Setter_PersistsToPlayerPrefs()
        {
            var svc = new HapticsService();
            svc.Enabled = false;
            Assert.AreEqual(0, PlayerPrefs.GetInt(Key, -1));
            svc.Enabled = true;
            Assert.AreEqual(1, PlayerPrefs.GetInt(Key, -1));
        }

        [Test]
        public void Enabled_RoundTripsThroughPlayerPrefs()
        {
            var a = new HapticsService();
            a.Enabled = false;
            var b = new HapticsService();
            Assert.IsFalse(b.Enabled);
        }
    }
}
```

- [ ] **Step 2: 实现接口** — `IHapticsService.cs`

```csharp
namespace EasyFramework.Services.Haptics
{
    public enum HapticStrength { Light, Medium, Heavy }

    public interface IHapticsService
    {
        bool Enabled { get; set; }   // 持久化 PlayerPrefs "ef.haptics"
        void Vibrate(HapticStrength strength);
    }
}
```

- [ ] **Step 3: 实现 HapticsService** — `HapticsService.cs`

```csharp
using System;
using UnityEngine;

namespace EasyFramework.Services.Haptics
{
    public sealed class HapticsService : IHapticsService
    {
        const string PrefsKey = "ef.haptics";

        /// <summary>平台振动钩子。默认调真实平台实现;EditMode 测试替换为记录型。</summary>
        internal static Action<HapticStrength> PlatformVibrate = DefaultPlatformVibrate;

        bool _enabled;

        public HapticsService()
        {
            _enabled = PlayerPrefs.GetInt(PrefsKey, 1) != 0;
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Vibrate(HapticStrength strength)
        {
            if (!_enabled) return;
            PlatformVibrate(strength);
        }

        static void DefaultPlatformVibrate(HapticStrength strength)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            long ms = strength switch
            {
                HapticStrength.Light => 50L,
                HapticStrength.Medium => 100L,
                HapticStrength.Heavy => 200L,
                _ => 50L,
            };
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator != null) vibrator.Call("vibrate", ms);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Android vibrate failed: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate(); // iOS 三档同振(系统未暴露强度)
#else
            Debug.Log($"[EasyFramework] Haptic ({strength}) — no device vibration in this environment.");
#endif
        }
    }
}
```

> 说明:平台分支用 `&& !UNITY_EDITOR` 保证编辑器内走 `#else` 的 `Debug.Log`,不触发真机 API。`PlatformVibrate` 钩子默认指向 `DefaultPlatformVibrate`,测试在 `[SetUp]` 替换、`[TearDown]` 还原——这样 EditMode 测「开关拦截 + 持久化」时不触平台调用。`Vibrate` 在 `Enabled==false` 时直接 return(不触钩子),与测试一致。

- [ ] **Step 4: 验证测试通过**(验证代理:`HapticsServiceTests`)
- [ ] **Step 5: Commit**(编排者)`git commit -m "feat(services): add haptics service with platform branches"`

---

### Task 8: 接线 —— FrameworkInstaller / G / RootLifetimeScope(串行,依赖 Task 1-7)

> **⚠️ 执行前必读:** 本 Task 修改的 `Boot/G.cs`、`Boot/FrameworkInstaller.cs`、`Boot/RootLifetimeScope.cs` 由 Phase 1/2(及并行的 Phase 3a)落地/修改中。**执行前必须先 `Read` 这三个文件的实际落地内容**,核对 `G` 当前属性集合(Phase 2 后应有 Events/Timer/Asset/Scene/Save/Config/Pool;Phase 3a 可能已加 `UI`)、`FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)` 的实际注册写法、`FrameworkOptions` 现有字段(Phase 2:SaveDirectory/ConfigTables/SaveProfile/InitialScene)、`RootLifetimeScope.Configure` 的 build callback / entrypoint / `SaveOnPauseListener` 写法。**下方给出的是「合并后的完整文件内容」,但必须以实际文件为基线做增量合并,只新增本 Phase 的 6 个服务 + 2 个 options 字段,不要覆盖 Phase 1/2/3a 已有内容。**

**Files:**
- Modify: `Assets/EasyFramework/Boot/FrameworkInstaller.cs`(FrameworkOptions 加 2 字段 + 注册 6 服务)
- Modify: `Assets/EasyFramework/Boot/G.cs`(新增 6 属性)
- Modify: `Assets/EasyFramework/Boot/RootLifetimeScope.cs`(options 传 LocalizationTables/DefaultLocale + 挂 Audio/Input Tick)
- Modify: `Assets/EasyFramework/Tests/EditMode/FrameworkInstallerTests.cs`(扩展断言)

- [ ] **Step 1: 升级失败测试** — `FrameworkInstallerTests.cs`

**先 Read 实际文件**(Phase 2 已扩展到含 Phase2 服务断言)。在其上增量加:6 个新服务可解析、`G` 绑定 6 个新属性。下方为合并后完整文件(以 Phase 2 版本为基线;若 Phase 3a 已加 UI 断言,保留之)。

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
using EasyFramework.Services.Pooling;
using EasyFramework.Services.Saves;
using EasyFramework.Services.Scenes;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class FrameworkInstallerTests
    {
        const string BgmKey = "ef.audio.bgm";
        const string SfxKey = "ef.audio.sfx";
        const string LocaleKey = "ef.locale";
        const string HapticsKey = "ef.haptics";

        [SetUp]
        public void SetUp()
        {
            // 避免 Audio/Loc/Haptics 服务构造读到污染的 PlayerPrefs
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.DeleteKey(HapticsKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            EasyFramework.G.Reset();
            PlayerPrefs.DeleteKey(BgmKey);
            PlayerPrefs.DeleteKey(SfxKey);
            PlayerPrefs.DeleteKey(LocaleKey);
            PlayerPrefs.DeleteKey(HapticsKey);
            PlayerPrefs.Save();
        }

        static FrameworkOptions MakeOptions()
            => new FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_installer_" + System.Guid.NewGuid().ToString("N")),
                ConfigTables = new List<ConfigTable>(),
                SaveProfile = null,
                LocalizationTables = new List<LocalizationTable>(),
                DefaultLocale = "zh-CN",
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
        public void Install_ResolvesPhase3bServices()
        {
            var c = Build();
            Assert.NotNull(c.Resolve<IAudioService>());
            Assert.NotNull(c.Resolve<IInputService>());
            Assert.NotNull(c.Resolve<ICameraService>());
            Assert.NotNull(c.Resolve<IJuiceService>());
            Assert.NotNull(c.Resolve<ILocalizationService>());
            Assert.NotNull(c.Resolve<IHapticsService>());
        }

        [Test]
        public void GFacade_BindsPhase3bServices()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            Assert.IsTrue(EasyFramework.G.IsInitialized);
            Assert.AreSame(c.Resolve<IAudioService>(), EasyFramework.G.Audio);
            Assert.AreSame(c.Resolve<IInputService>(), EasyFramework.G.Input);
            Assert.AreSame(c.Resolve<ICameraService>(), EasyFramework.G.Camera);
            Assert.AreSame(c.Resolve<IJuiceService>(), EasyFramework.G.Juice);
            Assert.AreSame(c.Resolve<ILocalizationService>(), EasyFramework.G.Loc);
            Assert.AreSame(c.Resolve<IHapticsService>(), EasyFramework.G.Haptics);
        }

        [Test]
        public void GFacade_ResetClearsBindings()
        {
            var c = Build();
            EasyFramework.G.Initialize(c);
            EasyFramework.G.Reset();
            Assert.IsFalse(EasyFramework.G.IsInitialized);
            Assert.IsNull(EasyFramework.G.Audio);
            Assert.IsNull(EasyFramework.G.Loc);
            Assert.IsNull(EasyFramework.G.Haptics);
        }
    }
}
```

- [ ] **Step 2: 升级 G 门面** — `Assets/EasyFramework/Boot/G.cs`

**先 Read 实际文件**,在 Phase 1/2(及 3a 若已加 UI)属性基础上**新增** Audio/Input/Camera/Juice/Loc/Haptics 六属性,`Initialize`/`Reset` 同步更新。下方为以 Phase 2 版本为基线的合并后完整内容(若实际已有 `UI` 属性,保留之,只增本 Phase 六项):

```csharp
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
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

        // Phase 3b 表现层
        public static IAudioService Audio { get; private set; }
        public static IInputService Input { get; private set; }
        public static ICameraService Camera { get; private set; }
        public static IJuiceService Juice { get; private set; }
        public static ILocalizationService Loc { get; private set; }
        public static IHapticsService Haptics { get; private set; }

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

            Audio = resolver.Resolve<IAudioService>();
            Input = resolver.Resolve<IInputService>();
            Camera = resolver.Resolve<ICameraService>();
            Juice = resolver.Resolve<IJuiceService>();
            Loc = resolver.Resolve<ILocalizationService>();
            Haptics = resolver.Resolve<IHapticsService>();

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

            Audio = null;
            Input = null;
            Camera = null;
            Juice = null;
            Loc = null;
            Haptics = null;

            IsInitialized = false;
        }
    }
}
```

- [ ] **Step 3: 升级 FrameworkInstaller** — `Assets/EasyFramework/Boot/FrameworkInstaller.cs`

**先 Read 实际文件**。`FrameworkOptions` **新增两字段** `LocalizationTables` 与 `DefaultLocale`;在 `Install` 末尾**追加** Phase 3b 六服务注册(保留 Phase 1/2/3a 已有注册)。下方为以 Phase 2 版本为基线的合并后完整内容:

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Assets;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Cameras;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Haptics;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Juice;
using EasyFramework.Services.Localization;
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
        /// <summary>本地化表;可空。(Phase 3b)</summary>
        public IReadOnlyList<LocalizationTable> LocalizationTables;
        /// <summary>默认 locale;无 PlayerPrefs 时的初值。(Phase 3b)默认 "zh-CN"。</summary>
        public string DefaultLocale = "zh-CN";
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

            // ---- Asset(Phase 2)----
            builder.Register<AddressablesAssetService>(Lifetime.Singleton)
                .As<IAssetService>().AsSelf();

            // ---- Scene(Phase 2)----
            builder.Register<ISceneTransition, NoopSceneTransition>(Lifetime.Singleton);
            builder.RegisterInstance<ISceneLoader>(new UnitySceneLoader());
            builder.Register<ISceneService>(c => new SceneService(
                c.Resolve<ISceneLoader>(),
                c.Resolve<ISceneTransition>(),
                c.Resolve<IEventBus>(),
                options.InitialScene), Lifetime.Singleton);

            // ---- Save(Phase 2)----
            var profile = options.SaveProfile ?? CreateDefaultSaveProfile();
            builder.RegisterInstance(profile);
            builder.Register<ISaveService>(c => new JsonSaveService(profile, options.SaveDirectory),
                Lifetime.Singleton);
            builder.Register<SaveBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Config(Phase 2)----
            builder.Register<IRemoteConfigProvider, NoopRemoteConfigProvider>(Lifetime.Singleton);
            var tables = options.ConfigTables ?? new List<ConfigTable>();
            builder.Register<ConfigService>(c => new ConfigService(tables, c.Resolve<IRemoteConfigProvider>()),
                Lifetime.Singleton).As<IConfigService>().AsSelf();
            builder.Register<ConfigBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- Pool(Phase 2)----
            builder.Register<PoolService>(Lifetime.Singleton).As<IPoolService>().AsSelf();

            // ---- Audio(Phase 3b)----
            // AudioService : ITickable,以 AsSelf 注册便于 RootLifetimeScope 取出挂 Tick。
            builder.Register<AudioService>(Lifetime.Singleton).As<IAudioService>().AsSelf();

            // ---- Input(Phase 3b)----
            builder.Register<InputService>(Lifetime.Singleton).As<IInputService>().AsSelf();

            // ---- Camera(Phase 3b)----
            builder.Register<CinemachineCameraService>(Lifetime.Singleton).As<ICameraService>();

            // ---- Juice(Phase 3b)----
            builder.Register<JuiceService>(Lifetime.Singleton).As<IJuiceService>();

            // ---- Localization(Phase 3b)----
            var locTables = options.LocalizationTables ?? new List<LocalizationTable>();
            var defaultLocale = string.IsNullOrEmpty(options.DefaultLocale) ? "zh-CN" : options.DefaultLocale;
            builder.Register<ILocalizationService>(c => new TableLocalizationService(
                locTables, c.Resolve<IEventBus>(), defaultLocale), Lifetime.Singleton);

            // ---- Haptics(Phase 3b)----
            builder.Register<HapticsService>(Lifetime.Singleton).As<IHapticsService>();
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
> - `AudioService` / `InputService` 都是 `ITickable`,以 `.AsSelf()` 同时注册具体类,供 `RootLifetimeScope` 从容器取出挂进 Tick 调度(与 Phase 1 `TimerTicker` 同思路);也可直接 `builder.Register<AudioService>(...).As<IAudioService>().As<ITickable>().AsSelf()` 让 VContainer 把它当入口点——**实现代理以实际 VContainer 版本的入口点 API 为准**(见 Step 4 说明)。
> - `CinemachineCameraService` / `JuiceService` / `HapticsService` / `TableLocalizationService` 构造均不碰 GameObject / 平台 API(懒构建),纯 `ContainerBuilder.Build()` 可成功 —— 保持 `FrameworkInstallerTests` 在无 MonoBehaviour 下可 Build 的约束。
> - `JuiceService` 依赖 `ITimerService`(Phase 1 已注册),`InputService`/`TableLocalizationService` 依赖 `IEventBus`(Phase 1 已注册),依赖闭合。

- [ ] **Step 4: 升级 RootLifetimeScope** — `Assets/EasyFramework/Boot/RootLifetimeScope.cs`

**先 Read 实际文件**,保留其 `TimerTicker` / `GameBootstrap` entrypoint / `SaveOnPauseListener` / `G.Initialize` build callback。增量加三件事:(a) `FrameworkOptions` 新增 `LocalizationTables`(Inspector `[SerializeField]`)与 `DefaultLocale` 赋值;(b) 把 `AudioService` / `InputService` 两个 `ITickable` 挂进 Tick 调度(同 `TimerTicker` 模式,新增两个 Ticker 桥或直接用 VContainer 入口点);(c) 其余不动。下方为以 Phase 2 版本为基线的合并后完整内容:

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Timing;
using EasyFramework.Services.Audio;
using EasyFramework.Services.Configs;
using EasyFramework.Services.Inputs;
using EasyFramework.Services.Localization;
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

        [Header("本地化表(可空)")]
        [SerializeField] List<LocalizationTable> _localizationTables = new();

        [Header("初始场景名")]
        [SerializeField] string _initialScene = "Boot";

        [Header("默认语言")]
        [SerializeField] string _defaultLocale = "zh-CN";

        protected override void Configure(IContainerBuilder builder)
        {
            var options = new FrameworkOptions
            {
                SaveDirectory = Application.persistentDataPath,
                ConfigTables = _configTables,
                SaveProfile = null,
                InitialScene = _initialScene,
                LocalizationTables = _localizationTables,
                DefaultLocale = _defaultLocale,
            };

            FrameworkInstaller.Install(builder, options);

            builder.RegisterEntryPoint<GameBootstrap>();
            builder.UseEntryPoints(ep =>
            {
                ep.Add<TimerTicker>();
                ep.Add<AudioTicker>();
                ep.Add<InputTicker>();
            });

            // SaveOnPauseListener 挂到本组合根 GameObject,build 后绑定 ISaveService。
            var pauseListener = gameObject.AddComponent<SaveOnPauseListener>();

            builder.RegisterBuildCallback(r =>
            {
                pauseListener.Bind(r.Resolve<ISaveService>());
                G.Initialize(r);
            });
        }
    }

    /// <summary>把 TimerService.Tick 桥接到 VContainer 的 Tick 循环。</summary>
    sealed class TimerTicker : ITickable
    {
        readonly TimerService _timer;
        public TimerTicker(TimerService timer) => _timer = timer;
        public void Tick() => _timer.Tick();
    }

    /// <summary>把 AudioService.Tick(BGM 淡变推进)桥接到 Tick 循环。</summary>
    sealed class AudioTicker : ITickable
    {
        readonly AudioService _audio;
        public AudioTicker(AudioService audio) => _audio = audio;
        public void Tick() => _audio.Tick();
    }

    /// <summary>把 InputService.Tick(读输入、喂手势)桥接到 Tick 循环。</summary>
    sealed class InputTicker : ITickable
    {
        readonly InputService _input;
        public InputTicker(InputService input) => _input = input;
        public void Tick() => _input.Tick();
    }
}
```

> 说明:沿用 Phase 1 的「桥接 Ticker」模式(`TimerTicker`),新增 `AudioTicker` / `InputTicker`,把两个 `ITickable` 服务接进 VContainer 的 Tick 循环。这样 `AudioService` / `InputService` 既以接口暴露给业务,又以具体类(`.AsSelf()`)被 Ticker 注入。**若 Phase 1 实际 `RootLifetimeScope` 用了不同的入口点写法(例如未用 `UseEntryPoints` 链式 API,或直接 `RegisterEntryPoint<AudioService>()` 让 VContainer 识别 `ITickable`),实现代理以实际文件为基线做等效合并** —— 目标只有一个:`AudioService.Tick` 与 `InputService.Tick` 每帧被调用,`G.Initialize` 与 `SaveOnPauseListener.Bind` 保持。`_localizationTables` / `_defaultLocale` 的 `[SerializeField]` 为新增,不影响已有逻辑。

- [ ] **Step 5: 验证测试通过**(验证代理:`FrameworkInstallerTests` 全过,注意 SetUp/TearDown 清 PlayerPrefs)
- [ ] **Step 6: Commit**(编排者)`git commit -m "feat(boot): wire phase3b presentation services into installer, facade and root scope"`

---

### Task 9: 全量验证(串行,使用 UnityMCP)

- [ ] **Step 1: 刷新编译** — `refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false`。

- [ ] **Step 2: 视觉层 API 核对(编译前/编译报错时)** — 用 `mcp__UnityMCP__unity_reflect` 核对下列真实成员,与计划中用法比对,有出入按实际签名修正(见偏差 2):
  - `Unity.Cinemachine.CinemachineCamera`(`Follow` 属性)
  - `Unity.Cinemachine.CinemachineConfiner2D`(`BoundingShape2D`、`InvalidateBoundingShapeCache`)
  - `Unity.Cinemachine.CinemachineImpulseSource`(`GenerateImpulseWithForce` 重载)
  - `PrimeTween.Tween`(`PunchScale`、`Custom`、`Color` 静态方法签名)
  - `UnityEngine.InputSystem.Touchscreen` / `Mouse` / `Keyboard`(`primaryTouch`/`press`/`position`/`leftButton`/`wKey` 等)

- [ ] **Step 3: 0 error** — `read_console(types=["error"])`。Expected: 0 errors。

- [ ] **Step 4: EditMode 全量测试** — `run_tests(mode="EditMode")` → `get_test_job` 轮询。Expected: Phase 1 + Phase 2 全部用例 + Phase 3b 新增全 PASS。Phase 3b 新增计数:
  - `CrossfadeStateTests` 5
  - `AudioServiceTests` 5
  - `GestureDetectorTests` 12
  - `PinchDetectorTests` 4
  - `HitStopStateTests` 5
  - `TableLocalizationServiceTests` 6
  - `HapticsServiceTests` 5
  - `FrameworkInstallerTests` 扩展后 6
  （合计 Phase 3b 约 48 个用例)

- [ ] **Step 5: 无新警告** — `read_console(types=["warning"])`。Expected: 无框架相关新警告。注意:`TableLocalizationServiceTests.Get_MissingKey_*` 会在测试运行时主动 `Debug.LogWarning`(已用 `LogAssert.Expect` 收口);`HapticsServiceTests` 在 EditMode 走 `#else Debug.Log` 路径但被钩子替换(不触发),不会产生 console 输出。若有未收口的 warning,按 LogAssert 模式补齐。

- [ ] **Step 6: 偏差记录** — 若实现与计划有偏差(如 Cinemachine 3.x / PrimeTween / Input System API 名调整、VContainer 入口点写法调整、OpenUPM PrimeTween 版本号),在本计划文档末尾追加 "Deviations" 小节记录(实际安装的 PrimeTween / Cinemachine 版本号、修正后的 API 调用)。

- [ ] **Step 7: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- **Spec 覆盖:** Phase 3b 范围 = 设计文档 §10 Phase 3 表现层中的 Audio / Input / Camera / Tween-Juice / Localization / Vibration 六项(UI 由并行 Phase 3a 承担)。逐项核对:
  - §4.6 Audio「BGM/SFX 双通道、AudioSource 池、淡入淡出、音量持久化」→ `AudioService`(双 BGM 源交叉淡变 + 8 路 SFX 池 + `CrossfadeState`)+ 音量 `Clamp01` + `PlayerPrefs`。spec 原文「音量存 Save」,本阶段按任务书锁定契约改存 `PlayerPrefs "ef.audio.*"`(契约硬性要求,优先于 spec 措辞)。✓
  - §4.7 Input「新 Input System 之上手势(Tap/LongPress/Swipe 带方向/Drag/Pinch)、事件发布、UI 穿透、虚拟摇杆与键盘合流」→ `GestureDetector` + `PinchDetector` + `InputService`(`IsPointerOverUI`)+ `VirtualJoystick` + WASD 合流为 `MoveAxis`。✓
  - §4.8 Camera「Cinemachine 2D:跟随、边界、震屏(Impulse)」→ `CinemachineCameraService`(`Follow`/`Confiner2D` Bounds/`ImpulseSource`)。Cinemachine 3.x API 名以实际为准(偏差 2)。✓
  - §4.9 Tween/Juice「PrimeTween 封装 + punch / 闪白 / 顿帧」→ `JuiceService`(`PunchScale`/`Flash`/`HitStopAsync`)+ `HitStopState` 重入。飘字归 UI 层(Phase 3a Overlay),本阶段不做。✓
  - §4.10 Localization「文本表 + 切语言广播 + UI 自动刷新 + 中英双语骨架」→ `TableLocalizationService` + `LocaleChangedEvent` + `LocalizedText`;**主动偏离 Unity Localization 封装(偏差 1),改 SO 表实现,留适配器接入点**。默认 `DefaultLocale="zh-CN"`,表支持 zh-CN/en 双列。✓(实现路径偏离,目标达成)
  - §4.11 Vibration「iOS Haptic / Android Vibrator 统一接口、轻/中/重三档」→ `HapticsService`(平台分支 + `HapticStrength` 三档 + `Enabled` 持久化)。✓
- **锁定契约一致性:** 任务书锁定的全部公共签名已逐字落到接口文件,并与测试/实现/Installer/G 门面四处核对:
  - `IAudioService.PlayBgmAsync/StopBgm/PlaySfx/BgmVolume/SfxVolume`(默认参数值 0.5/0.3/1f 一致)✓
  - `SwipeDirection`/`DragPhase` 枚举值顺序、`TapEvent/LongPressEvent/SwipeEvent/DragEvent/PinchEvent` 字段名与类型(`ScreenPosition`/`Start`/`End`/`Direction`/`Phase`/`Position`/`Delta`/`DeltaScale`)、`IInputService.MoveAxis/IsPointerOverUI` ✓
  - `ICameraService.Follow/SetBounds/Shake` ✓
  - `IJuiceService.PunchScale/Flash/HitStopAsync`(默认值 0.2/0.25/0.1/0.05 一致)✓
  - `LocaleChangedEvent.LocaleCode`、`ILocalizationService.CurrentLocale/Get/SetLocaleAsync` ✓
  - `HapticStrength`(Light/Medium/Heavy)、`IHapticsService.Enabled/Vibrate` ✓
  - 跨 Phase 衔接类型(`IEventBus`、`ITimerService`、`IAssetService`/`AssetScope`、`FrameworkOptions`、`FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)`、`G.Initialize/Reset`)均与 Phase 1/2 计划一致;`FrameworkOptions` 仅**追加** `LocalizationTables`/`DefaultLocale` 两字段,不改既有字段。✓
- **PlayerPrefs 持久化键:** `ef.audio.bgm`/`ef.audio.sfx`/`ef.locale`/`ef.haptics` 四键,全部触碰 fixture 已加 SetUp/TearDown 清理(含 `FrameworkInstallerTests`,因其 Build 会构造 Audio/Loc/Haptics 服务读 PlayerPrefs)。✓
- **懒构建 / 纯容器可 Build:** Audio/Camera 宿主 GameObject、Haptics 平台 API、Juice 视觉调用全部懒构建/钩子隔离,构造函数只赋值 + 订阅。`FrameworkInstaller.Install` 在纯 `ContainerBuilder.Build()` 下成功(`FrameworkInstallerTests` 验证),无 MonoBehaviour 依赖。✓
- **占位符:** 无 TBD。三处「不单测」均为 spec/任务书明确许可的薄视觉/平台层,非占位符:(a) Audio 播放路径(`AudioSource`)—— `CrossfadeState` + 音量持久化已测;(b) Camera Cinemachine 调用 —— 薄封装;(c) Juice PrimeTween 调用 —— `HitStopState` 已测;(d) Input `Tick` 读输入 —— `GestureDetector`/`PinchDetector` 已测。可能漂移处均给出具体回退动作:Cinemachine/PrimeTween/InputSystem API 名 → 验证代理 `unity_reflect` 核对后等效调整;VContainer 入口点写法 → 以实际文件为基线等效合并(Task 8 已声明 Read-then-merge 流程);PrimeTween 版本号 → OpenUPM 解析最新稳定,记入 Deviations。✓

---

## 自查发现并修正的问题(起草阶段)

1. **`HitStopAsync` 契约返回 `UniTask` 但顿帧恢复是异步的,若 await 恢复会破坏「调用即生效」语义,且重入会叠加多个恢复 await** —— 修正:`HitStopAsync` 置 `timeScale=0` 后立即返回 `UniTask.CompletedTask`,恢复交给 `ITimerService`(`useUnscaledTime:true`)回调;重入逻辑抽 `HitStopState`(`Request` 只延长 deadline、`ShouldRestore` 收口),`ScheduleRestore` 在到点时若被延长则重排,保证「不叠加恢复」。`HitStopState` 纯类 EditMode 全覆盖。
2. **`AudioService` 若在构造函数建 AudioSource 宿主,则 `FrameworkInstallerTests` 的纯 `ContainerBuilder.Build()` 会在无场景下创建游离 GameObject,污染测试且违反「构造不碰 GameObject」** —— 修正:宿主 `EnsureHost()` 懒构建,构造函数只读 PlayerPrefs 音量;`AudioServiceTests` 只调音量 setter/getter,不触播放,宿主永不建。
3. **音量 spec 说「存 Save」,但锁定契约明确「持久化 PlayerPrefs」** —— 修正:以锁定契约为准存 `PlayerPrefs "ef.audio.bgm"/"ef.audio.sfx"`,在 Self-Review 标注该偏离及理由(契约硬性 > spec 措辞);PlayerPrefs 在 EditMode 可用,持久化往返可测。
4. **`GestureDetector` 的「非 Tap 阈值」与「进 Drag 阈值」若取不同值,边界用例(位移 20~80px)行为含糊** —— 修正:统一为单一 `MoveThreshold=20px`,既是「超过即非 Tap」也是「进 Drag」门槛;`LargeMove_NotTap`(位移 30,无 move 事件)走 up 时 `_dragging==false` 不发 tap(dist>=20)也不发 swipe(dist<80),结果零事件,与断言一致;有 `OnPointerMove` 超阈值的路径才进 Drag。两条路径在自查时逐用例验证不矛盾。
5. **`TableLocalizationService.Get` 未命中 `Debug.LogWarning` 会被 Unity Test Runner 当作失败** —— 修正:测试用 `LogAssert.Expect(LogType.Warning, ...)` 预期该 warning;验证 Task Step 5 注明这是被测路径预期输出。
6. **`HapticsService` 在 EditMode 直接走 `#else Debug.Log` 会污染 console、且无法断言「调用了平台振动」** —— 修正:平台调用抽 `internal static Action<HapticStrength> PlatformVibrate` 钩子,测试 `[SetUp]` 替换为记录型、`[TearDown]` 还原;断言钩子调用次数/参数,EditMode 不触 `Debug.Log`。
7. **`LocalizedText` 用 `Start`/`OnDestroy` 订阅在组件反复启停时可能漏订/重订** —— 修正:改 `OnEnable` 订阅 + `OnDisable` 退订(配对),并在 `OnEnable`/`Refresh` 守卫 `G.IsInitialized`,冷启动(门面未初始化)时静默不刷新。
8. **`FrameworkInstallerTests.Build()` 会构造 Audio/Loc/Haptics 服务,它们在构造时读 PlayerPrefs,跨用例污染** —— 修正:给 `FrameworkInstallerTests` 加 `[SetUp]`/`[TearDown]` 清四键(`ef.audio.bgm`/`ef.audio.sfx`/`ef.locale`/`ef.haptics`),与各服务自己的 fixture 一致。
9. **`PinchDetector` 首帧无基线就算 DeltaScale 会除零/发噪声事件** —— 修正:首帧(或 `Reset` 后首帧)只记录基线、不发事件;`_lastDistance <= Epsilon` 时也只重置基线,避免除零。`Reset()` 在松指/单指时调用(`InputService.FeedPinch` 双指不足时 `Reset`)。
10. **Camera/Juice 视觉层 API 名可能与 Cinemachine 3.x / PrimeTween 实际不符,机械写死会编译失败** —— 修正:偏差 2 声明「API 名以实际为准」,Task 9 Step 2 用 `unity_reflect` 核对五组类型成员;接口契约 `ICameraService`/`IJuiceService` 不变,实现细节可等效调整,不算占位符。
