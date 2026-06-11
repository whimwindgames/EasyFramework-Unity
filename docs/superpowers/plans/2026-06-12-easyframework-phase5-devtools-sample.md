# EasyFramework Phase 5(DevTools + 新游戏模板 + 示例游戏验收)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase 1-4 之上交付设计文档 §6(DevTools 与新游戏工作流)与 §7(测试策略)收口:真机调试控制台 + `[Cheat]` 作弊系统 + FPS/内存角标(发布版零开销)、`_Template` 新游戏模板(可编译、十分钟上手)、示例游戏 **TapRush**(用整套框架做一个最小完整游戏,逐一打通 G 门面各能力 + Addressables 真实加载 + Fake 商业化真机冒烟)、框架主使用文档,最后全量 EditMode + TapRush PlayMode 验收。

**Architecture:** 沿用「VContainer DI 内核 + 静态门面 `G`」。DevTools 落在 `EasyFramework.DevTools` 程序集(命名空间 `EasyFramework.DevTools.*`),作弊/角标全部用代码内 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包裹保证发布版零开销、同时程序集始终编译(不用 `defineConstraints`)。`_Template` 落在独立 `EasyFramework.Template` 程序集(references 与 Game 同款),代码可编译非死文档。TapRush 落在既有 `Game` 程序集 + `Assets/Game/Editor/`(Editor-only 一次性资产生成脚本)。所有可测逻辑(Cheat 注册扫描、TapRush 计分/倒计时)抽纯类 EditMode 单测;薄视觉层(角标、UI 代码构建外观)标注不单测(非占位符)。

**Tech Stack:** Unity 6000.3.15f1 / VContainer / UniTask / MessagePipe / Addressables / **IngameDebugConsole(com.yasirkula,OpenUPM)** / Unity Test Framework 1.6。依赖 Phase 1-4 全部既有契约。

**执行环境说明(agent-team 模式):**
- Unity 编辑器已打开,通过 **UnityMCP** 工具操作(装包/加 registry scope `manage_packages`、刷新 `refresh_unity`、编译状态 `mcpforunity://editor/state` 资源、控制台 `read_console`、测试 `run_tests`/`get_test_job`、API 核对 `unity_reflect`、菜单/代码执行 `execute_menu_item`/`execute_code`、Play 控制 `manage_editor`)。
- **并行实现代理只允许用 Write/Edit 工具写文件,禁止调用任何 UnityMCP 工具**(避免并发触发编译)。`.meta` 文件不要手写,由 Unity 刷新时自动生成。
- 计划中的 "Run test" / "PlayMode 验收" / "execute_code" 步骤在 agent-team 模式下由**串行验证代理**统一执行;git 提交由编排者统一执行,实现代理**禁止运行 git 命令**。
- 测试统一写同步完成的用例(Cheat 桥接钩子同步收集;TapRush 纯逻辑类无异步;Fake 服务 `UniTask` 同步返回),用 `.GetAwaiter().GetResult()` 阻塞获取,不依赖 PlayerLoop。

---

## ⚠️ 执行基线声明(执行前必读)

1. **以磁盘实际文件为准。** 本计划锁定的所有跨 Phase 契约(`G.Xxx` 门面属性、`GameLifetimeScope`、`StateMachine<TContext>`/`State<TContext>`、`IBootTask`、`BootCompletedEvent`、`IAssetService`/`AssetScope`、`ISaveService`/`SaveData`/`SaveProfile`、`IPoolService`、`IUIService`/`UIPanel`/`UIPopup<TResult>`/`UILayer`、`IAudioService`、`IInputService`/`TapEvent`、`IAdsService`/`AdResult`、`IIAPService`/`ProductCatalog`/`ProductType`、`IAnalyticsService`、`FrameworkInstaller.Install(IContainerBuilder, FrameworkOptions)`、`FrameworkOptions`)均**以 Phase 1-4 计划文档锁定的签名为基线**;但 **Phase 1-4 正由其它组代理实现中,执行任何引用这些类型的 Task 前,实现代理必须先 `Read` 对应磁盘文件核对实际签名**(命名空间、属性名、方法签名、`G.Initialize`/`Reset` 写法)。**若磁盘实际文件与计划有偏差(各 Phase 的 Deviations 小节),以磁盘实际为准做等效调整,本计划不机械假定。** 偏差汇总见 Task 6 Step 3 的 spec 收口。

2. **本计划不改 Boot 三件套接线文件。** Phase 5 不修改 `Boot/G.cs`/`FrameworkInstaller.cs`/`RootLifetimeScope.cs`——DevTools 的 BootTask 经 `GameLifetimeScope`/`RootLifetimeScope` 既有的 `IBootTask` 收集机制注册(TapRush 的 `GameLifetimeScope` 注册自己的 DevTools BootTask;模板同理),不动框架组合根。**唯一例外**:Task 1 升级 `EasyFramework.DevTools.asmdef`(纯 references 调整)。

3. **DevTools 零开销策略:用代码内 `#if`,不用 `defineConstraints`。** `EasyFramework.DevTools` 程序集**始终参与编译**(asmdef 无 `defineConstraints`),保证其它程序集对它的引用恒定有效;作弊/角标的**实际逻辑体**全部包在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内,发布版(非 DEVELOPMENT_BUILD)这些方法体为空/no-op,零运行时开销。公共 API 签名(`CheatAttribute`/`CheatRegistry`/`PerfOverlay`/`DevToolsBootTask`)在 `#if` 内外**签名一致**——发布版保留空壳方法以维持引用方编译通过(详见各 Task)。

4. **第三方 API 名以实际为准。** `IngameDebugConsole` 的 `DebugLogConsole.AddCommand` 签名、prefab 加载方式由验证代理用 `unity_reflect` / 包文档核对;本计划给出主路径写法 + 回退动作。

---

## 锁定契约(Phase 5 新增,签名不可改,跨 Task 一致)

```csharp
namespace EasyFramework.DevTools.Cheats
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class CheatAttribute : System.Attribute
    {
        public string Command { get; }
        public string Description { get; }
        public CheatAttribute(string command, string description = "") { Command = command; Description = description; }
    }

    public static class CheatRegistry
    {
        // 扫描 assembly 内静态方法上的 [Cheat],桥接到 IngameDebugConsole。幂等:重复命令名后注册 LogWarning 跳过。
        public static void RegisterAssembly(System.Reflection.Assembly assembly);
        public static System.Collections.Generic.IReadOnlyList<string> RegisteredCommands { get; }
        // 桥接层测试钩子:替换收集器即可脱离真实 console 单测(默认指向 DebugLogConsole.AddCommand 桥)。
        internal static System.Action<string, string, System.Reflection.MethodInfo> AddCommandHandler;
    }
}

namespace EasyFramework.DevTools.Overlay
{
    public sealed class PerfOverlay : UnityEngine.MonoBehaviour { }   // OnGUI 角标,#if 内有效
}

namespace EasyFramework.DevTools
{
    // GameLifetimeScope 注册;DEVELOPMENT_BUILD/EDITOR 下 AddComponent<PerfOverlay> + 实例化 console prefab + 注册 cheats。
    public sealed class DevToolsBootTask : EasyFramework.Core.Boot.IBootTask
    {
        public int Priority => 90;
        public bool IsCritical => false;
        public Cysharp.Threading.Tasks.UniTask InitializeAsync(System.Threading.CancellationToken ct);
    }
}

// TapRush 纯逻辑(Assets/Game):
namespace Game
{
    public sealed class TapRushSession
    {
        public TapRushSession(float durationSeconds, int highScore);
        public int Score { get; }
        public float TimeRemaining { get; }
        public bool IsOver { get; }
        public int HighScore { get; }              // = max(已传入 highScore, Score)
        public void AddScore(int amount);          // IsOver 后调用无效
        public void TickDown(float deltaSeconds);  // 扣倒计时,夹到 0;归零即 IsOver
    }
}
```

---

## 文件结构总览

```
Packages/manifest.json                                  (修改:scopedRegistries 加 com.yasirkula scope;新增 com.yasirkula.ingamedebugconsole)
Assets/EasyFramework/
├── DevTools/
│   ├── EasyFramework.DevTools.asmdef                   (修改:references 升级 + IngameDebugConsole)
│   ├── AssemblyInfo.cs                                 (替换占位:InternalsVisibleTo Tests)
│   ├── Cheats/
│   │   ├── CheatAttribute.cs
│   │   └── CheatRegistry.cs                            (#if 包逻辑,签名恒在)
│   ├── Overlay/
│   │   └── PerfOverlay.cs                              (OnGUI 角标,#if 包逻辑)
│   └── DevToolsBootTask.cs                             (Priority=90, IsCritical=false)
├── _Template/
│   ├── EasyFramework.Template.asmdef                   (references 与 Game 同款 + DevTools)
│   ├── TemplateGameLifetimeScope.cs
│   ├── TemplateGameFlow.cs
│   ├── TemplateSaveData.cs                             (SaveProfile 覆盖示例载荷)
│   └── README.md                                       (中文:复制步骤 / 改名清单 / 十分钟上手)
├── README.md                                           (框架主使用文档:架构图 + G 速查表 + 上手 + SDK 接入槽)
└── Tests/EditMode/
    ├── EasyFramework.Tests.EditMode.asmdef             (修改:追加 EasyFramework.DevTools + Game 引用)
    ├── CheatRegistryTests.cs
    └── TemplateGameFlowTests.cs
Assets/Game/
├── Game.asmdef                                         (修改:追加 EasyFramework.DevTools 引用,确认 UI/Audio/Input/Monetization 可见)
├── TapRushSession.cs                                   (纯逻辑,可测)
├── TapRushSaveData.cs                                  (TapRushSaveData : SaveData{ HighScore })
├── TapRushGameLifetimeScope.cs                         (注册 SaveProfile/ProductCatalog/RewardHandler/DevToolsBootTask)
├── TapRushFlow.cs                                      (StateMachine 三态:Menu/Gameplay/Result)
├── TapRushFlowBootTask.cs                              (BootCompleted 后进 Menu)
├── TapRushCircle.cs                                    (圆圈对象:IPoolable + 点击得分)
├── UI/
│   ├── MenuWindow.cs                                   (代码构建 UI)
│   ├── GameHud.cs
│   ├── ResultWindow.cs
│   ├── ConfirmPopup.cs                                 (UIPopup<bool>)
│   └── UiBuild.cs                                      (programmatic uGUI 构建辅助)
├── TapRushCheats.cs                                    ([Cheat] 静态方法示例)
└── Editor/
    ├── Game.Editor.asmdef                              (includePlatforms: Editor;references Game + Addressables Editor)
    └── TapRushAssetSetup.cs                            (一次性:面板 prefab 化 + addressable 标记 + 程序化 sfx clip + sprite)
Assets/Game/Tests/EditMode/
├── Game.Tests.EditMode.asmdef                          (新增:references Game + TestRunner)
└── TapRushSessionTests.cs
docs/superpowers/specs/2026-06-12-easyframework-design.md (修改:末尾追加「实现偏差摘要」一节)
```

依赖方向:`Tests → {Boot, Core, Services, Monetization, DevTools, Game, Template}`;`DevTools → Core`;`Template → {Boot, Core, Services, Monetization, DevTools}`;`Game → {Boot, Core, Services, Monetization, DevTools}`;`Game.Editor → {Game, Addressables Editor}`。Phase 5 只在 DevTools/_Template/Game 内新增,并升级三个 asmdef references + 加包。

---

### Task 1: 包安装 + asmdef 升级(串行,使用 UnityMCP)

> 后续全部 Task 前置(Cheat 系统桥接 `IngameDebugConsole`;Tests/Game 要引用 `EasyFramework.DevTools`)。先落这一步。

**Files:**
- Modify: `Packages/manifest.json`(由 PM / 手动写入 scopedRegistries scope)
- Modify: `Assets/EasyFramework/DevTools/EasyFramework.DevTools.asmdef`
- Create/Replace: `Assets/EasyFramework/DevTools/AssemblyInfo.cs`
- Modify: `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`
- Modify: `Assets/Game/Game.asmdef`

- [ ] **Step 1: 给 OpenUPM scoped registry 追加 `com.yasirkula` scope 并安装 IngameDebugConsole**

OpenUPM scoped registry **已在 Phase 3b Task 1 添加**(`name: "OpenUPM"`, `url: "https://package.openupm.com"`, `scopes: ["com.kyrylokuzyk"]`)。本步骤只需把 `com.yasirkula` 加进同一 registry 的 `scopes` 数组,**不新增 registry**。

核对与执行步骤(优先用工具,工具不支持再手改 manifest):
1. **核对现状**:`Read` `Packages/manifest.json`,确认顶层有 `scopedRegistries` 且含 OpenUPM 项。
   - 若 `manage_packages` 支持 `add_registry`/`add_scoped_registry` 且**对已存在 registry 幂等合并 scopes**:调用 `add_registry(name="OpenUPM", url="https://package.openupm.com", scopes=["com.kyrylokuzyk","com.yasirkula"])`,验证它是**合并**而非新增重复 registry(再 `Read` manifest 核对只有一个 OpenUPM 项、`scopes` 同时含两者)。
   - 若工具不支持幂等或会重复添加:**直接编辑 manifest** 把 OpenUPM 项的 `scopes` 改为 `["com.kyrylokuzyk", "com.yasirkula"]`(只追加一个字符串,不动 registry 其余字段)。
2. **装包**:`manage_packages` add `com.yasirkula.ingamedebugconsole`(不带版本号,解析为 OpenUPM 当前最新稳定版;若工具支持 `list_versions` 先查到具体 `x.y.z` 再装)。

目标 manifest 顶层(片段,核对用):

```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": ["com.kyrylokuzyk", "com.yasirkula"]
  }
]
```

> 说明:`IngameDebugConsole` 程序集名以实际为准(常见为 `IngameDebugConsole`,命名空间 `IngameDebugConsole`)——Task 1 Step 2 验证编译后,实现代理用 `unity_reflect("IngameDebugConsole.DebugLogConsole")` 核对程序集名与 `AddCommand` 签名,确认后再据实填入 DevTools asmdef references(Step 3)。若程序集名不同(如 `yasirkula.IngameDebugConsole`),以实际为准。

- [ ] **Step 2: 验证装包后编译干净**

`refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false` → `read_console(types=["error"])`。Expected: 0 errors。IngameDebugConsole 会带入一个 prefab(`IngameDebugConsole` 文件夹,通常含 `Resources/IngameDebugConsole` 预制体),属正常产物。

- [ ] **Step 3: 升级 DevTools asmdef** — `Assets/EasyFramework/DevTools/EasyFramework.DevTools.asmdef`

Phase 1 中此 asmdef 为占位(references 仅 `EasyFramework.Core`/`UniTask`/`VContainer`/`MessagePipe`)。完整替换为(追加 `EasyFramework.Services`、`IngameDebugConsole` 程序集名以 Step 1 核对结果为准):

```json
{
  "name": "EasyFramework.DevTools",
  "rootNamespace": "EasyFramework.DevTools",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "UniTask",
    "VContainer",
    "MessagePipe",
    "IngameDebugConsole"
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

> 不加 `defineConstraints`:DevTools 程序集始终编译,Game/Template 对它的引用恒定有效(零开销靠代码内 `#if`,见 §执行基线 3)。`EasyFramework.Services` 供 `DevToolsBootTask` 在 EDITOR/DEV 下用到的服务(若仅用 Core 可去掉,但保留无害且便于扩展)。`IngameDebugConsole` 名以 `unity_reflect` 核对为准。

- [ ] **Step 4: DevTools AssemblyInfo** — `Assets/EasyFramework/DevTools/AssemblyInfo.cs`

替换 Phase 1 占位注释为(`CheatRegistry.AddCommandHandler` 为 internal,测试需可见):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EasyFramework.Tests.EditMode")]
```

- [ ] **Step 5: 升级 Tests asmdef** — `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`

**先 `Read` 实际落地文件**(Phase 1-4 多次升级过 references)。以实际为基线**追加** `"EasyFramework.DevTools"` 与 `"Game"`(CheatRegistry 测试要引用 DevTools internal;TemplateGameFlow 测试引用 `EasyFramework.Template` 不行——Template 是独立程序集,TemplateGameFlow 测试改放 `EasyFramework.Template` 可见性,见 Task 4 说明;此处 Tests 追加 `Game` 是因为 TapRush 纯逻辑测试放在独立 `Game.Tests.EditMode`,见 Task 5,故 Tests asmdef 这里**只追加 `EasyFramework.DevTools`**)。修正:本步只追加 `"EasyFramework.DevTools"`。合并后 references 数组示例(以实际基线为准增量):

```
"references": [ ...既有全部..., "EasyFramework.DevTools" ]
```

> 说明:`CheatRegistryTests` 放在主 `EasyFramework.Tests.EditMode`(引用 DevTools)。`TemplateGameFlowTests` 需引用 `EasyFramework.Template`——见 Task 4,将其放入主 Tests asmdef 并追加 `"EasyFramework.Template"` 引用(Template 程序集 Task 4 创建)。`TapRushSessionTests` 放独立 `Game.Tests.EditMode`(Task 5),不进主 Tests asmdef。故主 Tests asmdef 最终追加两项:`"EasyFramework.DevTools"`、`"EasyFramework.Template"`(Template 在 Task 4 落地后此引用才解析,验证统一在 Task 7 收口)。

- [ ] **Step 6: 升级 Game asmdef** — `Assets/Game/Game.asmdef`

**先 `Read` 实际文件**(Phase 1 创建,references 含 `EasyFramework.Core`/`Services`/`Boot`/`UniTask`/`VContainer`)。TapRush 要用 UI/Audio/Input(在 `EasyFramework.Services`,已含)、Monetization(`EasyFramework.Monetization`)、DevTools、以及 Addressables 标记 key 在运行时无需 Editor 程序集。以实际为基线追加 `"EasyFramework.Monetization"`、`"EasyFramework.DevTools"`、`"MessagePipe"`、`"Unity.InputSystem"`(订阅 `TapEvent` 经 `IEventBus` 无需 InputSystem,但 `TapRushCircle` 用屏幕坐标转换可能用到;若不直接用可省)、`"Unity.TextMeshPro"`(UI 文本)、`"PrimeTween"`(UI 入出场动画,可选)。合并后完整内容(以实际基线增量,下方为目标态):

```json
{
  "name": "Game",
  "rootNamespace": "Game",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "EasyFramework.Monetization",
    "EasyFramework.Boot",
    "EasyFramework.DevTools",
    "UniTask",
    "VContainer",
    "MessagePipe",
    "Unity.Addressables",
    "Unity.ResourceManager",
    "Unity.InputSystem",
    "Unity.TextMeshPro"
  ]
}
```

> 若实际 Phase 1 Game asmdef 字段更少,以实际为基线只追加上述新增项,不删既有。`Unity.TextMeshPro` 供 `TMP_Text`/`TextMeshProUGUI`(代码构建 UI 文本);`Unity.Addressables`/`Unity.ResourceManager` 供运行时 key 加载经 `G.Asset`(实际加载由框架做,Game 不直接调 Addressables API,但 prefab key 字符串无需引用——保留 Addressables 引用便于 `TapRushCircle` 若需 `AssetReference`;若不用可省)。**实现代理按实际编译错误增删**:缺什么类型补什么程序集,核心是 UI(TextMeshPro)与门面类型(Services/Monetization/DevTools)。

- [ ] **Step 7: 验证编译干净** — `refresh_unity` → 轮询 → `read_console(types=["error"])`。Expected: 0 errors(此时 DevTools/Game 仅 asmdef 升级,尚无新代码,应干净;`EasyFramework.Template` 引用在 Task 4 落地前,Tests asmdef 含该引用会报"未找到程序集"——**故 Step 5 的 `"EasyFramework.Template"` 追加延后到 Task 4 落地后再加**,Step 5 此刻只加 `"EasyFramework.DevTools"`,Task 4 再补 Template 引用。修正执行顺序见各 Task)。

- [ ] **Step 8: Commit**(编排者执行)

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/EasyFramework/DevTools Assets/Game/Game.asmdef Assets/EasyFramework/Tests
git commit -m "feat: add IngameDebugConsole (OpenUPM com.yasirkula), upgrade DevTools/Game asmdef references"
```

---

### Task 2: Cheat 系统(可并行;依赖 Task 1 asmdef)

**Files:**
- Create: `Assets/EasyFramework/DevTools/Cheats/CheatAttribute.cs`, `CheatRegistry.cs`
- Test: `Assets/EasyFramework/Tests/EditMode/CheatRegistryTests.cs`

**设计要点:**
- `CheatAttribute` 始终编译(纯特性,无 `#if`),供 Game 代码标注。`CheatRegistry.RegisterAssembly` 的扫描/桥接逻辑包在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内;发布版 `RegisterAssembly` 为空 no-op、`RegisteredCommands` 返回空列表(签名恒在,引用方编译通过、零开销)。
- 桥接层抽 `internal static Action<string command, string description, MethodInfo method> AddCommandHandler` 钩子:默认指向把命令注册到 `IngameDebugConsole.DebugLogConsole.AddCommand` 的桥;测试 `[SetUp]` 替换为收集器、`[TearDown]` 还原,**不依赖真实 console**。
- 仅支持**静态方法**且参数为 无参 / 单个 `int`/`float`/`string`/`bool`(或这些类型的有限组合——为契约简单,支持 0..N 个上述基元参数);不支持的参数类型 `Debug.LogWarning` 跳过该方法。命令名重复:后注册 `LogWarning` 跳过(先到先得)。

- [ ] **Step 1: 写失败测试** — `CheatRegistryTests.cs`

替换 `AddCommandHandler` 为收集器,断言:扫描注册计数、命令名去重(后注册被跳过)、不支持签名被跳过、`RegisteredCommands` 反映已注册命令。测试在**测试程序集自己**定义带 `[Cheat]` 的静态方法(扫描测试程序集),避免依赖真实 console。

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using EasyFramework.DevTools.Cheats;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class CheatRegistryTests
    {
        // ---- 被扫描的测试用作弊方法(本测试程序集内的静态方法)----
        static readonly List<string> Invoked = new();

        [Cheat("noarg_cmd", "no-arg cheat")]
        public static void NoArg() => Invoked.Add("noarg");

        [Cheat("int_cmd")]
        public static void IntArg(int amount) => Invoked.Add($"int:{amount}");

        [Cheat("mixed_cmd")]
        public static void Mixed(string name, bool flag) => Invoked.Add($"mixed:{name}:{flag}");

        // 重复命令名:后注册应被跳过(保留 NoArg 的 "noarg_cmd")
        [Cheat("noarg_cmd")]
        public static void DuplicateName() => Invoked.Add("dup");

        // 不支持的参数类型(Vector3 非基元)→ 跳过
        [Cheat("unsupported_cmd")]
        public static void Unsupported(UnityEngine.Vector3 v) => Invoked.Add("unsupported");

        // 非静态方法即使带 [Cheat] 也不应被注册(扫描只取静态)
        [Cheat("instance_cmd")]
        public void InstanceCheat() => Invoked.Add("instance");

        // ---- 收集器:替换桥接钩子 ----
        sealed class Collected
        {
            public string Command;
            public string Description;
            public MethodInfo Method;
        }

        List<Collected> _collected;
        Action<string, string, MethodInfo> _original;

        [SetUp]
        public void SetUp()
        {
            Invoked.Clear();
            _original = CheatRegistry.AddCommandHandler;
            _collected = new List<Collected>();
            CheatRegistry.AddCommandHandler =
                (cmd, desc, m) => _collected.Add(new Collected { Command = cmd, Description = desc, Method = m });
        }

        [TearDown]
        public void TearDown() => CheatRegistry.AddCommandHandler = _original;

        [Test]
        public void RegisterAssembly_RegistersSupportedStaticCheats()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());

            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);

            CollectionAssert.Contains(names, "noarg_cmd");
            CollectionAssert.Contains(names, "int_cmd");
            CollectionAssert.Contains(names, "mixed_cmd");
        }

        [Test]
        public void RegisterAssembly_SkipsDuplicateCommandName()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());

            var count = 0;
            foreach (var c in _collected) if (c.Command == "noarg_cmd") count++;
            Assert.AreEqual(1, count, "重复命令名只注册一次(后注册被跳过)");
        }

        [Test]
        public void RegisterAssembly_SkipsUnsupportedSignature()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);
            CollectionAssert.DoesNotContain(names, "unsupported_cmd");
        }

        [Test]
        public void RegisterAssembly_SkipsInstanceMethod()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            var names = new List<string>();
            foreach (var c in _collected) names.Add(c.Command);
            CollectionAssert.DoesNotContain(names, "instance_cmd");
        }

        [Test]
        public void RegisteredCommands_ReflectsRegistered()
        {
            CheatRegistry.RegisterAssembly(Assembly.GetExecutingAssembly());
            CollectionAssert.Contains(CheatRegistry.RegisteredCommands, "noarg_cmd");
            CollectionAssert.Contains(CheatRegistry.RegisteredCommands, "int_cmd");
            // 不支持/重复/实例方法不在已注册列表
            CollectionAssert.DoesNotContain(CheatRegistry.RegisteredCommands, "unsupported_cmd");
            CollectionAssert.DoesNotContain(CheatRegistry.RegisteredCommands, "instance_cmd");
        }
    }
}
```

> 测试备注:发布版构建(无 `UNITY_EDITOR`/`DEVELOPMENT_BUILD`)下 `RegisterAssembly` 为 no-op,`_collected` 为空——但 **EditMode 测试始终在编辑器下运行**(`UNITY_EDITOR` 恒真),故 `#if` 内逻辑被编译执行,测试有效。Cheat 命令名去重与跳过的 `LogWarning` 是被测路径的预期输出,验证代理可用 `LogAssert.Expect(LogType.Warning, ...)` 收口或在报告注明。

- [ ] **Step 2: 实现 CheatAttribute** — `CheatAttribute.cs`(始终编译,无 `#if`)

```csharp
namespace EasyFramework.DevTools.Cheats
{
    /// <summary>标注静态方法为作弊命令。仅在 UNITY_EDITOR || DEVELOPMENT_BUILD 下由 CheatRegistry 注册到调试控制台。</summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class CheatAttribute : System.Attribute
    {
        public string Command { get; }
        public string Description { get; }

        public CheatAttribute(string command, string description = "")
        {
            Command = command;
            Description = description;
        }
    }
}
```

- [ ] **Step 3: 实现 CheatRegistry** — `CheatRegistry.cs`

签名恒在;逻辑体 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内有效,否则 no-op。桥接钩子 `AddCommandHandler` 默认指向 `IngameDebugConsole` 桥(API 名以 `unity_reflect` 核对)。

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
#endif

namespace EasyFramework.DevTools.Cheats
{
    /// <summary>
    /// 扫描程序集内静态 [Cheat] 方法并桥接到 IngameDebugConsole。
    /// 全部逻辑包在 UNITY_EDITOR || DEVELOPMENT_BUILD 内(发布版零开销);公共签名恒在以保证引用方编译。
    /// </summary>
    public static class CheatRegistry
    {
        static readonly List<string> _registered = new();

        public static IReadOnlyList<string> RegisteredCommands => _registered;

        /// <summary>
        /// 桥接收集器:default 把命令转发到 DebugLogConsole.AddCommand;测试可替换为收集器以脱离真实 console。
        /// 参数:command / description / 目标静态方法。
        /// </summary>
        internal static Action<string, string, MethodInfo> AddCommandHandler =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BridgeToConsole;
#else
            null;
#endif

        /// <summary>扫描 assembly 内所有静态方法上的 [Cheat],注册受支持的命令。重复命令名/不支持签名跳过。</summary>
        public static void RegisterAssembly(Assembly assembly)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (assembly == null) return;

            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<CheatAttribute>();
                    if (attr == null) continue;

                    if (!IsSupportedSignature(method))
                    {
                        Debug.LogWarning(
                            $"[EasyFramework] Cheat '{attr.Command}' on {type.Name}.{method.Name} " +
                            "has unsupported parameters (only static methods with 0..N of int/float/string/bool are allowed); skipped.");
                        continue;
                    }

                    if (_registered.Contains(attr.Command))
                    {
                        Debug.LogWarning(
                            $"[EasyFramework] Duplicate cheat command '{attr.Command}' " +
                            $"({type.Name}.{method.Name}); keeping first registration, skipping this one.");
                        continue;
                    }

                    AddCommandHandler?.Invoke(attr.Command, attr.Description, method);
                    _registered.Add(attr.Command);
                }
            }
#else
            // 发布版:no-op(零开销)。
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static bool IsSupportedSignature(MethodInfo method)
        {
            if (!method.IsStatic) return false;          // 仅静态
            foreach (var p in method.GetParameters())
            {
                var t = p.ParameterType;
                if (t != typeof(int) && t != typeof(float) &&
                    t != typeof(string) && t != typeof(bool))
                    return false;
            }
            return true;
        }

        /// <summary>默认桥:把 Cheat 注册为 IngameDebugConsole 命令。API 名以 unity_reflect 核对为准。</summary>
        static void BridgeToConsole(string command, string description, MethodInfo method)
        {
            // IngameDebugConsole 主路径:DebugLogConsole.AddCommand( command, description, methodGroup )
            // 它支持把任意带兼容签名的方法注册为命令。无参/基元参数命令由 console 解析输入字符串转参后调用。
            // —— 实现代理用 unity_reflect("IngameDebugConsole.DebugLogConsole") 核对实际重载:
            //    优先用 AddCommand(string command, string description, Delegate method) 或
            //    AddCommand(string command, string description, MethodInfo method, object instance=null)。
            // 静态方法 instance 传 null。下方为主路径写法;若签名不符按实际等效调整(本桥是唯一触真实 console 处,逻辑极薄)。
            try
            {
                var del = method.CreateDelegate(
                    System.Linq.Expressions.Expression.GetDelegateType(GetDelegateTypeArgs(method)));
                IngameDebugConsole.DebugLogConsole.AddCommand(command, description, del);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] Failed to bridge cheat '{command}' to console: {e.Message}");
            }
        }

        static Type[] GetDelegateTypeArgs(MethodInfo method)
        {
            var ps = method.GetParameters();
            var args = new Type[ps.Length + 1];
            for (var i = 0; i < ps.Length; i++) args[i] = ps[i].ParameterType;
            args[ps.Length] = method.ReturnType; // Expression.GetDelegateType 末位为返回类型(void→typeof(void))
            return args;
        }
#endif
    }
}
```

> 实现说明:
> - `CreateDelegate` + `Expression.GetDelegateType` 把任意 0..N 基元参数 + void 返回的静态方法转成对应 `Action<...>`/`Func<...>` 委托交给 console。`DebugLogConsole.AddCommand(string, string, Delegate)` 是 IngameDebugConsole 支持的重载之一;**API 名/重载由 `unity_reflect` 核对**,若该版本只接受 `MethodInfo` 或方法组,按实际等效调整(`BridgeToConsole` 是唯一触真实 console 的薄层,逻辑极少,不单测——测试替换 `AddCommandHandler` 钩子覆盖了扫描/去重/跳过分支)。
> - `using System.Linq.Expressions` 经全限定名 `System.Linq.Expressions.Expression` 调用,避免顶层 using 在 `#else` 分支冗余;`System.Linq.Expressions` 随 mscorlib 可用,无需额外引用。
> - 若 `Expression.GetDelegateType` 在目标运行时受限(AOT/IL2CPP 对开放委托构造),回退:用 `switch (ps.Length)` 显式映射 0/1/2 参的 `Action`/`Action<T>`/`Action<T1,T2>`(覆盖 TapRush 实际作弊方法签名:无参与单参足够)。回退动作明确,非占位符。

- [ ] **Step 4: 验证测试通过**(验证代理:`run_tests` 按 `EasyFramework.Tests.CheatRegistryTests` 过滤)Expected: 全 PASS。
- [ ] **Step 5: Commit**(编排者)`git commit -m "feat(devtools): add [Cheat] attribute and CheatRegistry with console bridge"`

---

### Task 3: FPS/内存角标 + DevToolsBootTask(可并行;依赖 Task 2 的 CheatRegistry)

**Files:**
- Create: `Assets/EasyFramework/DevTools/Overlay/PerfOverlay.cs`, `Assets/EasyFramework/DevTools/DevToolsBootTask.cs`

> **薄视觉层标注不单测(非占位符):** `PerfOverlay` 是 `OnGUI` 简易角标(FPS 滑动平均 + `Profiler.GetTotalAllocatedMemoryLong()/MB`),无分支逻辑可测,行为靠 Play 模式肉眼验证(沿用 Phase 2/3 对薄实现不单测约定)。`DevToolsBootTask` 的可测部分(是否在非 DEV 下 no-op)由编译符号决定,EditMode 下 `UNITY_EDITOR` 恒真不便测两态,故标注不单测;其挂载行为在 Task 7 的 TapRush PlayMode 验收中间接覆盖(控制台呼出 + 角标可见)。

- [ ] **Step 1: 实现 PerfOverlay** — `PerfOverlay.cs`

逻辑体 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内;发布版组件存在但 `OnGUI` 空(零开销)。

```csharp
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.Profiling;
#endif

namespace EasyFramework.DevTools.Overlay
{
    /// <summary>左上角 FPS(滑动平均)+ 已分配内存(MB)简易角标。仅 UNITY_EDITOR || DEVELOPMENT_BUILD 下绘制,发布版零开销。</summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float Smoothing = 0.1f;   // 指数滑动平均系数
        float _avgDeltaTime;
        GUIStyle _style;

        void Update()
        {
            // 指数滑动平均,避免逐帧抖动
            _avgDeltaTime += (Time.unscaledDeltaTime - _avgDeltaTime) * Smoothing;
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                normal = { textColor = Color.green },
                alignment = TextAnchor.UpperLeft,
            };

            var fps = _avgDeltaTime > 0f ? 1f / _avgDeltaTime : 0f;
            var memMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            var text = $"FPS {fps:00.0}\nMEM {memMb:000.0} MB";

            // 安全区内偏移一点,避开刘海
            var rect = new Rect(10f + Screen.safeArea.x, 10f + (Screen.height - Screen.safeArea.yMax), 260f, 60f);
            GUI.Label(rect, text, _style);
        }
#endif
    }
}
```

- [ ] **Step 2: 实现 DevToolsBootTask** — `DevToolsBootTask.cs`

`IBootTask`(Priority=90, IsCritical=false);DEV/EDITOR 下:在 `RootLifetimeScope` 同节点 AddComponent `PerfOverlay` + 实例化 `IngameDebugConsole` prefab + 注册 cheats(扫描已加载程序集)。构造注入一个"宿主 GameObject 提供者"——为避免触 Boot 接线文件,`DevToolsBootTask` 用 `Object.FindFirstObjectByType<EasyFramework.RootLifetimeScope>()` 拿到根节点(它 `DontDestroyOnLoad`),AddComponent 到其 `gameObject`;若找不到则自建一个 `DontDestroyOnLoad` 的 `[DevTools]` 节点兜底。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Reflection;
using EasyFramework.DevTools.Cheats;
using EasyFramework.DevTools.Overlay;
using UnityEngine;
#endif

namespace EasyFramework.DevTools
{
    /// <summary>
    /// DevTools 启动任务:DEV/EDITOR 下挂 PerfOverlay 角标、实例化 IngameDebugConsole、注册作弊命令。
    /// 非关键(IsCritical=false)、晚启(Priority=90),失败不阻塞游戏。发布版 InitializeAsync 为 no-op。
    /// 由 GameLifetimeScope 注册为 IBootTask(不改框架 Boot 接线文件)。
    /// </summary>
    public sealed class DevToolsBootTask : IBootTask
    {
        public int Priority => 90;
        public bool IsCritical => false;

        public UniTask InitializeAsync(CancellationToken ct)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                var host = ResolveHost();

                // 1) FPS/内存角标
                if (host.GetComponent<PerfOverlay>() == null)
                    host.AddComponent<PerfOverlay>();

                // 2) IngameDebugConsole 预制体实例化(三指下滑呼出)
                //    IngameDebugConsole 在 Resources/IngameDebugConsole 提供 prefab(加载方式以包文档/unity_reflect 核对)。
                EnsureDebugConsole(host);

                // 3) 注册全部已加载程序集里的 [Cheat](Game/Template 的作弊方法)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // 只扫描业务/框架程序集,跳过系统程序集以省时(名字前缀过滤)
                    var name = asm.GetName().Name;
                    if (name.StartsWith("Game") || name.StartsWith("EasyFramework"))
                        CheatRegistry.RegisterAssembly(asm);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasyFramework] DevToolsBootTask failed (non-critical): {e.Message}");
            }
#endif
            return UniTask.CompletedTask;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static GameObject ResolveHost()
        {
            var root = UnityEngine.Object.FindFirstObjectByType<EasyFramework.RootLifetimeScope>();
            if (root != null) return root.gameObject;

            var host = new GameObject("[DevTools]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            return host;
        }

        static void EnsureDebugConsole(GameObject host)
        {
            // 已存在则不重复实例化(IngameDebugConsole 单例)。
            // 主路径:从 Resources 加载包自带 prefab 并 Instantiate;DontDestroyOnLoad。
            // 实现代理用 unity_reflect / 包 README 核对 prefab 路径与是否已有单例 API:
            //   var existing = UnityEngine.Object.FindFirstObjectByType<IngameDebugConsole.DebugLogManager>();
            //   if (existing != null) return;
            //   var prefab = Resources.Load<GameObject>("IngameDebugConsole");
            //   if (prefab != null) UnityEngine.Object.DontDestroyOnLoad(UnityEngine.Object.Instantiate(prefab));
            var existing = UnityEngine.Object.FindFirstObjectByType<IngameDebugConsole.DebugLogManager>();
            if (existing != null) return;

            var prefab = Resources.Load<GameObject>("IngameDebugConsole");
            if (prefab == null)
            {
                Debug.LogWarning("[EasyFramework] IngameDebugConsole prefab not found in Resources; console not spawned.");
                return;
            }
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "[IngameDebugConsole]";
            UnityEngine.Object.DontDestroyOnLoad(instance);
        }
#endif
    }
}
```

> 实现说明:
> - `IngameDebugConsole.DebugLogManager` 与 Resources prefab 路径以**包实际为准**(`unity_reflect("IngameDebugConsole.DebugLogManager")` 核对类型存在;`Resources.Load<GameObject>("IngameDebugConsole")` 路径以包 README 核对,常见即此名)。若包提供更高层 API(如自动单例),用其等效写法。这是接入薄层,逻辑由 `try/catch` + 非关键兜底,失败只 `LogWarning`。
> - `DevToolsBootTask` 经 `GameLifetimeScope` 注册(Task 4 模板 / Task 5 TapRush 都注册它),`GameBootstrap` 自动收集(`IsCritical=false` 失败不阻塞)。**不改框架 Boot 接线文件**(§执行基线 2)。
> - `FindFirstObjectByType` 为 Unity 2023+/6 API。

- [ ] **Step 3: 验证编译**(验证代理 Task 7 统一编译;本 Task 无独立测试,薄视觉层不单测)
- [ ] **Step 4: Commit**(编排者)`git commit -m "feat(devtools): add PerfOverlay and DevToolsBootTask (console + cheats wiring)"`

---

### Task 4: _Template 新游戏模板(可并行;依赖 Task 1-3)

**Files:**
- Create: `Assets/EasyFramework/_Template/EasyFramework.Template.asmdef`
- Create: `Assets/EasyFramework/_Template/TemplateSaveData.cs`, `TemplateGameLifetimeScope.cs`, `TemplateGameFlow.cs`, `README.md`
- Test: `Assets/EasyFramework/Tests/EditMode/TemplateGameFlowTests.cs`(在主 Tests asmdef,Task 1 Step 5 此时补 `"EasyFramework.Template"` 引用)

> **可编译非死文档:** `_Template` 代码放进独立 `EasyFramework.Template` 程序集(references 与 Game 同款 + DevTools),必须能编译。三态切换冒烟测试复用 Phase 1 `StateMachine` 测试手法。

- [ ] **Step 1: 写 Template asmdef** — `EasyFramework.Template.asmdef`

```json
{
  "name": "EasyFramework.Template",
  "rootNamespace": "EasyFramework.Template",
  "references": [
    "EasyFramework.Core",
    "EasyFramework.Services",
    "EasyFramework.Monetization",
    "EasyFramework.Boot",
    "EasyFramework.DevTools",
    "UniTask",
    "VContainer",
    "MessagePipe"
  ]
}
```

> 与 Game 同款(UI/Audio/Input 在 Services;Ads/IAP/Analytics 在 Monetization)。模板若示例代码用到 TextMeshPro/Addressables 再追加;骨架仅注册 + 状态机,Core/Services/Monetization/Boot/DevTools 足够。

- [ ] **Step 2: 补主 Tests asmdef 的 Template 引用**

回到 `Assets/EasyFramework/Tests/EditMode/EasyFramework.Tests.EditMode.asmdef`,在 Task 1 Step 5 追加的 `"EasyFramework.DevTools"` 基础上**再追加** `"EasyFramework.Template"`(此时 Template 程序集已在 Step 1 创建,引用可解析)。

- [ ] **Step 3: 实现 TemplateSaveData** — `TemplateSaveData.cs`

```csharp
using EasyFramework.Services.Saves;

namespace EasyFramework.Template
{
    /// <summary>模板存档载荷示例。复制后改成你游戏的字段(并相应改 SaveProfile 的 CurrentVersion/Migrations)。</summary>
    public sealed class TemplateSaveData : SaveData
    {
        // 示例字段:替换为你的游戏数据。
        public int ExampleCoins;
        public string ExamplePlayerName = "Player";
    }
}
```

- [ ] **Step 4: 实现 TemplateGameLifetimeScope** — `TemplateGameLifetimeScope.cs`

继承 `GameLifetimeScope`,`ConfigureGame` 里**带详细注释**示例:覆盖 SaveProfile、注册 RewardHandler、注册 DevToolsBootTask、注册游戏自己的服务/状态机。

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.DevTools;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Saves;
using VContainer;

namespace EasyFramework.Template
{
    /// <summary>
    /// 新游戏入口作用域模板。复制 _Template 到你的 Assets/<YourGame>/ 后:
    ///   1. 改类名 TemplateGameLifetimeScope → <YourGame>LifetimeScope
    ///   2. 改命名空间 EasyFramework.Template → <YourGame>
    ///   3. 在 Boot 场景把本组件挂为 RootLifetimeScope 的子作用域(嵌套 LifetimeScope)
    /// 它注册游戏特有的:存档 profile、商品发奖、状态机/玩法服务、DevTools。
    /// </summary>
    public sealed class TemplateGameLifetimeScope : GameLifetimeScope
    {
        protected override void ConfigureGame(IContainerBuilder builder)
        {
            // ---- 1) 覆盖框架默认存档:注册你游戏的 SaveProfile ----
            // 框架默认用 DefaultSaveData。在这里 RegisterInstance 一个 SaveProfile 即可替换。
            // 注意:RootLifetimeScope 已用 FrameworkOptions.SaveProfile 决定生产存档;子作用域这里注册
            // 的 profile 供游戏侧引用/测试参考。生产替换存档的标准做法是在 RootLifetimeScope 的 Inspector
            // 或 FrameworkOptions 传入本 profile(见 README「改名清单」)。
            builder.RegisterInstance(CreateSaveProfile());

            // ---- 2) 注册 DevTools 启动任务(控制台 + 角标 + 作弊命令)----
            // IsCritical=false,失败不阻塞;发布版(非 DEVELOPMENT_BUILD)内部 no-op。
            builder.Register<DevToolsBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- 3) 注册商品发奖(可选;有内购时)----
            // 框架 IAPService 默认 RewardHandler 只 Debug.Log。游戏在容器 Build 后用
            // resolver.Resolve<IAPService>().SetRewardHandler(...) 注册真实发奖(见 README 示例)。
            // 这里也可注册你的玩法服务、状态机宿主等。

            // ---- 4) 注册你的玩法服务 / 状态机 ----
            // 例:builder.Register<MyGameplaySystem>(Lifetime.Singleton);
            //     builder.RegisterEntryPoint<MyGameFlowDriver>();  // 驱动 TemplateGameFlow
        }

        /// <summary>示例 SaveProfile:把框架存档载荷换成 TemplateSaveData。</summary>
        static SaveProfile CreateSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(TemplateSaveData),
                CurrentVersion = 1,
                CreateNew = () => new TemplateSaveData { Version = 1 },
                Migrations = new List<ISaveMigration>(),   // 版本升级时在此加 v1→v2 等迁移
                FileName = "save.json",
                HmacSalt = "your-game-salt",               // 改成你游戏专属盐
            };
    }
}
```

> 注:`RegisterInstance(SaveProfile)` 的具体注册方式以磁盘 `FrameworkInstaller` 实际为准(Phase 2 计划里 RootLifetimeScope 经 `FrameworkOptions.SaveProfile` 传入生产 profile)。模板注释明确指引「生产替换存档走 RootLifetimeScope/FrameworkOptions」,子作用域注册仅为示例可达性。**实现代理执行时若磁盘 FrameworkInstaller 的 SaveProfile 注册与此不符,按实际机制调整注释,不改契约。**

- [ ] **Step 5: 实现 TemplateGameFlow** — `TemplateGameFlow.cs`

用 `StateMachine<TemplateGameFlow>` 定义 Menu/Gameplay/Result 三态骨架,注释标明扩展点。

```csharp
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Fsm;

namespace EasyFramework.Template
{
    /// <summary>
    /// 游戏流程骨架:Menu → Gameplay → Result。复制后在各 State 里接你的 UI/玩法。
    /// 用法:new TemplateGameFlow().ToMenu()(或由 LifetimeScope 的入口点在 BootCompleted 后驱动)。
    /// </summary>
    public sealed class TemplateGameFlow
    {
        readonly StateMachine<TemplateGameFlow> _fsm;

        // 公开当前状态便于查询/测试。
        public State<TemplateGameFlow> Current => _fsm.Current;

        public TemplateGameFlow()
        {
            _fsm = new StateMachine<TemplateGameFlow>(this);
            _fsm.AddState(new MenuState());
            _fsm.AddState(new GameplayState());
            _fsm.AddState(new ResultState());
        }

        // ---- 流程切换 API(供 UI 按钮 / 玩法事件调用)----
        public UniTask ToMenu() => _fsm.ChangeState<MenuState>();
        public UniTask ToGameplay() => _fsm.ChangeState<GameplayState>();
        public UniTask ToResult() => _fsm.ChangeState<ResultState>();

        public void Tick(float deltaTime) => _fsm.Update(deltaTime);

        // ---- 三态骨架:扩展点在注释里 ----

        public sealed class MenuState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:G.UI.PushAsync<MenuWindow>();播放菜单 BGM 等。
                return UniTask.CompletedTask;
            }
            // Exit / Update 按需覆盖。
        }

        public sealed class GameplayState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:加载关卡场景、G.UI.ShowHudAsync<GameHud>()、开始计时/生成对象。
                return UniTask.CompletedTask;
            }

            public override void Update(float deltaTime)
            {
                // 扩展点:玩法每帧逻辑(倒计时、刷怪)。结束时调 Context.ToResult()。
            }
        }

        public sealed class ResultState : State<TemplateGameFlow>
        {
            public override UniTask Enter()
            {
                // 扩展点:G.UI.PushAsync<ResultWindow>()、结算打点 G.Analytics.Track("level_end")、存档 G.Save.Save()。
                return UniTask.CompletedTask;
            }
        }
    }
}
```

> 注:`StateMachine<TContext>`/`State<TContext>`/`ChangeState<T>`/`Update`/`Current`/`AddState` 的签名以 Phase 1 磁盘文件为准(Phase 1 计划锁定:`State<TContext>` 有 `Enter()→UniTask`/`Update(float)`/`Exit()`;`StateMachine.ChangeState<T>()→UniTask`/`Update(float)`/`Current`/`AddState(State)`)。**实现代理先 Read Phase 1 落地的 `Core/Fsm/State.cs`、`StateMachine.cs` 核对。**

- [ ] **Step 6: 写冒烟测试** — `TemplateGameFlowTests.cs`(主 Tests asmdef,引用 `EasyFramework.Template`)

复用 Phase 1 StateMachine 测试手法,同步驱动三态切换跑通。

```csharp
using EasyFramework.Template;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class TemplateGameFlowTests
    {
        [Test]
        public void Flow_TransitionsThroughThreeStates()
        {
            var flow = new TemplateGameFlow();

            flow.ToMenu().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.MenuState>(flow.Current);

            flow.ToGameplay().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.GameplayState>(flow.Current);

            flow.ToResult().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.ResultState>(flow.Current);
        }

        [Test]
        public void Flow_CanReturnToMenuFromResult()
        {
            var flow = new TemplateGameFlow();
            flow.ToGameplay().GetAwaiter().GetResult();
            flow.ToResult().GetAwaiter().GetResult();
            flow.ToMenu().GetAwaiter().GetResult();
            Assert.IsInstanceOf<TemplateGameFlow.MenuState>(flow.Current);
        }

        [Test]
        public void Flow_TickDoesNotThrow()
        {
            var flow = new TemplateGameFlow();
            flow.ToGameplay().GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => flow.Tick(0.016f));
        }
    }
}
```

- [ ] **Step 7: 写 README.md** — `Assets/EasyFramework/_Template/README.md`

中文,含复制步骤 / 改名清单 / 十分钟上手。

```markdown
# EasyFramework 新游戏模板(_Template)

复制本目录到 `Assets/<你的游戏>/`,改名即跑。约 10 分钟做出一个能跑的新游戏骨架。

## 一、复制步骤(2 分钟)

1. 在 `Assets/` 下复制整个 `_Template` 目录为 `Assets/<YourGame>/`(例:`Assets/MyGame/`)。
2. 删掉本 `README.md`(或留作参考)。
3. Unity 自动重新生成 `.meta`,等待编译完成。

## 二、改名清单(5 分钟)

| 文件 | 改什么 |
|---|---|
| `EasyFramework.Template.asmdef` | `name` 改为 `<YourGame>`;删除/保留 references 按需 |
| 所有 `.cs` 的 `namespace EasyFramework.Template` | 改为 `namespace <YourGame>` |
| `TemplateGameLifetimeScope` | 类名改 `<YourGame>LifetimeScope` |
| `TemplateGameFlow` | 类名改 `<YourGame>Flow`(及 `StateMachine<TemplateGameFlow>` 泛型参数) |
| `TemplateSaveData` | 类名改 `<YourGame>SaveData`,字段换成你的存档数据 |
| `CreateSaveProfile()` 里的 `HmacSalt` | 改成你游戏专属盐 |

## 三、接进框架(3 分钟)

1. 打开 `Assets/Scenes/Boot.unity`。
2. 在 `[EasyFramework]`(挂 `RootLifetimeScope`)下新建子 GameObject,挂你的 `<YourGame>LifetimeScope`(嵌套 LifetimeScope,自动成为 Root 的子作用域)。
3. 若要替换存档:把 `<YourGame>SaveData` 的 `SaveProfile` 通过 `RootLifetimeScope` 的 Inspector / `FrameworkOptions` 传入(框架以 `FrameworkOptions.SaveProfile` 决定生产存档载荷)。
4. Play。`BootCompletedEvent` 发布后,用入口点驱动 `<YourGame>Flow.ToMenu()` 进菜单。

## 四、各能力速查(写玩法时用 G 门面)

| 能力 | 调用 |
|---|---|
| 事件 | `G.Events.Publish(evt)` / `G.Events.Subscribe<T>(h)` |
| 定时器 | `G.Timer.Schedule(delay, cb)` |
| 资源 | `await G.Asset.LoadAsync<T>(key)` |
| 场景 | `await G.Scene.LoadAsync("Level1")` |
| 存档 | `G.Save.Data<YourSaveData>()` / `G.Save.Save()` |
| 配置 | `G.Config.Get("key", defaultValue)` |
| 对象池 | `await G.Pool.SpawnAsync(key, pos)` / `G.Pool.Despawn(go)` |
| UI | `await G.UI.PushAsync<Win>()` / `await G.UI.ShowPopupAsync<P, bool>()` |
| 音频 | `await G.Audio.PlayBgmAsync(key)` / `G.Audio.PlaySfx(key)` |
| 输入手势 | `G.Events.Subscribe<TapEvent>(...)`(手势经事件总线广播) |
| 广告 | `await G.Ads.ShowRewardedAsync("placement")` |
| 内购 | `await G.IAP.PurchaseAsync(id)` / `G.IAP.IsOwned(id)` |
| 统计 | `G.Analytics.Track("event", ("k", v))` |

## 五、DevTools

模板已在 `LifetimeScope` 注册 `DevToolsBootTask`:DEV/编辑器构建下自动挂 FPS/内存角标、实例化调试控制台(三指下滑呼出)、注册你用 `[Cheat("cmd")]` 标注的静态作弊方法。发布版自动零开销。
```

> 注:速查表里的具体签名(如 `G.Audio.PlayBgmAsync`、`G.Pool.SpawnAsync`)以磁盘实际为准——实现代理落地 README 前用 Read 核对 Phase 2/3 的 `IAudioService`/`IPoolService`/`IUIService` 实际方法名,有出入按实际改 README(README 是给人读的,签名必须真)。

- [ ] **Step 8: 验证测试通过 + 编译**(验证代理:`run_tests` 按 `EasyFramework.Tests.TemplateGameFlowTests` 过滤)Expected: 全 PASS,Template 程序集 0 error。
- [ ] **Step 9: Commit**(编排者)`git commit -m "feat(template): add compilable new-game template with flow, save profile, README"`

---

### Task 5: 示例游戏 TapRush(依赖 Task 1-4;验收整个框架)

**玩法:** 点击屏幕上随机出现的圆圈得分,60 秒倒计时,结束进结算。逐一打通框架能力。

**Files:**
- Create(运行时,`Assets/Game/`):`TapRushSession.cs`、`TapRushSaveData.cs`、`TapRushGameLifetimeScope.cs`、`TapRushFlow.cs`、`TapRushFlowBootTask.cs`、`TapRushCircle.cs`、`TapRushCheats.cs`、`UI/UiBuild.cs`、`UI/MenuWindow.cs`、`UI/GameHud.cs`、`UI/ResultWindow.cs`、`UI/ConfirmPopup.cs`
- Create(Editor):`Assets/Game/Editor/Game.Editor.asmdef`、`Assets/Game/Editor/TapRushAssetSetup.cs`
- Create(测试):`Assets/Game/Tests/EditMode/Game.Tests.EditMode.asmdef`、`TapRushSessionTests.cs`

> **音效方案选定(写明):** 选 **Editor 脚本程序化生成 0.1s 正弦波 `AudioClip` 存为 asset**(`TapRushAssetSetup.cs` 内,由验证代理一次性执行),运行时经 `G.Asset.LoadAsync<AudioClip>("audio/tap")` + `G.Audio.PlaySfx` 真实加载播放——比 FakeAssetService 注入更贴近真实链路、且能补上 Phase 2 推迟的 Addressables 真实加载冒烟(音频 clip 与 UI prefab 都走 addressable key)。**理由:** TapRush 的核心验收目标之一就是"打通 Addressables 真实加载",程序化生成真实 asset 并标 addressable 比注入 Fake 更能验证该路径;一次性生成成本低、确定性强。

#### Step 1: TapRushSession 纯逻辑(TDD,先测后实现)

**Files:** `Assets/Game/TapRushSession.cs` + `Assets/Game/Tests/EditMode/TapRushSessionTests.cs` + `Game.Tests.EditMode.asmdef`

- [ ] **写 `Game.Tests.EditMode.asmdef`** — `Assets/Game/Tests/EditMode/Game.Tests.EditMode.asmdef`

```json
{
  "name": "Game.Tests.EditMode",
  "rootNamespace": "Game.Tests",
  "references": ["Game", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "overrideReferences": true,
  "autoReferenced": false
}
```

- [ ] **写失败测试** — `TapRushSessionTests.cs`(5 用例)

```csharp
using Game;
using NUnit.Framework;

namespace Game.Tests
{
    public class TapRushSessionTests
    {
        [Test]
        public void AddScore_AccumulatesWhileRunning()
        {
            var s = new TapRushSession(60f, highScore: 0);
            s.AddScore(1);
            s.AddScore(2);
            Assert.AreEqual(3, s.Score);
            Assert.IsFalse(s.IsOver);
        }

        [Test]
        public void TickDown_ReducesTime_AndEndsAtZero()
        {
            var s = new TapRushSession(2f, 0);
            s.TickDown(1f);
            Assert.AreEqual(1f, s.TimeRemaining, 1e-4);
            Assert.IsFalse(s.IsOver);
            s.TickDown(1.5f);                 // 越界扣到 0
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
            Assert.IsTrue(s.IsOver);
        }

        [Test]
        public void AddScore_AfterOver_IsIgnored()
        {
            var s = new TapRushSession(1f, 0);
            s.TickDown(1f);
            Assert.IsTrue(s.IsOver);
            s.AddScore(5);
            Assert.AreEqual(0, s.Score, "结束后加分无效");
        }

        [Test]
        public void HighScore_TracksMaxOfInitialAndCurrent()
        {
            var s = new TapRushSession(60f, highScore: 10);
            Assert.AreEqual(10, s.HighScore);   // 还没超过历史
            s.AddScore(7);
            Assert.AreEqual(10, s.HighScore);   // 7 < 10
            s.AddScore(5);                      // 12 > 10
            Assert.AreEqual(12, s.HighScore);
        }

        [Test]
        public void TimeRemaining_NeverNegative_AndTickAfterOverNoOp()
        {
            var s = new TapRushSession(0.5f, 0);
            s.TickDown(10f);
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
            Assert.IsTrue(s.IsOver);
            Assert.DoesNotThrow(() => s.TickDown(1f));   // 结束后再 Tick 安全
            Assert.AreEqual(0f, s.TimeRemaining, 1e-4);
        }
    }
}
```

- [ ] **实现 TapRushSession** — `TapRushSession.cs`

```csharp
namespace Game
{
    /// <summary>TapRush 计分/倒计时纯逻辑。无 Unity 依赖,全 EditMode 单测。</summary>
    public sealed class TapRushSession
    {
        readonly int _initialHighScore;

        public int Score { get; private set; }
        public float TimeRemaining { get; private set; }
        public bool IsOver => TimeRemaining <= 0f;
        public int HighScore => Score > _initialHighScore ? Score : _initialHighScore;

        public TapRushSession(float durationSeconds, int highScore)
        {
            TimeRemaining = durationSeconds > 0f ? durationSeconds : 0f;
            _initialHighScore = highScore;
        }

        public void AddScore(int amount)
        {
            if (IsOver) return;
            Score += amount;
        }

        public void TickDown(float deltaSeconds)
        {
            if (IsOver) return;
            TimeRemaining -= deltaSeconds;
            if (TimeRemaining < 0f) TimeRemaining = 0f;
        }
    }
}
```

- [ ] **验证测试通过**(验证代理:`run_tests` 按 `Game.Tests.TapRushSessionTests` 过滤)Expected: 5 PASS。

#### Step 2: TapRushSaveData

- [ ] **实现** — `Assets/Game/TapRushSaveData.cs`

```csharp
using EasyFramework.Services.Saves;

namespace Game
{
    /// <summary>TapRush 存档:仅最高分。经 SaveProfile 替换框架默认 DefaultSaveData。</summary>
    public sealed class TapRushSaveData : SaveData
    {
        public int HighScore;
    }
}
```

> `SaveData` 基类有 `int Version`(Phase 2 锁定)。`G.Save.Data<TapRushSaveData>()` 读回;`G.Save.Save()` 落盘。

#### Step 3: UI 代码构建(programmatic uGUI)

**Files:** `Assets/Game/UI/UiBuild.cs`, `MenuWindow.cs`, `GameHud.cs`, `ResultWindow.cs`, `ConfirmPopup.cs`

> **全部代码构建 UI**(不做 prefab 美术),但**经 `IUIService` 的 prefab 加载路径**:`TapRushAssetSetup.cs`(Step 7,Editor 一次性)把这些代码构建的面板存成 prefab 并标 addressable(key = `"ui/<TypeName>"`),运行时 `G.UI.PushAsync<MenuWindow>()` 经 `IAssetService.LoadAsync<GameObject>("ui/MenuWindow", Global)` 真实加载。面板脚本的 UI 子节点在 `Awake`/`OnSetup` 里用 `UiBuild` 程序化构建(prefab 只需挂面板脚本 + RectTransform,外观运行时建——这样 prefab 化简单且无需美术资源)。

- [ ] **实现 UiBuild 辅助** — `UiBuild.cs`

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>程序化 uGUI 构建辅助:在指定父节点下快速建文本/按钮/全屏背景。仅 TapRush 示例用。</summary>
    public static class UiBuild
    {
        public static RectTransform FullScreen(Transform parent, string name, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            go.GetComponent<Image>().color = bg;
            return rt;
        }

        public static TMP_Text Label(Transform parent, string name, string text,
            Vector2 anchoredPos, float fontSize = 48f, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(900f, 120f);
            rt.anchoredPosition = anchoredPos;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color ?? Color.white;
            return t;
        }

        public static Button Button(Transform parent, string name, string label,
            Vector2 anchoredPos, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(520f, 140f);
            rt.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.9f, 1f);

            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            Label(rt, "Label", label, Vector2.zero, 44f);
            return btn;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
```

- [ ] **实现 MenuWindow** — `MenuWindow.cs`(开始按钮 / 最高分文本)

```csharp
using Cysharp.Threading.Tasks;
using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>主菜单:显示最高分 + 开始按钮。点击开始 → 通过回调驱动 TapRushFlow 进 Gameplay。</summary>
    public sealed class MenuWindow : UIPanel
    {
        TMP_Text _highScoreText;

        /// <summary>由 OnSetup 接收的参数:最高分 + 开始回调。</summary>
        public sealed class Args
        {
            public int HighScore;
            public System.Action OnStart;
        }

        protected internal override void OnSetup(object args)
        {
            var a = (Args)args;
            BuildUi(a);
        }

        void BuildUi(Args a)
        {
            var root = (RectTransform)transform;
            UiBuild.FullScreen(root, "Bg", new Color(0.08f, 0.10f, 0.16f, 1f));
            UiBuild.Label(root, "Title", "TAP RUSH", new Vector2(0f, 420f), 96f);
            _highScoreText = UiBuild.Label(root, "HighScore", $"Best: {a.HighScore}", new Vector2(0f, 240f), 52f);
            UiBuild.Button(root, "StartBtn", "START", new Vector2(0f, -40f), () => a.OnStart?.Invoke());
        }

        protected internal override UniTask PlayEnter() => UniTask.CompletedTask;
    }
}
```

- [ ] **实现 GameHud** — `GameHud.cs`(分数 / 倒计时,事件驱动刷新)

```csharp
using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>战斗 HUD:分数 + 倒计时。由玩法每帧调 SetScore/SetTime 刷新(事件驱动,无 MVVM)。</summary>
    public sealed class GameHud : UIPanel
    {
        TMP_Text _scoreText;
        TMP_Text _timeText;

        protected internal override void OnSetup(object args)
        {
            var root = (RectTransform)transform;
            _scoreText = UiBuild.Label(root, "Score", "0", new Vector2(0f, 760f), 72f);
            _timeText = UiBuild.Label(root, "Time", "60.0", new Vector2(0f, 640f), 56f);
        }

        public void SetScore(int score)
        {
            if (_scoreText != null) _scoreText.text = score.ToString();
        }

        public void SetTime(float seconds)
        {
            if (_timeText != null) _timeText.text = seconds.ToString("00.0");
        }
    }
}
```

- [ ] **实现 ResultWindow** — `ResultWindow.cs`(得分 / 最高分 / 再来一次 / 看广告翻倍)

```csharp
using Cysharp.Threading.Tasks;
using EasyFramework.Services.UI;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>结算界面:本局得分 + 最高分 + "再来一次" + "看广告翻倍"。</summary>
    public sealed class ResultWindow : UIPanel
    {
        TMP_Text _scoreText;
        TMP_Text _bestText;

        public sealed class Args
        {
            public int Score;
            public int HighScore;
            public System.Action OnReplay;
            public System.Action OnDoubleViaAd;   // 看广告翻倍(回调里走 G.Ads + 更新分数)
        }

        Args _args;

        protected internal override void OnSetup(object args)
        {
            _args = (Args)args;
            BuildUi();
        }

        void BuildUi()
        {
            var root = (RectTransform)transform;
            UiBuild.FullScreen(root, "Bg", new Color(0.10f, 0.08f, 0.14f, 1f));
            UiBuild.Label(root, "Title", "RESULT", new Vector2(0f, 480f), 88f);
            _scoreText = UiBuild.Label(root, "Score", $"Score: {_args.Score}", new Vector2(0f, 300f), 60f);
            _bestText = UiBuild.Label(root, "Best", $"Best: {_args.HighScore}", new Vector2(0f, 200f), 52f);
            UiBuild.Button(root, "DoubleBtn", "WATCH AD x2", new Vector2(0f, 20f), () => _args.OnDoubleViaAd?.Invoke());
            UiBuild.Button(root, "ReplayBtn", "REPLAY", new Vector2(0f, -160f), () => _args.OnReplay?.Invoke());
        }

        /// <summary>广告翻倍成功后由玩法回调刷新显示。</summary>
        public void RefreshScore(int newScore, int newHigh)
        {
            if (_scoreText != null) _scoreText.text = $"Score: {newScore}";
            if (_bestText != null) _bestText.text = $"Best: {newHigh}";
        }

        protected internal override UniTask PlayEnter() => UniTask.CompletedTask;
    }
}
```

- [ ] **实现 ConfirmPopup** — `ConfirmPopup.cs`(退出确认,`UIPopup<bool>`)

```csharp
using EasyFramework.Services.UI;
using UnityEngine;

namespace Game.UI
{
    /// <summary>退出确认弹窗。await ShowPopupAsync&lt;ConfirmPopup, bool&gt; 得到用户选择。</summary>
    public sealed class ConfirmPopup : UIPopup<bool>
    {
        protected internal override void OnSetup(object args)
        {
            var message = args as string ?? "Quit to menu?";
            var root = (RectTransform)transform;

            // 半透明遮罩 + 对话框
            UiBuild.FullScreen(root, "Dim", new Color(0f, 0f, 0f, 0.6f));
            UiBuild.Label(root, "Msg", message, new Vector2(0f, 120f), 52f);
            UiBuild.Button(root, "YesBtn", "YES", new Vector2(-160f, -80f), () => SetResult(true));
            UiBuild.Button(root, "NoBtn", "NO", new Vector2(160f, -80f), () => SetResult(false));
        }

        // 返回键:等价于"取消"(覆盖基类默认的"拦截不动"),返回即取消退出。
        protected internal override bool OnBackRequested()
        {
            SetResult(false);
            return true;
        }
    }
}
```

> `UIPanel`/`UIPopup<TResult>` 的 `protected internal` 钩子(`OnSetup`/`PlayEnter`/`PlayExit`/`OnBackRequested`)与 `SetResult`/`Result` 以 Phase 3a 磁盘文件为准(Phase 3a 锁定契约)。**实现代理先 Read Phase 3a 落地的 `UIPanel.cs`/`UIPopup.cs` 核对**——尤其 `protected internal override` 在跨程序集(Game 重写 Services 的 `protected internal`)是否可行:`protected internal` 成员对派生类(跨程序集 `protected` 部分)可重写,Game 面板继承 `UIPanel` 可 `override`。若 Phase 3a 实际把钩子设为纯 `internal`(不可跨程序集重写),则面板改用 Phase 3a 提供的 `public`/`protected` 重写点——**以磁盘为准,有出入按实际重写点调整(契约不改,只调用方式)。**

#### Step 4: TapRushCircle(圆圈对象:Pool + 点击得分 + 音效)

- [ ] **实现** — `Assets/Game/TapRushCircle.cs`

```csharp
using EasyFramework.Core.Pooling;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 可点击圆圈。经 G.Pool 生成回收(IPoolable 重置);被点击时回调玩法加分 + 播音效。
    /// 点击判定:玩法订阅 TapEvent 做屏幕坐标命中检测后调 Hit();或本组件挂 Collider 由射线命中(选前者,见 TapRushFlow)。
    /// </summary>
    public sealed class TapRushCircle : MonoBehaviour, IPoolable
    {
        public System.Action<TapRushCircle> OnHit;   // 玩法注入:命中时加分 + 回收
        public float Radius { get; private set; } = 0.6f;

        public void OnSpawn()
        {
            // 重置可见性/缩放(池复用)。
            transform.localScale = Vector3.one;
        }

        public void OnDespawn()
        {
            OnHit = null;
        }

        /// <summary>由玩法命中检测调用。</summary>
        public void Hit() => OnHit?.Invoke(this);
    }
}
```

> 注:`IPoolable`(`OnSpawn`/`OnDespawn`)在 `EasyFramework.Core.Pooling`(Phase 1 锁定)。`G.Pool.SpawnAsync(key, pos)` 返回 `GameObject`(Phase 2),玩法 `GetComponent<TapRushCircle>()` 后注入 `OnHit`。圆圈 prefab 由 `TapRushAssetSetup`(Step 7)程序化生成(一个带 `SpriteRenderer` 圆 sprite + `TapRushCircle` 的 prefab,标 addressable key `"circle"`)。

#### Step 5: TapRushFlow(StateMachine 三态 + 玩法驱动)

- [ ] **实现** — `Assets/Game/TapRushFlow.cs`

用 `StateMachine<TapRushFlow>` 三态;构造注入门面所需服务经 `G`(Game 层允许用 `G`)。Gameplay 态:`G.UI.ShowHudAsync<GameHud>` + 生成圆圈(`G.Pool`)+ 订阅 `TapEvent` 命中 + 倒计时(`TapRushSession`)+ 音效(`G.Audio.PlaySfx`)。Result 态:`G.UI.PushAsync<ResultWindow>` + 存档(`G.Save`)+ 打点(`G.Analytics`)。

```csharp
using System;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Fsm;
using EasyFramework.Monetization.Ads;
using EasyFramework.Services.Inputs;
using Game.UI;
using UnityEngine;

namespace Game
{
    /// <summary>TapRush 流程:Menu → Gameplay → Result。用 G 门面打通框架各能力。</summary>
    public sealed class TapRushFlow
    {
        const float Duration = 60f;
        const string CircleKey = "circle";
        const string TapSfxKey = "audio/tap";

        readonly StateMachine<TapRushFlow> _fsm;

        TapRushSession _session;
        GameHud _hud;
        IDisposable _tapSub;
        readonly System.Collections.Generic.List<GameObject> _liveCircles = new();
        float _spawnTimer;

        public State<TapRushFlow> Current => _fsm.Current;

        public TapRushFlow()
        {
            _fsm = new StateMachine<TapRushFlow>(this);
            _fsm.AddState(new MenuState());
            _fsm.AddState(new GameplayState());
            _fsm.AddState(new ResultState());
        }

        public UniTask ToMenu() => _fsm.ChangeState<MenuState>();
        public UniTask ToGameplay() => _fsm.ChangeState<GameplayState>();
        public UniTask ToResult() => _fsm.ChangeState<ResultState>();
        public void Tick(float dt) => _fsm.Update(dt);

        int LoadHighScore() => G.Save.Data<TapRushSaveData>().HighScore;

        // ---------------- Menu ----------------
        public sealed class MenuState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                await G.UI.PushAsync<MenuWindow>(new MenuWindow.Args
                {
                    HighScore = f.LoadHighScore(),
                    OnStart = () => f.ToGameplay().Forget(),
                });
            }

            public override void Exit() => G.UI.PopAllAsync().Forget();
        }

        // ---------------- Gameplay ----------------
        public sealed class GameplayState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                f._session = new TapRushSession(Duration, f.LoadHighScore());
                f._spawnTimer = 0f;
                f._hud = await G.UI.ShowHudAsync<GameHud>();
                f._hud.SetScore(0);
                f._hud.SetTime(Duration);

                G.Analytics.Track("level_start", ("game", "tap_rush"));

                // 订阅点击:屏幕坐标命中检测最近圆圈。
                f._tapSub = G.Events.Subscribe<TapEvent>(f.OnTap);
            }

            public override void Update(float dt)
            {
                var f = Context;
                if (f._session == null || f._session.IsOver) return;

                f._session.TickDown(dt);
                f._hud.SetTime(f._session.TimeRemaining);

                // 周期性生成圆圈
                f._spawnTimer -= dt;
                if (f._spawnTimer <= 0f)
                {
                    f._spawnTimer = 0.7f;
                    f.SpawnCircle().Forget();
                }

                if (f._session.IsOver)
                    f.ToResult().Forget();
            }

            public override void Exit()
            {
                var f = Context;
                f._tapSub?.Dispose();
                f._tapSub = null;
                f.DespawnAllCircles();
                G.UI.HideHudAsync().Forget();
            }
        }

        // ---------------- Result ----------------
        public sealed class ResultState : State<TapRushFlow>
        {
            public override async UniTask Enter()
            {
                var f = Context;
                var score = f._session.Score;
                var high = f._session.HighScore;

                // 存档:写回最高分。
                G.Save.Data<TapRushSaveData>().HighScore = high;
                G.Save.Save();

                G.Analytics.Track("level_end", ("game", "tap_rush"), ("score", score), ("high_score", high));

                ResultWindow win = null;
                win = await G.UI.PushAsync<ResultWindow>(new ResultWindow.Args
                {
                    Score = score,
                    HighScore = high,
                    OnReplay = () => f.ToGameplay().Forget(),
                    OnDoubleViaAd = () => f.DoubleViaAd(win).Forget(),
                });
            }
        }

        // ---------------- 玩法辅助 ----------------

        async UniTask SpawnCircle()
        {
            var pos = new Vector3(UnityEngine.Random.Range(-2.2f, 2.2f), UnityEngine.Random.Range(-3.5f, 3.5f), 0f);
            var go = await G.Pool.SpawnAsync(CircleKey, pos);
            var circle = go.GetComponent<TapRushCircle>();
            if (circle != null) circle.OnHit = OnCircleHit;
            _liveCircles.Add(go);
        }

        void OnCircleHit(TapRushCircle circle)
        {
            _session.AddScore(1);
            _hud.SetScore(_session.Score);
            G.Audio.PlaySfx(TapSfxKey);
            _liveCircles.Remove(circle.gameObject);
            G.Pool.Despawn(circle.gameObject);
        }

        void OnTap(TapEvent e)
        {
            // 屏幕坐标 → 世界坐标命中最近圆圈(主相机正交)。
            var cam = Camera.main;
            if (cam == null) return;
            var world = cam.ScreenToWorldPoint(new Vector3(e.ScreenPosition.x, e.ScreenPosition.y, -cam.transform.position.z));
            TapRushCircle hit = null;
            var best = float.MaxValue;
            foreach (var go in _liveCircles)
            {
                if (go == null) continue;
                var c = go.GetComponent<TapRushCircle>();
                var d = Vector2.Distance(world, go.transform.position);
                if (d <= c.Radius && d < best) { best = d; hit = c; }
            }
            hit?.Hit();
        }

        void DespawnAllCircles()
        {
            foreach (var go in _liveCircles)
                if (go != null) G.Pool.Despawn(go);
            _liveCircles.Clear();
        }

        async UniTask DoubleViaAd(ResultWindow win)
        {
            var result = await G.Ads.ShowRewardedAsync("double_score");
            if (result != AdResult.Completed) return;

            var doubled = _session.Score * 2;
            // 用翻倍后分数刷新存档与显示(以翻倍分参与最高分)。
            var newHigh = doubled > G.Save.Data<TapRushSaveData>().HighScore
                ? doubled : G.Save.Data<TapRushSaveData>().HighScore;
            G.Save.Data<TapRushSaveData>().HighScore = newHigh;
            G.Save.Save();
            G.Analytics.Track("ad_double_score", ("score", doubled));
            win?.RefreshScore(doubled, newHigh);
        }
    }
}
```

> 实现说明:
> - **Game 层用 `G` 门面合规**(spec §2.2:门面只服务 Game 层)。`G.UI`/`G.Pool`/`G.Audio`/`G.Save`/`G.Analytics`/`G.Ads`/`G.Events` 的方法名以磁盘实际为准(Phase 2/3/4 锁定)——**实现代理先 Read 各服务接口核对**(`ShowHudAsync`/`HideHudAsync`/`PushAsync`/`PopAllAsync`、`SpawnAsync`/`Despawn`、`PlaySfx`、`Data<T>`/`Save`、`Track(name, params)`、`ShowRewardedAsync`、`Subscribe<TapEvent>`)。
> - **TapEvent 经事件总线广播:** Phase 3b 的 `InputService` 把手势经 `IGestureEmitter` 转发到 `IEventBus`,故 `G.Events.Subscribe<TapEvent>` 可收到点击(`TapEvent.ScreenPosition`)。**实现代理核对 Phase 3b 实际是否如此接线**(`IGestureEmitter` 生产实现是否 publish 到 bus);若实际 `TapEvent` 不经 bus 而走别的分发,按实际订阅方式调整。**回退:** 若 Input 服务的事件接线不可用,玩法改用 `UnityEngine.InputSystem` 直接读 `Touchscreen`/`Mouse` 的按下并自行 `ScreenToWorldPoint`(Game 层允许直接用 Input System)——回退动作明确,非占位符。
> - **倒计时驱动:** `TapRushFlow.Tick(dt)` 需每帧被调。由 `TapRushFlowBootTask` 注册的入口点(`ITickable`)或一个挂在场景的 `MonoBehaviour` 驱动(见 Step 6)。

#### Step 6: TapRushFlowBootTask + GameLifetimeScope

- [ ] **实现 TapRushFlowBootTask** — `Assets/Game/TapRushFlowBootTask.cs`

`BootCompletedEvent` 后进 Menu;并把 `TapRushFlow.Tick` 接入帧循环(实现 `ITickable`)。

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using UnityEngine;
using VContainer.Unity;

namespace Game
{
    /// <summary>
    /// 监听 BootCompletedEvent 进入 Menu,并每帧驱动 TapRushFlow.Tick。
    /// 经 GameLifetimeScope 注册:既作为 IBootTask(订阅 BootCompleted)又作为 ITickable(帧驱动)。
    /// </summary>
    public sealed class TapRushFlowBootTask : IBootTask, ITickable, IDisposable
    {
        readonly IEventBus _events;
        readonly TapRushFlow _flow;
        IDisposable _sub;
        bool _started;

        public int Priority => 50;        // 晚于框架服务(Save=0/Config=10/Ads=30),早于 DevTools(90)
        public bool IsCritical => false;

        public TapRushFlowBootTask(IEventBus events, TapRushFlow flow)
        {
            _events = events;
            _flow = flow;
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            // BootCompleted 后进 Menu(此刻 G 已 Initialize、存档已加载)。
            _sub = _events.Subscribe<BootCompletedEvent>(_ => _flow.ToMenu().Forget());
            return UniTask.CompletedTask;
        }

        public void Tick()
        {
            _flow.Tick(Time.deltaTime);
        }

        public void Dispose() => _sub?.Dispose();
    }
}
```

> 注:`GameBootstrap` 在所有 BootTask 跑完后 publish `BootCompletedEvent`(Phase 1)。`TapRushFlowBootTask.InitializeAsync` 先订阅,故能收到后续发布的事件进 Menu。`ITickable.Tick` 由 VContainer 入口点循环每帧调(需以 entrypoint 注册,见下)。**`BootCompletedEvent` 命名空间以 Phase 1 磁盘为准(`EasyFramework.Core.Boot`)。**

- [ ] **实现 TapRushGameLifetimeScope** — `Assets/Game/TapRushGameLifetimeScope.cs`

注册:`TapRushFlow`、`TapRushFlowBootTask`(As IBootTask + 入口点 ITickable)、`DevToolsBootTask`、游戏 SaveProfile(经子作用域 + 提示生产走 FrameworkOptions)、ProductCatalog、RewardHandler(build callback 里 `IAPService.SetRewardHandler`)。

```csharp
using System.Collections.Generic;
using EasyFramework.Core.Boot;
using EasyFramework.DevTools;
using EasyFramework.Monetization.IAP;
using EasyFramework.Services.Saves;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game
{
    /// <summary>TapRush 游戏入口作用域。挂为 Boot 场景 RootLifetimeScope 的子作用域。</summary>
    public sealed class TapRushGameLifetimeScope : EasyFramework.GameLifetimeScope
    {
        protected override void ConfigureGame(IContainerBuilder builder)
        {
            // ---- 玩法流程 ----
            builder.Register<TapRushFlow>(Lifetime.Singleton);

            // TapRushFlowBootTask 既是 IBootTask 又是 ITickable:两种角色都注册。
            builder.Register<TapRushFlowBootTask>(Lifetime.Singleton)
                .As<IBootTask>().AsSelf();
            builder.RegisterEntryPoint<TapRushFlowBootTask>();   // ITickable 帧驱动

            // ---- DevTools(控制台 + 角标 + 作弊)----
            builder.Register<DevToolsBootTask>(Lifetime.Singleton).As<IBootTask>();

            // ---- 存档 profile(供游戏侧引用;生产存档由 RootLifetimeScope 的 FrameworkOptions.SaveProfile 决定)----
            builder.RegisterInstance(CreateSaveProfile());

            // ---- 商品目录 + 发奖(build 后注册 RewardHandler)----
            builder.RegisterBuildCallback(resolver =>
            {
                var iap = resolver.Resolve<IAPService>();
                iap.SetRewardHandler(productId =>
                {
                    // TapRush 无真实内购商品;此处示例发奖逻辑(发奖即加最高分等)。
                    Debug.Log($"[TapRush] Granting reward for product '{productId}'.");
                });
            });
        }

        static SaveProfile CreateSaveProfile()
            => new SaveProfile
            {
                DataType = typeof(TapRushSaveData),
                CurrentVersion = 1,
                CreateNew = () => new TapRushSaveData { Version = 1, HighScore = 0 },
                Migrations = new List<ISaveMigration>(),
                FileName = "save.json",
                HmacSalt = "tap-rush-salt",
            };
    }
}
```

> 关键执行注意:**生产存档载荷必须是 `TapRushSaveData`,否则 `G.Save.Data<TapRushSaveData>()` 会强转失败。** Phase 2 的 `FrameworkInstaller` 用 `FrameworkOptions.SaveProfile` 决定生产 profile(为 null 用 `DefaultSaveData`)。因此 **TapRush 的 Boot 场景设置(Task 5 Step 8 由验证代理执行)必须让 `RootLifetimeScope` 的 `FrameworkOptions.SaveProfile` = TapRush 的 profile**——途径:在 `RootLifetimeScope` Inspector 暴露的 SaveProfile 字段(若 Phase 2 这样实现)或一个把 profile 注入 options 的钩子。**实现代理先 Read 磁盘 `RootLifetimeScope`/`FrameworkInstaller` 核对实际机制**:
> - 若 `FrameworkOptions.SaveProfile` 可由 `RootLifetimeScope` 的子作用域 `RegisterInstance<SaveProfile>` 覆盖(VContainer 子作用域覆盖父注册)→ 子作用域注册 TapRushSaveData profile 即生效。
> - 若不能(profile 在父作用域 `Install` 时就 new 了 `JsonSaveService(profile,...)`)→ 需在 Boot 场景给 `RootLifetimeScope` 的 Inspector SaveProfile 字段挂 TapRush profile(`ScriptableObject` 或序列化引用),或 TapRush 提供一个自定义 `RootLifetimeScope` 子类传 options。**这是接线细节,以磁盘实际为准选可行路径**;计划在 Step 8 的 Boot 场景设置里由验证代理 `execute_code` 落实(下方标注)。**契约不改,只确保生产 profile = TapRushSaveData。**

#### Step 7: TapRushCheats(作弊示例)

- [ ] **实现** — `Assets/Game/TapRushCheats.cs`

```csharp
using EasyFramework.DevTools.Cheats;
using UnityEngine;

namespace Game
{
    /// <summary>TapRush 作弊命令示例(DEV/编辑器下经调试控制台可用)。</summary>
    public static class TapRushCheats
    {
        [Cheat("tap_reset_highscore", "Reset TapRush high score to 0")]
        public static void ResetHighScore()
        {
            if (!G.IsInitialized) { Debug.LogWarning("[TapRush] Framework not initialized."); return; }
            G.Save.Data<TapRushSaveData>().HighScore = 0;
            G.Save.Save();
            Debug.Log("[TapRush] High score reset.");
        }

        [Cheat("tap_set_highscore", "Set TapRush high score to value")]
        public static void SetHighScore(int value)
        {
            if (!G.IsInitialized) { Debug.LogWarning("[TapRush] Framework not initialized."); return; }
            G.Save.Data<TapRushSaveData>().HighScore = value;
            G.Save.Save();
            Debug.Log($"[TapRush] High score set to {value}.");
        }
    }
}
```

> 验证 Cheat 系统:无参 `ResetHighScore` + 单 `int` 参 `SetHighScore` 都受支持,经 `DevToolsBootTask` 扫描 `Game` 程序集注册到控制台。`G.IsInitialized` 守卫(Phase 1)。

#### Step 8: TapRushAssetSetup(Editor 一次性资产生成,由验证代理执行)

- [ ] **写 Game.Editor asmdef** — `Assets/Game/Editor/Game.Editor.asmdef`

```json
{
  "name": "Game.Editor",
  "rootNamespace": "Game.Editor",
  "references": [
    "Game",
    "EasyFramework.Services",
    "Unity.Addressables",
    "Unity.Addressables.Editor",
    "Unity.ResourceManager",
    "Unity.TextMeshPro"
  ],
  "includePlatforms": ["Editor"]
}
```

> `includePlatforms: ["Editor"]` 使本程序集 Editor-only。`Unity.Addressables.Editor` 提供 `AddressableAssetSettings`(标记 addressable)。`Game` 引用供构建面板实例(`MenuWindow` 等类型);`Unity.TextMeshPro` 供构建时 UI。程序集名以 `unity_reflect` 核对(Addressables Editor 程序集常见名 `Unity.Addressables.Editor` 或 `Unity.Addressables.Editor.dll`,以实际为准)。

- [ ] **实现 TapRushAssetSetup** — `Assets/Game/Editor/TapRushAssetSetup.cs`

一个菜单项 `EasyFramework/TapRush/Setup Assets`:程序化构建四个面板 prefab(挂面板脚本 + RectTransform)、圆圈 prefab(SpriteRenderer 圆 + `TapRushCircle`)、tap 音效 clip(0.1s 正弦波)、圆 sprite,存盘并标 addressable(key 规则 `"ui/<TypeName>"`、`"circle"`、`"audio/tap"`)。由验证代理通过 `execute_menu_item("EasyFramework/TapRush/Setup Assets")` 或 `execute_code` 执行一次。

```csharp
using System.IO;
using EasyFramework.Services.UI;
using Game.UI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 一次性生成 TapRush 运行期资产并标记 Addressable:
    ///   - 四个 UI 面板 prefab(挂面板脚本,外观运行时由面板 OnSetup 代码构建)→ key "ui/<TypeName>"
    ///   - 圆圈 prefab(Sprite 圆 + TapRushCircle)→ key "circle"
    ///   - tap 音效 AudioClip(0.1s 正弦波)→ key "audio/tap"
    /// 由验证代理执行一次:菜单 EasyFramework/TapRush/Setup Assets。
    /// 同时补上 Phase 2 推迟的 Addressables 真实加载冒烟(运行时 G.Asset/G.UI/G.Audio 经 key 真实加载)。
    /// </summary>
    public static class TapRushAssetSetup
    {
        const string Dir = "Assets/Game/GeneratedAssets";

        [MenuItem("EasyFramework/TapRush/Setup Assets")]
        public static void SetupAssets()
        {
            Directory.CreateDirectory(Dir);

            var settings = EnsureAddressableSettings();

            // ---- UI 面板 prefab(仅脚本 + RectTransform;外观运行时建)----
            CreatePanelPrefab<MenuWindow>(settings);
            CreatePanelPrefab<GameHud>(settings);
            CreatePanelPrefab<ResultWindow>(settings);
            CreatePanelPrefab<ConfirmPopup>(settings);

            // ---- 圆 sprite + 圆圈 prefab ----
            var circleSprite = CreateCircleSprite();
            CreateCirclePrefab(settings, circleSprite);

            // ---- tap 音效 clip ----
            CreateTapClip(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TapRush] Asset setup complete (prefabs/sprite/clip generated and marked addressable).");
        }

        static AddressableAssetSettings EnsureAddressableSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            }
            return settings;
        }

        static void MarkAddressable(AddressableAssetSettings settings, string assetPath, string key)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = key;
        }

        static void CreatePanelPrefab<T>(AddressableAssetSettings settings) where T : UIPanel
        {
            var typeName = typeof(T).Name;
            var go = new GameObject(typeName, typeof(RectTransform));
            go.AddComponent<T>();

            var path = $"{Dir}/{typeName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            MarkAddressable(settings, path, "ui/" + typeName);
        }

        static Sprite CreateCircleSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            var radius = size / 2f - 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), center);
                tex.SetPixel(x, y, d <= radius ? new Color(0.95f, 0.4f, 0.3f, 1f) : Color.clear);
            }
            tex.Apply();

            var texPath = $"{Dir}/CircleTex.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            // 设为 Sprite 导入
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        }

        static void CreateCirclePrefab(AddressableAssetSettings settings, Sprite sprite)
        {
            var go = new GameObject("Circle");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            go.AddComponent<TapRushCircle>();

            var path = $"{Dir}/Circle.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            MarkAddressable(settings, path, "circle");
        }

        static void CreateTapClip(AddressableAssetSettings settings)
        {
            const int sampleRate = 44100;
            const float duration = 0.1f;
            const float freq = 880f;
            var count = (int)(sampleRate * duration);
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                // 正弦波 + 线性衰减包络,避免爆音。
                var env = 1f - (float)i / count;
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * i / sampleRate) * 0.5f * env;
            }

            var clip = AudioClip.Create("TapSfx", count, 1, sampleRate, false);
            clip.SetData(samples, 0);

            var path = $"{Dir}/TapSfx.asset";
            AssetDatabase.CreateAsset(clip, path);

            MarkAddressable(settings, path, "audio/tap");
        }
    }
}
```

> 实现说明:
> - **面板 prefab 只挂脚本 + RectTransform,外观在运行时面板 `OnSetup` 里用 `UiBuild` 构建** —— 这样 prefab 化无需美术资源,`PrefabUtility.SaveAsPrefabAsset` 简单可靠,且经 `IUIService.LoadAsync<GameObject>("ui/<Type>")` 真实加载验证了 UI 加载链路。
> - **`AddressableAssetSettings`/`CreateOrMoveEntry`/`entry.address` API 名以安装版本为准** —— 验证代理用 `unity_reflect("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings")` 核对(`CreateOrMoveEntry(guid, group, readOnly?, postEvent?)` 重载、`DefaultGroup`)。若签名不符按实际等效调整(这是 Editor 一次性脚本,逻辑确定)。
> - **`AudioClip.Create` 程序化生成存为 `.asset`** —— `AudioClip` 可 `AssetDatabase.CreateAsset` 持久化(Unity 支持序列化 procedural clip)。若某版本对 `.asset` 形式的 AudioClip 加载有限制,回退:导出为 `.wav`(写 WAV 头 + PCM)并 import——回退动作明确,非占位符。
> - **生成目录 `Assets/Game/GeneratedAssets/`** 由脚本创建;`.meta` 由 Unity 生成。

#### Step 9: PlayMode 验收(验证代理执行)+ Boot 场景设置

- [ ] **Boot 场景挂 TapRush 子作用域**(验证代理,UnityMCP):打开 `Assets/Scenes/Boot.unity`,在 `[EasyFramework]`(`RootLifetimeScope`)下新建子 GameObject `[TapRush]`,挂 `TapRushGameLifetimeScope`。**并确保生产 SaveProfile = TapRushSaveData**(按 Step 6 核对的实际机制:子作用域 RegisterInstance 覆盖,或给 RootLifetimeScope 的 SaveProfile 字段赋 TapRush profile;由验证代理 `execute_code` 落实)。保存场景。需要主相机(正交)与 EventSystem——UIService 懒建 EventSystem,主相机由 Boot 场景提供(若无则验证代理补一个正交 `Camera.main`)。

- [ ] **执行一次性资产生成**(验证代理):`execute_menu_item("EasyFramework/TapRush/Setup Assets")`(或 `execute_code` 调 `TapRushAssetSetup.SetupAssets()`)。核对 `Assets/Game/GeneratedAssets/` 生成 4 个 UI prefab + Circle prefab + CircleTex + TapSfx,且都在 Addressables 默认组、address 为对应 key。

- [ ] **PlayMode 验收流程**(验证代理):
  1. `manage_editor(action="play")` 进 Play。
  2. 等 Boot 完成(轮询:`execute_code` 查 `EasyFramework.G.IsInitialized == true` 或控制台出现 BootCompleted 后的 Menu)。
  3. `execute_code` 驱动 GameFlow:resolve `TapRushFlow` 调 `ToGameplay().Forget()`(或模拟点 START 按钮)。
  4. 模拟得分:`execute_code` 直接对 session 加分,或发布若干 `TapEvent` 命中圆圈;断言 HUD 分数更新。
  5. 驱动到 Result(`ToResult()` 或等倒计时——验收可 `execute_code` 把 `TapRushSession.TickDown` 推到结束)。
  6. `read_console(types=["error"])`:**控制台无 error**。
  7. 停 Play(`manage_editor(action="stop")`)。
  8. **HighScore 读回验收**:再次 Play → 等 Boot → `execute_code` 查 `G.Save.Data<TapRushSaveData>().HighScore` == 上一局写入值。无 error。停 Play。

- [ ] **Commit**(编排者)`git commit -m "feat(game): add TapRush sample game exercising full framework + addressables/sfx setup"`

---

### Task 6: 框架收尾(可并行;依赖 Task 1-5 落地)

**Files:**
- Create: `Assets/EasyFramework/README.md`(框架主使用文档)
- Modify: `docs/superpowers/specs/2026-06-12-easyframework-design.md`(末尾追加「实现偏差摘要」一节)

> `BRANCHING.md` 不需更新(任务书声明)。

- [ ] **Step 1: 写框架主使用文档** — `Assets/EasyFramework/README.md`

给人读的主文档:架构图、各服务 `G.Xxx` 速查表、新游戏十分钟上手指南、SDK 接入槽说明。**速查表签名以磁盘实际为准**(实现代理 Read 各接口核对后落地)。

```markdown
# EasyFramework 使用文档

Unity 2D 小游戏复用底座。DI 内核(VContainer)+ 静态门面(`G`)。从空项目到能跑的新游戏 < 10 分钟。

- Unity 6000.3.15f1(URP + 新 Input System)
- 设计文档:`docs/superpowers/specs/2026-06-12-easyframework-design.md`

## 一、架构总览

```
Game 层(你的小游戏,经 G 门面 / 构造注入)
  ↓
EasyFramework.Services(服务层,全接口化:Asset/Scene/Save/Config/UI/Audio/Input/Camera/...)
  ↓                         EasyFramework.Monetization(Ads/IAP/Analytics)
EasyFramework.Core(纯 C# 核心:EventBus/StateMachine/ObjectPool/Timer/Boot)
  ↓
第三方库(VContainer/UniTask/MessagePipe/Addressables/...)

EasyFramework.Boot   = 组合根(RootLifetimeScope / G 门面 / FrameworkInstaller)
EasyFramework.DevTools = [Cheat] 作弊 + FPS 角标 + 调试控制台
```

三条铁律:只能向下依赖;服务先接口后实现(SDK 可整体替换);框架内部禁用 `G` 门面(模块间构造注入),`G` 只服务 Game 层。

启动管线(`RootLifetimeScope`,DontDestroyOnLoad):
`FrameworkInstaller.Install` 注册服务 → `GameBootstrap` 按 `IBootTask.Priority` 异步初始化(Save→Config→表现层→商业化 SDK,商业化失败不阻塞)→ `G.Initialize` 绑定门面 → 发布 `BootCompletedEvent`。

## 二、G 门面速查表

> 业务代码(Game 层)直接用 `G.Xxx`。框架内部禁用,走构造注入。

| 门面 | 接口 | 常用调用 |
|---|---|---|
| `G.Events` | `IEventBus` | `Publish(evt)` / `Subscribe<T>(h)`(返回 IDisposable) |
| `G.Timer` | `ITimerService` | `Schedule(delay, cb, repeat?, unscaled?)` / `Cancel(handle)` |
| `G.Asset` | `IAssetService` | `await LoadAsync<T>(key, scope)` / `ReleaseScope(scope)` |
| `G.Scene` | `ISceneService` | `await LoadAsync(name, progress?)` / `CurrentScene` |
| `G.Save` | `ISaveService` | `await LoadAsync()` / `Save()` / `Data<T>()` |
| `G.Config` | `IConfigService` | `Get<T>(key, default)` / `Has(key)` |
| `G.Pool` | `IPoolService` | `await SpawnAsync(key, pos)` / `Despawn(go)` / `await PrewarmAsync(key, n)` |
| `G.UI` | `IUIService` | `await PushAsync<W>()` / `PopAsync()` / `await ShowPopupAsync<P,R>()` / `await ShowHudAsync<H>()` |
| `G.Audio` | `IAudioService` | `await PlayBgmAsync(key)` / `PlaySfx(key, vol?)` |
| `G.Ads` | `IAdsService` | `await ShowRewardedAsync(placement)` / `ShowInterstitialAsync` / `ShowBanner` |
| `G.IAP` | `IIAPService` | `await PurchaseAsync(id)` / `RestoreAsync()` / `IsOwned(id)` |
| `G.Analytics` | `IAnalyticsService` | `Track(name)` / `Track(name, (k,v)...)` / `SetUserProperty(k,v)` |

> 输入手势(Tap/LongPress/Swipe/Drag/Pinch)经 `G.Events` 广播,订阅对应事件结构体(如 `G.Events.Subscribe<TapEvent>(...)`)。`G.Input`(`IInputService`)提供 `MoveAxis`/`IsPointerOverUI`。

## 三、新游戏十分钟上手

1. 复制 `Assets/EasyFramework/_Template/` 到 `Assets/<YourGame>/`(详见 `_Template/README.md`)。
2. 改命名空间/类名(改名清单见模板 README)。
3. 在 `Assets/Scenes/Boot.unity` 的 `[EasyFramework]`(RootLifetimeScope)下挂你的 `<YourGame>LifetimeScope` 子作用域。
4. 用 `StateMachine` 写流程(Menu/Gameplay/Result),用 `G.UI` 建界面,用 `G.Pool` 管玩法对象,用 `G.Save` 存档。
5. Play。`BootCompletedEvent` 后进 Menu。

参考完整示例:`Assets/Game/`(TapRush —— 点圈得分 60 秒小游戏,逐一打通了上表所有能力)。

## 四、面板约定(UI)

面板 = 继承 `UIPanel`(或带返回值的 `UIPopup<TResult>`)的脚本 + 同名 prefab,Addressables 按 `"ui/<TypeName>"` 加载,无注册表。生命周期由 `IUIService` 驱动:`OnSetup(args)` → `PlayEnter()` → 显示 →`PlayExit()` → 销毁。Window=栈,Popup=队列(带返回值),HUD=单实例。

## 五、SDK 接入槽

框架商业化层为「接口 + 适配器」,编辑器/测试一律 Fake,真机接真实 SDK 不改业务代码:

| 槽 | 默认(编辑器/测试) | 接入真实 SDK |
|---|---|---|
| 广告 `IAdsProvider` | `FakeAdsProvider`(秒回成功) | 定义 `EF_ADMOB`,填充 `AdMobAdsProvider`,`GameLifetimeScope` 覆盖注册 `IAdsProvider` |
| 内购 `IIAPProvider` | `FakeIAPProvider`(编辑器)/ `UnityIAPProvider`(真机) | Unity IAP 官方包已接,配置 catalog/密钥即可 |
| 统计 `IAnalyticsBackend` | `DebugAnalyticsBackend`(控制台) | 定义 `EF_FIREBASE`,填充 `FirebaseAnalyticsBackend`,`GameLifetimeScope` 追加注册一个 `IAnalyticsBackend`(多后端广播) |
| 远程配置 `IRemoteConfigProvider` | `NoopRemoteConfigProvider` | 实现 `IRemoteConfigProvider` 覆盖注册 |
| 存档后端 `ISaveBackend`(云存档,v1 未做) | 本地文件 | 预留接口 |

## 六、DevTools

`GameLifetimeScope` 注册 `DevToolsBootTask` 后,DEV/编辑器构建下:
- **调试控制台**(IngameDebugConsole,三指下滑呼出)。
- **FPS/内存角标**(`PerfOverlay`,左上角)。
- **作弊命令**:在任意静态方法上标 `[Cheat("命令名", "说明")]`(参数支持无参/int/float/string/bool),自动注册到控制台。

发布版(非 DEVELOPMENT_BUILD)自动零开销(逻辑被 `#if` 编出)。

## 七、测试

- Core / 服务业务逻辑:EditMode 单测(状态机、对象池、存档迁移、事件、UI 栈/队列、广告频控、IAP 掉单、计分逻辑...)。
- 表现层 / SDK 真机路径:Fake 覆盖业务流转 + 真机冒烟(TapRush 即整框架的 PlayMode 验收)。

运行:Unity Test Runner → EditMode,或 `run_tests(mode="EditMode")`。
```

- [ ] **Step 2: spec 文档追加「实现偏差摘要」**

在 `docs/superpowers/specs/2026-06-12-easyframework-design.md` **末尾**(§10 之后)追加一节,汇总各 Phase 计划 Deviations 小节的累计偏差。**实现代理执行时,先逐个 Read 各 Phase 计划文档的 "Deviations" / "实现偏差摘要" 小节(由各 Phase 验证代理填写的实际偏差),把真实记录的偏差汇总进来**;下方为骨架(占位项以各 Phase 实际偏差替换,无偏差则写"无")。

```markdown
## 11. 实现偏差摘要(各 Phase 累计)

本节汇总实现阶段相对本设计规格的偏差,逐条注明 Phase、偏差内容、理由。详细记录见各 Phase 计划文档的 "Deviations" 小节。

| Phase | 偏差 | 理由 / 影响 |
|---|---|---|
| Phase 1 | (从 phase1 计划 Deviations 小节汇总,如:Tests asmdef 补 MessagePipe.VContainer 引用) | 仅构建依赖修正,不改契约 |
| Phase 2 | (从 phase2 计划 Deviations 汇总,如 UniTask Addressables 扩展 API / VContainer 工厂注册写法) | 等效调整,接口不变 |
| Phase 3a/3b | (从 phase3 计划 Deviations 汇总,如 Cinemachine 3.x / PrimeTween / InputSystem API 名、版本号) | 第三方实际 API/版本对齐 |
| Phase 4 | (从 phase4 计划 Deviations 汇总) 偏差 1:真实广告/Firebase SDK 不接入,只交付接口+业务层+Fake+接入槽;偏差 2:Unity IAP 真实现仅真机路径;偏差 3:Provider 真机路径不写 EditMode 单测 | 无人值守模式无法配置原生 SDK;业务逻辑全 Fake 覆盖 |
| Phase 5 | DevTools 零开销用代码内 `#if` 而非 defineConstraints;TapRush UI 为代码构建(无美术 prefab)经 prefab 化标 addressable;TapRush 音效用程序化正弦波 clip;Cheat 桥接/console prefab API 名以 unity_reflect 核对实际为准 | 示例游戏目标是打通框架能力与 Addressables 真实加载,非美术产品 |

> 备注:上表「(从 phaseN 计划 Deviations 汇总)」需实现代理执行本 Task 时,读取各 Phase 计划末尾验证代理实际填写的偏差替换。若某 Phase 无偏差则写"无"。
```

- [ ] **Step 3: 验证文档无误**(人读检查:速查表签名与磁盘接口一致;偏差摘要覆盖各 Phase)。
- [ ] **Step 4: Commit**(编排者)`git commit -m "docs: add framework README and implementation-deviations summary"`

---

### Task 7: 全量验证(串行,使用 UnityMCP)

- [ ] **Step 1: 刷新编译** — `refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `is_compiling == false`。
- [ ] **Step 2: 0 error** — `read_console(types=["error"])`。Expected: 0 errors(含 DevTools/Template/Game/Game.Editor 全部新程序集)。
- [ ] **Step 3: EditMode 全量测试** — `run_tests(mode="EditMode")` → `get_test_job` 轮询。Expected: **Phase 1-4 全部既有用例 + Phase 5 新增全绿**(Phase 5 新增:CheatRegistry 5、TemplateGameFlow 3、TapRushSession 5 = 13 个新用例)。Game.Tests.EditMode 与 EasyFramework.Tests.EditMode 两个测试程序集都跑到。
- [ ] **Step 4: 无新警告** — `read_console(types=["warning"])`。Expected: 无框架相关新警告(注意:CheatRegistry 重复/不支持签名测试会主动 `Debug.LogWarning`,这是被测路径的预期输出;若用 `LogAssert.Expect` 收口更佳,否则报告注明)。
- [ ] **Step 5: TapRush PlayMode 验收流程**(执行 Task 5 Step 9 全流程):一次性资产生成 → Play → Boot 完成 → 驱动到 Gameplay → 模拟得分 → 到 Result(控制台无 error)→ 停 → 再 Play 验证 HighScore 读回。`read_console` 收口确认全程无 error。
- [ ] **Step 6: 第三方 API 核对收口** — 验证代理用 `unity_reflect` 确认 `IngameDebugConsole.DebugLogConsole.AddCommand`(Cheat 桥)、`DebugLogManager`/Resources prefab(console 实例化)、`AddressableAssetSettings.CreateOrMoveEntry`/`entry.address`(asset setup)、`AudioClip.Create`/`AssetDatabase.CreateAsset` 与安装版本一致;有出入按实际等效调整后重验。
- [ ] **Step 7: 偏差记录** — 若实现与本计划有偏差(IngameDebugConsole API 名、Addressables Editor API、SaveProfile 生产替换机制、TapEvent 事件接线),在本计划文档末尾追加 "Deviations" 小节记录,并据实更新 Task 6 Step 2 写入 spec 的 Phase 5 偏差行。
- [ ] **Step 8: Commit**(编排者)收尾提交。

---

## Self-Review 记录

- **Spec 覆盖:** Phase 5 范围 = 设计文档 §6(DevTools 与新游戏工作流)+ §7(测试策略)+ §10 Phase 5 条目(DevTools / _Template / 示例游戏验收)。逐项核对:
  - §6「IngameDebugConsole 三指下滑呼出」→ Task 1 装包 + Task 3 `DevToolsBootTask.EnsureDebugConsole` 实例化 prefab。✓
  - §6「作弊命令 `[Cheat("add_gold")]` 特性注册」→ Task 2 `CheatAttribute` + `CheatRegistry`(扫描静态方法注册到控制台),TapRush `TapRushCheats` 示例。✓
  - §6「FPS/内存角标」→ Task 3 `PerfOverlay`(OnGUI,FPS 滑动平均 + Profiler 内存)。✓
  - §6「新游戏 = 复制 `_Template/` 改名即跑」→ Task 4 可编译模板(LifetimeScope/Flow/SaveData/README,十分钟上手)。✓
  - §7 测试策略「Core 全 EditMode、Services 关键路径 PlayMode、Monetization Fake + 真机冒烟」→ Cheat/Template/TapRushSession EditMode 单测;TapRush PlayMode 验收(整框架冒烟,含 Fake 商业化 + Addressables 真实加载 + 存档读回)。✓
  - 「发布版零开销」→ §执行基线 3:代码内 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`,asmdef 不用 defineConstraints,程序集始终编译、引用恒定有效。✓
- **类型一致性:** 跨 Phase 引用的契约均以「磁盘实际为准(各 Phase 计划锁定为基线)」声明,且每个引用 Task 标注「先 Read 磁盘核对」:`StateMachine<TContext>`/`State<TContext>`(Phase 1)、`IBootTask`/`BootCompletedEvent`(Phase 1)、`IAssetService`/`AssetScope`(Phase 2)、`ISaveService`/`SaveData`/`SaveProfile`/`FrameworkOptions.SaveProfile`(Phase 2)、`IPoolService`/`IPoolable`(Phase 1/2)、`IUIService`/`UIPanel`/`UIPopup<TResult>`(Phase 3a)、`IAudioService.PlaySfx/PlayBgmAsync`(Phase 3b)、`TapEvent`/`IInputService`(Phase 3b)、`IAdsService.ShowRewardedAsync`/`AdResult`(Phase 4)、`ProductCatalog`/`IAPService.SetRewardHandler`(Phase 4)、`IAnalyticsService.Track`(Phase 4)、`G.*` 门面属性。Phase 5 新增契约(`CheatAttribute`/`CheatRegistry`/`PerfOverlay`/`DevToolsBootTask`/`TapRushSession`)在「锁定契约」节集中定义,测试/实现/调用三处一致。✓
- **占位符:** 无 TBD。所有「不单测」均为 spec/任务书明确许可的薄层,非占位符:(a) `PerfOverlay` OnGUI 角标(薄视觉);(b) `DevToolsBootTask` 编译符号两态(EditMode 下 `UNITY_EDITOR` 恒真,行为在 PlayMode 间接覆盖);(c) `CheatRegistry.BridgeToConsole`(唯一触真实 console 薄层,扫描/去重/跳过分支经替换钩子全测);(d) TapRush UI 代码构建外观(经 prefab 加载链路 PlayMode 验收)。可能漂移处均给出具体回退:Cheat 委托构造 AOT 受限 → 显式 `Action`/`Action<T>` switch;console API 名不符 → `unity_reflect` 核对等效调整;`AudioClip.asset` 加载受限 → 导出 `.wav`;TapEvent 不经 bus → Game 层直读 Input System;SaveProfile 生产替换机制 → 按磁盘实际选可行路径(子作用域覆盖 / RootLifetimeScope Inspector / 自定义子类)。✓
- **DevTools 零开销可编译性:** `EasyFramework.DevTools` asmdef 无 `defineConstraints`(始终编译),`CheatAttribute` 纯特性恒在,`CheatRegistry`/`PerfOverlay`/`DevToolsBootTask` 公共签名在 `#if` 内外一致(发布版保留空壳/no-op),Game/Template 对其引用在任何构建配置下都解析。✓

---

## 自查发现并修正的问题(起草阶段)

1. **OpenUPM registry 重复添加风险** —— Phase 3b 已加 OpenUPM scoped registry(scopes 含 `com.kyrylokuzyk`)。Task 1 若再 `add_registry` 同 name/url 可能造成重复 registry。修正:Task 1 Step 1 明确「只往现有 OpenUPM registry 的 scopes 追加 `com.yasirkula`」,给出工具幂等核对 + 手改 manifest 两条路径,并要求 Read manifest 核对最终只有一个 OpenUPM 项。
2. **DevTools asmdef defineConstraints 会破坏引用** —— 若 DevTools 用 `defineConstraints: ["DEVELOPMENT_BUILD"]`,发布版该程序集不编译,Game/Template 对它的引用全断、整个工程编译失败。修正:asmdef 不用 defineConstraints(始终编译),零开销靠代码内 `#if`,公共 API 签名 `#if` 内外一致(发布版空壳)。已写入 §执行基线 3 与各 Task。
3. **Tests asmdef 引用 EasyFramework.Template 的编译序** —— Task 1 升级 Tests asmdef 时 Template 程序集尚未创建(Task 4 才建),提前加引用会报"未找到程序集"。修正:Task 1 Step 5 只加 `EasyFramework.DevTools`,`EasyFramework.Template` 引用延到 Task 4 Step 2 落地 Template 程序集后再补;Step 7 编译核对注明此顺序。
4. **TapRush 生产 SaveProfile 必须是 TapRushSaveData** —— `G.Save.Data<TapRushSaveData>()` 若生产 profile 仍是框架默认 `DefaultSaveData` 会强转崩溃。修正:Task 5 Step 6 详述「生产 profile 必须 = TapRushSaveData」,要求 Read 磁盘 `FrameworkInstaller`/`RootLifetimeScope` 核对 `FrameworkOptions.SaveProfile` 实际替换机制,Step 9 Boot 场景设置由验证代理落实(子作用域覆盖 / Inspector 字段 / 自定义子类三选一,以磁盘可行为准)。
5. **跨程序集重写 `protected internal` 钩子** —— Game 面板(`Game` 程序集)继承 `UIPanel`(`Services` 程序集)重写 `OnSetup`/`OnBackRequested` 等 `protected internal` 成员。`protected internal` 的 `protected` 部分允许跨程序集派生类重写,可行;但若 Phase 3a 实际设为纯 `internal`(不可跨程序集重写)则会编译失败。修正:Task 5 Step 3 注明先 Read Phase 3a `UIPanel.cs`/`UIPopup.cs` 核对重写点,有出入按实际重写点调整(契约不改,只调用方式)。
6. **Editor 脚本归属** —— `TapRushAssetSetup` 用 `UnityEditor`/`AddressableAssetSettings`,必须 Editor-only。修正:选定「放 `Assets/Game/Editor/` + 独立 `Game.Editor.asmdef`(`includePlatforms: ["Editor"]`)」方案(Task 5 Step 8),不污染运行时 `Game` 程序集;Addressables Editor 程序集名以 unity_reflect 核对。
7. **不改框架 Boot 接线文件** —— DevTools/TapRush 若改 `FrameworkInstaller`/`RootLifetimeScope`/`G.cs` 会与 Phase 1-4 接线冲突。修正:§执行基线 2 锁定「Phase 5 不改 Boot 三件套」,DevTools BootTask 经各游戏 `GameLifetimeScope` 的 `IBootTask` 收集机制注册,`DevToolsBootTask` 用 `FindFirstObjectByType<RootLifetimeScope>` 拿宿主节点(找不到自建 `[DevTools]` 兜底),不触组合根。
8. **DevToolsBootTask 扫描全部程序集性能** —— `AppDomain.CurrentDomain.GetAssemblies()` 全扫会慢且可能扫到系统程序集异常。修正:Task 3 按名字前缀(`Game`/`EasyFramework`)过滤只扫业务/框架程序集,`try/catch` 包裹非关键失败只 LogWarning。
