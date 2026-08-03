# Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.3.0] - 2026-08-03

### Added

- 新增 `FrameworkFeatures` 与 `FrameworkFeatureSets.ExistingProject`，项目可按需启用资源、场景、存档、UI、输入、相机、对象池及商业化模块。
- 新增资源、UI、输入、相机和对象池外部实现注册点，成熟项目可以保留既有运行时系统并渐进迁移到 VContainer。
- 新增 `UIRootProfile`：支持任意参考分辨率、横屏/竖屏预设、宽高缩放权重、Overlay/Camera/WorldSpace Canvas、独立层级排序、安全区和 EventSystem 策略。
- 新增 `IUIRootFactory` 与带所有权声明的 `UIRootHandle`，支持复用项目现有 Canvas、URP Overlay Camera 或自定义 UI 根结构。
- HUD 改为按类型缓存，允许多个不同 HUD 同时显示，并支持按类型隐藏。

### Changed

- `FrameworkInstaller` 不再强制注册全部服务；Core 始终可用，未启用模块不会被构造或绑定到 `G`。
- `G.Initialize` 对可选服务使用安全解析，关闭广告、内购、相机、输入或 UI 后仍能正常完成框架启动。
- 默认 UI 配置继续保持 v0.2.1 的 `1080×1920` 竖屏行为，已有项目升级不会被强制改变布局。
- 包定位从“仅 2D 小游戏”升级为支持 2D、2.5D 和 3D 项目的通用底座。
- Addressables、Cinemachine、UniTask 与 Newtonsoft.Json 的依赖基线对齐捕鱼项目现用版本，减少接入时的包版本重解析。

### Tests

- 新增核心最小安装、缺失模块依赖、外部输入实现、横屏 Canvas、ScreenSpaceCamera 和多 HUD 回归测试。

## [0.2.1] - 2026-07-30

### Security

- 正式包未安装广告或商店扩展时使用 `Unavailable` 实现,不会误发广告奖励或确认内购交易。
- Unity IAP 升级到 v5 API:交易保持 Pending,先写原子 journal,再验证、幂等发奖并确认;启动补单、恢复购买和重复交易共用同一链路。
- 无游戏服务端时使用显式 `ClientOnlyIAPReceiptValidator`:只接受带完整交易标识与收据的当前商店回调,拒绝旧版无收据交易;保留随时替换为服务端验签的接口。
- HTTP POST 默认禁止自动重试;只有显式开启并提供幂等键时才重试。

### Fixed

- 根服务 provider 改为在 `ConfigureFrameworkOptions` 构建前配置,修复子 LifetimeScope “覆盖注册但根服务仍使用 Fake”的问题。
- UI 创建/动画异常不再让调用方永久 pending;Window/HUD 操作串行化,销毁时取消所有等待者并释放宿主。
- 场景加载串行化;加载、取消或转场失败后会恢复淡出遮罩,不再留下黑屏和射线阻挡。
- 非关键 BootTask 的取消不再被吞掉并错误发布 `BootCompletedEvent`。
- 定时器拒绝零间隔 repeat,捕获单个回调异常并限制单帧追赶次数。
- 远程配置按完整快照替换;热更成功后清空待应用列表,避免同一 catalog 重复应用。
- Addressables 失败句柄会从缓存移除并释放,缓存键加入资源类型。
- 对象池清理被外部销毁的闲置实例,并补齐参数校验、回调失败回滚和幂等 Dispose。
- 修复音频并发 BGM/Stop 竞态、SFX 异常和持久宿主泄漏;镜头震动时长参数现在真实生效。
- 顿帧会恢复进入前的 `Time.timeScale`,重入精确延长且 `HitStopAsync` 等到恢复才完成。
- 触摸 UI 使用正确 finger id,双指缩放不再同时触发单指手势,手势阈值按 DPI 缩放。
- `LocalizedText` 在运行时晚初始化时会自动绑定;联机服务 Dispose 后正确退订 provider。
- 存档 profile、迁移推进与反序列化增加校验,损坏 payload 会备份并安全重建。

### Changed

- AppLovin MAX 迁移到独立可选包 `com.yifei.easyframework.max`。
- Unity IAP 5 商店适配迁移到独立可选包 `com.yifei.easyframework.iap`。
- 核心包不再依赖 `com.applovin.mediation.ads` 或 `com.unity.purchasing`。

### Tests

- 209 项核心 EditMode 与 3 项 PlayMode 冒烟测试;扩展包另有独立测试。
- 新增 `scripts/run-unity-tests.sh`,可用于本地或自托管 CI。

## [0.2.0] - 2026-07-05

### Added

- `IContentUpdateService`:基于 Addressables 官方 Remote Content Update 机制的资源热更新服务,启动时静默检查、`ContentAvailableEvent` 通知业务层决定是否下载,`G.ContentUpdate` 挂载。
- `IHttpService`:通用 HTTP 基础设施(GET/POST、超时、重试、JSON 序列化走 Newtonsoft),`G.Http` 挂载。
- `IMultiplayerService` / `IMultiplayerProvider`:可选联机适配层,只定义连接生命周期契约,不内置任何传输协议;不进 `FrameworkInstaller`/`G`,由需要联机的具体游戏自行注册实现。
- `IEventBus` 递归深度保护,防止事件互相触发导致的死循环拖垮主循环。
- `GameBootstrap` 逐任务耗时日志(开发模式下超阈值打印),便于定位启动变慢的具体 `IBootTask`。
- `PoolService` 支持按 key 配置闲置超时自动销毁(默认关闭,不影响现有行为)。
- 本地化编辑器批量漏翻译检测工具。
- `AudioService` 同音效并发限流,避免高频触发同一音效造成音量堆叠。

### Notes

- 明确不引入代码热更新(HybridCLR 等):经评估违反 Apple App Store Review Guideline 2.5.2,存在开发者账号级别的下架风险,风险与收益不成比例(详见 `docs/superpowers/specs/2026-07-05-content-update-service-design.md` 背景说明)。

## [0.1.0] - 2026-06-13

### Added

- 首个 UPM 包发布。EasyFramework 从仓库 `Assets/` 目录重构为单一标准 UPM 包 `com.yifei.easyframework`。
- 核心层:异步状态机、对象池、定时器、强类型事件总线、优先级启动管线(纯 C#,全单测)。
- 服务层:资源(Addressables 按场景自动释放)、场景过渡、存档(HMAC + 版本迁移 + 原子写)、远程配置、四层 UI 栈、音频、手势、相机、Juice、本地化、震动。
- 商业化层:广告(频控)、内购(恢复购买 + 掉单补发)、数据统计;编辑器 Fake 实现 + 真实 SDK `#if` 接入槽。
- 开发体验:真机调试控制台、`[Cheat]` 作弊命令、FPS/内存角标。
- 全部依赖在 `package.json` 声明,由 OpenUPM registry 解析。
- 两个 Sample:Template(新游戏空白模板)、TapRush(完整示例游戏)。
