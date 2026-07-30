using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Juice
{
    public interface IJuiceService
    {
        void PunchScale(UnityEngine.Transform target, float strength = 0.2f, float duration = 0.25f);
        void Flash(UnityEngine.SpriteRenderer renderer, UnityEngine.Color color, float duration = 0.1f);
        UniTask HitStopAsync(float duration = 0.05f, CancellationToken ct = default);
    }
}
