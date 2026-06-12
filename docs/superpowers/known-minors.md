# 已知 Minor 问题清单(评审记录,Phase 5 收尾统一处理)

## Phase 1
- StateMachine 版本守卫在同步 Enter 下不生效(异步 Enter 场景才有意义,保留)
- GameBootstrap 启动时 LINQ 一次性分配(仅启动一次,可接受)
- TimerService.Cancel O(n) 扫描;TimerHandle.IsValid 语义需澄清
- 公共类型 XML 文档注释普遍缺失

## Phase 2
- AddressablesAssetService.Entry.RefCount 死字段(ReleaseScope 忽略计数直接整组释放)
- PoolService.SpawnAsync 用 rotation == default 判默认值,default(Quaternion) 是非法旋转
- JsonSaveService 原子写未 Flush/fsync,极端断电可能空文件
- SaveOnPauseListener 未挂 OnApplicationQuit(编辑器停播/桌面端 Alt+F4 不落盘)
- Phase 2 计划文档内部测试数量描述不一致(文档问题)

## Phase 3
- RootLifetimeScope:Audio/Input 走显式 Ticker 桥接而 UIService 直接实现 ITickable,风格不对称
- NoopSceneTransition 定义仍在 ISceneService.cs,Fade 替换后建议挪文件
- AudioService/HapticsService/TableLocalizationService 构造函数读 PlayerPrefs(轻量可接受)
- UIService.Dispose 中 PopAllAsync().Forget() 真异步退场时与同步 Destroy 有顺序隐患
- InputService.IsPointerOverUI 无参重载在多点触控下只对最后指针有效
- CinemachineCameraService.Shake 的 duration 参数被丢弃(由 Impulse 包络承载)
- LocalizedText OnEnable 早于 Localization 初始化时有竞态(已有 IsInitialized 守卫,首帧文本延迟)
- JuiceService.HitStopAsync 用 0.01s timer 轮询重排恢复(可改精确重排)
