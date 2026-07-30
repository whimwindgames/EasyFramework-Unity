// ===========================================================================
// FirebaseAnalyticsBackend —— 真实统计后端接入槽(NOT a placeholder)。
//
// 本阶段(无人值守)不接入 Firebase:其需要 google-services.json / GoogleService-Info.plist
// 与 Firebase Unity SDK 原生插件。接入步骤:
//   1. 导入 Firebase Analytics Unity SDK;
//   2. 在 Player Settings > Scripting Define Symbols 定义 EF_FIREBASE;
//   3. 填充下方方法体(把 parameters 映射为 Firebase.Analytics.Parameter[]);
//   4. 在 RootLifetimeScope.ConfigureFrameworkOptions 设置 AnalyticsBackendsFactory。
// IAnalyticsBackend / IAnalyticsService 接口、AnalyticsService、打点代码全部不变。
// ===========================================================================
#if EF_FIREBASE
using System.Collections.Generic;

namespace EasyFramework.Monetization.Analytics
{
    public sealed class FirebaseAnalyticsBackend : IAnalyticsBackend
    {
        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            // 接入时:把 parameters 转 Firebase.Analytics.Parameter[](按值类型 string/long/double 分支),
            // 调 FirebaseAnalytics.LogEvent(eventName, parms)。
        }

        public void SetUserProperty(string key, string value)
        {
            // 接入时:FirebaseAnalytics.SetUserProperty(key, value);
        }
    }
}
#endif
