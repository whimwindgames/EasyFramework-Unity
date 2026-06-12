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
3. 若要替换存档:新建一个 `<YourGame>RootLifetimeScope` 继承 `RootLifetimeScope`,override `protected virtual SaveProfile GetSaveProfile()` 返回你游戏的 `SaveProfile`(参见 `TemplateGameLifetimeScope.CreateSaveProfile()` 示例)。然后把 Boot 场景 `[EasyFramework]` 节点上的 `RootLifetimeScope` 组件替换为你的 `<YourGame>RootLifetimeScope`。框架通过该 seam 决定 `G.Save.Data<T>()` 的载荷类型。注:`SaveProfile` 是普通 C# 类(非 ScriptableObject),无法通过 Inspector 字段传入。
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
