using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Configs;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class ConfigServiceTests
    {
        sealed class FakeRemote : IRemoteConfigProvider
        {
            readonly IReadOnlyDictionary<string, string> _data;
            public FakeRemote(IReadOnlyDictionary<string, string> data) => _data = data;
            public UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct)
                => UniTask.FromResult(_data);
        }

        sealed class MutableRemote : IRemoteConfigProvider
        {
            public IReadOnlyDictionary<string, string> Data;
            public UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct)
                => UniTask.FromResult(Data);
        }

        static ConfigTable MakeTable(params (string, string)[] entries)
        {
            var t = ScriptableObject.CreateInstance<ConfigTable>();
            foreach (var (k, v) in entries) t.AddEntryForTest(k, v);
            return t;
        }

        static ConfigService MakeService(ConfigTable table, IReadOnlyDictionary<string, string> remote = null)
            => new ConfigService(new[] { table }, new NoopRemoteConfigProvider());

        [Test]
        public void Get_ReturnsLocalValue()
        {
            var svc = MakeService(MakeTable(("cooldown", "30"), ("enabled", "true")));
            Assert.AreEqual(30, svc.Get("cooldown", 0));
            Assert.IsTrue(svc.Get("enabled", false));
        }

        [Test]
        public void Get_ParsesAllSupportedTypes()
        {
            var svc = MakeService(MakeTable(
                ("i", "7"), ("f", "1.5"), ("b", "true"), ("s", "hello")));
            Assert.AreEqual(7, svc.Get("i", 0));
            Assert.AreEqual(1.5f, svc.Get("f", 0f));
            Assert.IsTrue(svc.Get("b", false));
            Assert.AreEqual("hello", svc.Get("s", ""));
        }

        [Test]
        public void Get_MissingKey_ReturnsDefault()
        {
            var svc = MakeService(MakeTable(("x", "1")));
            Assert.AreEqual(99, svc.Get("missing", 99));
        }

        [Test]
        public void Get_ParseFailure_ReturnsDefault()
        {
            var svc = MakeService(MakeTable(("n", "not_a_number")));
            Assert.AreEqual(42, svc.Get("n", 42)); // 解析失败回退,内部 Debug.LogWarning
        }

        [Test]
        public void Has_ReflectsPresence()
        {
            var svc = MakeService(MakeTable(("present", "1")));
            Assert.IsTrue(svc.Has("present"));
            Assert.IsFalse(svc.Has("absent"));
        }

        [Test]
        public void RemoteValue_OverridesLocal()
        {
            var table = MakeTable(("cooldown", "30"));
            var svc = new ConfigService(new[] { table },
                new FakeRemote(new Dictionary<string, string> { { "cooldown", "5" } }));
            svc.RefreshRemoteAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(5, svc.Get("cooldown", 0));
            Assert.IsTrue(svc.Has("cooldown"));
        }

        [Test]
        public void RefreshRemote_ReplacesSnapshotAndRemovesDeletedKeys()
        {
            var remote = new MutableRemote
            {
                Data = new Dictionary<string, string> { ["temporary"] = "1" },
            };
            var service = new ConfigService(
                new[] { MakeTable(("local", "2")) }, remote);
            service.RefreshRemoteAsync().GetAwaiter().GetResult();
            Assert.IsTrue(service.Has("temporary"));

            remote.Data = new Dictionary<string, string>();
            service.RefreshRemoteAsync().GetAwaiter().GetResult();

            Assert.IsFalse(service.Has("temporary"));
            Assert.AreEqual(2, service.Get("local", 0));
        }
    }
}
