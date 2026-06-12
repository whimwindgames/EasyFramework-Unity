# EasyFramework UPM 插件化设计文档

- 日期:2026-06-13
- 状态:已与所有者确认,待实现
- 前置:框架五个 Phase 已交付(145 测试全绿,TapRush PlayMode 验收通过)

## 1. 目标与需求

把 EasyFramework 从"本仓库的 Assets 目录"重构为**开箱即用的 Unity 插件**。

| 维度 | 决定 |
|---|---|
| 分发目标 | **现在就公开**:GitHub 公开仓库 + 提交 OpenUPM(用户最终确认,取代早先"先朋友后公开"的过渡方案) |
| 形态 | **单一标准 UPM 包**,全部依赖(含第三方)在 package.json 声明,由 OpenUPM registry 解析 |
| 包名 | `com.yifei.easyframework`,displayName "EasyFramework",版本 0.1.0 起 |
| 验收标准 | 全新 Unity 6 项目:按 README 安装章节操作(openupm-cli 一条命令,或手动粘贴一段 manifest 配置)→ 依赖自动装齐 → 编译干净可用 |

**明确排除的方案**(讨论后否决):全家桶 .unitypackage(无法声明依赖、第三方源码再分发有许可证问题、升级地狱);模板仓库(不是插件形态);**内置依赖自举安装器**(早先方案 A′——选定 OpenUPM 公开发布后,依赖解析由 registry 原生完成,安装器失去存在必要,砍掉)。

## 2. 包结构

```
Packages/com.yifei.easyframework/
├── package.json                  ← 身份/版本/官方源依赖/Samples 声明
├── README.md                     ← 原 Assets/EasyFramework/README.md 迁移并更新安装章节
├── CHANGELOG.md                  ← 新增,0.1.0 起记录
├── LICENSE.md                    ← MIT(仅覆盖框架自身代码;第三方全部为外部依赖,不随包分发)
├── Core/ Services/ Monetization/ Boot/ DevTools/
│                                 ← 五个 asmdef 及全部源码原样 git mv,程序集名不变
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
- dependencies **声明全部依赖**(版本与当前工程验证版本一致):
  - 官方源:com.unity.addressables@3.1.0、com.unity.cinemachine@3.1.7、com.unity.inputsystem@1.19.0、com.unity.ugui@2.0.0、com.unity.nuget.newtonsoft-json@3.2.1、com.unity.purchasing@5.3.1
  - OpenUPM:com.cysharp.unitask@2.5.10、jp.hadashikick.vcontainer@1.16.9、com.cysharp.messagepipe@1.8.1、com.cysharp.messagepipe.vcontainer@1.8.1、com.kyrylokuzyk.primetween@1.3.3、com.yasirkula.ingamedebugconsole@1.8.7(实现阶段先在 OpenUPM 核实各包的实际收录版本号,以可解析为准)
- `"samples"` 数组声明 Template 与 TapRush 两个 Sample(displayName/description/path)

### 迁移规则

1. **git mv 迁移**,全部 .meta 跟随 → GUID 不变,既有场景/prefab/asmdef 引用不断。
2. asmdef 程序集名全部保持不变(EasyFramework.Core 等),消费项目代码引用无感。
3. TapRush **完全自包含**:含专属启动场景(用 TapRushRootLifetimeScope)、自有 asmdef、自有测试;导入 Sample 即玩。
4. Template 的 TemplateGameFlowTests 跟随 Template Sample 走(Samples~ 不参与编译,框架测试套件不再包含它们)。

## 3. 依赖解析方案(OpenUPM 原生)

不做任何自定义安装器。依赖解析完全交给 UPM + OpenUPM registry:

1. 框架的全部依赖在 package.json 声明(见 §2);消费项目只要配置了 OpenUPM scoped registry 且 scopes 覆盖 `com.yifei`、`com.cysharp`、`jp.hadashikick`、`com.kyrylokuzyk`、`com.yasirkula`,UPM 会自动解析整棵依赖树。
2. README 安装章节提供两条等价路径:
   - **openupm-cli**(推荐):`openupm add com.yifei.easyframework`——CLI 自动配置 registry、scopes 和包,真正一条命令。
   - **手动**:复制 README 提供的完整 `scopedRegistries` 配置块粘贴进 manifest.json,再在 Package Manager 里按名安装(或同样粘贴 dependency 行)。配置块由我们写好,用户只做一次复制粘贴,无抄错空间。
3. 配置好 registry 后,包会出现在 Package Manager 的 "My Registries" 分区,升级/降级走 Unity 原生 UI。

## 4. 项目级资产处理

- **Boot 场景不随包分发**(场景属于项目层):`EasyFramework/Create Boot Scene` 菜单一键生成——新建场景、创建 `[EasyFramework]` 节点挂 `RootLifetimeScope`、保存到 Assets/Scenes/Boot.unity、加入 Build Settings 第 0 位。
- 本仓库自身改造:`Assets/EasyFramework` 整体迁出;`Assets/Scenes/Boot.unity` 改用基础 `RootLifetimeScope`(TapRushRootLifetimeScope 随 Sample 走);`Assets/Game` 迁入 Samples~ 后,在本仓库**导入 TapRush Sample**(Assets/Samples/EasyFramework/0.1.0/TapRush/)作为开发工作副本,修改经 `Dev/Sync Sample to Package` 菜单同步回 Samples~。
- 本仓库 manifest.json 增加 `"testables": ["com.yifei.easyframework"]`,框架测试照常在 Test Runner 运行。
- **本仓库依赖源统一(吃自己的狗粮)**:manifest.json 中 UniTask/VContainer/MessagePipe 三个 git URL 依赖改为 OpenUPM registry 依赖(scopes 补 `com.cysharp`、`jp.hadashikick`),与 package.json 声明一致,确保我们自己每天走的就是用户的解析路径。

## 5. 分发与版本工作流

- 版本号唯一来源 = package.json;每次发版更新 CHANGELOG.md 并打 semver git tag(`v0.1.0` 起,OpenUPM 按 tag 自动构建新版本)。
- **发布步骤**(实现计划内,部分需所有者参与):
  1. 仓库推送到 **GitHub 公开仓库**(需要所有者创建仓库/授权,人工步骤)。
  2. 打 tag `v0.1.0`。
  3. 向 OpenUPM 提交收录(openupm.com "Add Package" 表单,实质是向 openupm/openupm 仓库发一个添加 `data/packages/com.yifei.easyframework.yml` 的 PR;我们准备好 yml 内容,提交动作可由 gh CLI 完成或所有者在网页上点)。
  4. 收录后约几分钟至几小时内 OpenUPM 完成首次构建,即可按 §3 安装。
- 过渡期(等待 OpenUPM 收录期间)备用安装方式:git URL `https://github.com/<owner>/EasyFrameWork.git?path=Packages/com.yifei.easyframework#v0.1.0` + README 的 registry 配置块(依赖仍由 OpenUPM 解析)——此方式收录后依然长期有效。

## 6. 验收标准

1. 本仓库迁移后:编译 0 错误,框架 EditMode 测试全绿(原 145 减去随 Sample 迁走的,约 137 个),导入的 TapRush 工作副本 PlayMode 冒烟通过。
2. **全新空白 Unity 6 工程实测**(用 Unity CLI 创建临时工程):按 README"手动路径"操作——粘贴 registry 配置块 + 以 `file:` 本地路径替代包名安装(模拟 OpenUPM 收录前状态,依赖仍从 OpenUPM 真实解析)→ 依赖自动装齐 → 编译 0 错误 → `Create Boot Scene` 生成可用 → 导入 TapRush Sample 编译通过。
3. README 安装章节(openupm-cli 与手动两条路径)与真实流程逐字一致;registry 配置块复制即用。
4. GitHub 公开仓库 + tag v0.1.0 + OpenUPM 收录 PR 提交(收录构建完成与否不阻塞本计划验收,属外部异步流程)。

## 7. 错误处理与边界

- 消费项目已有 OpenUPM registry(任意 name)→ README 指引"只需把缺的 scopes 合并进既有条目",配置块注释说明。
- 消费项目已装更高版本的某依赖 → UPM 原生按最高版本解析,无需处理;明显不兼容时由 CHANGELOG 标注已验证版本组合。
- Unity 版本低于 6000.0 → Package Manager 原生拒装并提示版本要求(package.json `"unity"` 字段负责)。

## 8. YAGNI 裁剪

- 不做依赖自举安装器(OpenUPM 公开发布后无必要,已从早期方案中移除)。
- 不做外置 .unitypackage 安装器。
- 不做自动版本检查/自更新提示。
- 不做 CI 自动发版流水线(发版频率低,手动 tag 足够;将来需要再加 GitHub Actions)。
