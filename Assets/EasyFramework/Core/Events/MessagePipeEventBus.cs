using System;
using System.Collections.Generic;
using MessagePipe;
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
    /// </summary>
    public sealed class MessagePipeEventBus : IEventBus
    {
        readonly IObjectResolver _resolver;
        // 仅主线程读写;不使用并发集合以避免移动端 GC 开销
        readonly Dictionary<Type, object> _publishers = new();
        readonly Dictionary<Type, object> _subscribers = new();

        public MessagePipeEventBus(IObjectResolver resolver) => _resolver = resolver;

        public void Publish<T>(T evt)
        {
            if (!_publishers.TryGetValue(typeof(T), out var pub))
            {
                pub = _resolver.Resolve<IPublisher<T>>();
                _publishers[typeof(T)] = pub;
            }
            ((IPublisher<T>)pub).Publish(evt);
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
