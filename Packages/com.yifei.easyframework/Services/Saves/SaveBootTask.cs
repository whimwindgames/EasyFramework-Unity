using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Core.Boot;

namespace EasyFramework.Services.Saves
{
    public sealed class SaveBootTask : IBootTask
    {
        readonly ISaveService _save;
        public SaveBootTask(ISaveService save) => _save = save;

        public int Priority => 0;     // 存档最先
        public bool IsCritical => true;

        public async UniTask InitializeAsync(CancellationToken ct) => await _save.LoadAsync();
    }
}
