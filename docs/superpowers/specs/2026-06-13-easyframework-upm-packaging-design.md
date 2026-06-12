# EasyFramework UPM 插件化设计文档

- 日期:2026-06-13
- 状态:已与所有者确认,待实现
- 前置:框架五个 Phase 已交付(145 测试全绿,TapRush PlayMode 验收通过)

## 1. 目标与需求

把 EasyFramework 从"本仓库的 Assets 目录"重构为**开箱即用的 Unity 插件**。

| 维度 | 决定 |
|---|---|
| 分发目标 | 现阶段给朋友/小团队傻瓜化安装;保留升级公开发布(GitHub/OpenUPM)的路径 |
| 形态 | **单一 UPM 包 + 内置依赖自举安装器**(方案 A′,用户确认) |
| 包名 | `com.yifei.easyframework`,displayName "EasyFramework",版本 0.1.0 起 |
| 验收标准 | 朋友在全新 Unity 6 项目里:粘贴一个 git URL → 弹窗点一次"安装依赖" → 编译干净可用,全程零手改文件 |

**明确排除的方案**(讨论后否决):全家桶 .unitypackage(无法声明依赖、第三方源码再分发有许可证问题、升级地狱);模板仓库(不是插件形态)。

## 2. 包结构

```
Packages/com.yifei.easyframework/
├── package.json                  ← 身份/版本/官方源依赖/Samples 声明
├── README.md                     ← 原 Assets/EasyFramework/README.md 迁移并更新安装章节
├── CHANGELOG.md                  ← 新增,0.1.0 起记录
├── LICENSE.md                    ← MIT(仅覆盖框架自身代码;第三方全部为外部依赖,不随包分发)
├── Core/ Services/ Monetization/ Boot/ DevTools/
│                                 ← 五个 asmdef 及全部源码原样 git mv,程序集名不变
├── Installer/
│   ├── EasyFramework.Installer.asmdef   ← Editor-only、零引用(自举关键)
│   ├── DependencyInstaller.cs           ← 自检/弹窗/改写 manifest/Resolve
│   └── MiniJson.cs                      ← 内嵌 JSON 解析(不依赖 Newtonsoft)
├── Editor/
│   ├── EasyFramework.Editor.asmdef      ← 引用 Boot/Core
│   ├── BootSceneCreator.cs              ← 菜单 EasyFramework/Create Boot Scene
│   └── SampleSyncTool.cs                ← 菜单 EasyFramework/Dev/Sync Sample to Package(开发用)
├── Tests/EditMode/               ← 框架 EditMode 测试原样迁移(不含 TapRush/Template 的测试)
└── Samples~/
    ├── Template/                 ← 原 Assets/EasyFramework/_Template(含 asmdef、README、TemplateGameFlowTests)
    └── TapRush/                  ← 原 Assets/Game 全部 + GeneratedAssets + 专属 Boot 场景 + 游戏测试
```

### package.json 要点

- `"unity": "6000.0"`
- dependencies **只含官方源包**:com.unity.addressables@3.1.0、com.unity.cinemachine@3.1.7、com.unity.inputsystem@1.19.0、com.unity.ugui@2.0.0、com.unity.nuget.newtonsoft-json@3.2.1、com.unity.purchasing@5.3.1(版本与当前工程一致)
- `"samples"` 数组声明 Template 与 TapRush 两个 Sample(displayName/description/path)

### 迁移规则

1. **git mv 迁移**,全部 .meta 跟随 → GUID 不变,既有场景/prefab/asmdef 引用不断。
2. asmdef 程序集名全部保持不变(EasyFramework.Core 等),消费项目代码引用无感。
3. TapRush **完全自包含**:含专属启动场景(用 TapRushRootLifetimeScope)、自有 asmdef、自有测试;导入 Sample 即玩。
4. Template 的 TemplateGameFlowTests 跟随 Template Sample 走(Samples~ 不参与编译,框架测试套件不再包含它们)。

## 3. 依赖自举安装器

**原理**:Unity 按 asmdef 独立编译,零引用的 Installer 程序集在框架主体因缺第三方依赖而报错时依然正常工作。

行为规格:

1. `[InitializeOnLoad]` + `EditorApplication.delayCall` 自检 `Packages/manifest.json`:
   - scopedRegistries 是否含 OpenUPM(url https://package.openupm.com)且 scopes 覆盖:`com.cysharp`、`jp.hadashikick`、`com.kyrylokuzyk`、`com.yasirkula`
   - dependencies 是否含五个第三方包(版本钉死为当前验证版本):com.cysharp.unitask@2.5.10、jp.hadashikick.vcontainer@1.16.9、com.cysharp.messagepipe@1.8.1、com.cysharp.messagepipe.vcontainer@1.8.1、com.kyrylokuzyk.primetween@1.3.3、com.yasirkula.ingamedebugconsole@1.8.7
2. 有缺失 → `EditorUtility.DisplayDialog` 列出将做的修改,**文案注明"安装期间 Console 短暂报红属正常现象"**;确认 → 改写 manifest.json(已有 OpenUPM registry 则只合并 scopes,不重复添加)→ `Client.Resolve()`。
3. 实现优先探测 `UnityEditor.PackageManager` 是否提供 scoped registry 官方 API(unity_reflect 核对),没有则用内嵌 MiniJson 直改文件。
4. 防骚扰:用户选"跳过"记 EditorPrefs(按项目)不再自动弹;`SessionState` 防同一会话重复弹;手动入口菜单 `EasyFramework/Install Dependencies` 始终可用。
5. 安装器自身不依赖任何第三方程序集(含 Newtonsoft)。

## 4. 项目级资产处理

- **Boot 场景不随包分发**(场景属于项目层):`EasyFramework/Create Boot Scene` 菜单一键生成——新建场景、创建 `[EasyFramework]` 节点挂 `RootLifetimeScope`、保存到 Assets/Scenes/Boot.unity、加入 Build Settings 第 0 位。
- 本仓库自身改造:`Assets/EasyFramework` 整体迁出;`Assets/Scenes/Boot.unity` 改用基础 `RootLifetimeScope`(TapRushRootLifetimeScope 随 Sample 走);`Assets/Game` 迁入 Samples~ 后,在本仓库**导入 TapRush Sample**(Assets/Samples/EasyFramework/0.1.0/TapRush/)作为开发工作副本,修改经 `Dev/Sync Sample to Package` 菜单同步回 Samples~。
- 本仓库 manifest.json 增加 `"testables": ["com.yifei.easyframework"]`,框架测试照常在 Test Runner 运行。

## 5. 分发与版本工作流

- 版本号唯一来源 = package.json;每次发版更新 CHANGELOG.md 并打 git tag(v0.1.0 起)。
- 安装 URL:`https://github.com/<owner>/EasyFrameWork.git?path=Packages/com.yifei.easyframework#v0.1.0`。
- **前提**:仓库推送到 GitHub(私有可用 + 朋友配 token;公开最省事)——需要所有者提供仓库地址,此为实现计划中的人工步骤。
- 升级公开发布:结构已是 OpenUPM 标准形态,届时打 tag 提交 OpenUPM 即可,零返工。

## 6. 验收标准

1. 本仓库迁移后:编译 0 错误,框架 EditMode 测试全绿(原 145 减去随 Sample 迁走的,约 137 个),导入的 TapRush 工作副本 PlayMode 冒烟通过。
2. **全新空白 Unity 6 工程实测**(用 Unity CLI 创建临时工程):通过本地路径模拟 git URL 安装(`"com.yifei.easyframework": "file:..."`)→ 安装器弹出(批处理模式下走静默 API 路径)→ 依赖装齐 → 编译 0 错误 → `Create Boot Scene` 生成可用 → 导入 TapRush Sample 编译通过。
3. README 安装章节与真实流程逐字一致。

## 7. 错误处理与边界

- manifest.json 已有 OpenUPM registry(任意 name)→ 合并 scopes 不重复;已有更高版本依赖 → 不降级,跳过并提示。
- manifest.json 解析失败 → 弹窗给出手工安装指引(README 安装章节链接),不写坏文件(改写前备份 manifest.json.backup)。
- 批处理/CI 环境(`Application.isBatchMode`)→ 不弹窗,直接静默安装并打日志。

## 8. YAGNI 裁剪

- 不做外置 .unitypackage 安装器(单包自举已覆盖)。
- 不做自动版本检查/自更新提示。
- 不本期提交 OpenUPM(留作公开发布时的一步)。
