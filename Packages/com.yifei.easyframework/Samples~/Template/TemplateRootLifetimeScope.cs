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
            // 正式项目在这里配置 MaxAdsSettings(Android/iOS 奖励、插屏、Banner 广告位)。
            // 无服务端内购验证时框架默认使用 ClientOnlyIAPReceiptValidator;以后可在这里
            // 通过 IAPReceiptValidatorFactory 无缝替换成服务器验签。
            // options.RemoteConfigProviderFactory = resolver => new YourRemoteConfigProvider(...);
            // options.AnalyticsBackendsFactory = resolver =>
            //     new IAnalyticsBackend[] { new YourAnalyticsBackend(...) };
        }
    }
}
