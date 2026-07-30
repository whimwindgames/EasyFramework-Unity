using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Configs
{
    public interface IRemoteConfigProvider
    {
        UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct);
    }

    public sealed class NoopRemoteConfigProvider : IRemoteConfigProvider
    {
        static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
        public UniTask<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken ct)
            => UniTask.FromResult(Empty);
    }

    public interface IConfigService
    {
        T Get<T>(string key, T defaultValue);
        bool Has(string key);
        UniTask RefreshRemoteAsync(CancellationToken ct = default);
    }
}
