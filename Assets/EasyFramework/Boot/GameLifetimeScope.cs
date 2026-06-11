using VContainer;
using VContainer.Unity;

namespace EasyFramework
{
    /// <summary>每个小游戏继承此类注册自己的服务;作为 RootLifetimeScope 的子作用域。</summary>
    public abstract class GameLifetimeScope : LifetimeScope
    {
        protected sealed override void Configure(IContainerBuilder builder) => ConfigureGame(builder);
        protected abstract void ConfigureGame(IContainerBuilder builder);
    }
}
