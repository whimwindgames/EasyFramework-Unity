using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Services.Configs
{
    public sealed class ConfigBootTask : IBootTask
    {
        readonly ConfigService _config;
        public ConfigBootTask(ConfigService config) => _config = config;

        public int Priority => 10;
        public bool IsCritical => false;   // 远程拉取失败不阻塞启动

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await _config.RefreshRemoteAsync(ct);
        }
    }
}
