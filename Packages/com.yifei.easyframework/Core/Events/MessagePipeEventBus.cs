using System;
using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace EasyFramework.Core.Events
{
    /// <summary>
    /// MessagePipe 驱动的事件总线实现。
    ///
    /// 线程安全约定(Thread-Safety Contract):
    ///   此类 <b>仅供 Unity 主线程调用</b>。
    ///   _publishers / _subscribers 的懒填充不是线程安全的;若从 SDK 回调线程(广告 / IAP 等)
    ///   调用 Publish/Subscribe,必须先 Dispatcher.InvokeOnMainThread(或 UniTask.SwitchToMainThread)
    ///   切换回主线程。调用方违反此约定可能触发 Dictionary 并发异常或脏读。
    ///   如需跨线程发布,建议在业务层封装一个线程安全的转发队列,不在此处引入锁开销。
    ///
    /// 递归深度保护(Reentrancy Guard):
    ///   Publish&lt;T&gt; 内部触发的事件处理函数如果又同步 Publish 了同一个或另一个事件类型,
    ///   层层嵌套可能形成"事件 A 触发事件 B 又触发事件 A"的死循环拖垮主循环。
    ///   这里用一个跨类型共享的调用深度计数器,超过 <see cref="MaxPublishDepth"/> 时记一条错误日志并
    ///   跳过本次发布(不再往下调用 MessagePipe 的 Publish),避免栈溢出/卡死。
    ///   深度计数器不区分事件类型:无论是同类型自递归还是不同类型互相触发,只要嵌套调用链变长都会被拦截。
    /// </summary>
    public sealed class MessagePipeEventBus : IEventBus
    {
        /// <summary>Publish 嵌套调用深度阈值,超过后跳过发布并记录错误。可在业务层通过反射或子类化调整;默认 20 足够覆盖正常的链式事件场景。</summary>
        public const int MaxPublishDepth = 20;

        readonly IObjectResolver _resolver;
        // 仅主线程读写;不使用并发集合以避免移动端 GC 开销
        readonly Dictionary<Type, object> _publishers = new();
        readonly Dictionary<Type, object> _subscribers = new();

        int _publishDepth;

        public MessagePipeEventBus(IObjectResolver resolver) => _resolver = resolver;

        public void Publish<T>(T evt)
        {
            if (_publishDepth >= MaxPublishDepth)
            {
                Debug.LogError(
                    $"[EasyFramework] IEventBus.Publish<{typeof(T).Name}> exceeded max reentrancy depth " +
                    $"({MaxPublishDepth}). Skipping this publish to avoid a runaway event loop. " +
                    "Check for events that trigger each other in a cycle.");
                return;
            }

            _publishDepth++;
            try
            {
                if (!_publishers.TryGetValue(typeof(T), out var pub))
                {
                    pub = _resolver.Resolve<IPublisher<T>>();
                    _publishers[typeof(T)] = pub;
                }
                ((IPublisher<T>)pub).Publish(evt);
            }
            finally
            {
                _publishDepth--;
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var sub))
            {
                sub = _resolver.Resolve<ISubscriber<T>>();
                _subscribers[typeof(T)] = sub;
            }
            return ((ISubscriber<T>)sub).Subscribe(handler);
        }
    }
}
