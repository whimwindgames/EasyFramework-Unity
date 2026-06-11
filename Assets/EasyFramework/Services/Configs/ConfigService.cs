using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.Services.Configs
{
    public sealed class ConfigService : IConfigService
    {
        readonly Dictionary<string, string> _local = new();
        readonly Dictionary<string, string> _remote = new();
        readonly IRemoteConfigProvider _provider;

        public ConfigService(IReadOnlyList<ConfigTable> tables, IRemoteConfigProvider provider)
        {
            _provider = provider;
            if (tables != null)
                foreach (var table in tables)
                    if (table != null)
                        foreach (var e in table.Entries)
                            _local[e.Key] = e.Value;
        }

        public async UniTask RefreshRemoteAsync(CancellationToken ct)
        {
            var fetched = await _provider.FetchAsync(ct);
            if (fetched == null) return;
            foreach (var kv in fetched) _remote[kv.Key] = kv.Value;
        }

        public bool Has(string key) => _remote.ContainsKey(key) || _local.ContainsKey(key);

        public T Get<T>(string key, T defaultValue)
        {
            if (!_remote.TryGetValue(key, out var raw) && !_local.TryGetValue(key, out raw))
                return defaultValue;

            try
            {
                var t = typeof(T);
                object parsed;
                if (t == typeof(int)) parsed = int.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) parsed = float.Parse(raw, CultureInfo.InvariantCulture);
                else if (t == typeof(bool)) parsed = bool.Parse(raw);
                else if (t == typeof(string)) parsed = raw;
                else
                {
                    Debug.LogWarning($"[EasyFramework] Config '{key}': unsupported type {t.Name}, using default.");
                    return defaultValue;
                }
                return (T)parsed;
            }
            catch (Exception)
            {
                Debug.LogWarning($"[EasyFramework] Config '{key}': failed to parse '{raw}' as {typeof(T).Name}, using default.");
                return defaultValue;
            }
        }
    }
}
