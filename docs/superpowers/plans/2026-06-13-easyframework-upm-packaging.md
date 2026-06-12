# EasyFramework UPM 插件化打包重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task。本计划串行性强、风险高(GUID/.meta 迁移、依赖源切换、目录搬迁),**不适合大量并行**——主要由**串行验证代理**逐 Task 推进,**git 提交由编排者统一执行**。Steps 用 checkbox(`- [ ]`)语法跟踪。

**Goal:** 把 EasyFramework 从「本仓库 `Assets/EasyFramework/` 目录」重构为一个**开箱即用的嵌入式 UPM 包** `Packages/com.yifei.easyframework/`,全部依赖(含第三方)在 `package.json` 声明、由 OpenUPM registry 解析。验收标准:全新 Unity 6 项目按 README 安装章节(openupm-cli 一条命令,或手动粘贴 manifest 配置)→ 依赖自动装齐 → 编译干净可用。本仓库自身先「吃自己的狗粮」——manifest 的 git URL 第三方依赖切成 OpenUPM registry 依赖,与 package.json 声明一致。

**Architecture:** 单一标准 UPM 包(设计文档 §1 否决了 .unitypackage / 模板仓库 / 自举安装器)。框架五个程序集(`EasyFramework.Core/Services/Monetization/Boot/DevTools`)及框架 `EditMode` 测试 `git mv` 进包,**程序集名全部保持不变**,消费项目代码引用无感。示例(`_Template`、`Assets/Game` TapRush)迁入 `Samples~/`,经 Package Manager 「Import Sample」分发。Boot 场景属**项目层不进包**(设计 §4),改用基础 `RootLifetimeScope`,TapRush 专属启动随 Sample 走。

**Tech Stack:** Unity 6000.3.15f1 / 嵌入式 UPM 包 / OpenUPM scoped registry / VContainer / UniTask / MessagePipe / Newtonsoft Json / Addressables / Cinemachine / PrimeTween / Unity IAP / IngameDebugConsole / Unity Test Framework 1.6

---

## ⚠️ 贯穿全程的三条硬约束(每个 Task 都要遵守)

1. **GUID / .meta 保护 —— 一律 `git mv`,严禁删了重建。**
   所有代码/资产迁移都用 `git mv`,**连同 `.meta` 一起移动**,GUID 不变,既有场景 / prefab / asmdef 引用不断裂。`git mv <目录>` 会自动带上目录内文件,但**目录自身的 `.meta`(如 `Core.meta`、`Boot.meta`)是单独文件,必须显式一并 `git mv`**,否则新位置目录无 GUID、Unity 重新生成,关联引用会断。下文每条迁移命令都已把目录 `.meta` 显式列出。

2. **`Samples~/` 不编译 —— 测试基线随之从 145 降到约 137。**
   `Packages/com.yifei.easyframework/Samples~/` 目录名带波浪号(`~`),Unity **完全忽略、不导入、不编译**。一旦 `_Template` 和 TapRush(`Assets/Game`)移入 `Samples~/`,它们就**退出本工程编译**:本工程 `EditMode` 测试数从 **145 降到约 137**(仅剩框架自有测试)。随 Sample 迁走的测试:`Game.Tests.EditMode`(TapRushSession,约 5 个)、`_Template` 的 `TemplateGameFlowTests`(约 3 个)。**这是预期行为**——Task 4 之后,本工程测试**基线 = 137**,不要把「测试变少」当成回归。要在本工程内重新跑 TapRush,须经 Package Manager 「Import Sample」把它复制回 `Assets/Samples/`(Task 5)。

3. **`com.coplaydev.unity-mcp` 绝不进包、切 registry 时绝不动它。**
   manifest 里的 `com.coplaydev.unity-mcp`(git URL)是**开发用的 UnityMCP 桥**,只服务本仓库的 agent-team 工作流,**不能写进 `package.json` 的 `dependencies`,也不在 Task 1 切 registry 时改动**。它停留在本仓库 manifest 的 `dependencies` 里、保持 git URL 原样。

---

## agent-team 执行环境说明

- Unity 编辑器已打开。UnityMCP 服务器名为 **`unityMCP`**(小写 `u` 开头),工具形如 `mcp__unityMCP__refresh_unity` / `mcp__unityMCP__read_console` / `mcp__unityMCP__run_tests` / `mcp__unityMCP__get_test_job` / `mcp__unityMCP__manage_packages`,编译状态读 `mcpforunity://editor/state` 资源的 `isCompiling` 字段。
- **验证 = 编译 0 error + 既有 EditMode 套件全绿 + 空工程可装。** 这是打包重构而非 TDD 功能开发,没有红绿测试新增;每个 Task 的「验证」按此三件套裁剪。
- **串行验证代理**负责执行 `git mv`、改文件、`refresh_unity`、读 console、`run_tests`。**实现/验证代理禁止运行 `git commit`**;每个 Task 末尾的 Commit 由**编排者**统一执行。
- `.meta` 文件不要手写、不要手改 GUID;`git mv` 已保住 GUID。`package.json` / `LICENSE.md` / `CHANGELOG.md` 这类**新文件**用 `Write` 创建,其 `.meta` 由 Unity 刷新时自动生成。
- Unity Cloud 控制台的 `resolving packages` / `Token Exchange` 报错是**网络噪音**,与本重构无关,忽略。UnityMCP 桥偶发的 `Cannot access a disposed object` 同样忽略。
- **Unity CLI 可用(Task 7 空工程验收):** `/Applications/Unity/Hub/Editor/6000.3.15f1/Unity.app/Contents/MacOS/Unity`。注意环境里实际安装的 Editor 也可能是 6000.3.15f1;若该路径不存在,验证代理用 `ls /Applications/Unity/Hub/Editor/` 取实际版本目录。

---

## 迁移前后文件结构总览

### 迁移前(现状)

```
Assets/
├── EasyFramework/
│   ├── Core/        Core.meta              (EasyFramework.Core)
│   ├── Services/    Services.meta          (EasyFramework.Services)
│   ├── Monetization/ Monetization.meta     (EasyFramework.Monetization)
│   ├── Boot/        Boot.meta              (EasyFramework.Boot:G/FrameworkInstaller/RootLifetimeScope/GameLifetimeScope)
│   ├── DevTools/    DevTools.meta          (EasyFramework.DevTools)
│   ├── Tests/       Tests.meta             (EasyFramework.Tests.EditMode,框架自有,约 137 个)
│   ├── _Template/   _Template.meta         (EasyFramework.Template + TemplateGameFlowTests,约 3 个)
│   └── README.md    README.md.meta         (框架使用文档)
├── Game/                                   (TapRush 示例 + Game.Tests.EditMode,约 5 个)
│   ├── TapRush*.cs / UI/ / Editor/ / GeneratedAssets/ / Tests/ / AssemblyInfo.cs / Game.asmdef
├── Scenes/
│   ├── Boot.unity   ← 当前挂 TapRushRootLifetimeScope + TapRushGameLifetimeScope(随 Game 迁走会丢引用)
│   └── SampleScene.unity
Packages/
├── manifest.json   ← 4 个第三方依赖是 git URL;OpenUPM scopes 只有 com.kyrylokuzyk/com.yasirkula
└── (无 com.yifei.easyframework)
```

当前 EditMode 测试合计 **145**(框架约 137 + TapRush 约 5 + Template 约 3)。

### 迁移后(目标)

```
Assets/
├── Scenes/
│   ├── Boot.unity   ← 改挂基础 RootLifetimeScope(GUID 19ea866847422446ea6246f0a0b090bd)
│   └── SampleScene.unity
└── Samples/EasyFramework/0.1.0/TapRush/   ← Task 5 经 Package Manager 导入的开发工作副本(不入 git 或单独管理)
Packages/
├── manifest.json   ← 4 依赖切 OpenUPM registry;scopes 补 com.cysharp/jp.hadashikick;新增 testables;
│                       com.coplaydev.unity-mcp 保持 git URL 不动
└── com.yifei.easyframework/            ← 嵌入式 UPM 包(Unity 直接编译)
    ├── package.json
    ├── README.md            ← 原 Assets/EasyFramework/README.md
    ├── CHANGELOG.md
    ├── LICENSE.md
    ├── Core/ Services/ Monetization/ Boot/ DevTools/   ← 五程序集原样 git mv,程序集名不变
    ├── Tests/                ← 框架 EditMode 测试(约 137,嵌入包测试,Test Runner 自动出现)
    └── Samples~/             ← 波浪号目录,Unity 不编译
        ├── Template/         ← 原 Assets/EasyFramework/_Template
        └── TapRush/          ← 原 Assets/Game
```

迁移后本工程 EditMode 测试基线 **≈ 137**(`Samples~/` 内的 TapRush/Template 测试退出编译)。

> **关于 `Editor/` 目录(设计 §2):** 设计文档列了包内 `Editor/`(`BootSceneCreator.cs` = `EasyFramework/Create Boot Scene` 菜单、`SampleSyncTool.cs` = `EasyFramework/Dev/Sync Sample to Package` 菜单)。当前 `Assets/EasyFramework/Boot/` 下未见独立 `Editor` 程序集;`BootSceneCreator` / `SampleSyncTool` 属**本计划新增的可选编辑器工具**(见 Task 5,标低优先)。Task 3 只迁移既有的五个程序集 + Tests + README,不凭空造 `Editor/`。

---

### Task 1: 吃自己的狗粮 —— manifest 第三方依赖切 OpenUPM registry

> 先做这步,用来**提前暴露 OpenUPM 版本解析问题**:若某版本号在 OpenUPM 不存在,本 Task 当场修正并记入 Deviations,避免到 Task 2 写 `package.json` 时才发现。

**Files:** Modify: `Packages/manifest.json`

- [ ] **Step 1: 把 4 个 git URL 依赖改成 registry 版本号**

在 `Packages/manifest.json` 的 `dependencies` 中,把下面 4 行从 git URL 改成纯版本号(键名不变,值改为版本字符串):

```jsonc
// 改前(git URL):
"com.cysharp.messagepipe": "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe#1.8.1",
"com.cysharp.messagepipe.vcontainer": "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.VContainer#1.8.1",
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.10",
"jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer#1.16.9",

// 改后(OpenUPM registry 版本):
"com.cysharp.messagepipe": "1.8.1",
"com.cysharp.messagepipe.vcontainer": "1.8.1",
"com.cysharp.unitask": "2.5.10",
"jp.hadashikick.vcontainer": "1.16.9",
```

**不动** `com.coplaydev.unity-mcp`(保持 git URL)。**不动**已是 registry 的 `com.kyrylokuzyk.primetween=1.3.3` / `com.yasirkula.ingamedebugconsole=1.8.7`。官方源依赖(`com.unity.*`)不动。

- [ ] **Step 2: OpenUPM scopes 追加 com.cysharp 与 jp.hadashikick**

`scopedRegistries` 现有唯一一条 OpenUPM(`url: https://package.openupm.com`),`scopes` 当前为 `["com.kyrylokuzyk", "com.yasirkula"]`。改为(按字母序整理,新增两条):

```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": [
      "com.cysharp",
      "com.kyrylokuzyk",
      "com.yasirkula",
      "jp.hadashikick"
    ]
  }
]
```

> `com.cysharp` 一个 scope 同时覆盖 `com.cysharp.unitask` / `com.cysharp.messagepipe` / `com.cysharp.messagepipe.vcontainer`(scope 是前缀匹配)。`jp.hadashikick` 覆盖 `jp.hadashikick.vcontainer`。

- [ ] **Step 3: 刷新并核实版本可解析**

`mcp__unityMCP__refresh_unity` → 轮询 `mcpforunity://editor/state` 至 `isCompiling == false`。UPM 会用新 registry 重新解析这 4 个包。
**若某版本号在 OpenUPM 不存在 / 不可解析**(console 报 `Package [xxx@1.8.1] cannot be found` 之类):用 `mcp__unityMCP__manage_packages`(search / list versions 能力)查该包在 OpenUPM 的**实际可用版本**,取最接近的可解析版本回填 Step 1,并在 manifest 改完后**同步 Task 2 的 `package.json` 对应版本**。把每处偏差记入文末 Deviations。

- [ ] **Step 4: 验证**
  - `mcp__unityMCP__read_console(types=["error"])` → Expected: **0 编译 error**(忽略 unity-mcp 桥的 disposed-object 噪音、Unity Cloud 网络噪音)。
  - `mcp__unityMCP__run_tests(mode="EditMode")` → `get_test_job` 轮询 → Expected: **145 全绿**(此时还没搬任何目录,基线仍是 145)。

- [ ] **Step 5: Commit**(编排者)

```bash
git add Packages/manifest.json Packages/packages-lock.json
git commit -m "chore(deps): switch UniTask/VContainer/MessagePipe from git URL to OpenUPM registry"
```

---

### Task 2: 建包壳(package.json / LICENSE.md / CHANGELOG.md)

> 此时包内还没有代码,只立身份 + 依赖声明 + 法务文件。

**Files:** Create:
- `Packages/com.yifei.easyframework/package.json`
- `Packages/com.yifei.easyframework/LICENSE.md`
- `Packages/com.yifei.easyframework/CHANGELOG.md`

- [ ] **Step 1: 创建 `Packages/com.yifei.easyframework/package.json`**

写入完整内容(`dependencies` 含全部官方源 + OpenUPM 依赖,版本与 Task 1 验证后一致;`samples` 先声明 Template 与 TapRush 两条):

```json
{
  "name": "com.yifei.easyframework",
  "version": "0.1.0",
  "displayName": "EasyFramework",
  "description": "开箱即用的 Unity 2D 移动游戏复用底座。VContainer 依赖注入内核 + 静态门面,服务层全部接口化,商业化 SDK 可整体替换而不改业务代码。",
  "unity": "6000.0",
  "author": {
    "name": "yifei"
  },
  "license": "MIT",
  "documentationUrl": "https://github.com/whimwindgames/EasyFramework-Unity#readme",
  "changelogUrl": "https://github.com/whimwindgames/EasyFramework-Unity/blob/main/Packages/com.yifei.easyframework/CHANGELOG.md",
  "dependencies": {
    "com.unity.addressables": "3.1.0",
    "com.unity.cinemachine": "3.1.7",
    "com.unity.inputsystem": "1.19.0",
    "com.unity.ugui": "2.0.0",
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.unity.purchasing": "5.3.1",
    "com.cysharp.unitask": "2.5.10",
    "jp.hadashikick.vcontainer": "1.16.9",
    "com.cysharp.messagepipe": "1.8.1",
    "com.cysharp.messagepipe.vcontainer": "1.8.1",
    "com.kyrylokuzyk.primetween": "1.3.3",
    "com.yasirkula.ingamedebugconsole": "1.8.7"
  },
  "samples": [
    {
      "displayName": "Template (新游戏空白模板)",
      "description": "复制即用的新游戏起步模板:GameLifetimeScope + GameFlow + SaveData 骨架。",
      "path": "Samples~/Template"
    },
    {
      "displayName": "TapRush (完整示例游戏)",
      "description": "用本框架做的完整可玩小游戏:点击得分 + 60 秒倒计时 + 结算 + 看广告翻倍 + 存档最高分。框架全能力的活文档。",
      "path": "Samples~/TapRush"
    }
  ]
}
```

> **版本同步提醒:** 若 Task 1 Step 3 因 OpenUPM 收录差异调整过任一 OpenUPM 版本号,这里的 `dependencies` 必须用**同样的调整后版本**,二者逐字一致(设计 §4「本仓库走的就是用户的解析路径」)。`com.coplaydev.unity-mcp` **不在此列**。

- [ ] **Step 2: 创建 `Packages/com.yifei.easyframework/LICENSE.md`(MIT 全文)**

```
MIT License

Copyright (c) 2026 yifei

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

> 许可仅覆盖框架自身代码;第三方全部是外部依赖、不随包再分发(设计 §2)。

- [ ] **Step 3: 创建 `Packages/com.yifei.easyframework/CHANGELOG.md`(0.1.0 首条)**

```markdown
# Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.1.0] - 2026-06-13

### Added

- 首个 UPM 包发布。EasyFramework 从仓库 `Assets/` 目录重构为单一标准 UPM 包 `com.yifei.easyframework`。
- 核心层:异步状态机、对象池、定时器、强类型事件总线、优先级启动管线(纯 C#,全单测)。
- 服务层:资源(Addressables 按场景自动释放)、场景过渡、存档(HMAC + 版本迁移 + 原子写)、远程配置、四层 UI 栈、音频、手势、相机、Juice、本地化、震动。
- 商业化层:广告(频控)、内购(恢复购买 + 掉单补发)、数据统计;编辑器 Fake 实现 + 真实 SDK `#if` 接入槽。
- 开发体验:真机调试控制台、`[Cheat]` 作弊命令、FPS/内存角标。
- 全部依赖在 `package.json` 声明,由 OpenUPM registry 解析。
- 两个 Sample:Template(新游戏空白模板)、TapRush(完整示例游戏)。
```

- [ ] **Step 4: 验证**
  - `mcp__unityMCP__refresh_unity` → 轮询至 `isCompiling == false`。
  - 包应出现在 Package Manager「In Project / Embedded」分区,`displayName` 显示 "EasyFramework"、版本 0.1.0。可用 `mcp__unityMCP__manage_packages`(list 能力)确认 `com.yifei.easyframework` 在列。
  - `mcp__unityMCP__read_console(types=["error"])` → Expected: **无新编译 error**(包内还没代码,只是壳;若 `package.json` JSON 语法错会在此暴露)。

- [ ] **Step 5: Commit**(编排者)

```bash
git add Packages/com.yifei.easyframework/package.json \
        Packages/com.yifei.easyframework/LICENSE.md \
        Packages/com.yifei.easyframework/CHANGELOG.md
git commit -m "feat(pkg): scaffold com.yifei.easyframework UPM package shell"
```

> 新文件的 `.meta`(Unity 刷新生成)如已生成,一并 `git add`(`git add Packages/com.yifei.easyframework/` 整目录即可)。

---

### Task 3: 迁移框架代码进包(git mv,五程序集 + Tests + README)

> 严格 `git mv`,目录 `.meta` 显式列出。`_Template` 本 Task **不动**(Task 4 处理),所以 `Assets/EasyFramework/` 目录在本 Task 后仍保留(只剩 `_Template` 及其 `.meta`)。

**Files:** Move(git mv):`Assets/EasyFramework/{Core,Services,Monetization,Boot,DevTools,Tests}` + 各目录 `.meta` + `Assets/EasyFramework/README.md` → `Packages/com.yifei.easyframework/`

- [ ] **Step 1: 逐条执行 git mv(连目录 .meta)**

逐条运行(顺序无关,但建议照抄全部):

```bash
cd /Users/yifei/ClaudeWorkSpace/EasyFrameWork

# Core
git mv Assets/EasyFramework/Core        Packages/com.yifei.easyframework/Core
git mv Assets/EasyFramework/Core.meta   Packages/com.yifei.easyframework/Core.meta

# Services
git mv Assets/EasyFramework/Services        Packages/com.yifei.easyframework/Services
git mv Assets/EasyFramework/Services.meta   Packages/com.yifei.easyframework/Services.meta

# Monetization
git mv Assets/EasyFramework/Monetization        Packages/com.yifei.easyframework/Monetization
git mv Assets/EasyFramework/Monetization.meta   Packages/com.yifei.easyframework/Monetization.meta

# Boot
git mv Assets/EasyFramework/Boot        Packages/com.yifei.easyframework/Boot
git mv Assets/EasyFramework/Boot.meta   Packages/com.yifei.easyframework/Boot.meta

# DevTools
git mv Assets/EasyFramework/DevTools        Packages/com.yifei.easyframework/DevTools
git mv Assets/EasyFramework/DevTools.meta   Packages/com.yifei.easyframework/DevTools.meta

# Tests(框架自有 EditMode,约 137)
git mv Assets/EasyFramework/Tests        Packages/com.yifei.easyframework/Tests
git mv Assets/EasyFramework/Tests.meta   Packages/com.yifei.easyframework/Tests.meta

# README(框架使用文档 → 包根 README.md)
git mv Assets/EasyFramework/README.md        Packages/com.yifei.easyframework/README.md
git mv Assets/EasyFramework/README.md.meta   Packages/com.yifei.easyframework/README.md.meta
```

> `git mv <目录>` 会把目录内全部文件(含其 `.cs.meta`)一并移动;但**目录自身的 `.meta`**(`Core.meta` 等)是平级文件,故每个目录都补了一条显式 `git mv ...meta`。
> **GUID 不变**:asmdef、脚本、prefab 的 GUID 全部保留,Boot 里的 `RootLifetimeScope` / `G` 等引用不断。程序集名(`EasyFramework.Core` 等)不变,消费代码无感。

- [ ] **Step 2: 确认 `Assets/EasyFramework/` 仅剩 _Template**

```bash
ls -a /Users/yifei/ClaudeWorkSpace/EasyFrameWork/Assets/EasyFramework/
```

Expected: 只剩 `_Template`、`_Template.meta`(以及 `.DS_Store`,无关)。**此时不要删 `Assets/EasyFramework/` 目录**——Task 4 才把 `_Template` 移走。

- [ ] **Step 3: 刷新 + 验证**
  - `mcp__unityMCP__refresh_unity` → 轮询至 `isCompiling == false`。Unity 会把这些 asmdef 当作**嵌入包内的程序集**重新编译(嵌入包 Unity 直接编译)。
  - `mcp__unityMCP__read_console(types=["error"])` → Expected: **0 编译 error**。
  - `mcp__unityMCP__run_tests(mode="EditMode")` → `get_test_job` → Expected: **仍 145 全绿**。本 Task `_Template`(约 3)和 `Assets/Game` TapRush(约 5)还在 `Assets/` 下参与编译,框架 137 个测试从包里跑(嵌入包测试在 Test Runner 自动出现)。合计 **145 不变**。
  - 若框架 README 移动产生 `.meta` 警告:`mcp__unityMCP__read_console(types=["warning"])` 检查,通常 `git mv` 带走 `.meta` 后无警告;若有孤立 `.meta` 警告则核对该文件是否漏移。

- [ ] **Step 4: Commit**(编排者)

```bash
git add -A Assets/EasyFramework Packages/com.yifei.easyframework
git commit -m "refactor(pkg): git mv framework assemblies + tests + README into package"
```

---

### Task 4: Template 与 TapRush 移入 Samples~,Boot 场景改基础 RootLifetimeScope

> 本 Task 后 `Samples~/` 内容退出编译,本工程测试**降到约 137**(基线切换)。

**Files:**
- Move:`Assets/EasyFramework/_Template` → `Packages/com.yifei.easyframework/Samples~/Template`
- Move:`Assets/Game` → `Packages/com.yifei.easyframework/Samples~/TapRush`
- Modify:`Assets/Scenes/Boot.unity`(换 LifetimeScope 脚本引用)
- (samples 数组 Task 2 已写好正确路径,本 Task 核对一致即可)

- [ ] **Step 1: 建 Samples~ 目录并 git mv 两个示例**

`Samples~` 带 `~`,git 与 shell 都能正常处理(`~` 只在路径**开头**才被 shell 展开为 home;此处在路径中间,无需转义)。

```bash
cd /Users/yifei/ClaudeWorkSpace/EasyFrameWork
mkdir -p Packages/com.yifei.easyframework/Samples~

# Template(原 _Template;含 asmdef、README、TemplateGameFlowTests)
git mv Assets/EasyFramework/_Template        Packages/com.yifei.easyframework/Samples~/Template
git mv Assets/EasyFramework/_Template.meta   Packages/com.yifei.easyframework/Samples~/Template.meta

# TapRush(原 Assets/Game 全部:TapRush*.cs / UI / Editor / GeneratedAssets / Tests / AssemblyInfo / Game.asmdef)
git mv Assets/Game        Packages/com.yifei.easyframework/Samples~/TapRush
git mv Assets/Game.meta   Packages/com.yifei.easyframework/Samples~/TapRush.meta
```

> `git mv Assets/Game` 一次带走整个 TapRush(脚本 + UI + Editor + GeneratedAssets 的 prefab/png + Tests + asmdef),`.meta` 全部跟随,GUID 不变(Sample 内部引用自洽)。
> 移动后 `Assets/EasyFramework/` 应已空(只剩 `.DS_Store`);可保留空目录或顺手清理,但**清理非必须**,不要为清理引入额外风险。`Assets/Game.meta` 若不存在(`ls` 确认),省略那一行。

- [ ] **Step 2: 处理 Boot 场景丢失脚本引用(设计 §4)**

`Assets/Scenes/Boot.unity` 当前挂的两个 MonoBehaviour 都是 **TapRush 专属**,随 Sample 移走后会变成 "missing script":
- `TapRushRootLifetimeScope`,GUID `1f533e0c94f0b4b72b8a8444a24097cf`
- `TapRushGameLifetimeScope`,GUID `3bcf48c90ed2c460597f64c6ff1d4c34`

按设计 §4:**Boot 场景属项目层、不进包,改用基础 `RootLifetimeScope`;TapRush 专属启动随 Sample 走。** 处理方案(二选一,推荐 A):

**方案 A(推荐,保住场景 GUID):用 UnityMCP 在编辑器内改场景。**
1. `mcp__unityMCP__manage_scene`(load `Assets/Scenes/Boot.unity`)。
2. 删除挂着 `TapRushGameLifetimeScope` 的对象/组件(它是 TapRush 子作用域,基础 Boot 场景不需要)。
3. 找到 `[EasyFramework]` 根对象(原挂 `TapRushRootLifetimeScope`),把其 LifetimeScope 组件换成基础 `RootLifetimeScope`(GUID `19ea866847422446ea6246f0a0b090bd`,程序集 `EasyFramework.Boot`,现已在包内):用 `mcp__unityMCP__manage_gameobject` 移除旧(missing)组件、`add_component` 加 `RootLifetimeScope`;或用 `mcp__unityMCP__manage_components`。
4. `manage_scene` 保存场景。
5. 该场景仍是 `Assets/Scenes/Boot.unity`,GUID 不变,留在 Build Settings 第 0 位。

**方案 B(文本兜底,仅在 A 不可行时):** 直接编辑 `Boot.unity` YAML,把 `TapRushGameLifetimeScope` 的 MonoBehaviour 块整段删除,把 `[EasyFramework]` 根上 `m_Script: {... guid: 1f533e0c94f0b4b72b8a8444a24097cf ...}` 改为 `guid: 19ea866847422446ea6246f0a0b090bd`(基础 `RootLifetimeScope`)。注意基础 scope 若缺 TapRush 专属 `[SerializeField]` 字段,删除对应序列化行避免 YAML 残留警告。改后 `refresh_unity` 核对无 missing-script 报错。

> 基础 `RootLifetimeScope` 已随 Task 3 进包(`Packages/com.yifei.easyframework/Boot/RootLifetimeScope.cs`,GUID 不变),编辑器可直接挂。

- [ ] **Step 3: 核对 package.json samples 路径**

Task 2 已把 `samples[].path` 写为 `Samples~/Template`、`Samples~/TapRush`,与本 Task 落地目录名(`Template` / `TapRush`)一致,**无需改动**。仅在此 Step 二次确认两条 `path` 与实际目录逐字相符。

- [ ] **Step 4: 刷新 + 验证(基线切到 137)**
  - `mcp__unityMCP__refresh_unity` → 轮询至 `isCompiling == false`。`Samples~/` **不编译**,TapRush/Template 退出工程。
  - `mcp__unityMCP__read_console(types=["error"])` → Expected: **0 编译 error**,且**无 "The referenced script (Unknown) on this Behaviour is missing" 之类的 Boot 场景丢失脚本报错**(Step 2 已修)。
  - `mcp__unityMCP__run_tests(mode="EditMode")` → `get_test_job` → Expected: **≈ 137 全绿**(框架自有测试;TapRush 约 5 + Template 约 3 已随 Sample 移出、不再运行)。这是**预期下降**,记录实际数字作为后续基线。
  - 验证代理在报告里写明:测试数 145 → 约 137 是 `Samples~/` 不编译的预期行为,非回归。

- [ ] **Step 5: Commit**(编排者)

```bash
git add -A Assets Packages/com.yifei.easyframework
git commit -m "refactor(pkg): move Template + TapRush into Samples~, switch Boot scene to base RootLifetimeScope"
```

---

### Task 5: 保留开发可验证性 + 同步工具 + testables

> `Samples~/` 不编译,要在本工程验证 TapRush 须经 Package Manager 「Import Sample」复制回 `Assets/`。

**Files:**
- Modify:`Packages/manifest.json`(加 `testables`)
- (可选,低优先)Create:`Packages/com.yifei.easyframework/Editor/EasyFramework.Editor.asmdef` + `SampleSyncTool.cs`

- [ ] **Step 1: manifest 加 testables**

在 `Packages/manifest.json` 顶层(与 `dependencies`、`scopedRegistries` 平级)加:

```json
"testables": [
  "com.yifei.easyframework"
]
```

作用:让嵌入包的 `EasyFramework.Tests.EditMode` 在本工程 Test Runner 中照常出现并运行(设计 §4)。

- [ ] **Step 2: 导入 TapRush Sample 作为开发工作副本**

经 Package Manager 把 TapRush Sample 复制到 `Assets/Samples/EasyFramework/0.1.0/TapRush/`:
- 手动:Package Manager → EasyFramework → Samples → TapRush → Import。
- 或脚本化:`mcp__unityMCP__execute_menu_item` / `mcp__unityMCP__execute_code` 调 `UnityEditor.PackageManager.UI.Sample.FindByPackage("com.yifei.easyframework", "0.1.0")` 找到 TapRush 条目并 `.Import()`。

导入后 `Assets/Samples/.../TapRush/` 参与编译,可在本工程跑 TapRush 与其测试。

- [ ] **Step 3:(可选,低优先,可省)同步工具 `Dev/Sync Sample to Package`**

设计 §2/§4 提到 `EasyFramework/Dev/Sync Sample to Package` 菜单:把 `Assets/Samples/EasyFramework/0.1.0/TapRush/` 的开发改动复制回 `Packages/com.yifei.easyframework/Samples~/TapRush/`。**标为低优先、本次可省**——发版前手动 `cp -R` 同步亦可。若实现,放包内新建 `Editor/`(`EasyFramework.Editor.asmdef` 引用 Boot/Core,`autoReferenced`、`includePlatforms: ["Editor"]`),`SampleSyncTool.cs` 用 `[MenuItem("EasyFramework/Dev/Sync Sample to Package")]` 做目录拷贝(排除 `.meta` 之外的全拷,保留 GUID 用整文件覆盖)。**若不实现,在报告中注明已跳过、留待后续。**

- [ ] **Step 4: 刷新 + 验证(导入后冒烟)**
  - `mcp__unityMCP__refresh_unity` → 轮询至 `isCompiling == false`。
  - `mcp__unityMCP__read_console(types=["error"])` → Expected: **0 error**(导入的 TapRush 工作副本编译通过)。
  - **PlayMode 冒烟**:`mcp__unityMCP__manage_editor`(进入 Play)→ 打开/确认有可玩场景(导入的 TapRush Sample 若含其专属启动场景则用之)→ `read_console(types=["error"])` 确认运行时 0 error → `manage_editor`(退出 Play)。Expected: play → console 无错 → stop 干净。
  - `run_tests(mode="EditMode")` → 导入 TapRush 后本工程测试数应回升(137 + 导入的 TapRush/Template 测试),全绿。

- [ ] **Step 5: Commit**(编排者)

```bash
git add Packages/manifest.json
# 若实现了同步工具:
# git add Packages/com.yifei.easyframework/Editor
git commit -m "chore(pkg): add testables for embedded package; (optional) sample sync tool"
```

> 导入产物 `Assets/Samples/EasyFramework/0.1.0/TapRush/` 是否纳入 git 由编排者定(通常作为开发工作副本可入库,或加 `.gitignore`);本计划不强制。

---

### Task 6: README 安装章节(根 + 包内同步)

> 安装章节必须与 Task 1 落地的真实 registry 配置**逐字一致**(设计 §6.3)。

**Files:**
- Modify:`/Users/yifei/ClaudeWorkSpace/EasyFrameWork/README.md`(根)
- Modify:`Packages/com.yifei.easyframework/README.md`(包内,Task 3 迁入)

- [ ] **Step 1: 替换根 README「安装」段(当前 24–32 行的占位)**

把现有 `## 安装` 段(`> UPM 包...正在打包中...` 占位)整段替换为下面正式内容。两条等价路径:openupm-cli 一行命令 + 手动 registry 配置块。

````markdown
## 安装

要求 Unity **6000.0** 及以上。两种安装方式任选其一。

### 方式一:openupm-cli(推荐,一条命令)

```bash
openupm add com.yifei.easyframework
```

CLI 会自动配置 OpenUPM scoped registry、补齐全部 scopes 并安装本包,依赖树由 OpenUPM 自动解析。

### 方式二:手动配置 manifest

打开消费项目的 `Packages/manifest.json`,把下面的 `scopedRegistries` 条目合并进去(若已有 OpenUPM 条目,只需把缺的 scopes 并入其 `scopes` 数组):

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.yifei",
        "com.cysharp",
        "jp.hadashikick",
        "com.kyrylokuzyk",
        "com.yasirkula"
      ]
    }
  ]
}
```

再在 `dependencies` 中加入(或在 Package Manager → My Registries 中按名安装):

```json
"com.yifei.easyframework": "0.1.0"
```

保存后回到 Unity,Package Manager 会自动解析并装齐全部依赖。

### 过渡期(OpenUPM 收录前)备用:git URL

OpenUPM 收录构建完成前,可先用 git URL 装包(依赖仍由上面的 OpenUPM registry 解析,故 registry 配置块仍需先粘贴):

```
https://github.com/whimwindgames/EasyFramework-Unity.git?path=Packages/com.yifei.easyframework#v0.1.0
```
````

> `com.yifei` 一个 scope 覆盖 `com.yifei.easyframework`。`com.cysharp` 覆盖 UniTask + MessagePipe(×2)。其余每条对应一个第三方包。该 scopes 列表与 Task 1 落地的本仓库 manifest **是同一套解析路径**(本仓库自己也走 OpenUPM)。

- [ ] **Step 2: 包内 README.md 同步安装章节**

`Packages/com.yifei.easyframework/README.md`(Task 3 从 `Assets/EasyFramework/README.md` 迁入)是 OpenUPM 包详情页与 Package Manager 详情面板展示的文档。在其顶部(标题之后、架构正文之前)插入与 Step 1 **完全相同**的「安装」章节(同样两条路径 + 同样的 registry 配置块,逐字一致)。其余框架使用文档(`G.Xxx` 速查、上手指南)保留。

- [ ] **Step 3: 验证**
  - 逐字比对:README 两处的 `scopes` 数组 = Task 1 落地 manifest 的 OpenUPM `scopes` ∪ `com.yifei`(消费项目额外需要 `com.yifei`,本仓库因是嵌入包不需要——这是唯一允许的差异,且已在 README 注释说明)。
  - 包名、版本 `0.1.0`、`com.yifei.easyframework`、git URL 的 `path=Packages/com.yifei.easyframework#v0.1.0` 与 `package.json` 一致。
  - 无 Markdown 渲染破损(嵌套代码块用了四反引号围栏)。

- [ ] **Step 4: Commit**(编排者)

```bash
git add README.md Packages/com.yifei.easyframework/README.md
git commit -m "docs: add OpenUPM install section to root and package README"
```

---

### Task 7: 空工程验收(Unity CLI 批处理)

> 设计 §6.2 的核心验收:全新空白 Unity 6 工程粘贴 registry 配置 + 以 `file:` 本地路径引用本包(模拟 OpenUPM 收录前)→ 依赖从 OpenUPM 真实解析 → 编译 0 error。

**Files:** 仅在临时目录操作,**不改本仓库任何文件**。

- [ ] **Step 1: Unity CLI 建空工程**

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.15f1/Unity.app/Contents/MacOS/Unity
# 若该版本目录不存在:ls /Applications/Unity/Hub/Editor/ 取实际版本回填 UNITY
TMP=$(mktemp -d /tmp/ef-upm-verify.XXXX)
"$UNITY" -createProject "$TMP/Blank" -batchmode -quit -logFile "$TMP/create.log"
```

- [ ] **Step 2: 写入 manifest(OpenUPM 全 scopes + file: 本地路径引用本包)**

用 `Write` 覆盖 `$TMP/Blank/Packages/manifest.json`,内容如下(`file:` 指向本仓库的包路径,模拟「已安装本包」;本包的依赖树由 OpenUPM registry 真实解析):

```json
{
  "dependencies": {
    "com.yifei.easyframework": "file:/Users/yifei/ClaudeWorkSpace/EasyFrameWork/Packages/com.yifei.easyframework",
    "com.unity.test-framework": "1.6.0"
  },
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp",
        "com.kyrylokuzyk",
        "com.yasirkula",
        "jp.hadashikick"
      ]
    }
  ]
}
```

> 消费项目无需 `com.yifei` scope(本包用 `file:` 直接引用,不走 registry);但其**依赖**(UniTask/VContainer/MessagePipe/PrimeTween/IngameDebugConsole)要走 OpenUPM,故 scopes 含这 4 个前缀。官方 `com.unity.*` 依赖由 Unity 官方源解析,无需 scope。

- [ ] **Step 3: 批处理跑一次导入编译**

```bash
"$UNITY" -batchmode -quit -projectPath "$TMP/Blank" -logFile "$TMP/import.log"
echo "exit=$?"
```

UPM 先解析 + 下载依赖(首次会联网拉 OpenUPM 包),再编译。

- [ ] **Step 4: grep 日志确认 0 编译错误**

```bash
# 编译错误检查(应无输出)
grep -E "error CS[0-9]+" "$TMP/import.log" || echo "NO_CS_ERRORS"
# 包解析失败检查
grep -iE "cannot be found|failed to resolve|Unable to add package|404" "$TMP/import.log" || echo "NO_RESOLVE_ERRORS"
# 退出码
echo "import exit propagated above"
```

Expected:`NO_CS_ERRORS` 且 `NO_RESOLVE_ERRORS`,日志无 `error CS`。

- [ ] **Step 5: 降级方案(若 batchmode 解析 OpenUPM 太慢/不稳)**

OpenUPM 在 `-batchmode` 下首次解析可能慢或受网络波动。若 Step 3 超时 / 间歇性 `Token Exchange` 之类网络失败:
- **降级判定等价证明**:本工程内(Task 1–5)已确认「嵌入包 `com.yifei.easyframework` 的 asmdef 按程序集名引用、依赖经同一套 OpenUPM scopes 解析、编译 0 error + 137 测试全绿」——这**已等价证明** `package.json` 的依赖声明正确、registry scopes 完整。
- 把空工程实测**标为人工可选步骤**:在报告中记「自动 batchmode 空工程验收因网络/耗时降级为人工可选;嵌入包解析 + 编译干净已等价覆盖验收点」。
- 清理:`rm -rf "$TMP"`(无论成功与否,跑完删临时工程)。

- [ ] **Step 6: Commit**(编排者)

本 Task 不产生本仓库文件改动,**通常无 commit**。若验证过程顺手修了 README/package.json 的笔误,单独 commit 那处修正;否则在 Task 7 仅记录验收结论(写入文末 Deviations/验收记录),不空提交。

---

### Task 8: 发版准备(徽章 + OpenUPM 收录 yml + 发版步骤)

> 多为人工 / 编排者动作;**本计划内不执行真实发版**(不打 tag、不推送、不提交 OpenUPM PR)。

**Files:** Modify:`/Users/yifei/ClaudeWorkSpace/EasyFrameWork/README.md`(顶部徽章)

- [ ] **Step 1: 更新根 README 顶部徽章/状态**

`README.md` 第 5 行测试徽章当前是 `tests-145%20passing`。本工程基线已切到约 137(`Samples~/` 不编译),更新为实际数字,并加一枚 OpenUPM 徽章:

```markdown
  <img src="https://img.shields.io/badge/tests-137%20passing-2dd4bf" alt="137 tests">
  <img src="https://img.shields.io/npm/v/com.yifei.easyframework?label=openupm&registry_uri=https://package.openupm.com&color=teal" alt="OpenUPM">
```

> 把 `137` 换成 Task 4 验证代理记录的**实测框架测试数**。OpenUPM 徽章在收录构建完成前显示 "no published version",收录后自动显示版本号——这是预期,不阻塞。第 32 行「正在打包中」那句兜底文案(若 Task 6 已被安装章节替换则无需再动)。

- [ ] **Step 2: 准备 OpenUPM 收录 yml(供编排阶段用,不在本计划提交)**

OpenUPM 收录 = 向 `openupm/openupm` 仓库加一个 `data/packages/com.yifei.easyframework.yml`。内容(占位 `<owner>` 待 GitHub 仓库归属确定后替换为真实 owner):

```yaml
name: com.yifei.easyframework
displayName: EasyFramework
description: 开箱即用的 Unity 2D 移动游戏复用底座(VContainer DI 内核 + 静态门面)。
repoUrl: https://github.com/whimwindgames/EasyFramework-Unity
parentRepoUrl: null
licenseSpdxId: MIT
licenseName: MIT License
topics:
  - unity
  - game-framework
  - dependency-injection
  - mobile
readme: main:Packages/com.yifei.easyframework/README.md
hunter: whimwindgames
gitTagPrefix: v
gitTagIgnore: null
minVersion: 0.1.0
image: ""
```

> `readme` 字段指向**包内** README(OpenUPM 包详情页用它)。`gitTagPrefix: v` 对应 `v0.1.0` 标签。`packageBranch` 缺省为默认分支。

- [ ] **Step 3: 写明发版步骤(人工/编排者动作,本计划不执行)**

设计 §5 发布流程,留给编排/所有者:

1. 仓库推送到 **GitHub 公开仓库**(需所有者创建仓库/授权)。
2. 打 tag:`git tag v0.1.0 && git push origin v0.1.0`(版本号唯一来源 = `package.json`,需与之一致)。
3. 提交 OpenUPM 收录:去 `https://openupm.com/packages/add/` 填表(实质是向 `openupm/openupm` 发 PR 加 Step 2 的 yml),或用 `gh` CLI 提该 PR。
4. 收录后约几分钟至几小时 OpenUPM 完成首次构建,即可按 README 方式一/二安装。
5. 过渡期备用安装 = README「方式三 git URL」(收录后仍长期有效)。

- [ ] **Step 4: 验证**
  - `mcp__unityMCP__refresh_unity` → README 改动不影响编译,`read_console(types=["error"])` 仍 0 error。
  - 徽章数字 = Task 4 实测测试数;yml 的 `name` / `licenseSpdxId` / `gitTagPrefix` 与 `package.json`、`LICENSE.md`、版本 `0.1.0` 一致。

- [ ] **Step 5: Commit**(编排者)

```bash
git add README.md
git commit -m "docs: update README badges for UPM packaging (tests baseline + OpenUPM)"
```

---

## Self-Review

### 1. 规格覆盖(逐项核对设计文档)

- **§1 目标/验收**「单一标准 UPM 包,全部依赖 package.json 声明,OpenUPM 解析;全新工程按 README 装齐编译干净」→ Task 2(package.json 全依赖)+ Task 6(README 两路径)+ Task 7(空工程 file: + OpenUPM 实测)。✓
- **§2 包结构** Core/Services/Monetization/Boot/DevTools + Tests + README + CHANGELOG + LICENSE + Samples~/{Template,TapRush} → Task 3(五程序集 + Tests + README git mv)+ Task 2(CHANGELOG/LICENSE/package.json samples 声明)+ Task 4(两 Sample 入 Samples~)。`Editor/`(BootSceneCreator/SampleSyncTool)按 §2 列出但当前工程无该程序集,标为新增:`Create Boot Scene` 未单列(Boot 场景已由 Task 4 直接落地基础 RootLifetimeScope,空工程可手动建);`Sync Sample` 在 Task 5 Step 3 标低优先可省。✓(偏差已显式说明)
- **§3 依赖解析**「package.json 声明 + 消费项目配 OpenUPM scopes 覆盖 com.yifei/com.cysharp/jp.hadashikick/com.kyrylokuzyk/com.yasirkula;openupm-cli 与手动两路径」→ Task 6 README 配置块 scopes 五项齐全。✓
- **§4 项目级资产**「Boot 场景不进包改基础 RootLifetimeScope;Assets/Game 入 Samples~ 后导入工作副本;testables;manifest 三 git URL 切 OpenUPM」→ Task 4 Step 2(场景)+ Task 5(导入 + testables)+ Task 1(切 registry)。✓
- **§5 分发工作流**「版本源 = package.json;CHANGELOG + semver tag;GitHub 公开 + OpenUPM 收录 PR + git URL 过渡」→ Task 8(yml + 发版步骤)+ Task 6(git URL 备用块)。✓
- **§6 验收标准**「本仓库迁移后 0 error + 约 137 测试 + TapRush PlayMode 冒烟;空工程 file: 实测;README 逐字一致」→ Task 4/5(137 + 冒烟)+ Task 7(空工程)+ Task 6 Step 3(逐字比对)。✓
- **§7 边界**「已有 registry 只并 scopes;已装更高版本 UPM 自解析;Unity<6000 拒装」→ Task 6 README 注释「合并进既有条目」+ package.json `unity:6000.0`。✓
- **§8 YAGNI**「不做自举安装器/外置安装器/自更新/CI 流水线」→ 计划无任何安装器或 CI Task。✓

### 2. 占位符 / TBD 检查

- `package.json`、`LICENSE.md`、`CHANGELOG.md`、manifest 切换片段、README registry 配置块**均给出完整逐字内容**,无 TBD。
- 唯一参数化处:OpenUPM yml 的 `<owner>` 与 README/package.json 的 `github.com/yifei/...` URL——GitHub 仓库归属是设计 §5「需所有者创建仓库/授权」的人工前置,**非本计划可定**,已显式标为待替换占位,非隐藏 TBD。
- OpenUPM 版本号若收录差异需调,Task 1 Step 3 给出**明确回退动作**(manage_packages 查实际版本 + 同步 package.json + 记 Deviations),非占位。

### 3. 一致性检查

- **三条硬约束贯穿全程**:git mv 保 .meta(Task 3/4 每条命令含目录 .meta)、Samples~ 不编译致测试 145→137(顶部约束 #2 + Task 4 验证 + Task 8 徽章)、com.coplaydev.unity-mcp 不进包/不切(顶部约束 #3 + Task 1 Step 1 + Task 2 版本同步提醒)——三处反复点名,一致。
- **版本一致性**:`0.1.0` 在 package.json / CHANGELOG / README dependency 行 / git URL `#v0.1.0` / yml `minVersion` / tag `v0.1.0` 全一致;OpenUPM 依赖版本在 manifest(Task 1)与 package.json(Task 2)绑定同步。
- **GUID 一致性**:Boot 场景换 scope 用实测 GUID(基础 `RootLifetimeScope` = `19ea866847422446ea6246f0a0b090bd`,被替换的 `TapRushRootLifetimeScope` = `1f533e0c...`、`TapRushGameLifetimeScope` = `3bcf48c9...`),非臆测。
- **scopes 一致性**:Task 1 manifest scopes(com.cysharp/com.kyrylokuzyk/com.yasirkula/jp.hadashikick)+ 消费项目额外 `com.yifei` = Task 6 README 五项,差异(`com.yifei`)已在 README/Self-Review 显式说明(本仓库是嵌入包不需要它)。
- **验证口径一致**:每个 Task 验证 = 编译 0 error + EditMode 套件(Task 1–3 用 145、Task 4 起用 137)全绿(+ Task 5 PlayMode 冒烟、Task 7 空工程),无硬凑红绿测试。
