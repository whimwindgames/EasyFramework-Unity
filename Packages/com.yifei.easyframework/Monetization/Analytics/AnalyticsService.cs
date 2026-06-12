using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Monetization.Analytics
{
    public sealed class AnalyticsService : IAnalyticsService
    {
        static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

        readonly IReadOnlyList<IAnalyticsBackend> _backends;

        public AnalyticsService(IReadOnlyList<IAnalyticsBackend> backends)
            => _backends = backends ?? Array.Empty<IAnalyticsBackend>();

        public void Track(string eventName) => Dispatch(eventName, Empty);

        /// <summary>
        /// 打点并传递参数元组。
        /// <para>
        /// <b>GC 注意:</b>每次调用都会分配一个新的 <see cref="Dictionary{TKey,TValue}"/> 以及
        /// <c>params</c> 数组,值类型参数(int / float / bool 等)还会装箱进 <c>object</c>。
        /// 在每帧或高频路径(得分更新、每次广告曝光等)调用时会持续产生 GC 压力。
        /// 热点路径建议改用 <see cref="Track(string, IReadOnlyDictionary{string, object})"/> 并在外部
        /// 复用/池化同一个字典实例,从而彻底消除分配。
        /// </para>
        /// </summary>
        public void Track(string eventName, params (string key, object value)[] parameters)
        {
            var dict = new Dictionary<string, object>(parameters?.Length ?? 0);
            if (parameters != null)
                foreach (var (k, v) in parameters) dict[k] = v;
            Dispatch(eventName, dict);
        }

        /// <summary>
        /// 零分配热路径重载:直接接受已构建的 <see cref="IReadOnlyDictionary{TKey,TValue}"/>。
        /// 调用方可在类成员中持有并复用同一个 <see cref="Dictionary{TKey,TValue}"/> 实例,
        /// 避免每次打点的字典分配与值类型装箱。
        /// <para>
        /// 示例:
        /// <code>
        /// readonly Dictionary&lt;string,object&gt; _params = new() { ["score"] = 0 };
        /// // 高频路径:
        /// _params["score"] = currentScore;   // 仍有装箱,但字典本身不重分配
        /// analytics.Track("score_update", _params);
        /// </code>
        /// </para>
        /// </summary>
        public void Track(string eventName, IReadOnlyDictionary<string, object> parameters)
            => Dispatch(eventName, parameters ?? Empty);

        public void SetUserProperty(string key, string value)
        {
            for (var i = 0; i < _backends.Count; i++)
            {
                try { _backends[i].SetUserProperty(key, value); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Analytics backend {_backends[i].GetType().Name} " +
                                     $"threw on SetUserProperty('{key}'): {e.Message}");
                }
            }
        }

        void Dispatch(string eventName, IReadOnlyDictionary<string, object> parameters)
        {
            for (var i = 0; i < _backends.Count; i++)
            {
                try { _backends[i].Track(eventName, parameters); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EasyFramework] Analytics backend {_backends[i].GetType().Name} " +
                                     $"threw on Track('{eventName}'): {e.Message}");
                }
            }
        }
    }
}
