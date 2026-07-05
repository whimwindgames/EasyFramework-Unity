using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Services.ContentUpdate
{
    public sealed class ContentUpdateBootTask : IBootTask
    {
        readonly ContentUpdateService _contentUpdate;
        public ContentUpdateBootTask(ContentUpdateService contentUpdate) => _contentUpdate = contentUpdate;

        public int Priority => 20;         // 晚于 Save(0)/Config(10),早于 Ads(30)/IAP(31)
        public bool IsCritical => false;   // 检查失败不阻塞启动;不自动下载

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await _contentUpdate.CheckAsync();
        }
    }
}
