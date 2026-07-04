# EasyFramework 打磨清单设计文档

- 日期:2026-07-05
- 状态:待所有者确认
- 前置:MyFramework 架构对比研究(与所有者对话中产出的对比 Artifact)

## 1. 目标与背景

从 MyFramework 对比研究中筛选出的低成本改进项,针对现有服务做独立的健壮性/体验补强,不新增子系统。

写这份文档前重新核对了一遍现有实现,发现对比研究里原本列入"可借鉴"的两项其实已经实现,已从范围内删除:

- ~~返回键统一路由到栈顶面板~~——`UIService.HandleBack()` 已经实现了 popup 优先、栈顶 Window 兜底的完整路由逻辑,`UIPanel.OnBackRequested()` / `UIPopup<TResult>` 默认拦截都已就位。
- ~~本地化运行时切换批量刷新~~——`LocalizedText` 组件已经订阅 `LocaleChangedEvent`,`SetLocaleAsync` 触发后自动刷新所有已启用的文本,效果上已经是"切语言不用重开界面",只是实现方式是"每个文本自己订阅事件"而非 MyFramework 的中心化注册表,效果等价。

实际要做的是下面 5 项,互相独立,不新增公开接口(除 2.4 的编辑器工具)。

## 2. 改动清单

### 2.1 IEventBus 重入安全验证 + 递归深度保护(Core/Events)

**现状**:`MessagePipeEventBus` 是 MessagePipe 库的薄封装,`Publish<T>` 的实际分发循环发生在 MessagePipe 内部,不在 EasyFramework 代码里。MyFramework "分发前快照订阅者列表"的具体做法不适用——我们没有自己维护订阅者列表可以快照。

**动作**:
1. 先写一个回归测试:验证"事件处理函数内部对同一事件类型再 `Subscribe` 或 `Dispose` 自己的订阅"不会导致异常或漏发。这是验证 MessagePipe 是否已经安全处理重入,而不是预先假设它有问题。如果测试证明现状安全,这一项到此为止,不加生产代码,只留下这个测试做回归保护。
2. 递归深度保护独立于 MessagePipe 内部实现:在 `MessagePipeEventBus.Publish<T>` 这层包装本身加一个深度计数器,超过阈值(默认 20,可调)时 `Debug.LogError` 并跳过本次发布,防止"事件 A 触发事件 B 又触发事件 A"的死循环拖垮主循环。改动范围仅这一个文件。

### 2.2 GameBootstrap 逐任务耗时日志(Core/Boot)

**现状**:`GameBootstrap.RunSafe` 只在非关键任务失败时 `LogWarning`,没有计时。

**动作**:给每个 `IBootTask.InitializeAsync` 包一层耗时测量,仅在开发模式下(`Debug.isDebugBuild || Application.isEditor`)、超过阈值(默认 50ms,可调)时打印任务类型名 + 耗时,用于快速定位启动变慢的元凶。Release 包不受影响。

### 2.3 PoolService 闲置自动缩容(Services/Pooling)

**现状**:`_idle: Dictionary<string, Stack<GameObject>>` 只在场景卸载(`SceneWillUnloadEvent`)时整体清空,平时只增不减。

**动作**:
- 保留 `Stack<GameObject>` 做实际存取(LIFO 复用最近释放的实例,内存局部性更好),额外维护一个并行的 `Dictionary<GameObject, float> _idleSince` 记录每个实例进入闲置状态的时间戳;`Despawn` 时写入,`SpawnAsync` 命中缓存复用时移除对应记录。
- 复用现有 `ITimerService.Schedule(delay, callback, repeat:true)`(不新起 Update 循环)按固定周期(默认 10 秒,可调)扫描 `_idleSince`,超过可配置的 `idleTimeoutSeconds` 的实例销毁并从 `_idle`/`_idleSince` 移除。
- 默认关闭(不配置 `idleTimeoutSeconds`,或设为 0/null,行为与现在完全一致),可按 key 选择性开启,不影响现有调用方。

### 2.4 Localization 编辑器漏翻译检测(Services/Localization)

**现状**:`TableLocalizationService.Get(key)` 找不到 key 时 `LogWarning` 并返回 key 本身——这是运行时检测,只有实际打开对应界面才会触发,没法在打包前批量发现遗漏。

**动作**:新增一个仅编辑器可用的批量校验入口(菜单 `EasyFramework/校验本地化表`,或 `LocalizationTable` 的自定义 Inspector 按钮),遍历所有 `LocalizationTable` 资产,对每个已配置的 Locale 检查是否所有 key 都有对应 value,缺失项汇总打印(表名 + key + 缺失的 locale)。不改动运行时 `ILocalizationService` 接口。

### 2.5 AudioService 同音效并发限流(Services/Audio)

**现状**:`PlaySfx` 用 8 个 `AudioSource` 轮询调用 `PlayOneShot`。`PlayOneShot` 本身允许在同一个 `AudioSource` 上叠加多个不截断的一次性播放,所以并不存在 MyFramework 那种"实例被截断"的问题;真正的风险是同一个 `key` 被高频触发(比如连击音效)时,短时间内在多个 `AudioSource` 上叠加大量相同音效,造成音量堆叠/削波,而不是播放被打断。

**动作**(MyFramework 的"淘汰剩余时间最短实例"是给它按实例管理的 AudioSource 池设计的,直接照搬到 `PlayOneShot` 架构上没有"实例"可淘汰,改用适配现有架构的等价方案):按 `key` 记录上次播放时间,`PlaySfx` 内部对同一个 `key` 加一个可配置的最小播放间隔(默认 30ms),间隔内的重复调用直接跳过,避免同一音效瞬时堆叠导致音量溢出。

## 3. 验收标准

- 5 项改动各自独立可测,EditMode 单测覆盖:
  - EventBus 重入回归测试 + 深度保护越界测试
  - GameBootstrap 耗时日志(通过可注入的计时抽象或验证日志调用触发)
  - PoolService 超时销毁(需要可控的虚拟时间源,实现计划阶段确认现有测试里时间相关用例的既有手法)
  - Localization 校验工具(编辑器脚本测试或手动验证)
  - AudioService 限流(可注入时间源,验证同 key 间隔内跳过、间隔外正常播放)
- 不破坏现有 137 个测试,不改变任何现有公开接口签名(全部是内部实现增强,或新增独立的编辑器工具/可选参数)。

## 4. YAGNI 裁剪

- 不引入 MyFramework 式的编译期 Roslyn Analyzer 强制检查对象池字段重置——现有 `IPoolable.OnDespawn` 契约靠注释 + code review 足够。
- 不对 EventBus 做类型 ID 化(int key)优化——.NET 泛型静态字段本身已按类型分片、不经反射,不存在"Type 当 key 较慢"的顾虑。
- 不做本地化的中心化注册表重构——现有事件订阅式的 `LocalizedText` 已达到同等效果,重构没有收益。
