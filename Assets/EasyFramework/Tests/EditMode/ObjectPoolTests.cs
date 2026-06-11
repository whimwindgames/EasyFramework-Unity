using System;
using EasyFramework.Core.Pooling;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ObjectPoolTests
    {
        class Bullet : IPoolable
        {
            public int SpawnCount, DespawnCount;
            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        [Test]
        public void Spawn_ReusesDespawnedInstance()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet());
            var a = pool.Spawn();
            pool.Despawn(a);
            var b = pool.Spawn();
            Assert.AreSame(a, b);
            Assert.AreEqual(2, a.SpawnCount);
            Assert.AreEqual(1, a.DespawnCount);
        }

        [Test]
        public void Despawn_Twice_Throws()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet());
            var a = pool.Spawn();
            pool.Despawn(a);
            Assert.Throws<InvalidOperationException>(() => pool.Despawn(a));
        }

        [Test]
        public void Prewarm_FillsInactiveCount()
        {
            var pool = new ObjectPool<Bullet>(() => new Bullet(), initialCapacity: 5);
            Assert.AreEqual(5, pool.CountInactive);
        }

        [Test]
        public void Clear_InvokesDestroyCallback()
        {
            var destroyed = 0;
            var pool = new ObjectPool<Bullet>(() => new Bullet(), initialCapacity: 3);
            pool.Clear(_ => destroyed++);
            Assert.AreEqual(3, destroyed);
            Assert.AreEqual(0, pool.CountInactive);
        }
    }
}
