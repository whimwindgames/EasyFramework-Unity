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
            // 正式项目示例:
            // options.AdsProviderFactory = resolver => new YourAdsProvider(...);
            // options.IAPReceiptValidatorFactory = resolver => new ServerReceiptValidator(...);
            // options.RemoteConfigProviderFactory = resolver => new YourRemoteConfigProvider(...);
            // options.AnalyticsBackendsFactory = resolver =>
            //     new IAnalyticsBackend[] { new YourAnalyticsBackend(...) };
        }
    }
}
