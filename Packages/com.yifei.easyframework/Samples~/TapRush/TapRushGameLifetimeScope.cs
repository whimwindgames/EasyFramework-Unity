using EasyFramework;
using EasyFramework.Core.Boot;
using EasyFramework.DevTools;
using EasyFramework.Monetization.IAP;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game
{
    /// <summary>TapRush 游戏入口作用域。挂为 Boot 场景 RootLifetimeScope 的子作用域。</summary>
    public sealed class TapRushGameLifetimeScope : GameLifetimeScope
    {
        protected override void ConfigureGame(IContainerBuilder builder)
        {
            // ---- 玩法流程 ----
            builder.Register<TapRushFlow>(Lifetime.Singleton);

            // TapRushFlowBootTask 既是 IBootTask 又是 ITickable:单次注册经
            // RegisterEntryPoint 的 AsImplementedInterfaces() 暴露全部接口(IBootTask /
            // ITickable / IDisposable),避免重复注册同一具体类型触发 VContainer 冲突。
            builder.RegisterEntryPoint<TapRushFlowBootTask>();   // IBootTask + ITickable 帧驱动

            // ---- DevTools(控制台 + 角标 + 作弊)----
            // 经 RegisterEntryPoint 暴露 IInitializable,由子作用域入口点 dispatcher 调用
            // (根 GameBootstrap 只收集根作用域 IBootTask,看不到子作用域的)。
            builder.RegisterEntryPoint<DevToolsBootTask>();

            // ---- 商品发奖(build 后注册 RewardHandler)----
            // 生产存档载荷 TapRushSaveData 由 TapRushRootLifetimeScope.GetSaveProfile() 决定。
            builder.RegisterBuildCallback(resolver =>
            {
                var iap = resolver.Resolve<IAPService>();
                iap.SetRewardHandler((transaction, _) =>
                {
                    // 正式游戏必须把 transaction.TransactionId 与游戏存档一起做幂等。
                    Debug.Log($"[TapRush] Granting '{transaction.ProductId}', tx={transaction.TransactionId}.");
                    return Cysharp.Threading.Tasks.UniTask.FromResult(true);
                });
            });
        }
    }
}
