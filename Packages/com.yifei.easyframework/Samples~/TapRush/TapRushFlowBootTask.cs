using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;
using EasyFramework.Core.Events;
using UnityEngine;
using VContainer.Unity;

namespace Game
{
    /// <summary>
    /// 监听 BootCompletedEvent 进入 Menu,并每帧驱动 TapRushFlow.Tick。
    /// 经子作用域 GameLifetimeScope 经 RegisterEntryPoint 注册:作为 IInitializable
    /// (订阅 BootCompleted,由子作用域的入口点 dispatcher 调用——框架 GameBootstrap 只收集
    /// 根作用域的 IBootTask,看不到子作用域的 IBootTask,故子作用域玩法 BootTask 自行经
    /// IInitializable 入口点驱动)、ITickable(帧驱动)。同时保留 IBootTask 契约。
    /// </summary>
    public sealed class TapRushFlowBootTask : IBootTask, IInitializable, ITickable, IDisposable
    {
        readonly IEventBus _events;
        readonly TapRushFlow _flow;
        IDisposable _sub;

        public int Priority => 50;        // 晚于框架服务(Save=0/Config=10/Ads=30),早于 DevTools(90)
        public bool IsCritical => false;

        public TapRushFlowBootTask(IEventBus events, TapRushFlow flow)
        {
            _events = events;
            _flow = flow;
        }

        // VContainer 入口点:子作用域 build 后调用,订阅 BootCompleted 进 Menu。
        public void Initialize() => Subscribe();

        public UniTask InitializeAsync(CancellationToken ct)
        {
            // IBootTask 路径(若被某 bootstrap 收集时)。幂等。
            Subscribe();
            return UniTask.CompletedTask;
        }

        void Subscribe()
        {
            if (_sub != null) return;
            // BootCompleted 后进 Menu(此刻 G 已 Initialize、存档已加载)。
            _sub = _events.Subscribe<BootCompletedEvent>(_ => _flow.ToMenu().Forget());
        }

        public void Tick()
        {
            _flow.Tick(Time.deltaTime);
        }

        public void Dispose() => _sub?.Dispose();
    }
}
