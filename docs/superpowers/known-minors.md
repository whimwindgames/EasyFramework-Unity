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
