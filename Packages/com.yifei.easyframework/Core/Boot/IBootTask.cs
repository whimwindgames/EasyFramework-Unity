using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Core.Boot
{
    public interface IBootTask
    {
        /// <summary>越小越先执行;相同优先级的任务并行执行。</summary>
        int Priority { get; }
        /// <summary>true:失败中止启动并抛 BootFailedException;false:记录日志后继续。</summary>
        bool IsCritical { get; }
        UniTask InitializeAsync(CancellationToken ct);
    }
}
