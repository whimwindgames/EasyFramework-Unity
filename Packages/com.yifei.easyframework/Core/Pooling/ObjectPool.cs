using System;
using System.Collections.Generic;

namespace EasyFramework.Core.Pooling
{
    public sealed class ObjectPool<T> where T : class
    {
        readonly Func<T> _factory;
        readonly Stack<T> _inactive = new();
        readonly HashSet<T> _inactiveSet = new();

        public int CountInactive => _inactive.Count;

        public ObjectPool(Func<T> factory, int initialCapacity = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            for (var i = 0; i < initialCapacity; i++)
            {
                var item = _factory();
                _inactive.Push(item);
                _inactiveSet.Add(item);
            }
        }

        public T Spawn()
        {
            var item = _inactive.Count > 0 ? _inactive.Pop() : _factory();
            _inactiveSet.Remove(item);
            (item as IPoolable)?.OnSpawn();
            return item;
        }

        public void Despawn(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!_inactiveSet.Add(item))
                throw new InvalidOperationException("Item already despawned.");
            (item as IPoolable)?.OnDespawn();
            _inactive.Push(item);
        }

        public void Clear(Action<T> onDestroy = null)
        {
            while (_inactive.Count > 0)
                onDestroy?.Invoke(_inactive.Pop());
            _inactiveSet.Clear();
        }
    }
}
