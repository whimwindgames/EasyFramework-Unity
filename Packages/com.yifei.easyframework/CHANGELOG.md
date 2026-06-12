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
