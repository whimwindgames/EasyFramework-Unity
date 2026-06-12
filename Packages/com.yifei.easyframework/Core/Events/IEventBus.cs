using System;

namespace EasyFramework.Core.Events
{
    public interface IEventBus
    {
        void Publish<T>(T evt);
        IDisposable Subscribe<T>(Action<T> handler);
    }
}
