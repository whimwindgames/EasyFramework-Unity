using EasyFramework.Services.Saves;

namespace EasyFramework.Template
{
    /// <summary>
    /// 挂在 Boot 场景根节点。根服务适配器必须通过此处的 FrameworkOptions 在构建前配置,
    /// 不能在 TemplateGameLifetimeScope 子作用域中覆盖。
    /// </summary>
    public sealed class TemplateRootLifetimeScope : RootLifetimeScope
    {
        protected override SaveProfile GetSaveProfile()
            => TemplateGameLifetimeScope.CreateSaveProfile();

        protected override void ConfigureFrameworkOptions(FrameworkOptions options)
        {
            // 安装可选 MAX 扩展后:options.UseAppLovinMax(new MaxAdsSettings { ... });
            // 安装可选 Unity IAP 扩展后:options.UseUnityIAP();
            // options.RemoteConfigProviderFactory = resolver => new YourRemoteConfigProvider(...);
            // options.AnalyticsBackendsFactory = resolver =>
            //     new IAnalyticsBackend[] { new YourAnalyticsBackend(...) };
        }
    }
}
