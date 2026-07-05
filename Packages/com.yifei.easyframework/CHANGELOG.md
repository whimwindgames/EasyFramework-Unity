# Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

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
