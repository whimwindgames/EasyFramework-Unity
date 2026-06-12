using System.Collections.Generic;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>统计后端适配点。Debug / Firebase / 其它后端各实现一个,多注册广播。</summary>
    public interface IAnalyticsBackend
    {
        void Track(string eventName, IReadOnlyDictionary<string, object> parameters);
        void SetUserProperty(string key, string value);
    }

    /// <summary>业务层入口(G.Analytics)。广播到所有 IAnalyticsBackend。</summary>
    public interface IAnalyticsService
    {
        void Track(string eventName);
        void Track(string eventName, params (string key, object value)[] parameters);
        void SetUserProperty(string key, string value);
    }
}
