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
| `TemplateRootLifetimeScope` | 类名改 `<YourGame>RootLifetimeScope` |
| `TemplateGameLifetimeScope` | 类名改 `<YourGame>GameLifetimeScope` |
| `TemplateGameFlow` | 类名改 `<YourGame>Flow`(及 `StateMachine<TemplateGameFlow>` 泛型参数) |
| `TemplateSaveData` | 类名改 `<YourGame>SaveData`,字段换成你的存档数据 |
| `CreateSaveProfile()` 里的 `HmacSalt` | 改成你游戏专属盐 |

## 三、接进框架(3 分钟)

1. 打开 `Assets/Scenes/Boot.unity`。
2. 在 `[EasyFramework]` 上挂复制改名后的 `<YourGame>RootLifetimeScope`;它已接好游戏存档,
   也是广告、IAP 验签、统计、远程配置等根适配器的唯一配置位置。
3. 在其下新建子 GameObject,挂 `<YourGame>GameLifetimeScope`(嵌套 LifetimeScope,
   自动成为 Root 的子作用域),只注册游戏玩法与可选模块。不要在这里覆盖根服务 provider。
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

## 六、联机(可选模块,按需接入)

`IMultiplayerService`/`IMultiplayerProvider` 不在 `FrameworkInstaller` 里注册,也不会出现在 `G` 门面上——这是框架里第一个"可选模块":大多数小游戏不需要联机,`G` 的契约是"`BootCompletedEvent` 后一定可用",给不需要联机的游戏也挂一个可能是 null 的 `G.Multiplayer` 会破坏这个契约。需要联机的游戏按以下步骤自己接线:

1. 实现游戏专属的 `IMultiplayerProvider`(比如接 Photon 就叫 `PhotonMultiplayerProvider`,内部调用 Photon SDK;走异步回合制就在内部用 `G.Http` 轮询自选后端)。
2. 在你的 `<YourGame>LifetimeScope.ConfigureGame(IContainerBuilder builder)` 里追加两行注册(参照 `TemplateGameLifetimeScope.ConfigureGame` 里现有的注册写法风格):

   ```csharp
   builder.Register<IMultiplayerProvider, PhotonMultiplayerProvider>(VContainer.Lifetime.Singleton);
   builder.Register<EasyFramework.Services.Multiplayer.MultiplayerService>(VContainer.Lifetime.Singleton)
       .As<EasyFramework.Services.Multiplayer.IMultiplayerService>();
   ```

3. 业务代码走**构造函数注入** `IMultiplayerService`,不经过 `G`:

   ```csharp
   public sealed class MatchController
   {
       readonly EasyFramework.Services.Multiplayer.IMultiplayerService _multiplayer;
       public MatchController(EasyFramework.Services.Multiplayer.IMultiplayerService multiplayer)
           => _multiplayer = multiplayer;

       public async Cysharp.Threading.Tasks.UniTask JoinAsync(string sessionId)
           => await _multiplayer.ConnectAsync(sessionId);
   }
   ```

4. 订阅连接状态/收消息统一走事件总线,不用关心底层是 Photon 回调还是 HTTP 轮询在触发:

   ```csharp
   G.Events.Subscribe<EasyFramework.Services.Multiplayer.ConnectionStateChangedEvent>(evt =>
       Debug.Log($"Connection state: {evt.State}"));
   G.Events.Subscribe<EasyFramework.Services.Multiplayer.MultiplayerMessageReceivedEvent>(evt =>
       Debug.Log($"Received on {evt.Channel}: {evt.Payload.Length} bytes"));
   ```

5. 编辑器/单测下不想连真实后端,注册框架自带的 `EasyFramework.Services.Multiplayer.FakeMultiplayerProvider` 代替真实 Provider——它是本地回环:`SendAsync` 会直接原地触发一个 `MultiplayerMessageReceivedEvent`,模拟"自己发的消息自己收到",足够跑通接入联机后的业务流程联调。

同步/异步(实时 vs 回合制)两种模式在契约层面是同一套接口,差异完全在于你注册的 `IMultiplayerProvider` 内部怎么实现——框架不区分这两种模式。
