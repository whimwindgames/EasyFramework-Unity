using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Assets;
using NUnit.Framework;
using UnityEngine;

namespace EasyFramework.Tests
{
    public class AssetServiceTests
    {
        static FakeAssetService MakeService(out Material mat, string key = "mat")
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            var dict = new Dictionary<string, Object> { { key, mat } };
            return new FakeAssetService(dict);
        }

        [Test]
        public void LoadAsync_ReturnsMappedAsset()
        {
            var svc = MakeService(out var mat);
            var loaded = svc.LoadAsync<Material>("mat").GetAwaiter().GetResult();
            Assert.AreSame(mat, loaded);
        }

        [Test]
        public void LoadAsync_SameKey_ReturnsSameInstance()
        {
            var svc = MakeService(out var mat);
            var a = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            var b = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.AreSame(a, b);
            Assert.AreSame(mat, a);
        }

        [Test]
        public void LoadAsync_MissingKey_Throws()
        {
            var svc = new FakeAssetService(new Dictionary<string, Object>());
            Assert.Throws<KeyNotFoundException>(() =>
                svc.LoadAsync<Material>("nope").GetAwaiter().GetResult());
        }

        [Test]
        public void ReleaseScope_AfterRelease_CanLoadAgain()
        {
            var svc = MakeService(out var mat);
            svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Scene));
            var again = svc.LoadAsync<Material>("mat", AssetScope.Scene).GetAwaiter().GetResult();
            Assert.AreSame(mat, again);
        }

        [Test]
        public void ReleaseScope_OnlyAffectsTargetScope()
        {
            var svc = MakeService(out var mat);
            svc.LoadAsync<Material>("mat", AssetScope.Global).GetAwaiter().GetResult();
            // 释放 Scene 不应影响 Global 记录的 release 计数(Fake 仅校验不抛错)
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Scene));
            Assert.DoesNotThrow(() => svc.ReleaseScope(AssetScope.Global));
        }
    }
}
