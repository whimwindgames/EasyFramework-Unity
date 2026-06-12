using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EasyFramework.Monetization.Analytics
{
    /// <summary>编辑器 / 真机调试用后端:把事件打到 Console。真机 DevTools 可看事件流。</summary>
    public sealed class DebugAnalyticsBackend : IAnalyticsBackend
    {
        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                Debug.Log($"[Analytics] {eventName}");
                return;
            }
            var sb = new StringBuilder();
            sb.Append("[Analytics] ").Append(eventName).Append(" { ");
            var first = true;
            foreach (var kv in parameters)
            {
                if (!first) sb.Append(", ");
                sb.Append(kv.Key).Append('=').Append(kv.Value);
                first = false;
            }
            sb.Append(" }");
            Debug.Log(sb.ToString());
        }

        public void SetUserProperty(string key, string value)
            => Debug.Log($"[Analytics] user.{key} = {value}");
    }
}
