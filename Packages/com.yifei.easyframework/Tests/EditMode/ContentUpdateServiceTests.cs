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
        }

        sealed class FakeAddressablesCatalogGateway : IAddressablesCatalogGateway
        {
            public List<string> CatalogsWithUpdates = new();
            public bool UpdateSucceeds = true;

            public UniTask<List<string>> CheckForCatalogUpdatesAsync()
                => UniTask.FromResult(CatalogsWithUpdates);

            public UniTask<bool> UpdateCatalogsAsync(List<string> catalogKeys, IProgress<float> progress)
                => UniTask.FromResult(UpdateSucceeds);
        }

        [Test]
        public void ContentUpdateInfo_FieldsAreReadable()
        {
            var info = new ContentUpdateInfo(true, 1024L);
            Assert.IsTrue(info.IsAvailable);
            Assert.AreEqual(1024L, info.EstimatedDownloadSizeBytes);
        }
    }
}
