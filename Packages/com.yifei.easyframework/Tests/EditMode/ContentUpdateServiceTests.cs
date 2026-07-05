using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Events;
using EasyFramework.Services.ContentUpdate;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class ContentUpdateServiceTests
    {
        sealed class FakeBus : IEventBus
        {
            readonly Dictionary<System.Type, List<System.Delegate>> _handlers = new();
            public readonly List<object> Published = new();

            public void Publish<T>(T evt)
            {
                Published.Add(evt);
                if (_handlers.TryGetValue(typeof(T), out var list))
                    foreach (var d in list.ToArray()) ((System.Action<T>)d)(evt);
            }

            public System.IDisposable Subscribe<T>(System.Action<T> handler)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new List<System.Delegate>();
                list.Add(handler);
                return new Sub(() => list.Remove(handler));
            }

            sealed class Sub : System.IDisposable
            {
                readonly System.Action _dispose;
                public Sub(System.Action d) => _dispose = d;
                public void Dispose() => _dispose();
            }

            public List<T> PublishedOf<T>()
            {
                var result = new List<T>();
                foreach (var p in Published)
                    if (p is T t) result.Add(t);
                return result;
            }
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public bool UpdateSucceeds = true;
            public int CheckCallCount;
            public int UpdateCallCount;

            public UniTask<List<string>> CheckForCatalogUpdatesAsync()
            {
                CheckCallCount++;
                return UniTask.FromResult(CatalogsWithUpdates);
            }

            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
            {
                UpdateCallCount++;
                return UniTask.FromResult(UpdateSucceeds);
            }
        }

        [Test]
        public void ContentUpdateInfo_FieldsAreReadable()
        {
            var info = new ContentUpdateInfo(true, 1024L);
            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(1024L, info.EstimatedDownloadSizeBytes);
        }

        [Test]
        public void CheckAsync_UpdateAvailable_ReturnsAvailableAndPublishesEvent()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string> { "catalog_001" } };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            var info = svc.CheckAsync().GetAwaiter().GetResult();

            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(-1L, info.EstimatedDownloadSizeBytes);
            Assert.IsTrue(svc.HasChecked);
            var published = bus.PublishedOf<ContentAvailableEvent>();
            Assert.AreEqual(1, published.Count);
            Assert.IsTrue(published[0].Info.IsAvailable);
        }

        [Test]
        public void CheckAsync_NoUpdate_ReturnsNotAvailableAndPublishesNoEvent()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string>() };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            var info = svc.CheckAsync().GetAwaiter().GetResult();

            Assert.IsFalse(info.IsAvailable);
            Assert.IsTrue(svc.HasChecked);
            Assert.AreEqual(0, bus.PublishedOf<ContentAvailableEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_Success_PublishesAppliedEventAndReturnsTrue()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = true,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result);
            Assert.AreEqual(1, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_Failure_PublishesFailedEventReturnsFalseNoThrow()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = false,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            bool result = false;
            Assert.DoesNotThrow(() => result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult());

            Assert.IsFalse(result);
            Assert.AreEqual(1, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
        }

        [Test]
        public void DownloadAndApplyAsync_WithoutPriorCheck_ChecksInternallyFirst()
        {
            var gateway = new FakeAddressablesCatalogGateway
            {
                CatalogsWithUpdates = new List<string> { "catalog_001" },
                UpdateSucceeds = true,
            };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);

            Assert.IsFalse(svc.HasChecked);
            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result);
            Assert.IsTrue(svc.HasChecked);
            Assert.AreEqual(1, gateway.CheckCallCount);
            Assert.AreEqual(1, gateway.UpdateCallCount);
        }

        [Test]
        public void DownloadAndApplyAsync_NoUpdateAvailable_ReturnsFalseWithoutCallingUpdate()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string>() };
            var bus = new FakeBus();
            var svc = new ContentUpdateService(gateway, bus);
            svc.CheckAsync().GetAwaiter().GetResult();

            var result = svc.DownloadAndApplyAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result);
            Assert.AreEqual(0, gateway.UpdateCallCount);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateAppliedEvent>().Count);
            Assert.AreEqual(0, bus.PublishedOf<ContentUpdateFailedEvent>().Count);
        }
    }

    public class AddressablesCatalogGatewayTests
    {
        [Test]
        public void ImplementsIAddressablesCatalogGateway()
        {
            IAddressablesCatalogGateway gateway = new AddressablesCatalogGateway();
            Assert.IsNotNull(gateway);
        }
    }

    public class ContentUpdateBootTaskTests
    {
        sealed class FakeBus : EasyFramework.Core.Events.IEventBus
        {
            public void Publish<T>(T evt) { }
            public System.IDisposable Subscribe<T>(System.Action<T> handler) => new NoopSub();
            sealed class NoopSub : System.IDisposable { public void Dispose() { } }
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public UniTask<List<string>> CheckForCatalogUpdatesAsync() => UniTask.FromResult(CatalogsWithUpdates);
            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
                => UniTask.FromResult(true);
        }

        [Test]
        public void Priority_Is20()
        {
            var svc = new ContentUpdateService(new FakeAddressablesCatalogGateway(), new FakeBus());
            var task = new ContentUpdateBootTask(svc);
            Assert.AreEqual(20, task.Priority);
        }

        [Test]
        public void IsCritical_IsFalse()
        {
            var svc = new ContentUpdateService(new FakeAddressablesCatalogGateway(), new FakeBus());
            var task = new ContentUpdateBootTask(svc);
            Assert.IsFalse(task.IsCritical);
        }

        [Test]
        public void InitializeAsync_CallsCheckAsyncOnce()
        {
            var gateway = new FakeAddressablesCatalogGateway { CatalogsWithUpdates = new List<string> { "c" } };
            var svc = new ContentUpdateService(gateway, new FakeBus());
            var task = new ContentUpdateBootTask(svc);

            Assert.IsFalse(svc.HasChecked);
            task.InitializeAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(svc.HasChecked);
        }
    }
}
